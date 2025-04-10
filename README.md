# MongoSandbox - temporary and disposable MongoDB for integration tests and local debugging

[![build](https://img.shields.io/github/actions/workflow/status/wassim-k/MongoSandbox/release.yml?logo=github)](https://github.com/wassim-k/MongoSandbox/actions/workflows/release.yml)

## Introduction

**MongoSandbox** is a set of multiple NuGet packages wrapping the binaries of **MongoDB 6, 7,** and **8**.
Each package is compatible with **.NET Framework 4.7.2** up to **.NET 8 and later**.

The supported operating systems are **Linux** (x64), **macOS** (arm64), and **Windows** (x64).
Each package provides access to:

* Multiple isolated MongoDB sandbox databases for tests running,
* A quick way to set up a MongoDB database for a local development environment,
* _mongoexport_ and _mongoimport_ tools for [exporting](https://www.mongodb.com/docs/database-tools/mongoexport/) and [importing](https://docs.mongodb.com/database-tools/mongoimport/) collections,
* Support for **single-node replica sets**, enabling transactions and change streams.

This project is an actively maintained fork of [ephemeral-mongo](https://github.com/asimmon/ephemeral-mongo).

## Usage

Add `MongoSandbox.Core` package reference to your project:

```xml
<PackageReference Include="MongoSandbox.Core" Version="*" />
```

Add one or more target runtime packages to your project:

```xml
<PackageReference Include="MongoSandbox8.runtime.linux-x64" Version="*" Condition="$([MSBuild]::IsOSPlatform('Linux'))" />
<PackageReference Include="MongoSandbox8.runtime.osx-arm64" Version="*" Condition="$([MSBuild]::IsOSPlatform('OSX'))" />
<PackageReference Include="MongoSandbox8.runtime.win-x64" Version="*" Condition="$([MSBuild]::IsOSPlatform('Windows'))" />
```

Use the static `MongoRunner.Run()` method to create a disposable instance that provides access to a **MongoDB connection string**, **import**, and **export tools**:

```csharp
// All properties below are optional. The whole "options" instance is optional too!
var options = new MongoRunnerOptions
{
    UseSingleNodeReplicaSet = true, // Default: false
    StandardOutputLogger = line => Console.WriteLine(line), // Default: null
    StandardErrorLogger = line => Console.WriteLine(line), // Default: null
    DataDirectory = "/path/to/data/", // Default: null
    BinaryDirectory = "/path/to/mongo/bin/", // Default: null
    ConnectionTimeout = TimeSpan.FromSeconds(10), // Default: 30 seconds
    ReplicaSetSetupTimeout = TimeSpan.FromSeconds(5), // Default: 10 seconds
    AdditionalArguments = "--quiet", // Default: null
    MongoPort = 27017, // Default: random available port
    DataDirectoryLifetime = TimeSpan.FromDays(1), // Default: 12 hours
};

// Disposing the runner will kill the MongoDB process (mongod) and delete the associated data directory
using (var runner = MongoRunner.Run(options))
{
    var database = new MongoClient(runner.ConnectionString).GetDatabase("default");

    // Do something with the database
    database.CreateCollection("people");

    // Export a collection. Full method signature:
    // Export(string database, string collection, string outputFilePath, string? additionalArguments = null)
    runner.Export("default", "people", "/path/to/default.json");

    // Import a collection. Full method signature:
    // Import(string database, string collection, string inputFilePath, string? additionalArguments = null, bool drop = false)
    runner.Import("default", "people", "/path/to/default.json");
}
```

## How it works

* At build time, the MongoDB binaries (`mongod`, `mongoimport`, and `mongoexport`) are copied to your project output directory,
* At runtime, the library chooses the right binaries for your operating system,
* `MongoRunner.Run` always starts a new `mongod` process with a random available port,
* The resulting connection string will depend on your options (`UseSingleNodeReplicaSet` and `AdditionalArguments`),
* By default, a unique temporary data directory is used.

## Windows Defender Firewall prompt

On Windows, you might get a **Windows Defender Firewall prompt**.
This is because MongoSandbox starts the `mongod.exe` process from your build output directory, and `mongod.exe` tries to [open an available port](https://github.com/wassim-k/MongoSandbox/blob/main/src/MongoSandbox.Core/MongoRunner.cs#L64).

## Optimization tips

Avoid calling `MongoRunner.Run` concurrently, as this will create many `mongod` processes and make your operating system slower.
Instead, try to use a single instance and reuse it - create as many databases as you need, one per test, for example.

## Changelog

### 2.0.0

- **Breaking change**: MongoSandbox6, 7, and 8 (All-in-one) packages have been removed, directly referencing target runtime packages is now required.
- **Breaking change**: Support for MongoDB 5.0 has been removed, as its [end-of-life](https://www.mongodb.com/legal/support-policy/lifecycles) has passed.
- **Breaking change**: arm64 is now the default target for macOS. The previous target was x64.
- **Breaking change**: The Linux runtime package now uses Ubuntu 22.04's MongoDB binaries instead of the 18.04 ones. OpenSSL 3.0 is now required.
- Introduced data directory management to delete old data directories automatically.
