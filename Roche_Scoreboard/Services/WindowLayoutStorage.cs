using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Persists and restores window positions and sizes between app launches
/// so the operator's chosen layout is remembered. Stored as JSON in the
/// same MyDocuments\Roche_Scoreboard folder as other user data.
/// </summary>
internal static class WindowLayoutStorage
{
    /// <summary>Logical key for the operator console (the main window).</summary>
    public const string ControlPanelKey = "controlPanel";

    /// <summary>Logical key for the AFL display window (live scorebug + break screens).</summary>
    public const string AflDisplayKey = "displayAfl";

    /// <summary>Logical key for the Cricket display window.</summary>
    public const string CricketDisplayKey = "displayCricket";

    /// <summary>Logical key for the Training display window.</summary>
    public const string TrainingDisplayKey = "displayTraining";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record WindowLayout(double Left, double Top, double Width, double Height);

    private static string GetStorageDirectory()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetStoragePath() => Path.Combine(GetStorageDirectory(), "window_layout.json");

    private static Dictionary<string, WindowLayout> Load()
    {
        string path = GetStoragePath();
        if (!File.Exists(path)) return new();
        try
        {
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, WindowLayout>>(json, JsonOptions);
            return data ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void Save(Dictionary<string, WindowLayout> data)
    {
        try
        {
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(GetStoragePath(), json);
        }
        catch
        {
            // Persisting layout is best-effort; never crash the app over it.
        }
    }

    /// <summary>Returns the saved layout for <paramref name="key"/>, or null if none.</summary>
    public static WindowLayout? Get(string key)
    {
        var data = Load();
        return data.TryGetValue(key, out var layout) ? layout : null;
    }

    /// <summary>Stores the current layout for <paramref name="key"/>.</summary>
    public static void Set(string key, WindowLayout layout)
    {
        var data = Load();
        data[key] = layout;
        Save(data);
    }

    /// <summary>
    /// Reads a window's current Left/Top/Width/Height into a layout record.
    /// Falls back to RestoreBounds when the window is currently maximised
    /// or minimised so the saved layout reflects the user's chosen size.
    /// </summary>
    public static WindowLayout Capture(Window window)
    {
        if (window.WindowState != WindowState.Normal && window.RestoreBounds is { } rb && !rb.IsEmpty)
        {
            return new WindowLayout(rb.Left, rb.Top, rb.Width, rb.Height);
        }
        return new WindowLayout(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
    }

    /// <summary>
    /// Applies a saved layout to <paramref name="window"/>, clamping to the
    /// current work area so a window saved on a now-disconnected monitor
    /// can never spawn off-screen.
    /// </summary>
    public static void Apply(Window window, WindowLayout layout, double minWidth = 200, double minHeight = 120)
    {
        var workArea = SystemParameters.WorkArea;

        double width = Math.Max(minWidth, Math.Min(layout.Width, workArea.Width));
        double height = Math.Max(minHeight, Math.Min(layout.Height, workArea.Height));

        double left = layout.Left;
        double top = layout.Top;

        // If the saved position is fully off-screen (e.g. a disconnected
        // monitor), pull the window back into the visible work area.
        if (left + width <= workArea.Left + 20 || left >= workArea.Right - 20)
            left = workArea.Right - width - 20;
        if (top + height <= workArea.Top + 20 || top >= workArea.Bottom - 20)
            top = workArea.Bottom - height - 20;

        // Final clamp so we always land inside the work area.
        left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));

        window.Width = width;
        window.Height = height;
        window.Left = left;
        window.Top = top;
    }

    /// <summary>Captures a window's current state and saves it under <paramref name="key"/>.</summary>
    public static void Save(string key, Window window)
    {
        Set(key, Capture(window));
    }
}
