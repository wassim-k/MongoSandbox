using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;

namespace MongoSandbox;

internal sealed class ReplicaSetInitializer
{
    private readonly MongoRunnerOptions _options;
    private readonly TaskCompletionSource<bool> _replicaSetReadyTcs;
    private readonly TaskCompletionSource<bool> _transactionReadyTcs;

    public ReplicaSetInitializer(MongoRunnerOptions options)
    {
        _options = options;
        _replicaSetReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transactionReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task InitializeAsync()
    {
        await InitializeReplicaSetConfigAsync();
        await WaitForReplicaSetReadinessAsync();
        await WaitForTransactionReadinessAsync();
    }

    private async Task InitializeReplicaSetConfigAsync()
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
            await admin.RunCommandAsync<BsonDocument>(command, cancellationToken: cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _options.StandardErrorLogger?.Invoke("An error occurred while initializing the replica set: " + ex);
            throw;
        }
    }

    private async Task WaitForReplicaSetReadinessAsync()
    {
        using var cts = new CancellationTokenSource(_options.ReplicaSetSetupTimeout);
        using var registration = cts.Token.Register(() =>
            _replicaSetReadyTcs.TrySetException(new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "Replica set initialization took longer than the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                _options.ReplicaSetSetupTimeout.TotalSeconds,
                nameof(_options.ReplicaSetSetupTimeout)))));

        await _replicaSetReadyTcs.Task;
    }

    private async Task WaitForTransactionReadinessAsync()
    {
        using var cts = new CancellationTokenSource(_options.ReplicaSetSetupTimeout);
        using var registration = cts.Token.Register(() =>
            _transactionReadyTcs.TrySetException(new TimeoutException(string.Format(
                CultureInfo.InvariantCulture,
                "Cluster readiness for transactions took longer than the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                _options.ReplicaSetSetupTimeout.TotalSeconds,
                nameof(_options.ReplicaSetSetupTimeout)))));

        await _transactionReadyTcs.Task;
    }

    private void OnClusterDescriptionChanged(ClusterDescriptionChangedEvent @event)
    {
        if (@event.NewDescription.Servers.Any(s => s.Type == ServerType.ReplicaSetPrimary && s.State == ServerState.Connected))
        {
            _replicaSetReadyTcs.TrySetResult(true);
        }

        if (@event.NewDescription.Servers.Any(server => server.State == ServerState.Connected && server.IsDataBearing))
        {
            _transactionReadyTcs.TrySetResult(true);
        }
    }
}