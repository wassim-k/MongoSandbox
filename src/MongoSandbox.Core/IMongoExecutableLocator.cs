namespace MongoSandbox;

internal interface IMongoExecutableLocator
{
    string FindMongoExecutablePath(MongoRunnerOptions options, MongoProcessKind processKind);
}