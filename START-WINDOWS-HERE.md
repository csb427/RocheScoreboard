# Run this Windows test source

**Snapshot:** RocheScoreboard-5.0.0-windows-refinement-20261103-v1. App version5.0.0 is unchanged.

1. Extract the entire ZIP into a **new folder**, outside previous test folders/checkouts.
2. Use Visual Studio **2026** (or a current .NET10-capable version), **.NET desktop development**, and the **.NET10 SDK**.
3. Open **Roche_Scoreboard.slnx** (or **Roche_Scoreboard\Roche_Scoreboard.csproj**).
4. Set **Roche_Scoreboard** as startup project, choose **Debug**, restore NuGet, then **F5**. Keep the existing win-x64 platform.

Optional Windows terminal launch from the extracted root:

```powershell
dotnet run --project .\Roche_Scoreboard\Roche_Scoreboard.csproj -c Debug
```

Normal local launch needs no cloud password. Existing dependencies/native Windows VLC restore through NuGet. These are Windows/WPF instructions, not a Mac launch route.

Normal Documents\Roche_Scoreboard saves/settings remain shared between versions. Copy that data folder before testing resets/corrections if needed. No operator data is included.

## Focused acceptance

- AFL: switch Settings to Match with pointer and Ctrl+1, immediately add two goals. Both should register once; try navigation near scoring controls, keyboard focus and rapid repeats.
- Cricket: cancel Wicket/Wide/figure sheets with pointer and Escape, immediately score or navigate. Fresh input should work; the closing gesture must not score.
- Dismissals: Bowled/Caught present only applicable runs/extras/player fields. Switch types and edit prior deliveries; verify totals, corrections and undo.
- Scorecard: scroll to late players at compact size, focus figures and open Edit. Player/role/team/innings should remain clear. Switch roles, return and cancel without losing the draft.
- Wicket summary: long names, actual victim/dismissal, valid next-striker/fielder details and reachable Apply/Cancel.
- Home/setup: one landing, safe Resume, shared sport choice, conditional saved-club Delete and optional goal video. Check cancel/back and small-window scrolling.
- Cricket: reachable scoring/player controls and distinct presentation/overlay/weather regions. AFL: compact history rows with keyboard/pointer Edit. Training: compact Start/Reset/Skip and reachable utilities.
- Retained earlier fixes: Extras and Innings Boundaries are separate grouped overlay controls; charts suppress overlays. Verify delivery-log Edit reveal, output typography, combined scorecard/wide corrections and save/reopen.
- Cold-first-open/rapid selections and menus: record exact control, motion preference and build/snapshot if an animation skips. This intermittent native issue is not certified fixed. Check Expressive, Calm, reduced motion and Windows scaling.

See TEST-VERIFICATION.md and KNOWN-LIMITATIONS.md. Without a Git checkout the app may truthfully show local/unknown revision; this snapshot manifest identifies the ZIP.
