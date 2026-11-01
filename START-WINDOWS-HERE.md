# Run the revised Windows test source

**Snapshot:** RocheScoreboard-5.0.0-windows-feedback-20261101-v2. App version5.0.0 is unchanged.

1. Extract the whole ZIP into a **new folder**, outside previous test folders/checkouts.
2. Open Visual Studio **2026** (or a current .NET10-capable version) with **.NET desktop development** and the **.NET10 SDK** installed.
3. Open **Roche_Scoreboard.slnx**. Alternatively open **Roche_Scoreboard\Roche_Scoreboard.csproj**.
4. Set **Roche_Scoreboard** as startup project, choose **Debug**, wait for NuGet restore, then **F5**. Keep the default win-x64 project platform.

Optional Windows terminal launch from extracted root:

```powershell
dotnet run --project .\Roche_Scoreboard\Roche_Scoreboard.csproj -c Debug
```

Existing dependencies, including native Windows VLC files, restore through NuGet. Normal local UI launch needs no password/cloud credential. This is the Windows/WPF project; these instructions do not run it on Mac.

Windows still uses its normal **Documents\Roche_Scoreboard** data folder. Copy that folder first if you want to preserve matches/settings before testing resets/corrections. No operator data is included in this ZIP.

## Try the reported fixes

- Home: compare Start Game/Training hover. Start Game: check rectangular sport selection fill, balanced Cancel/Continue and Back on later steps. Repeat format/options, toss and batter/bowler selection; try keyboard and reduced motion.
- Delivery log: thin full-width rows, no ball/header overlap. Hover to reveal Edit, Tab/Enter/Space or click Edit; Cancel must preserve the match.
- Cricket output: compact wicket/FOR spacing, larger team names/clock/weather in the same panels, long names and small output size.
- Output → Cricket Overlays: **Extras under Match Context**, **Innings Boundaries under Recent Play**. Verify wides/no-balls/byes/leg-byes versus fours/sixes; charts must suppress overlays.
- Cold launch and first selection: the reported intermittent animation skip is **not yet resolved**. Note exact control and motion setting if it recurs.
- Existing operations: scorecard combined batter/wide corrections, undo/save/reopen, Ctrl+K, Training, output close/reopen and native sound/video/monitor behavior.

See TEST-VERIFICATION.md and KNOWN-LIMITATIONS.md. SOURCE-SNAPSHOT.json identifies this source copy; ZIP About/revision may correctly say unknown/local.
