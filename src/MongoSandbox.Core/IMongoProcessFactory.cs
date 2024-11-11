﻿namespace MongoSandbox;

internal interface IMongoProcessFactory
{
    IMongoProcess CreateMongoProcess(MongoRunnerOptions options, MongoProcessKind processKind, string executablePath, string arguments);
}