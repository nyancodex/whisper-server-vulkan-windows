using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WhisperLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
            return SelfTest().GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    /// Headless smoke test of WhisperDeployer. Used by CI/dev to verify the
    /// extract -> start -> transcribe flow without driving the GUI. Requires
    /// a pre-staged model under <InstallDir>/models/ since downloading 500+ MB
    /// in a test is wasteful.
    private static async Task<int> SelfTest()
    {
        // Allocate a console for output when running from a Windows GUI subsystem build.
        AllocConsoleIfNeeded();

        var exeDir = AppContext.BaseDirectory;
        var bundleZip = Path.Combine(exeDir, "whisper-server-bundle.zip");
        if (!File.Exists(bundleZip))
        {
            Console.Error.WriteLine($"Bundle zip not at {bundleZip}");
            return 1;
        }

        var installDir = Environment.GetEnvironmentVariable("WHISPER_TEST_DIR")
                         ?? Path.Combine(Path.GetTempPath(), "whisper-launcher-selftest");
        if (Directory.Exists(installDir)) Directory.Delete(installDir, true);

        var model = Environment.GetEnvironmentVariable("WHISPER_TEST_MODEL")
                    ?? WhisperModel.DefaultFile;
        var cachedModel = Environment.GetEnvironmentVariable("WHISPER_TEST_MODEL_PATH");

        var d = new WhisperDeployer(bundleZip, installDir, "127.0.0.1", 8095,
                                    "/v1/audio/transcriptions");
        d.LogLine  += s => Console.WriteLine($"LOG: {s}");
        d.Progress += (p, l) => Console.WriteLine($"PROG {p}%: {l}");

        d.ExtractBundle();

        if (cachedModel != null && File.Exists(cachedModel))
        {
            File.Copy(cachedModel, Path.Combine(d.ModelsDir, model));
            Console.WriteLine("Used cached model.");
        }
        else if (!d.ModelPresent(model))
        {
            var modelObj = Array.Find(WhisperModel.Choices, m => m.File == model);
            if (modelObj == null) { Console.Error.WriteLine($"Unknown model {model}"); return 4; }
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            await d.DownloadModelAsync(modelObj, cts.Token);
        }

        d.StartServer(model);
        using var rcts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ready = await d.WaitForReadyAsync(60_000, rcts.Token);
        if (!ready) { d.StopServer(); Console.Error.WriteLine("not ready"); return 2; }

        var resp = await d.TranscribeAsync(d.SampleWav, "json", rcts.Token);
        Console.WriteLine($"RESPONSE: {resp}");
        d.StopServer();

        var ok = resp.Contains("ask not what your country");
        Console.WriteLine(ok ? "SELFTEST PASSED" : "SELFTEST FAILED");
        return ok ? 0 : 3;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    private static void AllocConsoleIfNeeded()
    {
        if (GetConsoleWindow() == IntPtr.Zero) AllocConsole();
    }
}
