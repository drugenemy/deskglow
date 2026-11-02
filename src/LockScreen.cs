using System.Globalization;
using System.Text.Json.Nodes;
using Windows.Storage;

namespace Deskglow;

/// <summary>
/// Makes the lock screen image: asks the wallpaper for a still frame with widgets
/// (weather, note, year progress) and hands it to Windows.
///
/// The lock screen cannot run live content, so the image is refreshed on a timer
/// and on unlock, which keeps it fresh for the next time the PC locks.
/// </summary>
internal sealed class LockScreen
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    readonly Config cfg;
    JsonObject? weather;
    DateTime weatherAt = DateTime.MinValue;
    int flip;

    public LockScreen(Config cfg) => this.cfg = cfg;

    /// <summary>Widget data for the page, as JSON.</summary>
    public async Task<string> BuildDataAsync()
    {
        var data = new JsonObject
        {
            ["widgets"] = new JsonArray(cfg.LockWidgets.Select(w => (JsonNode?)JsonValue.Create(w)).ToArray()),
            ["note"] = cfg.Note,
            ["now"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        };
        if (cfg.LockWidgets.Contains("weather"))
        {
            var w = await GetWeatherAsync();
            if (w != null) data["weather"] = w.DeepClone();
        }
        return data.ToJsonString();
    }

    /// <summary>Saves the JPEG (a data: URL from the page) and sets it as the lock screen.</summary>
    public async Task ApplyAsync(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        Directory.CreateDirectory(Config.Dir);
        // Alternate between two files: Windows may ignore a "new" image at the same path.
        flip ^= 1;
        var path = Path.Combine(Config.Dir, $"lockscreen-{flip}.jpg");
        await File.WriteAllBytesAsync(path, bytes);
        var file = await StorageFile.GetFileFromPathAsync(path);
        await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
    }

    /// <summary>Looks up a city name. Returns the resolved name, or null if not found.</summary>
    public async Task<string?> SetCityAsync(string city)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name={Uri.EscapeDataString(city)}";
        var json = JsonNode.Parse(await Http.GetStringAsync(url));
        var hit = json?["results"]?[0];
        if (hit == null) return null;
        cfg.City = city;
        cfg.CityName = (string?)hit["name"];
        cfg.Lat = (double?)hit["latitude"];
        cfg.Lon = (double?)hit["longitude"];
        cfg.Save();
        weather = null;
        return cfg.CityName;
    }

    async Task<JsonObject?> GetWeatherAsync()
    {
        if (cfg.Lat == null || cfg.Lon == null) return null;
        if (weather != null && DateTime.Now - weatherAt < TimeSpan.FromMinutes(20)) return weather;
        try
        {
            var unit = cfg.Fahrenheit ? "&temperature_unit=fahrenheit" : "";
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={cfg.Lat}&longitude={cfg.Lon}" +
                $"&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                $"&timezone=auto&forecast_days=4{unit}");
            var j = JsonNode.Parse(await Http.GetStringAsync(url))!;
            var cur = j["current"]!;
            var daily = j["daily"]!;
            var days = new JsonArray();
            for (int i = 1; i < 4; i++)
            {
                days.Add(new JsonObject
                {
                    ["date"] = (string?)daily["time"]![i],
                    ["hi"] = Math.Round((double)daily["temperature_2m_max"]![i]!),
                    ["lo"] = Math.Round((double)daily["temperature_2m_min"]![i]!),
                    ["text"] = Describe((int)daily["weather_code"]![i]!),
                });
            }
            weather = new JsonObject
            {
                ["city"] = cfg.CityName ?? cfg.City,
                ["temp"] = Math.Round((double)cur["temperature_2m"]!),
                ["text"] = Describe((int)cur["weather_code"]!),
                ["hi"] = Math.Round((double)daily["temperature_2m_max"]![0]!),
                ["lo"] = Math.Round((double)daily["temperature_2m_min"]![0]!),
                ["days"] = days,
                ["at"] = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
            };
            weatherAt = DateTime.Now;
        }
        catch
        {
            // Offline or the service is down: keep the last result if we have one.
        }
        return weather;
    }

    /// <summary>WMO weather codes, in plain words.</summary>
    static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "—",
    };
}
