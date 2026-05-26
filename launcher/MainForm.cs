using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WhisperLauncher;

public sealed class MainForm : Form
{
    private readonly LauncherSettings _settings = LauncherSettings.Load();
    private WhisperDeployer? _deployer;
    private CancellationTokenSource? _opCts;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };

    // Controls referenced after init
    private readonly TextBox    _txtInstallDir = new() { Width = 320 };
    private readonly NumericUpDown _numPort    = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly TextBox    _txtHost       = new() { Width = 120 };
    private readonly ComboBox   _cmbModel      = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    private readonly Button     _btnStart      = new() { Text = "Start", Width = 110, Height = 32 };
    private readonly Button     _btnStop       = new() { Text = "Stop",  Width = 110, Height = 32, Enabled = false };
    private readonly Label      _lblStatusDot  = new() { Width = 18, Height = 18, BackColor = Color.Gray };
    private readonly Label      _lblStatusText = new() { AutoSize = true, Text = "Idle" };
    private readonly ProgressBar _progress     = new() { Minimum = 0, Maximum = 100, Width = 460, Height = 18 };
    private readonly Label      _lblProgress   = new() { AutoSize = true, Text = "" };
    private readonly TextBox    _logBox        = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
        Font = new Font("Consolas", 9), Width = 760, Height = 220,
        BackColor = Color.Black, ForeColor = Color.LightGray,
    };
    private readonly Label _lblDropTarget = new()
    {
        Text = "Drop a .wav / .mp3 / .m4a here to transcribe",
        TextAlign = ContentAlignment.MiddleCenter,
        BorderStyle = BorderStyle.FixedSingle,
        Width = 760, Height = 60,
        BackColor = SystemColors.ControlLight,
        AllowDrop = true,
    };
    private readonly TextBox _txtTranscript = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
        Width = 760, Height = 80, Font = new Font("Segoe UI", 10),
    };

    public MainForm()
    {
        Text = "whisper-server launcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 720);
        MinimumSize = new Size(820, 760);
        FormClosing += OnClosing;

        BuildLayout();
        BindEvents();
        ApplySettingsToControls();

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        RefreshStatus();
        Log("Launcher ready. Adjust settings, pick a model, click Start.");
    }

    private void BuildLayout()
    {
        // Vertical stack with grouped panels.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12),
            AutoSize = true,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // -- Group 1: Settings --
        var settingsGroup = new GroupBox { Text = "Settings", AutoSize = true, Width = 780 };
        var settingsTable = new TableLayoutPanel
        {
            ColumnCount = 3, AutoSize = true, Padding = new Padding(8),
        };
        settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var btnPickDir = new Button { Text = "Browse...", Width = 90 };
        btnPickDir.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _txtInstallDir.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtInstallDir.Text = dlg.SelectedPath;
        };

        settingsTable.Controls.Add(new Label { Text = "Install dir:",   AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        settingsTable.Controls.Add(_txtInstallDir, 1, 0);
        settingsTable.Controls.Add(btnPickDir,     2, 0);

        settingsTable.Controls.Add(new Label { Text = "Port:",          AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        settingsTable.Controls.Add(_numPort,                                                                            1, 1);

        settingsTable.Controls.Add(new Label { Text = "Bind host:",     AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        settingsTable.Controls.Add(_txtHost,                                                                            1, 2);

        settingsTable.Controls.Add(new Label { Text = "Model:",         AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        settingsTable.Controls.Add(_cmbModel,                                                                           1, 3);

        settingsGroup.Controls.Add(settingsTable);
        root.Controls.Add(settingsGroup, 0, 0);

        // -- Group 2: Action row --
        var actionPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true,
            Padding = new Padding(0, 6, 0, 6),
        };
        actionPanel.Controls.Add(_btnStart);
        actionPanel.Controls.Add(_btnStop);
        actionPanel.Controls.Add(new Label { Text = "  ", Width = 16 });
        actionPanel.Controls.Add(_lblStatusDot);
        actionPanel.Controls.Add(_lblStatusText);
        root.Controls.Add(actionPanel, 0, 1);

        // -- Group 3: Progress --
        var progressPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true,
        };
        progressPanel.Controls.Add(_progress);
        progressPanel.Controls.Add(_lblProgress);
        root.Controls.Add(progressPanel, 0, 2);

        // -- Group 4: Log --
        var logGroup = new GroupBox { Text = "Log", AutoSize = true, Width = 780 };
        var logTable = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(8) };
        logTable.Controls.Add(_logBox);
        logGroup.Controls.Add(logTable);
        root.Controls.Add(logGroup, 0, 3);

        // -- Group 5: Transcribe test --
        var testGroup = new GroupBox { Text = "Test transcribe", AutoSize = true, Width = 780 };
        var testTable = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(8) };
        testTable.Controls.Add(_lblDropTarget);
        testTable.Controls.Add(_txtTranscript);
        testGroup.Controls.Add(testTable);
        root.Controls.Add(testGroup, 0, 4);

        Controls.Add(root);
    }

    private void BindEvents()
    {
        foreach (var m in WhisperModel.Choices)
            _cmbModel.Items.Add(m.DisplayName);

        _btnStart.Click += async (_, _) => await OnStartAsync();
        _btnStop.Click  += (_, _) => OnStop();

        _lblDropTarget.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        _lblDropTarget.DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                await TranscribeFileAsync(files[0]);
        };
    }

    private void ApplySettingsToControls()
    {
        _txtInstallDir.Text = _settings.InstallDir;
        _numPort.Value      = _settings.Port;
        _txtHost.Text       = _settings.BindHost;

        var idx = Array.FindIndex(WhisperModel.Choices, m => m.File == _settings.Model);
        _cmbModel.SelectedIndex = idx >= 0 ? idx : Array.FindIndex(WhisperModel.Choices,
                                              m => m.File == WhisperModel.DefaultFile);
    }

    private void CaptureSettings()
    {
        _settings.InstallDir = _txtInstallDir.Text.Trim();
        _settings.Port       = (int)_numPort.Value;
        _settings.BindHost   = _txtHost.Text.Trim();
        _settings.Model      = WhisperModel.Choices[Math.Max(0, _cmbModel.SelectedIndex)].File;
        _settings.Save();
    }

    private string BundleZipPath()
    {
        // 1) Side-by-side with the launcher exe.
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, "whisper-server-bundle.zip");
        if (File.Exists(candidate)) return candidate;
        // 2) Current directory.
        var cwd = Path.Combine(Environment.CurrentDirectory, "whisper-server-bundle.zip");
        if (File.Exists(cwd)) return cwd;
        // Fall back to side-by-side path even if missing -- the deployer will error with a clear message.
        return candidate;
    }

    private WhisperDeployer NewDeployer()
    {
        CaptureSettings();
        var d = new WhisperDeployer(
            bundleZip: BundleZipPath(),
            installDir: _settings.InstallDir,
            bindHost: _settings.BindHost,
            port: _settings.Port,
            inferencePath: "/v1/audio/transcriptions");
        d.LogLine  += Log;
        d.Progress += (pct, label) => RunOnUi(() =>
        {
            _progress.Value     = Math.Clamp(pct, 0, 100);
            _lblProgress.Text   = label;
        });
        return d;
    }

    private async Task OnStartAsync()
    {
        try
        {
            _btnStart.Enabled = false;
            _opCts = new CancellationTokenSource();
            _deployer = NewDeployer();
            if (!_deployer.VulkanPresent())
            {
                Log("WARNING: vulkan-1.dll not in System32. Install your GPU driver first or the server will fail to start.");
            }

            if (!File.Exists(_deployer.ServerExePath))
                _deployer.ExtractBundle();

            var model = WhisperModel.Choices[_cmbModel.SelectedIndex];
            if (!_deployer.ModelPresent(model.File))
                await Task.Run(() => _deployer.DownloadModelAsync(model, _opCts.Token));

            _progress.Value = 0; _lblProgress.Text = "";

            _deployer.StartServer(model.File);
            Log("Waiting for server to become ready...");
            var ready = await _deployer.WaitForReadyAsync(60_000, _opCts.Token);
            if (!ready)
            {
                Log("Server did not become ready within 60s. Check log above for errors.");
                _deployer.StopServer();
                return;
            }
            Log($"Server READY at {_deployer.EndpointUrl}");
        }
        catch (OperationCanceledException) { Log("Cancelled."); }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Start failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void OnStop()
    {
        try
        {
            _opCts?.Cancel();
            _deployer?.StopServer();
        }
        finally
        {
            RefreshStatus();
        }
    }

    private async Task TranscribeFileAsync(string path)
    {
        if (_deployer is null || !_deployer.IsRunning)
        {
            MessageBox.Show(this, "Start the server first.", "Not running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _txtTranscript.Text = $"Transcribing {Path.GetFileName(path)}...";
            _lblDropTarget.Text = $"Transcribing {Path.GetFileName(path)}...";
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var raw = await _deployer.TranscribeAsync(path, "json", cts.Token);
            string text;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                text = doc.RootElement.TryGetProperty("text", out var t)
                    ? t.GetString() ?? raw
                    : raw;
            }
            catch { text = raw; }
            _txtTranscript.Text = text.Trim();
            Log($"Transcribed {Path.GetFileName(path)} ({text.Length} chars).");
        }
        catch (Exception ex)
        {
            _txtTranscript.Text = "ERROR: " + ex.Message;
            Log("Transcribe error: " + ex.Message);
        }
        finally
        {
            _lblDropTarget.Text = "Drop a .wav / .mp3 / .m4a here to transcribe";
        }
    }

    private void RefreshStatus()
    {
        var running = _deployer?.IsRunning == true;
        _btnStart.Enabled = !running;
        _btnStop.Enabled  = running;
        _lblStatusDot.BackColor = running ? Color.LimeGreen : Color.Gray;
        _lblStatusText.Text = running
            ? $"Running on {_deployer!.EndpointUrl}"
            : "Idle";
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        CaptureSettings();
        try { _deployer?.StopServer(); } catch { /* ignore on shutdown */ }
        _statusTimer.Stop();
    }

    // --- helpers ---
    private void Log(string line)
    {
        RunOnUi(() =>
        {
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        });
    }

    private void RunOnUi(Action a)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }
}
