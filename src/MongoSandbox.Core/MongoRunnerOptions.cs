namespace MongoSandbox;

public sealed class MongoRunnerOptions
{
    private string? _dataDirectory;
    private string? _binaryDirectory;
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);
    private TimeSpan _replicaSetSetupTimeout = TimeSpan.FromSeconds(10);
    private int? _mongoPort;
    private TimeSpan _dataDirectoryLifetime = TimeSpan.FromHours(12);

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
        _dataDirectoryLifetime = options._dataDirectoryLifetime;

        AdditionalArguments = options.AdditionalArguments;
        UseSingleNodeReplicaSet = options.UseSingleNodeReplicaSet;
        StandardOutputLogger = options.StandardOutputLogger;
        StandardErrorLogger = options.StandardErrorLogger;
        ReplicaSetName = options.ReplicaSetName;
        RootDataDirectoryPath = options.RootDataDirectoryPath;
    }

    /// <summary>
    /// Gets or sets the directory where the mongod instance stores its data. If not specified, a temporary directory will be used.
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    /// <seealso href="https://www.mongodb.com/docs/manual/reference/program/mongod/#std-option-mongod.--dbpath"/>
    public string? DataDirectory
    {
        get => _dataDirectory;
        set => _dataDirectory = CheckDirectoryPathFormat(value) is { } ex ? throw new ArgumentException(nameof(DataDirectory), ex) : value;
    }

    /// <summary>
    /// Gets or sets the directory where your own MongoDB binaries can be found (mongod, mongoexport and mongoimport).
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    public string? BinaryDirectory
    {
        get => _binaryDirectory;
        set => _binaryDirectory = CheckDirectoryPathFormat(value) is { } ex ? throw new ArgumentException(nameof(BinaryDirectory), ex) : value;
    }

    /// <summary>
    /// Gets or sets additional mongod CLI arguments.
    /// </summary>
    /// <seealso href="https://www.mongodb.com/docs/manual/reference/program/mongod/#options"/>
    public string? AdditionalArguments { get; set; }

    /// <summary>
    /// Gets or sets maximum timespan to wait for mongod process to be ready to accept connections.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout cannot be negative.</exception>
    public TimeSpan ConnectionTimeout
    {
        get => _connectionTimeout;
        set => _connectionTimeout = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(ConnectionTimeout));
    }

    /// <summary>
    /// Gets or sets a value indicating whether whether to create a single node replica set or use a standalone mongod instance.
    /// </summary>
    public bool UseSingleNodeReplicaSet { get; set; }

    /// <summary>
    /// Gets or sets maximum timespan to wait for the replica set to accept database writes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout cannot be negative.</exception>
    public TimeSpan ReplicaSetSetupTimeout
    {
        get => _replicaSetSetupTimeout;
        set => _replicaSetSetupTimeout = value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(ReplicaSetSetupTimeout));
    }

    /// <summary>
    /// Gets or sets a delegate that provides access to any MongodDB-related process standard output.
    /// </summary>
    public Logger? StandardOutputLogger { get; set; }

    /// <summary>
    /// Gets or sets a delegate that provides access to any MongodDB-related process error output.
    /// </summary>
    public Logger? StandardErrorLogger { get; set; }

    /// <summary>
    /// Gets or sets the mongod port to use. If not specified, a random available port will be used.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The port must be greater than zero.</exception>
    public int? MongoPort
    {
        get => _mongoPort;
        set => _mongoPort = value is not <= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MongoPort));
    }

    /// <summary>
    /// Gets or sets the lifetime of data directories that are automatically created when they are not specified by the user.
    /// When their age exceeds this value, they will be deleted on the next run of MongoRunner.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime cannot be negative.</exception>
    public TimeSpan? DataDirectoryLifetime
    {
        get => _dataDirectoryLifetime;
        set => _dataDirectoryLifetime = value is not null && value >= TimeSpan.Zero ? value.Value : throw new ArgumentOutOfRangeException(nameof(DataDirectoryLifetime));
    }

    // Internal properties start here
    internal string ReplicaSetName { get; set; } = "singleNodeReplSet";

    // Useful for testing data directories cleanup
    internal string? RootDataDirectoryPath { get; set; }

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