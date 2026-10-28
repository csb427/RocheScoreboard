// Generated from Services/UxPreferences.cs; edit translator/adapters.
// Source SHA-256: f65a703ec204c2a6f2d255ca9b1b337e300d8bdef7043527c5dd3c1309413be8
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
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
    public bool EnableQuickStartupHints { get; set; }
    public bool HideTeamNames { get; set; }
    public string ScorebugLayout { get; set; } = "classic";
    public bool GuidedOperatorMode { get; set; } = true;

    [JsonConverter(typeof(TolerantEnumConverter<AppTheme>))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

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

    internal static string GetStoragePath()
    {
        string dir = SelfTestStorage.DocumentsDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ux_preferences.json");
    }

    public static UxPreferencesData Load()
    {
        string path = GetStoragePath();
        try
        {
            return AtomicFile.TryReadJson<UxPreferencesData>(path, JsonOptions) ?? new UxPreferencesData();
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
            AtomicFile.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Best effort persistence only.
        }
    }
}
