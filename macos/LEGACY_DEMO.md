> Superseded simplified demo. This document does not describe the faithful Mac port.

# Roche Scoreboard · Mac demo

A native Avalonia desktop port for local testing and demonstrations on a Mac.
The controller and output are two resizable, ordinary windows. It operates entirely
locally and requires neither Supabase nor a running Windows scoreboard.

This is the first functional port, not yet complete Windows presentation parity.
The original Windows project and mobile cloud remote are separate applications.

## Run on a Mac

Requires macOS 15 or newer (the supported macOS baseline for the current .NET 10
runtime). Both Apple silicon and Intel are supported.

From this source checkout, install the **.NET 10 SDK** from
<https://dotnet.microsoft.com/download/dotnet/10.0>, then double-click
**Start Demo.command**, or run:

```bash
dotnet run --project macos/RocheScoreboard.Mac/RocheScoreboard.Mac.csproj
```

Select AFL, Cricket or Training, then click **Open output window**. Move that
window to another display or resize it alongside the controller. Closing the
output does not stop the session; reopen it at any time. Closing the controller
pauses the clocks, saves the demo, and closes the output.

## Share an application without installing .NET

From the repository root on a Mac:

```bash
bash macos/scripts/package-mac.sh osx-arm64  # Apple silicon: M1 and newer
bash macos/scripts/package-mac.sh osx-x64    # Intel Mac
```

Each command creates a self-contained ZIP in `artifacts/macos/`. Extract it and
open **Roche Scoreboard Demo.app**. Keep all files inside the `.app` bundle.
The script uses ad-hoc signing when run on macOS. These are local development
builds, without Developer ID signing or Apple notarisation; macOS may require
**Privacy & Security → Open Anyway** after attempting to open a downloaded build.
Do not disable Gatekeeper globally. A bundle cross-built on Linux must first be
signed on the Mac with:

```bash
bash "Prepare Mac Demo.command"
```

The ZIP includes that helper next to the app. It signs the app's native binaries
individually with the .NET JIT entitlement, verifies the bundle, then opens it.
It does not require the .NET SDK or a paid Apple developer account.

The **Build Mac demo** GitHub Actions workflow builds and tests both architectures,
signs the bundles on macOS, and uploads ZIPs as workflow artifacts. It does not
publish a GitHub release. It also launches the packaged controller and output
when the runner architecture matches the package. This workflow still needs its
first run on GitHub.

## Current functionality

| Area | Available in the Mac demo | Remaining Windows parity |
| --- | --- | --- |
| AFL scoring | Goals, behinds, undo, editable event timeline, recorded scores, per-quarter snapshots, post-siren scoring | Finals setup and Windows setup wizard |
| AFL clock | Count up/countdown, duration, elapsed correction, pause/resume, expiry, quarter breaks, continue ended quarter, full time | Siren/media integration |
| AFL output | Side-by-side/stacked scores, quarter summary, statistics, scoreworm, intro, goal notification, goal-run/drought stories | Exact broadcast visuals, full automatic overlay rotation, win probability presentation |
| Cricket | Rosters, formats, batting order, all standard delivery controls, extras, dismissals, batter/bowler selection, strike swap, undo, innings/chase/result handling | Windows setup wizard and the complete overlay catalogue |
| Cricket output | Live scores, batters/bowler, current over, batting/bowling summary, innings totals, analytics, intro | Detailed dismissal text in the summary and viewing previous innings' full scorecards |
| Training | Editable/reorderable queue, repeated work/rest builder, start/pause/reset/skip/extra time, immediate auto-advance, completion notices, session summary | Exact Windows transitions/strobe (the demo uses a non-flashing completion notice) |
| Teams and messages | Names/abbreviations, primary/text colours, local PNG/JPEG logos, five-second rotating ticker, 4.2-second announcements, custom centre label | Shared team library, logo cropping, SVG logos |
| Recovery and sharing | Five-second autosave with backup, restore paused, JSON export/import for all three modes | Direct import of Windows save files |
| Platform integrations | Ordinary independent windows, keyboard scoring/clock/undo | VLC video, weather fetching, cloud remote hosting, updater, output calibration, Windows display management |

An item listed as remaining is not presented as a working button. Windows view
code owns much of its presentation behavior, so compiling the scoring models
alone does not reproduce every animation and scheduled overlay.

## Keyboard and saved sessions

- **A / S:** Home goal / behind.
- **K / L:** Away goal / behind.
- **Space:** Start or pause the AFL clock / training interval.
- **⌘Z / Ctrl+Z:** Undo latest AFL score / cricket delivery.

Shortcuts are suppressed while editing text or choosing values. Resetting a match,
clearing a queue, removing an event, and replacing a demo require confirmation.

Autosave is stored under .NET's `LocalApplicationData` directory in
`RocheScoreboardMacDemo/session.json`, with a `.bak` fallback. This directory is
independent of the Windows app's storage. Exported JSON contains the local demo,
not credentials. Logo paths refer to local files; copy those files too when sharing.
Clocks always restore paused rather than accumulating time while the app is closed.

## Development and validation

```bash
dotnet build macos/RocheScoreboard.Mac.slnx
dotnet test macos/RocheScoreboard.Mac.slnx
```

The Core project links the **existing** Windows `MatchManager`, `ScoreEvent`,
cricket models, `TrainingSession`, analytics, and AFL goal-run/drought services.
Rules are not reimplemented in another language. The Windows project itself has
not been converted to Avalonia or retargeted.

Packaging follows [Avalonia's macOS deployment guidance](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/deployment/macos.mdx)
and the [.NET 10 supported OS list](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

Tests exercise post-siren/quarter correction behavior, countdown expiry, cricket
extras/wickets/chase/undo, timer queues, paused restore, backup recovery, and actual
headless Avalonia control/output windows. Window tests render screenshots to
`$ROCHE_CAPTURE_DIR` or the temporary directory's `roche-mac-captures` folder.
Linux headless tests and cross-publishing cannot establish Mac title-bar, monitor,
file-picker, Gatekeeper, or Retina behavior.

### Mac acceptance checklist

1. Launch the `.app` on the target architecture. Open, move, resize, close and
   reopen the output. Try another monitor and Retina scaling.
2. Score both teams, undo, edit an earlier event, end/continue a quarter, and
   score after the siren. Check scores, summary and scoreworm remain consistent.
3. Start a one-minute countdown; verify expiry, pause/resume and elapsed editing.
4. In cricket, confirm the pair and bowler, record runs/extras/wicket, choose an
   incoming batter and next-over bowler, undo, and complete a chase.
5. Add short training intervals. Confirm auto-advance starts the next stopwatch
   immediately even during the completion notice and with the output closed.
6. Test team colours/logos, all presentations, ticker and announcements.
7. Export/import a demo, restart, and check all sports restore with clocks paused.
   Cancel each native file picker and each destructive-action confirmation.

The debug-panel research and proposed scenario catalogue are in
[DEBUG_PANEL_RESEARCH.md](DEBUG_PANEL_RESEARCH.md). Implement the outstanding port
parity before expanding that panel.
