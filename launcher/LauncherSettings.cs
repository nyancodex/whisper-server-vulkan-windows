using System;
using System.IO;
using System.Text.Json;

namespace WhisperLauncher;

public sealed class LauncherSettings
{
    public string InstallDir   { get; set; } = DefaultInstallDir();
    public int    Port         { get; set; } = 8088;
    public string BindHost     { get; set; } = "0.0.0.0";
    public string Model        { get; set; } = WhisperModel.DefaultFile;
    public bool   AutoStart    { get; set; }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "whisper-launcher",
        "settings.json");

    public static string DefaultInstallDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper-server");

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? new LauncherSettings();
            }
        }
        catch { /* malformed file -> fall back to defaults */ }
        return new LauncherSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best-effort */ }
    }
}
