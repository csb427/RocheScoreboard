// Generated from Services/SelfTestStorage.cs; edit translator/adapters.
// Source SHA-256: 74532f4a16e29b17e75afebf1b7341acc90c9f8655acd569e2f34627c7fe1c8a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
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
        : Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard");
}
