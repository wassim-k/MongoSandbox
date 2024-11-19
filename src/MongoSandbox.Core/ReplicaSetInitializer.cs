using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;

namespace MongoSandbox;

internal sealed class ReplicaSetInitializer : IDisposable
{
    private readonly MongoRunnerOptions _options;
    private readonly ManualResetEventSlim _replicaSetReadyEvent;
    private readonly ManualResetEventSlim _transactionReadyEvent;

    public ReplicaSetInitializer(MongoRunnerOptions options)
    {
        _options = options;
        _replicaSetReadyEvent = new ManualResetEventSlim(false);
        _transactionReadyEvent = new ManualResetEventSlim(false);
    }

    public void Initialize()
    {
        InitializeReplicaSetConfig();
        WaitForReplicaSetReadiness();
        WaitForTransactionReadiness();
    }

    private void InitializeReplicaSetConfig()
    {
        try
        {
            var settings = new MongoClientSettings
            {
                Server = new MongoServerAddress("127.0.0.1", _options.MongoPort!.Value),
                ReplicaSetName = _options.ReplicaSetName,
                DirectConnection = true,
                ClusterConfigurator = cb => cb.Subscribe<ClusterDescriptionChangedEvent>(OnClusterDescriptionChanged),
            };

            using var client = new MongoClient(settings);
            var admin = client.GetDatabase("admin");

            var replConfig = new BsonDocument(new List<BsonElement>
            {
                new BsonElement("_id", _options.ReplicaSetName),
                new BsonElement("members", new BsonArray
                {
                    new BsonDocument { { "_id", 0 }, { "host", string.Format(CultureInfo.InvariantCulture, "127.0.0.1:{0}", _options.MongoPort) } },
                }),
            });

            using var cts = new CancellationTokenSource(_options.ReplicaSetSetupTimeout);
            var command = new BsonDocument("replSetInitiate", replConfig);
            admin.RunCommand<BsonDocument>(command, cancellationToken: cts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _options.StandardErrorLogger?.Invoke("An error occurred while initializing the replica set: " + ex);
            throw;
        }
    }

    private void WaitForReplicaSetReadiness()
    {
        if (!_replicaSetReadyEvent.Wait(_options.ReplicaSetSetupTimeout))
        {
            throw new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "Replica set initialization took longer than the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                _options.ReplicaSetSetupTimeout.TotalSeconds,
                nameof(_options.ReplicaSetSetupTimeout)));
        }
    }

    private void WaitForTransactionReadiness()
    {
        if (!_transactionReadyEvent.Wait(_options.ReplicaSetSetupTimeout))
        {
            throw new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "Cluster readiness for transactions took longer than the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                _options.ReplicaSetSetupTimeout.TotalSeconds,
                nameof(_options.ReplicaSetSetupTimeout)));
        }
    }

    private void OnClusterDescriptionChanged(ClusterDescriptionChangedEvent @event)
    {
        if (@event.NewDescription.Servers.Any(s => s.Type == ServerType.ReplicaSetPrimary && s.State == ServerState.Connected))
        {
            _replicaSetReadyEvent.Set();
        }

        if (@event.NewDescription.Servers.Any(server => server.State == ServerState.Connected && server.IsDataBearing))
        {
            _transactionReadyEvent.Set();
        }
    }

    public void Dispose()
    {
        _replicaSetReadyEvent.Dispose();
        _transactionReadyEvent.Dispose();
    }
}
