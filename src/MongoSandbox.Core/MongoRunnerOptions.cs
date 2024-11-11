namespace MongoSandbox;

public sealed class MongoRunnerOptions
{
    private string? _dataDirectory;
    private string? _binaryDirectory;
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);
    private TimeSpan _replicaSetSetupTimeout = TimeSpan.FromSeconds(10);
    private int? _mongoPort;

    public MongoRunnerOptions()
    {
    }

    public MongoRunnerOptions(MongoRunnerOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _dataDirectory = options._dataDirectory;
        _binaryDirectory = options._binaryDirectory;
        _connectionTimeout = options._connectionTimeout;
        _replicaSetSetupTimeout = options._replicaSetSetupTimeout;
        _mongoPort = options._mongoPort;

        AdditionalArguments = options.AdditionalArguments;
        UseSingleNodeReplicaSet = options.UseSingleNodeReplicaSet;
        StandardOuputLogger = options.StandardOuputLogger;
        StandardErrorLogger = options.StandardErrorLogger;
        ReplicaSetName = options.ReplicaSetName;
        KillMongoProcessesWhenCurrentProcessExits = options.KillMongoProcessesWhenCurrentProcessExits;
    }

    /// <summary>
    /// The directory where the mongod instance stores its data. If not specified, a temporary directory will be used.
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    /// <seealso cref="https://www.mongodb.com/docs/manual/reference/program/mongod/#std-option-mongod.--dbpath"/>
    public string? DataDirectory
    {
        get => _dataDirectory;
        set => _dataDirectory = CheckDirectoryPathFormat(value) is { } ex ? throw new ArgumentException(nameof(DataDirectory), ex) : value;
    }

    /// <summary>
    /// The directory where your own MongoDB binaries can be found (mongod, mongoexport and mongoimport).
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    public string? BinaryDirectory
    {
        get => _binaryDirectory;
        set => _binaryDirectory = CheckDirectoryPathFormat(value) is { } ex ? throw new ArgumentException(nameof(BinaryDirectory), ex) : value;
    }

    /// <summary>
    /// Additional mongod CLI arguments.
    /// </summary>
    /// <seealso cref="https://www.mongodb.com/docs/manual/reference/program/mongod/#options"/>
    public string? AdditionalArguments { get; set; }

    /// <summary>
    /// Maximum timespan to wait for mongod process to be ready to accept connections.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout cannot be negative.</exception>
    public TimeSpan ConnectionTimeout
    {
        get => _connectionTimeout;
        set => _connectionTimeout = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(ConnectionTimeout));
    }

    /// <summary>
    /// Whether to create a single node replica set or use a standalone mongod instance.
    /// </summary>
    public bool UseSingleNodeReplicaSet { get; set; }

    /// <summary>
    /// Maximum timespan to wait for the replica set to accept database writes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout cannot be negative.</exception>
    public TimeSpan ReplicaSetSetupTimeout
    {
        get => _replicaSetSetupTimeout;
        set => _replicaSetSetupTimeout = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(ReplicaSetSetupTimeout));
    }

    /// <summary>
    /// A delegate that provides access to any MongodDB-related process standard output.
    /// </summary>
    public Logger? StandardOuputLogger { get; set; }

    /// <summary>
    /// A delegate that provides access to any MongodDB-related process error output.
    /// </summary>
    public Logger? StandardErrorLogger { get; set; }

    /// <summary>
    /// The mongod port to use. If not specified, a random available port will be used.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The port must be greater than zero.</exception>
    public int? MongoPort
    {
        get => _mongoPort;
        set => _mongoPort = value is not <= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MongoPort));
    }

    /// <summary>
    /// EXPERIMENTAL - Only works on Windows and modern .NET (netcoreapp3.1, net5.0, net6.0, net7.0 and so on):
    /// Ensures that all MongoDB child processes are killed when the current process is prematurely killed,
    /// for instance when killed from the task manager or the IDE unit tests window.
    /// </summary>
    public bool KillMongoProcessesWhenCurrentProcessExits { get; set; }

    // Internal properties start here
    internal string ReplicaSetName { get; set; } = "singleNodeReplSet";

    private static Exception? CheckDirectoryPathFormat(string? path)
    {
        if (path == null)
        {
            return new ArgumentNullException(nameof(path));
        }

        try
        {
            _ = new DirectoryInfo(path);
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }
}