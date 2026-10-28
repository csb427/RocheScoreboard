# Roche Scoreboard for Mac

A licence-free Avalonia port of the original WPF application, using the original
screen designs and controller code. The normal entry opens the production
launcher, AFL/cricket/training controls and independent output windows. Native
output borders/title bars are the accepted presentation difference.

The earlier basic demo is superseded. It is available only with `--legacy-demo`
for regression work; it is not the default application.

## Run from source on a Mac

Requires macOS 15 or later, the .NET 10 SDK, and the VLC application matching the
Mac's architecture in `/Applications`. Packaged builds include .NET and VLC.

```bash
dotnet run --project macos/RocheScoreboard.Mac
```

Alternatively open `Start Roche Scoreboard.command` in this directory. Optional
entry points are `--production-scorebug-preview`, `--design-gallery` and
`--afl-presentation-gallery`. `ROCHE_VLC_HOME` can select a VLC `lib` directory;
`ROCHE_DATA_HOME` isolates application settings, snapshots and preset files for
review without touching the normal user data.

## What is carried over

- All 34 production views (including MainWindow), 14 shared resource
  dictionaries, and the native equivalent of App initialization: 49 source
  XAML surfaces from Windows 5.0.0 at `636140761a9348b5030de731d3d91e5f741c1cbf`. All source identifiers and declared event handlers are tracked.
- Complete source code behind the views and MainWindow partials: scoring,
  clock/quarters/finals, undo/corrections/recovery, all four console tabs,
  Essentials/Advanced, keybindings, settings, teams, crop/colour dialogs,
  presentations, overlays, ticker, weather, cricket and training.
- The authoritative scoring/analytics/timer engines compile directly from the
  Windows source. Portable storage, weather and cloud remote code is reused.
- Native adapters for windows/dialogs/files/clipboard, pointer capture and drag
  ordering, themes, typography, timelines/easing, images/SVG, screen sampling,
  composited LibVLC video and application updates.

The original font declarations are preserved. The Windows repository includes
no typeface files; unavailable fonts use embedded Inter. Mac ImageIO adds TIFF
and other system image codecs. WDP/JPEG-XR is Windows-specific and must be
converted to PNG for Mac import. The original glass controls use their source
software fallback on Avalonia: gradient/material rendering and selection motion
are retained, while the Windows DirectX refraction shader is unavailable. This
requires a visual comparison on Mac before exact appearance can be signed off.

## Current verification status

The 5.0 source migration compiles for both native UI and shared engines. The
current regression results and local preview artifacts are recorded in
[PORT_VERIFICATION.md](PORT_VERIFICATION.md). Coverage includes current production
AFL navigation/scoring, new cricket rules and screens, small-window action access,
independent outputs, modal Escape/ownership, search, animation cleanup and a
35-second mouse/keyboard stress check. Native video decoding is exercised with
Linux LibVLC. These checks do not establish native Mac behavior or visual parity.

**Native Mac acceptance is still outstanding.** Retina/multiple displays, Mac
file/modal dialogs, screen sampling permissions, font comparison, cloud pairing
and update/restart must be exercised on macOS. The [acceptance guide](MAC_ACCEPTANCE.md)
lists the required native review. [port-state.json](port-state.json) distinguishes
implemented source coverage from verified acceptance; release packaging remains
guarded until acceptance is recorded.

## Packages

Create a self-contained local preview for either architecture:

```bash
bash macos/scripts/package-mac.sh osx-arm64 artifacts/macos --preview
bash macos/scripts/package-mac.sh osx-x64 artifacts/macos --preview
```

The script downloads the official VLC 3.0.24 runtime, verifies a pinned SHA-256,
selects the matching architecture, and bundles its libraries/plugins/data and
licence notices. Python 3 and the .NET 10 SDK are needed to package; Linux cross
packaging also needs `7zz` or `SEVEN_ZIP`. macOS uses `hdiutil`.

Extract the entire ZIP on a Mac and run `Prepare Roche Scoreboard.command` once.
It applies a local ad-hoc signature and opens `Roche Scoreboard.app`. Neither the
.NET SDK nor a separate VLC installation is needed by the packaged app. Local
previews are not Apple notarised. After native acceptance, omitting `--preview`
creates a release package; incomplete acceptance blocks that command.
If macOS blocks a local preview, its first launch may require **Open Anyway**
under System Settings → Privacy & Security.

## Maintaining the translation

The Windows source under `Roche_Scoreboard/` is the authority. The
[28 October preview refresh](../docs/refinement/2026-10-28-preview-refresh.md)
includes shared refinements recorded in [batch 1](../docs/refinement/2026-10-28-batch-1.md),
[batch 2](../docs/refinement/2026-10-28-batch-2.md) and
[batch 3](../docs/refinement/2026-10-28-batch-3.md) and
[batch 4](../docs/refinement/2026-10-28-batch-4.md);
model fixes compile directly on both platforms and view edits must be regenerated.
[production-source-contract.json](production-source-contract.json) records the
source inventory (49 XAML surfaces, 249 C# files, 530 UI event bindings).

```bash
python3 macos/tools/translate_production.py
python3 macos/tools/audit_parity.py --check-translation
dotnet build macos/RocheScoreboard.Mac.slnx -c Release
```

Edit the translator or native adapters, then regenerate. Files under
`Native/Generated/` carry source hashes; do not hand edit them. The older
`translate_scorebug.py`/`Presentation/ProductionScorebug` tree is an isolated
regression fixture, not the production output. `audit_parity.py --write-contract`
updates the source inventory after intentional Windows source changes.

The existing suite runs with `dotnet test macos/RocheScoreboard.Mac.slnx -c Release`.
Set `ROCHE_NATIVE_MEDIA=1` at build time to include the decoded-video integration
check when native LibVLC is installed. `ROCHE_CAPTURE_DIR` selects capture output.
The Mac CI workflow builds the source, checks translation freshness, runs the
existing suite and can create architecture-specific preview artifacts.

## Troubleshooting unresponsive input

Managed animations share one Background frame clock; animation updates, clock callbacks and decoded-video uploads yield to native
mouse/keyboard input. A local `input-diagnostics.log` in the `Roche_Scoreboard`
application-data directory records five-second UI heartbeats, input counts,
visible-window enabled states and active panel states. It does not record keys,
typed text or team names, and is limited to 1 MiB. This supplements `crash.log`
when diagnosing input stops that leave animations running.

The proposed [debug panel](DEBUG_PANEL_RESEARCH.md) remains deferred until native
acceptance of this port is complete.
