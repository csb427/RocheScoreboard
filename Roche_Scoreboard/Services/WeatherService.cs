using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Roche_Scoreboard.Services;

/// <summary>Single hour of forecast data.</summary>
public sealed record HourlyForecast(
    DateTime Time,
    double Temperature,
    int WeatherCode,
    string Icon,
    string Description,
    double PrecipitationMm,
    int PrecipitationProbability);

/// <summary>Structured weather snapshot for overlay display.</summary>
public sealed record WeatherSnapshot(
    double CurrentTemp,
    double FeelsLike,
    double DayMin,
    double DayMax,
    int WeatherCode,
    string Icon,
    string Description,
    IReadOnlyList<HourlyForecast> HourlyForecast);

/// <summary>
/// Fetches live weather data from the Open-Meteo API (free, no API key required).
/// Call <see cref="StartAsync"/> with a city name to begin periodic updates.
/// </summary>
internal sealed class WeatherService : IDisposable
{
    private static readonly HttpClient s_http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
    private double _latitude;
    private double _longitude;
    private bool _resolved;
    private bool _disposed;

    /// <summary>
    /// Fires on the UI thread whenever new weather data arrives.
    /// The string is a formatted summary like "18°C  ☀️ Clear".
    /// </summary>
    public event Action<string>? WeatherUpdated;

    /// <summary>
    /// Fires on the UI thread with full structured forecast data for overlays.
    /// </summary>
    public event Action<WeatherSnapshot>? ForecastUpdated;

    public string CurrentWeather { get; private set; } = "";

    /// <summary>Latest structured forecast data, or null if not yet fetched.</summary>
    public WeatherSnapshot? LatestSnapshot { get; private set; }

    public WeatherService()
    {
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            try { await RefreshWeatherAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException) { }
        };
    }

    /// <summary>
    /// Geocodes the city name and fetches the first weather reading.
    /// </summary>
    public async Task StartAsync(string cityName)
    {
        ArgumentNullException.ThrowIfNull(cityName);
        if (string.IsNullOrWhiteSpace(cityName))
            return;

        _resolved = await GeocodeAsync(cityName);
        if (!_resolved)
            return;

        await RefreshWeatherAsync();
        _refreshTimer.Start();
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        CurrentWeather = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
    }

    private async Task<bool> GeocodeAsync(string city)
    {
        try
        {
            string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json";
            string json = await s_http.GetStringAsync(url);

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
                return false;

            JsonElement first = results[0];
            _latitude = first.GetProperty("latitude").GetDouble();
            _longitude = first.GetProperty("longitude").GetDouble();
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task RefreshWeatherAsync()
    {
        if (!_resolved) return;

        try
        {
            string lat = _latitude.ToString(CultureInfo.InvariantCulture);
            string lon = _longitude.ToString(CultureInfo.InvariantCulture);
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}"
                       + "&current=temperature_2m,apparent_temperature,weather_code"
                       + "&hourly=temperature_2m,weather_code,precipitation,precipitation_probability"
                       + "&daily=temperature_2m_max,temperature_2m_min"
                       + "&forecast_hours=8&forecast_days=1&timezone=auto";
            string json = await s_http.GetStringAsync(url);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement current = doc.RootElement.GetProperty("current");
            double temp = current.GetProperty("temperature_2m").GetDouble();
            double feelsLike = current.GetProperty("apparent_temperature").GetDouble();
            int code = current.GetProperty("weather_code").GetInt32();

            string icon = WeatherCodeToIcon(code);
            string desc = WeatherCodeToDescription(code);

            CurrentWeather = $"{temp:0}°C  {icon} {desc}";
            WeatherUpdated?.Invoke(CurrentWeather);

            // Parse daily min/max
            JsonElement daily = doc.RootElement.GetProperty("daily");
            double dayMin = daily.GetProperty("temperature_2m_min")[0].GetDouble();
            double dayMax = daily.GetProperty("temperature_2m_max")[0].GetDouble();

            // Parse hourly forecast
            JsonElement hourly = doc.RootElement.GetProperty("hourly");
            JsonElement hTimes = hourly.GetProperty("time");
            JsonElement hTemps = hourly.GetProperty("temperature_2m");
            JsonElement hCodes = hourly.GetProperty("weather_code");
            JsonElement hPrecip = hourly.GetProperty("precipitation");
            JsonElement hProb = hourly.GetProperty("precipitation_probability");

            List<HourlyForecast> hours = [];
            int count = Math.Min(hTimes.GetArrayLength(), 8);
            for (int i = 0; i < count; i++)
            {
                DateTime time = DateTime.Parse(hTimes[i].GetString()!, CultureInfo.InvariantCulture);
                int hCode = hCodes[i].GetInt32();
                hours.Add(new HourlyForecast(
                    time,
                    hTemps[i].GetDouble(),
                    hCode,
                    WeatherCodeToIcon(hCode),
                    WeatherCodeToDescription(hCode),
                    hPrecip[i].GetDouble(),
                    hProb[i].GetInt32()));
            }

            LatestSnapshot = new WeatherSnapshot(temp, feelsLike, dayMin, dayMax, code, icon, desc, hours);
            ForecastUpdated?.Invoke(LatestSnapshot);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            // Keep last known weather on transient failures
        }
    }

    internal static string WeatherCodeToIcon(int code) => code switch
    {
        0 => "☀️",
        1 or 2 => "🌤️",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 => "🌦️",
        56 or 57 => "🥶",
        61 or 63 or 65 => "🌧️",
        66 or 67 => "🌨️",
        71 or 73 or 75 => "❄️",
        77 => "🌨️",
        80 or 81 or 82 => "🌧️",
        85 or 86 => "❄️",
        95 => "⛈️",
        96 or 99 => "🌩️",
        _ => "🌡️"
    };

    internal static string WeatherCodeToDescription(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly Clear",
        2 => "Partly Cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 => "Light Drizzle",
        53 => "Drizzle",
        55 => "Heavy Drizzle",
        56 or 57 => "Freezing Drizzle",
        61 => "Light Rain",
        63 => "Rain",
        65 => "Heavy Rain",
        66 or 67 => "Freezing Rain",
        71 => "Light Snow",
        73 => "Snow",
        75 => "Heavy Snow",
        77 => "Snow Grains",
        80 => "Light Showers",
        81 => "Showers",
        82 => "Heavy Showers",
        85 => "Light Snow Showers",
        86 => "Heavy Snow Showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm w/ Hail",
        _ => ""
    };
}
