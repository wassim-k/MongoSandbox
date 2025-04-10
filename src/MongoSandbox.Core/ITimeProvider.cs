namespace MongoSandbox;

internal interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}