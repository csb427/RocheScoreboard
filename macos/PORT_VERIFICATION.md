# Windows 5.0 Mac port verification

Baseline: `origin/master` at **636140761a9348b5030de731d3d91e5f741c1cbf** (5.0.0).
The port uses the separate `codex/mac-download-preview` branch. GitHub Actions preview
builds are authorized; master merges, deployment and release publication are excluded.
Research and existing user work are preserved. All **364 files** under the imported
Windows source match that commit; see `windows-baseline-verification.json`.

## Implementation

- All **49 XAML surfaces, 246 C# reference files and 530 declared UI event bindings**
  are inventoried. The translation audit passes. `source-coverage.json` accounts
  for 162 translated runtime files, 30 direct Core links, one direct Mac link,
  10 native equivalents, one scenario factory and 42 Windows QA references. Runtime controllers/partials are
  translated and authoritative models compile directly from Windows source.
- Latest launcher artwork, settings workspace, resume prompt, cricket roster/roles,
  delivery entry, workflow/presentation code and shared operator resources are included.
- Window bounds use physical desktop positions and logical client sizes; saved layouts
  survive missing monitors. Native wrappers provide scroll access on short displays,
  constraints fit work areas, owner/focus paths are retained, and outputs stay separate.
- Command-key canonicalization (including Cmd+F), safe optional animation lookup,
  keyboard activation, filtered visibility/selection events, two-way text templates,
  native drag/drop, drawing, font/resource and image adapters are implemented.
- Fixed runtime issues found during this pass: checked/unchecked feedback loop, sealed
  dynamic name registration, nested resource dictionaries taking over transform names,
  nullable default transforms, immutable brush casts, missing scroll templates, inherited
  unsupported fonts, paint layers covering text, and SVG image-brush support.
- Bounded scrollable screen roots and a native keyboard-focus reveal pass adjust
  nested viewports from inside out. Pointer focus does not trigger this scrolling.
- Managed animations share a Background frame clock. Existing input-starvation timeout,
  diagnostics and composited software-video protections are preserved.

## Linux runtime evidence

The final combined run passed **71/71 tests, 0 failed, 0 skipped**, with native
LibVLC decoding enabled (2026-10-28; 2.0432 minutes). The results are saved as
`mac-5-final-71.trx` in the evidence ZIP beside the preview archives.

Coverage includes source AFL setup/scoring/undo/clock/navigation, six goal styles,
output rendering/ticker/overlay interruption, training controls, new cricket rule
suites linked unchanged, modal ownership/Escape, search, long dialogs and independent
outputs. Small-window tests exercise actual source screens at 700×450, 480×320 and
420×280, focusing and scrolling actions into the client area. Pure placement checks
cover negative/disconnected monitor positions, scaling, invalid data and 386×193.

A **35-second** stress check keeps **120 repeating animations** active while mouse
and keyboard activations continue. This supplements, and does not replace, native
Mac investigation of the reported AFL freeze.

The sequence/fill animation test uses deterministic samples; the separate stress and
production UI tests exercise real elapsed-time clocks. Earlier fixed-delay animation
assertions were unreliable under simultaneous build load.

## Cross-builds and local artifacts

Both `osx-arm64` and `osx-x64` Release preview packages are built on Linux with .NET
and official architecture-matched VLC 3.0.24. The VLC downloads use pinned SHA-256
values. Final archives include build-input provenance, this report, platform audit,
Mac acceptance instructions and local signing/launch helpers.

Artifacts are local workspace files under `artifacts/macos-5-preview/`. No public
URL or uploaded attachment is claimed. Use the matching ZIP on a Mac, extract the
whole archive, and run `Prepare Roche Scoreboard.command` once for local ad-hoc
signing and launch. The packaged app does not need an SDK or separate VLC install.
Archive hashes and structure checks are in `artifact-manifest.json` beside them.

The dedicated preview workflow uploads authenticated GitHub Actions artifacts for
Apple silicon and Intel, including the corresponding source commit and verification
reports. It only builds, tests, signs and uploads preview artifacts; it does not
merge master, deploy, or publish a release. Artifacts expire after 30 days.
Headless tests on Mac runners still do not establish native desktop acceptance.

## Remaining acceptance and limits

**No macOS executor is available in this task.** Cocoa input and modal blocking,
Retina/mixed-display behavior, maximize/fullscreen/restore, popup edges, screen capture
permissions, native file dialogs, update/restart, cloud pairing and live weather error
paths have not been established. The reported native AFL freeze remains unverified.
See `MAC_ACCEPTANCE.md` for exact native steps.

The Windows DirectX glass shader uses the source software fallback. Original fonts
must be installed to compare their rendering; otherwise Inter is used. Cache metadata,
tooltip decoration ownership, contrast palette and format differences are itemized
in `PLATFORM_BEHAVIOR_AUDIT.md`, including reviewed empty methods and catches.
There are no remaining known compiler errors, but nullable/interop/XAML loader warnings
remain. Constructor-requiring dialogs are created explicitly, not via runtime lookup.

`ready_to_package` and `mac_acceptance_verified` remain **false** in `port-state.json`;
only explicit preview packaging is allowed. A verified release or an exact visual/
native-functionality parity claim requires the Mac acceptance matrix.

## Reproduction and changed files

With .NET 10 and a native LibVLC runtime available, run:

```bash
ROCHE_NATIVE_MEDIA=1 dotnet test macos/RocheScoreboard.Mac.Tests
python3 macos/tools/audit_parity.py --check-translation
bash macos/scripts/package-mac.sh osx-arm64 artifacts/macos-5-preview --preview
bash macos/scripts/package-mac.sh osx-x64 artifacts/macos-5-preview --preview
python3 macos/tools/verify_preview.py artifacts/macos-5-preview
```

On Linux, configure `LD_LIBRARY_PATH`/`VLC_PLUGIN_PATH` for the runtime, and set
`SEVEN_ZIP` to the actual 7-Zip binary if cross-packaging. The local .NET SDK is
`/workspace/.tools/dotnet/dotnet`. The exact working-tree inventory is in
`CHANGED_FILES.txt`; the main changes are the production source import, translator,
native adapters/overrides, four new regression files and existing test updates,
project references/assets, packaging/provenance scripts and these reports.

There were two cloud transport interruptions during final reruns. Saved work was
preserved and uncertain process state was inspected before resuming; the final
71-test success above is from the completed rerun after recovery.
