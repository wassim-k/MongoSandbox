using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoSandbox;

public sealed class MongoRunner
{
    private readonly IFileSystem _fileSystem;
    private readonly IPortFactory _portFactory;
    private readonly IMongoExecutableLocator _executableLocator;
    private readonly IMongoProcessFactory _processFactory;
    private readonly MongoRunnerOptions _options;

    private IMongoProcess? _process;
    private string? _dataDirectory;

    private MongoRunner(IFileSystem fileSystem, IPortFactory portFactory, IMongoExecutableLocator executableLocator, IMongoProcessFactory processFactory, MongoRunnerOptions options)
    {
        _fileSystem = fileSystem;
        _portFactory = portFactory;
        _executableLocator = executableLocator;
        _processFactory = processFactory;
        _options = options;
    }

    public static IMongoRunner Run(MongoRunnerOptions? options = null)
    {
        var optionsCopy = options == null ? new MongoRunnerOptions() : new MongoRunnerOptions(options);
        var runner = new MongoRunner(new FileSystem(), new PortFactory(), new MongoExecutableLocator(), new MongoProcessFactory(), optionsCopy);
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
            _dataDirectory = _options.DataDirectory ?? Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _fileSystem.CreateDirectory(_dataDirectory);

            try
            {
                // ...and has no existing MongoDB lock file
                // https://stackoverflow.com/a/6857973/825695
                var lockFilePath = Path.Combine(_dataDirectory, "mongod.lock");
                _fileSystem.DeleteFile(lockFilePath);
            }
            catch
            {
                // Ignored - this data directory might already be in use, we'll see later how mongod reacts
            }

            _options.MongoPort ??= _portFactory.GetRandomAvailablePort();

            // Build MongoDB executable arguments
            var arguments = string.Format(CultureInfo.InvariantCulture, "--dbpath {0} --port {1} --bind_ip 127.0.0.1", ProcessArgument.Escape(_dataDirectory), _options.MongoPort);
            arguments += RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? string.Empty : " --tlsMode disabled";
            arguments += _options.UseSingleNodeReplicaSet ? " --replSet " + _options.ReplicaSetName : string.Empty;
            arguments += string.IsNullOrWhiteSpace(_options.AdditionalArguments) ? string.Empty : " " + _options.AdditionalArguments;

            _process = _processFactory.CreateMongoProcess(_options, MongoProcessKind.Mongod, executablePath, arguments);
            _process.Start();

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

    private void Dispose(bool throwOnException)
    {
        var exceptions = new List<Exception>(1);

        try
        {
            _process?.Dispose();
        }
        catch (Exception ex)
        {
            exceptions.Add(ex);
        }

        try
        {
            // Do not dispose data directory if it was a user input
            if (_dataDirectory != null && _options.DataDirectory == null)
            {
                _fileSystem.DeleteDirectory(_dataDirectory);
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
        private readonly MongoRunner _runner;
        private int _isDisposed;

        public StartedMongoRunner(MongoRunner runner, string connectionString)
        {
            _runner = runner;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1117:Parameters should be on same line or separate lines", Justification = "Would have used too many lines, and this way string.Format is still very readable")]
        public void Import(string database, string collection, string inputFilePath, string? additionalArguments = null, bool drop = false)
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) == 1)
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

            var executablePath = _runner._executableLocator.FindMongoExecutablePath(_runner._options, MongoProcessKind.MongoImport);
            _runner._fileSystem.MakeFileExecutable(executablePath);

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                @"--uri=""{0}"" --db={1} --collection={2} --file={3} {4} {5}",
                ConnectionString, database, collection, ProcessArgument.Escape(inputFilePath), drop ? " --drop" : string.Empty, additionalArguments ?? string.Empty);

            using var process = _runner._processFactory.CreateMongoProcess(_runner._options, MongoProcessKind.MongoImport, executablePath, arguments);
            process.Start();
        }

        [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1117:Parameters should be on same line or separate lines", Justification = "Would have used too many lines, and this way string.Format is still very readable")]
        public void Export(string database, string collection, string outputFilePath, string? additionalArguments = null)
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) == 1)
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

            var executablePath = _runner._executableLocator.FindMongoExecutablePath(_runner._options, MongoProcessKind.MongoExport);
            _runner._fileSystem.MakeFileExecutable(executablePath);

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                @"--uri=""{0}"" --db={1} --collection={2} --out={3} {4}",
                ConnectionString, database, collection, ProcessArgument.Escape(outputFilePath), additionalArguments ?? string.Empty);

            using var process = _runner._processFactory.CreateMongoProcess(_runner._options, MongoProcessKind.MongoExport, executablePath, arguments);
            process.Start();
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
            {
                TryShutdownQuietly();
                _runner.Dispose(throwOnException: true);
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
                // FYI this is the expected behavior as mongod is shutting down
            }
            catch
            {
                // Ignore other exceptions as well, we'll kill the process anyway
            }
        }
    }
}