using System.Text.Json;

namespace Deskglow;

internal sealed class Config
{
    public string Current { get; set; } = "ura";
    public bool AutoPause { get; set; } = true;

    /// <summary>On Windows 11 24H2+, make the window layered so DWM draws it. Turn off if the wallpaper stays black.</summary>
    public bool LayeredOnNewLayout { get; set; } = true;

    // ── lock screen ──
    public bool LockEnabled { get; set; } = false;
    public List<string> LockWidgets { get; set; } = new() { "weather", "note", "year" };
    public string City { get; set; } = "";
    public string? CityName { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public bool Fahrenheit { get; set; } = false;
    public string Note { get; set; } = "";

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deskglow");
    static string FilePath => Path.Combine(Dir, "config.json");

    /// <summary>Loads the saved settings. On first run, a bundle can pick its starting wallpaper with wallpapers/default.txt.</summary>
    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch { }

        var cfg = new Config();
        try
        {
            var def = Path.Combine(AppContext.BaseDirectory, "wallpapers", "default.txt");
            if (File.Exists(def)) cfg.Current = File.ReadAllText(def).Trim();
        }
        catch { }
        return cfg;
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
