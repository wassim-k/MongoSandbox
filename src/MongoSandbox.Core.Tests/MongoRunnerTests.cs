using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace MongoSandbox.Core.Tests;

public class MongoRunnerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public MongoRunnerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void RunFailsWhenBinaryDirectoryDoesNotExist()
    {
        var options = new MongoRunnerOptions
        {
            StandardOutputLogger = MongoMessageLogger,
            StandardErrorLogger = MongoMessageLogger,
            BinaryDirectory = Guid.NewGuid().ToString(),
            AdditionalArguments = "--quiet",
        };

        IMongoRunner? runner = null;

        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => runner = MongoRunner.Run(options));
            Assert.Contains(options.BinaryDirectory, ex.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("runtimes", ex.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            runner?.Dispose();
        }
    }

    [Fact]
    public async Task RunCleansUpTemporaryDataDirectory()
    {
        var rootDataDirectoryPath = Path.Combine(Path.GetTempPath(), "mongo-sandbox-data-cleanup-tests");

        try
        {
            // Start with a clean slate
            Directory.Delete(rootDataDirectoryPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }

        var options = new MongoRunnerOptions
        {
            StandardOutputLogger = MongoMessageLogger,
            StandardErrorLogger = MongoMessageLogger,
            RootDataDirectoryPath = rootDataDirectoryPath,
            AdditionalArguments = "--quiet",
        };

        _testOutputHelper.WriteLine($"Root data directory path: {options.RootDataDirectoryPath}");
        Assert.False(Directory.Exists(options.RootDataDirectoryPath), "The root data directory should not exist yet.");

        // Creating a first data directory
        using (MongoRunner.Run(options))
        {
        }

        // Creating another data directory
        using (MongoRunner.Run(options))
        {
        }

        // Assert there's now two data directories
        var dataDirectories = new HashSet<string>(Directory.EnumerateDirectories(options.RootDataDirectoryPath), StringComparer.Ordinal);
        _testOutputHelper.WriteLine($"Data directories: {string.Join(", ", dataDirectories)}");
        Assert.Equal(2, dataDirectories.Count);

        // Shorten the lifetime of the data directories and wait for a longer time
        options.DataDirectoryLifetime = TimeSpan.FromSeconds(1);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // This should delete the old data directories and create a new one
        using (MongoRunner.Run(options))
        {
        }

        var dataDirectoriesAfterCleanup = new HashSet<string>(Directory.EnumerateDirectories(options.RootDataDirectoryPath), StringComparer.Ordinal);
        _testOutputHelper.WriteLine($"Data directories after cleanup: {string.Join(", ", dataDirectoriesAfterCleanup)}");

        var thirdDataDirectory = Assert.Single(dataDirectoriesAfterCleanup);
        Assert.DoesNotContain(thirdDataDirectory, dataDirectories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportExportWorks(bool useSingleNodeReplicaSet)
    {
        const string databaseName = "default";
        const string collectionName = "people";

        var options = new MongoRunnerOptions
        {
            UseSingleNodeReplicaSet = useSingleNodeReplicaSet,
            StandardOutputLogger = MongoMessageLogger,
            StandardErrorLogger = MongoMessageLogger,
            AdditionalArguments = "--quiet",
        };

        using (var runner = MongoRunner.Run(options))
        {
            if (useSingleNodeReplicaSet)
            {
                Assert.Contains("replicaSet", runner.ConnectionString, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("replicaSet", runner.ConnectionString, StringComparison.Ordinal);
            }
        }

        var originalPerson = new Person("john", "John Doe");
        var exportedFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            using (var runner1 = MongoRunner.Run(options))
            {
                var database = new MongoClient(runner1.ConnectionString).GetDatabase(databaseName);

                // Verify that the collection is empty
                var personBeforeImport = database.GetCollection<Person>(collectionName).Find(FilterDefinition<Person>.Empty).FirstOrDefault();
                Assert.Null(personBeforeImport);

                // Add a document
                database.GetCollection<Person>(collectionName).InsertOne(new Person(originalPerson.Id, originalPerson.Name));
                runner1.Export(databaseName, collectionName, exportedFilePath);

                // Verify that the document was inserted successfully
                var personAfterImport = database.GetCollection<Person>(collectionName).Find(FilterDefinition<Person>.Empty).FirstOrDefault();
                Assert.Equal(originalPerson, personAfterImport);
            }

            IMongoRunner runner2;
            using (runner2 = MongoRunner.Run(options))
            {
                var database = new MongoClient(runner2.ConnectionString).GetDatabase(databaseName);

                // Verify that the collection is empty
                var personBeforeImport = database.GetCollection<Person>(collectionName).Find(FilterDefinition<Person>.Empty).FirstOrDefault();
                Assert.Null(personBeforeImport);

                // Import the exported collection
                runner2.Import(databaseName, collectionName, exportedFilePath);

                // Verify that the document was imported successfully
                var personAfterImport = database.GetCollection<Person>(collectionName).Find(FilterDefinition<Person>.Empty).FirstOrDefault();
                Assert.Equal(originalPerson, personAfterImport);
            }

            // Disposing twice does nothing
            runner2.Dispose();

            // Can't use import or export if already disposed
            Assert.Throws<ObjectDisposedException>(() => runner2.Export("whatever", "whatever", "whatever.json"));
            Assert.Throws<ObjectDisposedException>(() => runner2.Import("whatever", "whatever", "whatever.json"));
        }
        finally
        {
            File.Delete(exportedFilePath);
        }
    }

    private void MongoMessageLogger(string message)
    {
        try
        {
            var trace = JsonSerializer.Deserialize<MongoTrace>(message);

            if (trace != null && !string.IsNullOrEmpty(trace.Message))
            {
                // https://www.mongodb.com/docs/manual/reference/log-messages/#std-label-log-severity-levels
                var logLevel = trace.Severity switch
                {
                    "F" => LogLevel.Critical,
                    "E" => LogLevel.Error,
                    "W" => LogLevel.Warning,
                    _ => LogLevel.Information,
                };

                const int longestComponentNameLength = 8;
                _testOutputHelper.WriteLine($"{trace.Component,-longestComponentNameLength} {trace.Message}");
                return;
            }
        }
        catch (JsonException)
        {
        }

        _testOutputHelper.WriteLine(message);
    }

    private sealed class MongoTrace
    {
        [JsonPropertyName("s")]
        public string Severity { get; set; } = string.Empty;

        [JsonPropertyName("c")]
        public string Component { get; set; } = string.Empty;

        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed record Person(string Id, string Name)
    {
        public Person()
            : this(string.Empty, string.Empty)
        {
        }
    }
}