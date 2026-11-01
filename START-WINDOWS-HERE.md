# Roche Scoreboard — Windows source test snapshot

**Snapshot:** RocheScoreboard-5.0.0-local-20261101-v1  
**App version:** 5.0.0  
This includes the current integrated cloud source, beyond the older a007d8c test download. It does not change another checkout.

## Open in Visual Studio

1. Extract the whole ZIP into a new folder outside your existing checkout, for example `C:\RocheTests\RocheScoreboard-5.0.0-local-20261101-v1`.
2. Use Visual Studio **2026** (or a current version supporting .NET10) with **.NET desktop development** and the **.NET10 SDK** installed.
3. Open **`Roche_Scoreboard.slnx`**. If needed, open `Roche_Scoreboard\Roche_Scoreboard.csproj` directly.
4. Set **Roche_Scoreboard** as the startup project, choose **Debug**, let NuGet restore complete, then press **F5**. Keep the project's default platform; it already targets `win-x64`.

NuGet supplies the existing dependencies, including the Windows VLC native files. No password, cloud credential or additional app installer is required to launch the normal local UI. The desktop project is Windows/WPF; opening these sources on Mac does not make it run there.

Optional Windows terminal launch from the extracted root:

```powershell
dotnet run --project .\Roche_Scoreboard\Roche_Scoreboard.csproj -c Debug
```

## Identify the test copy

Check `SOURCE-SNAPSHOT.json` for the snapshot ID and per-file hashes. `.git` is deliberately absent, so About/build information may show revision/state **unknown/local**. That is expected; it is not a claim of a clean Git revision. Keep this folder separate from the older preview/your normal checkout.

A separate source folder does **not** isolate saved app data: Windows still uses the normal `Documents\Roche_Scoreboard` folder. Copy that folder first if you want to preserve existing matches/settings before testing resets or corrections. The ZIP contains no operator saves.

## First acceptance checks

- Home has Resume only for real recoverable state, one Start Game → AFL/Cricket choice, and direct Training. Back/Cancel preserves the match; Resume stays off-air until output is enabled.
- In cricket, Extras displays extras; Innings Boundaries is separate. Other presentation screens suppress overlays. Verify all-innings chart labels and long names.
- Edit a batter score and a wide together; totals should recompute from the edited draft. Invalid fields should remain editable with clear feedback.
- Try Ctrl+K with a misspelling/synonym; use arrows/Enter/Escape and confirm focus returns. Typing must not score.
- Test selection/menu/window motion, repeated clicks, Escape, small windows and reduced motion; scoring must remain immediate.
- Debug cricket preview tools: after entering cricket, **Ctrl+Alt+D** opens practice situations and animation previews. Restore/close returns to the original match. This is not exhaustive rules coverage.
- Check normal Training edits/remote controls, undo/save/reopen, output close/reopen, sound/video and physical monitor positioning on Windows.

## What is and is not verified

The current Linux/Avalonia aggregate passed137/137. This exact frozen source built Windows Debug cleanly using the existing package cache. These do not establish native Windows GUI/hardware acceptance.

The historical Linux font-host crash and old host abort, original Mac freeze, default portable selector auto-scroll behavior, actual OS notifications and native media/monitor behavior remain open. See `Verification\WINDOWS-HANDOFF-20261101.md` and the verification receipt. Companion Mac/web/source and repo-local skills are included, but are not extra prerequisites for the Windows solution.
