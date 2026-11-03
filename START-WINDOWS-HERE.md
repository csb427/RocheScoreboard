# Run the current Windows testing revision

Snapshot: **RocheScoreboard-5.0.0-testing-feedback-20261103-v1**. Application version remains5.0.0.

## Existing Visual Studio testing clone

1. Stop debugging (Shift+F5).
2. Confirm the checked-out branch is **testing**, then **Git > Pull**. Keep your own changes on a separate branch; do not discard tracked edits to force an update.
3. Open **Roche_Scoreboard.slnx**, set **Roche_Scoreboard** as startup project, select **Debug** and the existing win-x64 platform, then **F5**.

## First clone or ZIP

Use Visual Studio2026/current.NET10-capable version, the **.NET desktop development** workload and **.NET10SDK**. Clone https://github.com/csb427/RocheScoreboard.git and check out **testing**, or extract the whole source ZIP into a new folder. Open the solution, restore NuGet and F5. A ZIP will not receive Git Pull updates.

```powershell
dotnet run --project .\Roche_Scoreboard\Roche_Scoreboard.csproj -c Debug
```

Local launch needs no cloud credentials. Existing saved data/settings are shared between versions; preserve any important operator recovery before testing deliberate Reset. Active recovery normally uses LocalApplicationData/Roche_Scoreboard; other existing settings/media can use the app's Documents storage. No operator data is in this source snapshot. A ZIP may truthfully show a local/unknown build revision; a clean Git build can stamp its actual source revision.

## Check this revision

- Start Game: AFL and Cricket → Teams, first use/repeat, Back/Cancel/rapid sport changes, Expressive/Calm/reduced motion. Actual team forms should arrive consistently while input remains immediate.
- Existing match → finish setup or Training: integrated New session sheet, readable consequence, safe default Cancel/Enter/Escape, explicit Start once; changed session rejects stale approval.
- Cricket: wide/intermediate/compact/scaled sizes, paired players, natural-height content, aligned Options/Undo/End innings/Reset, scrollbar edges and continuously backed bottom dock. Populated logs and long forms remain scrollable. Existing output geometry is unchanged.
- AFL/Cricket Reset: clear Cancel, no redundant match-state box, readable irreversible consequence, preserved state on cancel, deliberate reset returns Home. No history/archive is created.
- What's New: normal/short window, first/last release, selector/scrolling, search/filter, hover/selected/keyboard focus and rapid changes.

See TEST-VERIFICATION.md and KNOWN-LIMITATIONS.md. Native Windows desktop acceptance remains required.
