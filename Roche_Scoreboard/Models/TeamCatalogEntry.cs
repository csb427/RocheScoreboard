namespace Roche_Scoreboard.Models;

public enum TeamLibrarySport { AustralianFootball, Cricket }

public sealed record TeamCatalogEntry(
    string Id,
    string TeamName,
    string Abbreviation,
    string League,
    string State,
    string PrimaryColor,
    string SecondaryColor,
    string LogoSource)
{
    public bool IsFavourite { get; set; }
    public TeamLibrarySport Sport { get; init; } = TeamLibrarySport.AustralianFootball;
    public string? SourcePage { get; init; }
    public string? BundledLogo { get; init; }
    public string? PreviewLogo => BundledLogo is null ? null : "pack://application:,,,/" + BundledLogo;
}

public sealed record TeamLibrarySelection(TeamCatalogEntry Team, string TransparentLogoPath);
