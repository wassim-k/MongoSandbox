using System.Diagnostics;
using System.Globalization;
using MongoDB.Driver;

namespace MongoSandbox;

internal sealed class MongodProcess : BaseMongoProcess
{
    private const string ConnectionReadySentence = "waiting for connections";
    private readonly ManualResetEventSlim _connectionReadyEvent;

    public MongodProcess(MongoRunnerOptions options, string executablePath, string arguments)
        : base(options, executablePath, arguments)
    {
        _connectionReadyEvent = new ManualResetEventSlim(false);
    }

    public override void Start()
    {
        try
        {
            StartAndWaitForConnectionReadiness();

            if (Options.UseSingleNodeReplicaSet)
            {
                ConfigureAndWaitForReplicaSetReadiness();
            }
        }
        finally
        {
            _connectionReadyEvent.Dispose();
        }
    }

    private void StartAndWaitForConnectionReadiness()
    {
        void OnOutputDataReceivedForConnectionReadiness(object sender, DataReceivedEventArgs args)
        {
#pragma warning disable CA2249
            if (args.Data != null && args.Data.IndexOf(ConnectionReadySentence, StringComparison.OrdinalIgnoreCase) >= 0)
#pragma warning restore CA2249
            {
                _connectionReadyEvent.Set();
            }
        }

        Process.OutputDataReceived += OnOutputDataReceivedForConnectionReadiness;

        try
        {
            Process.Start();
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();

            var isReadyToAcceptConnections = _connectionReadyEvent.Wait(Options.ConnectionTimeout);

            if (!isReadyToAcceptConnections)
            {
                throw new TimeoutException(string.Format(
                    CultureInfo.InvariantCulture,
                    "MongoDB connection availability took longer than the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                    Options.ConnectionTimeout.TotalSeconds,
                    nameof(Options.ConnectionTimeout)));
            }
        }
        finally
        {
            Process.OutputDataReceived -= OnOutputDataReceivedForConnectionReadiness;
        }
    }

    private void ConfigureAndWaitForReplicaSetReadiness()
    {
        try
        {
            var initializer = new ReplicaSetInitializer(Options);
            initializer.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Options.StandardErrorLogger?.Invoke($"Failed to initialize replica set: {ex.Message}");

            throw new MongoConfigurationException(
                "Failed to initialize MongoDB replica set. Check the error logs for details.", ex);
        }
    }

    public override void Dispose()
    {
        _connectionReadyEvent.Dispose();
        base.Dispose();
    }
}