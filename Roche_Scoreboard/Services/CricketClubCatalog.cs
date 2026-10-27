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
