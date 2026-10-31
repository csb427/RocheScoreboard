using System.Reflection;

namespace Roche_Scoreboard.Services;

/// <summary>Safe support identity read from this binary, never the running checkout.</summary>
internal static class BuildIdentity
{
    internal static string Description { get; } = Read();
    private static string Read()
    {
        var assembly = typeof(BuildIdentity).Assembly;
        string? Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        return Format(assembly.GetName().Version?.ToString(3), Metadata("RocheSourceRevision"), Metadata("RocheSourceState"), assembly.ManifestModule.ModuleVersionId);
    }
    internal static string Format(string? version, string? revision, string? state, Guid binaryId)
    {
        string release = Version.TryParse(version, out var parsed) && parsed.Build >= 0 ? "v" + parsed.ToString(3) : "Version unknown";
        bool validRevision = revision is { Length: 40 or 64 } && revision.All(Uri.IsHexDigit);
        string source = validRevision ? revision![..12].ToLowerInvariant() : "unknown";
        string context = state == "modified" && validRevision ? $"LOCAL CHANGES · based on {source}"
            : state == "clean" && validRevision ? $"source {source} · clean checkout at build"
            : $"source {source} · local/unverified build";
        string build = binaryId == Guid.Empty ? "unknown" : binaryId.ToString("N")[..12];
        return $"{release} · {context} · build {build}";
    }
}
