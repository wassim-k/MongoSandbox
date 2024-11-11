namespace MongoSandbox;

internal interface IMongoProcess : IDisposable
{
    void Start();
}