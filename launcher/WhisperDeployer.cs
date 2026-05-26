using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperLauncher;

/// Encapsulates the deploy steps: extract the bundle zip, download a model,
/// launch whisper-server.exe, and poll its readiness. Pure logic + events --
/// no UI dependencies, so MainForm just wires events to controls.
public sealed class WhisperDeployer
{
    public string BundleZipPath { get; }
    public string InstallDir    { get; }
    public string BindHost      { get; }
    public int    Port          { get; }
    public string InferencePath { get; }

    public event Action<string>?       LogLine;
    public event Action<int, string>?  Progress; // 0-100, label

    private Process? _serverProc;

    public WhisperDeployer(string bundleZip, string installDir, string bindHost, int port,
                           string inferencePath)
    {
        BundleZipPath = bundleZip;
        InstallDir    = installDir;
        BindHost      = bindHost;
        Port          = port;
        InferencePath = inferencePath;
    }

    public string ServerExePath => Path.Combine(InstallDir, "whisper-server.exe");
    public string ModelsDir     => Path.Combine(InstallDir, "models");
    public string SampleWav     => Path.Combine(InstallDir, "samples", "jfk.wav");

    public string EndpointUrl =>
        $"http://{(BindHost == "0.0.0.0" ? "localhost" : BindHost)}:{Port}{InferencePath}";

    public bool IsRunning => _serverProc is { HasExited: false };

    public bool VulkanPresent() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "vulkan-1.dll"));

    public void ExtractBundle()
    {
        if (!File.Exists(BundleZipPath))
            throw new FileNotFoundException(
                $"Bundle zip not found at {BundleZipPath}. Place whisper-server-bundle.zip next to the launcher.");

        LogLine?.Invoke($"Extracting {BundleZipPath} -> {InstallDir}");
        if (Directory.Exists(InstallDir))
            Directory.Delete(InstallDir, recursive: true);
        Directory.CreateDirectory(InstallDir);
        ZipFile.ExtractToDirectory(BundleZipPath, InstallDir, overwriteFiles: true);
        Directory.CreateDirectory(ModelsDir);
        LogLine?.Invoke("Extracted.");
    }

    public bool ModelPresent(string modelFile) =>
        File.Exists(Path.Combine(ModelsDir, modelFile));

    public async Task DownloadModelAsync(WhisperModel model, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        var dst = Path.Combine(ModelsDir, model.File);
        var tmp = dst + ".part";

        LogLine?.Invoke($"Downloading {model.File} (~{model.ApproxMb} MB) from {model.DownloadUrl}");
        Progress?.Invoke(0, $"Downloading {model.File}");

        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var resp = await http.GetAsync(model.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? model.ApproxBytes;
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                             FileShare.None, 1 << 16, useAsync: true))
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buf = new byte[1 << 16];
            long received = 0;
            int lastPct = -1;
            int n;
            while ((n = await src.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                received += n;
                int pct = (int)(received * 100 / total);
                if (pct != lastPct && pct >= 0 && pct <= 100)
                {
                    lastPct = pct;
                    Progress?.Invoke(pct,
                        $"Downloading {model.File}: {received / 1024 / 1024} / {total / 1024 / 1024} MB");
                }
            }
        }

        var size = new FileInfo(tmp).Length;
        if (size < 20L * 1024 * 1024)
        {
            File.Delete(tmp);
            throw new InvalidDataException(
                $"Downloaded only {size} bytes -- likely an HTML error page from HuggingFace.");
        }

        if (File.Exists(dst)) File.Delete(dst);
        File.Move(tmp, dst);
        LogLine?.Invoke($"Model saved: {dst}  ({size / 1024 / 1024} MB)");
    }

    public void StartServer(string modelFile)
    {
        if (IsRunning) return;
        var modelPath = Path.Combine(ModelsDir, modelFile);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file missing", modelPath);
        if (!File.Exists(ServerExePath))
            throw new FileNotFoundException("whisper-server.exe missing -- extract the bundle first.", ServerExePath);

        var psi = new ProcessStartInfo
        {
            FileName = ServerExePath,
            WorkingDirectory = InstallDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-m");          psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add("--host");      psi.ArgumentList.Add(BindHost);
        psi.ArgumentList.Add("--port");      psi.ArgumentList.Add(Port.ToString());
        psi.ArgumentList.Add("--inference-path"); psi.ArgumentList.Add(InferencePath);

        _serverProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _serverProc.OutputDataReceived += (_, e) => { if (e.Data != null) LogLine?.Invoke(e.Data); };
        _serverProc.ErrorDataReceived  += (_, e) => { if (e.Data != null) LogLine?.Invoke(e.Data); };
        _serverProc.Exited             += (_, _) => LogLine?.Invoke($"[server exited code={_serverProc?.ExitCode}]");
        _serverProc.Start();
        _serverProc.BeginOutputReadLine();
        _serverProc.BeginErrorReadLine();
        LogLine?.Invoke($"Launched whisper-server PID {_serverProc.Id} on {EndpointUrl}");
    }

    public void StopServer()
    {
        if (_serverProc is null || _serverProc.HasExited) return;
        try
        {
            _serverProc.Kill(entireProcessTree: true);
            _serverProc.WaitForExit(5000);
            LogLine?.Invoke("Server stopped.");
        }
        catch (Exception ex) { LogLine?.Invoke($"Stop error: {ex.Message}"); }
        finally { _serverProc = null; }
    }

    /// Polls the server root until it responds 200, or returns false after timeoutMs.
    public async Task<bool> WaitForReadyAsync(int timeoutMs, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var url = $"http://127.0.0.1:{Port}/";
                using var r = await http.GetAsync(url, ct).ConfigureAwait(false);
                if ((int)r.StatusCode is >= 200 and < 500) return true;
            }
            catch { /* not up yet */ }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    /// POSTs a wav/mp3/etc. to the server's transcription endpoint and returns
    /// the raw JSON body. Caller can parse it for `text` (json) or the richer
    /// verbose_json shape.
    public async Task<string> TranscribeAsync(string audioPath, string responseFormat,
                                              CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(audioPath, ct).ConfigureAwait(false);
        var file = new ByteArrayContent(bytes);
        file.Headers.Add("Content-Type", "application/octet-stream");
        content.Add(file, "file", Path.GetFileName(audioPath));
        content.Add(new StringContent(responseFormat), "response_format");
        content.Add(new StringContent("0"),            "temperature");

        using var resp = await http.PostAsync(EndpointUrl, content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {body}");
        return body;
    }
}
