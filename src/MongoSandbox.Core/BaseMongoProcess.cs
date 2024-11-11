using System.Diagnostics;

namespace MongoSandbox;

internal abstract class BaseMongoProcess : IMongoProcess
{
    protected BaseMongoProcess(MongoRunnerOptions options, string executablePath, string arguments)
    {
        Options = options;

        if (options.KillMongoProcessesWhenCurrentProcessExits)
        {
            NativeMethods.EnsureMongoProcessesAreKilledWhenCurrentProcessIsKilled();
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process = new Process
        {
            StartInfo = processStartInfo,
        };

        Process.OutputDataReceived += OnOutputDataReceivedForLogging;
        Process.ErrorDataReceived += OnErrorDataReceivedForLogging;
    }

    protected MongoRunnerOptions Options { get; }

    protected Process Process { get; }

    private void OnOutputDataReceivedForLogging(object sender, DataReceivedEventArgs args)
    {
        if (Options.StandardOuputLogger != null && args.Data != null)
        {
            Options.StandardOuputLogger(args.Data);
        }
    }

    private void OnErrorDataReceivedForLogging(object sender, DataReceivedEventArgs args)
    {
        if (Options.StandardErrorLogger != null && args.Data != null)
        {
            Options.StandardErrorLogger(args.Data);
        }
    }

    public abstract void Start();

    public virtual void Dispose()
    {
        Process.OutputDataReceived -= OnOutputDataReceivedForLogging;
        Process.ErrorDataReceived -= OnErrorDataReceivedForLogging;

        Process.CancelOutputRead();
        Process.CancelErrorRead();

        if (!Process.HasExited)
        {
            try
            {
                Process.Kill();
                Process.WaitForExit();
            }
            catch
            {
                // ignored, we did our best to stop the process
            }
        }

        Process.Dispose();
    }
}