namespace MongoSandbox;

internal sealed class MongoImportExportProcess : BaseMongoProcess
{
    public MongoImportExportProcess(MongoRunnerOptions options, string executablePath, string arguments)
        : base(options, executablePath, arguments)
    {
    }

    public override void Start()
    {
        Process.Start();

        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();

        // Wait for the end of import or export
        Process.WaitForExit();
    }
}