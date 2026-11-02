using System.Text.Json;

namespace Deskglow;

internal sealed class Config
{
    public string Current { get; set; } = "ura";
    public bool AutoPause { get; set; } = true;

    /// <summary>On Windows 11 24H2+, make the window layered so DWM draws it. Turn off if the wallpaper stays black.</summary>
    public bool LayeredOnNewLayout { get; set; } = true;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deskglow");
    static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { }
        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
