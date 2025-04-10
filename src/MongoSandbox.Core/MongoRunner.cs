using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoSandbox;

public sealed class MongoRunner
{
    private static readonly string DefaultRootDataDirectory = Path.Combine(Path.GetTempPath(), "mongo-sandbox");
    private static int ongoingRootDataDirectoryCleanupCount;

    private readonly IFileSystem _fileSystem;
    private readonly ITimeProvider _timeProvider;
    private readonly IPortFactory _portFactory;
    private readonly IMongoExecutableLocator _executableLocator;
    private readonly IMongoProcessFactory _processFactory;
    private readonly MongoRunnerOptions _options;

    private IMongoProcess? process;
    private string? dataDirectory;

    private MongoRunner(IFileSystem fileSystem, ITimeProvider timeProvider, IPortFactory portFactory, IMongoExecutableLocator executableLocator, IMongoProcessFactory processFactory, MongoRunnerOptions? options = null)
    {
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _portFactory = portFactory;
        _executableLocator = executableLocator;
        _processFactory = processFactory;
        _options = options == null ? new MongoRunnerOptions() : new MongoRunnerOptions(options);
    }

    public static IMongoRunner Run(MongoRunnerOptions? options = null)
    {
        var runner = new MongoRunner(new FileSystem(), new TimeProvider(), new PortFactory(), new MongoExecutableLocator(), new MongoProcessFactory(), options);
        return runner.RunInternal();
    }

    private IMongoRunner RunInternal()
    {
        try
        {
            // Find MongoDB and make it executable
            var executablePath = _executableLocator.FindMongoExecutablePath(_options, MongoProcessKind.Mongod);
            _fileSystem.MakeFileExecutable(executablePath);

            // Ensure data directory exists...
            if (_options.DataDirectory != null)
            {
                dataDirectory = _options.DataDirectory;
            }
            else
            {
                _options.RootDataDirectoryPath ??= DefaultRootDataDirectory;
                dataDirectory = Path.Combine(_options.RootDataDirectoryPath, Path.GetRandomFileName());
            }

            _fileSystem.CreateDirectory(dataDirectory);

            try
            {
                // ...and has no existing MongoDB lock file
                // https://stackoverflow.com/a/6857973/825695
                var lockFilePath = Path.Combine(dataDirectory, "mongod.lock");
                _fileSystem.DeleteFile(lockFilePath);
            }
            catch
            {
                // Ignored - this data directory might already be in use, we'll see later how mongod reacts
            }

            CleanupOldDataDirectories();

            _options.MongoPort ??= _portFactory.GetRandomAvailablePort();

            // Build MongoDB executable arguments
            var arguments = string.Format(CultureInfo.InvariantCulture, "--dbpath {0} --port {1} --bind_ip 127.0.0.1", ProcessArgument.Escape(dataDirectory), _options.MongoPort);
            arguments += RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? string.Empty : " --tlsMode disabled";
            arguments += _options.UseSingleNodeReplicaSet ? " --replSet " + _options.ReplicaSetName : string.Empty;
            arguments += string.IsNullOrWhiteSpace(_options.AdditionalArguments) ? string.Empty : " " + _options.AdditionalArguments;

            process = _processFactory.CreateMongoProcess(_options, MongoProcessKind.Mongod, executablePath, arguments);
            process.Start();

            var connectionStringFormat = _options.UseSingleNodeReplicaSet ? "mongodb://127.0.0.1:{0}/?directConnection=true&replicaSet={1}&readPreference=primary" : "mongodb://127.0.0.1:{0}";
            var connectionString = string.Format(CultureInfo.InvariantCulture, connectionStringFormat, _options.MongoPort, _options.ReplicaSetName);

            return new StartedMongoRunner(this, connectionString);
        }
        catch
        {
            Dispose(throwOnException: false);
            throw;
        }
    }

    private void CleanupOldDataDirectories()
    {
        if (_options.DataDirectory != null)
        {
            // Data directory was set by user, do not trigger cleanup
            return;
        }

        try
        {
            var isCleanupOngoing = Interlocked.Increment(ref ongoingRootDataDirectoryCleanupCount) > 1;
            if (isCleanupOngoing)
            {
                return;
            }

            string[] dataDirectoryPaths;
            try
            {
                dataDirectoryPaths = _fileSystem.GetDirectories(_options.RootDataDirectoryPath!, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                _options.StandardErrorLogger?.Invoke($"An error occurred while trying to enumerate existing data directories for cleanup in '{DefaultRootDataDirectory}': {ex.Message}");
                return;
            }

            foreach (var dataDirectoryPath in dataDirectoryPaths)
            {
                try
                {
                    var dataDirectoryAge = _timeProvider.UtcNow - _fileSystem.GetDirectoryCreationTimeUtc(dataDirectoryPath);
                    if (dataDirectoryAge >= _options.DataDirectoryLifetime)
                    {
                        _fileSystem.DeleteDirectory(dataDirectoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _options.StandardErrorLogger?.Invoke($"An error occurred while trying to delete old data directory '{dataDirectoryPath}': {ex.Message}");
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref ongoingRootDataDirectoryCleanupCount);
        }
    }

    private void Dispose(bool throwOnException)
    {
        var exceptions = new List<Exception>(1);

        try
        {
            process?.Dispose();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        try
        {
            // Do not dispose data directory if set from user input or the root data directory path was set for tests
            if (dataDirectory != null && _options.DataDirectory == null && _options.RootDataDirectoryPath == DefaultRootDataDirectory)
            {
                _fileSystem.DeleteDirectory(dataDirectory);
            }
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        if (throwOnException)
        {
            if (exceptions.Count == 1)
            {
                ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
            }
            else if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }
    }

    private sealed class StartedMongoRunner : IMongoRunner
    {
        private readonly MongoRunner runner;
        private int isDisposed;

        public StartedMongoRunner(MongoRunner runner, string connectionString)
        {
            this.runner = runner;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1117:Parameters should be on same line or separate lines", Justification = "Would have used too many lines, and this way string.Format is still very readable")]
        public void Import(string database, string collection, string inputFilePath, string? additionalArguments = null, bool drop = false)
        {
            if (Interlocked.CompareExchange(ref isDisposed, 0, 0) == 1)
            {
                throw new ObjectDisposedException("MongoDB runner is already disposed");
            }

            if (string.IsNullOrWhiteSpace(database))
            {
                throw new ArgumentException("Database name is required", nameof(database));
            }

            if (string.IsNullOrWhiteSpace(collection))
            {
                throw new ArgumentException("Collection name is required", nameof(collection));
            }

            if (string.IsNullOrWhiteSpace(inputFilePath))
            {
                throw new ArgumentException("Input file path is required", nameof(inputFilePath));
            }

            var executablePath = runner._executableLocator.FindMongoExecutablePath(runner._options, MongoProcessKind.MongoImport);
            runner._fileSystem.MakeFileExecutable(executablePath);

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                @"--uri=""{0}"" --db={1} --collection={2} --file={3} {4} {5}",
                ConnectionString, database, collection, ProcessArgument.Escape(inputFilePath), drop ? " --drop" : string.Empty, additionalArguments ?? string.Empty);

            using var process = runner._processFactory.CreateMongoProcess(runner._options, MongoProcessKind.MongoImport, executablePath, arguments);
            process.Start();
        }

        [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1117:Parameters should be on same line or separate lines", Justification = "Would have used too many lines, and this way string.Format is still very readable")]
        public void Export(string database, string collection, string outputFilePath, string? additionalArguments = null)
        {
            if (Interlocked.CompareExchange(ref isDisposed, 0, 0) == 1)
            {
                throw new ObjectDisposedException("MongoDB runner is already disposed");
            }

            if (string.IsNullOrWhiteSpace(database))
            {
                throw new ArgumentException("Database name is required", nameof(database));
            }

            if (string.IsNullOrWhiteSpace(collection))
            {
                throw new ArgumentException("Collection name is required", nameof(collection));
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentException("Output file path is required", nameof(outputFilePath));
            }

            var executablePath = runner._executableLocator.FindMongoExecutablePath(runner._options, MongoProcessKind.MongoExport);
            runner._fileSystem.MakeFileExecutable(executablePath);

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                @"--uri=""{0}"" --db={1} --collection={2} --out={3} {4}",
                ConnectionString, database, collection, ProcessArgument.Escape(outputFilePath), additionalArguments ?? string.Empty);

            using var process = runner._processFactory.CreateMongoProcess(runner._options, MongoProcessKind.MongoExport, executablePath, arguments);
            process.Start();
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref isDisposed, 1, 0) == 0)
            {
                TryShutdownQuietly();
                runner.Dispose(throwOnException: true);
            }
        }

        private void TryShutdownQuietly()
        {
            // https://www.mongodb.com/docs/v4.4/reference/command/shutdown/
            const int defaultShutdownTimeoutInSeconds = 10;

            var shutdownCommand = new BsonDocument
            {
                { "shutdown", 1 },
                { "force", true },
                { "timeoutSecs", defaultShutdownTimeoutInSeconds },
            };

            try
            {
                var client = new MongoClient(ConnectionString);
                var admin = client.GetDatabase("admin");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(defaultShutdownTimeoutInSeconds));
                admin.RunCommand<BsonDocument>(shutdownCommand, cancellationToken: cts.Token);
            }
            catch (MongoConnectionException)
            {
                // This is the expected behavior as mongod is shutting down
            }
            catch
            {
                // Ignore other exceptions as well, we'll kill the process anyway
            }
        }
    }
}