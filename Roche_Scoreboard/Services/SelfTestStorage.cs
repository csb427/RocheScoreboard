using System;
using System.Globalization;
using System.IO;

namespace Roche_Scoreboard.Services;

/// <summary>Process-local storage for native regression runs; never reads operator data.</summary>
internal static class SelfTestStorage
{
    internal static bool IsEnabled { get; private set; }

    // Keep isolation latched through shutdown and any final background callbacks.
    internal static void Initialize(bool isSelfTest)
    {
        if (isSelfTest)
            IsEnabled = true;
    }

    internal static string RootPath { get; } = Path.Combine(
        Path.GetTempPath(), "RocheScoreboard-SelfTests", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    internal static string PathFor(string area, string fileName) => Path.Combine(RootPath, area, fileName);

    internal static string DocumentsDirectory => IsEnabled
        ? Path.Combine(RootPath, "preferences")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard");
}
