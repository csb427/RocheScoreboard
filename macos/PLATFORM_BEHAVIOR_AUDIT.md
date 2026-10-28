# Native behavior audit

Audited 2026-10-28 against Windows 5.0.0, baseline 636140761a9348b5030de731d3d91e5f741c1cbf.
This separates implemented source behavior from native platform acceptance.

## Explicit differences and compatibility limits

| Area | Implementation and remaining limit |
| --- | --- |
| Glass rendering | `native-overrides/Services/OperatorGlassEffect.cs` deliberately returns no WPF DirectX shader. The original `OperatorGlassSurface` software fallback draws material, gradients, sampled backdrop, edges and selection motion. Shader refraction and its shader-driven pointer lighting are unavailable; exact visual parity is unverified. |
| Keyboard | Command is canonicalized to Control for existing Windows command chords; Ctrl remains supported. Cmd+F is exercised against the source settings workspace. Native Cocoa event delivery still requires Mac acceptance. |
| Native windows | Cocoa/Avalonia own title bars, traffic lights, resizing, fullscreen and dragging. Win32 hooks, custom Windows chrome and minimum-track messages are replaced by native constraints/work-area fitting. Borders on outputs are allowed. |
| Fonts | Original declarations resolve to installed families, with embedded Inter for missing Windows fonts. Attached/inherited font declarations use the same resolver. Original font files are absent from the repository. |
| WPF Freezable/cache metadata | `Freeze` records clone guards; `NativeCacheMode` stores compatibility metadata. Avalonia/Skia own rendering/cache/thread affinity. These do not reproduce WPF bitmap-cache or Freezable internals. |
| Property value sources | `NativeValueSource` distinguishes locally set values from inherited/default values together. The only current caller tests local cursor intent, so that distinction is sufficient for its branch. `BindsTwoWayByDefault` metadata is not automatically reproduced; no translated source registration currently requests it. Explicit two-way bindings are preserved. |
| Tooltips | Explicit tooltip styling/animation is ported. `NativeToolTipPolicy.PlacementTarget` falls back to the operator window; exact per-tooltip ownership styling is approximate. This affects decoration, not command routing. |
| High contrast | macOS Increase Contrast and Reduce Motion are read through NSWorkspace and observed. App contrast colors are used, rather than Windows system palette values. Live macOS changes remain untested. |
| Popup layout | Native placement replaces WPF custom-placement callbacks. Flip/slide behavior is delegated to Avalonia and requires the edge-placement acceptance matrix on Mac. |
| Image formats | Raster/SVG/SVGZ loading, crop/trim and logo brushes are implemented. SVG/region images are rasterized for image-brush use (maximum 2048 pixels). WDP/JPEG-XR needs conversion to PNG. Mac ImageIO adds native formats. |
| Video | Software-decoded LibVLC frames are composited with the source foreground overlays; HWND airspace is removed. Real Linux decoding/transport tests pass. Native Mac audio/device behavior and long-run input require acceptance. Optional ffmpeg optimization needs ffmpeg on PATH and otherwise retains the original source file, matching the source fallback. |
| Scoring and workflow rules | Authoritative models compile directly from the Windows source. Six existing Windows cricket rule suites are linked unchanged into Mac tests; source controllers and partials are translated, not replaced by a simplified demo. |
| QA-only source | Windows-only QA harness files are not included in the application binary. Six pure rule check files are linked into tests. The cricket scenario factory and runtime debug partials are retained where referenced. Full Windows QA UI automation is not a claim of Mac acceptance. |

## Empty methods reviewed

- `NativeBitmap.BeginInit` defers actual image decoding to `EndInit`; it is an API
  compatibility boundary, not an omitted load operation.
- `NativeKeyboard.EnsureInitialized` triggers its static event subscriptions.
- Base `RenderNative`, `RenderBase`, template hooks and drawing-operation `Dispose`
  methods are intentional base/no-owned-resource operations. Concrete painters,
  text editing and image drawing have actual implementations.
- Range command `CanExecuteChanged` accessors are empty because the commands always
  execute against the current range parameter. Range values are clamped on execution.
- Property/routed observable `OnCompleted`/`OnError` terminal notifications have no
  operation; normal property and routed callbacks are invoked and not swallowed.

## Exception/fallback inventory

No event handlers or controls were removed merely to pass compilation.

- Native compatibility/property/event/drawing/window adapters added in this pass do
  not catch command-handler failures. Synchronous modal task exceptions propagate.
- Layout persistence catches storage/JSON failures so a failed save does not crash
  the console; corrupt/missing data falls back to a usable window layout.
- Existing image load/optional trimming catches log to Debug and return null/original
  images; source logo placeholders remain. SVG brush rasterization now has a regression.
- Existing video unload/disposal and failed frame allocation catches are retained;
  allocation failures write to Debug. Source media error/recovery paths remain.
- Existing crash/input logging suppresses IO/permission failures while writing logs;
  updater cleanup ignores unavailable staging files. Other update failures are reported
  through the existing progress/error flow.
- Original controller best-effort recovery, logo/color fallbacks, weather/network
  handling and media teardown catches are retained from the source. Their presence
  is not runtime validation of every exceptional branch.

## Still unrun on this executor

No macOS executor is available. Native modal blocking/focus, file pickers, screen
capture permission, Retina/mixed-display placement, fullscreen/restore, popup edges,
cloud pairing/reconnect, weather failures, update/restart and the reported AFL input
freeze require macOS acceptance. Linux rendering and successful ZIP construction do
not validate these behaviors. See MAC_ACCEPTANCE.md for a concrete matrix.

`graphify update .` could not be run: the graphify CLI is not installed in this
executor. Existing graph navigation used the documented NetworkX fallback; graph
metadata is stale and was not used as evidence of implementation or execution.
