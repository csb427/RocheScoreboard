using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roche_Scoreboard.Services;

internal enum StartupExperience
{
    SportSelection = 0,
    LastMode = 1,
    ResumeIfAvailable = 2,
}

internal enum PersistedMode
{
    SportSelection = 0,
    Afl = 1,
    Cricket = 2,
    Training = 3,
    GlobalSettings = 4,
}

public enum AppTheme
{
    Light = 0,
    Dark = 1,
}

public enum UiDensity
{
    Comfortable = 0,
    Compact = 1,
}

/// <summary>
/// Tolerant enum converter that defaults to the first enum value when
/// encountering invalid JSON values, preventing entire document loss.
/// </summary>
internal sealed class TolerantEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();
                if (value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out TEnum result))
                    return result;
            }

            // Invalid value or unexpected token type: default to first enum value
            return default;
        }
        catch
        {
            // Invalid value: default to first enum value
            return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        // Use camelCase for enum output to match existing convention
        string text = value.ToString();
        string camelCase = char.ToLowerInvariant(text[0]) + text.Substring(1);
        writer.WriteStringValue(camelCase);
    }
}

internal sealed class UxPreferencesData
{
    public StartupExperience StartupMode { get; set; } = StartupExperience.ResumeIfAvailable;
    public PersistedMode LastMode { get; set; } = PersistedMode.SportSelection;
    public string LastSettingsCategory { get; set; } = "teams";
    public int LastMainTabIndex { get; set; } = 0;
    public bool EnableQuickStartupHints { get; set; } = true;
    public bool HideTeamNames { get; set; }

    [JsonConverter(typeof(TolerantEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.Light;

    [JsonConverter(typeof(TolerantEnumConverter<UiDensity>))]
    public UiDensity Density { get; set; } = UiDensity.Comfortable;
}

internal static class UxPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string GetStoragePath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ux_preferences.json");
    }

    public static UxPreferencesData Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return new UxPreferencesData();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UxPreferencesData>(json, JsonOptions) ?? new UxPreferencesData();
        }
        catch
        {
            return new UxPreferencesData();
        }
    }

    public static void Save(UxPreferencesData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Best effort persistence only.
        }
    }
}
