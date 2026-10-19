using System;
using System.IO;
using System.Text.Json;

namespace ScreenWallpaper;

internal enum FitMode { Fill, Fit, Stretch }

internal sealed class Settings
{
    public string? FilePath { get; set; }
    public FitMode Mode { get; set; } = FitMode.Fill;
    public bool Muted { get; set; } = true;
    public bool PauseOnFullscreen { get; set; } = true;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenWallpaper");
    private static readonly string FilePathOnDisk = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePathOnDisk))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePathOnDisk)) ?? new Settings();
        }
        catch
        {
            // Corrupt settings: fall back to defaults.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePathOnDisk, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
