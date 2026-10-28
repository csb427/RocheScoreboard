// Generated from Services/CricketClubCatalog.cs; edit translator/adapters.
// Source SHA-256: 47775b36b29db487deabfc8338f8d533c1f44ccedf56c6b5a79c768ec8133334
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Reflection;
using System.Text.Json;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Official Victorian Premier and VSDCA identities, with offline source artwork.</summary>
internal static class CricketClubCatalog
{
    internal static readonly IReadOnlyList<TeamCatalogEntry> Teams = Load();
    private static IReadOnlyList<TeamCatalogEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Roche_Scoreboard.Resources.CricketClubs.json")
            ?? throw new InvalidOperationException("The bundled cricket club directory is missing.");
        return JsonSerializer.Deserialize<List<TeamCatalogEntry>>(stream) ?? [];
    }
}
