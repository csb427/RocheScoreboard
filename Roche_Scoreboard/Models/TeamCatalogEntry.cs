namespace Roche_Scoreboard.Models;

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
}

public sealed record TeamLibrarySelection(TeamCatalogEntry Team, string TransparentLogoPath);
