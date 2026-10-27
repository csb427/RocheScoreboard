# Liquid Glass operator refinement

26 October 2026. Implemented in the existing Visual Studio WPF application. Version remains 4.1.3. No commit, push or release.

## Design changes

The approved Control Centre references now inform the native interface more directly: black content surfaces, rounded floating navigation, sculpted commands, coloured original vector symbols, and selective translucent materials. Live scoring retains its essential control geography.

- Sports home has three floating sport tiles, a spatial lighting backdrop and compact utility actions. It reflows for short, narrow and portrait windows. Tile feedback retains velocity when interrupted; idle decorative animation no longer competes for attention.
- What's New is a reversible glass sheet. Search, New/Fixed filters, release hierarchy, empty-state recovery and independently scrolling content replace the old dense layout. Closing returns focus; rapid reopening cannot let an old close callback hide the new sheet.
- Shared settings use a floating category rail, coloured symbols, contextual headings, grouped preference rows and search. At narrow widths the rail becomes a category popover. Controls remain native WPF controls underneath their custom templates.
- Appearance and density use a continuous moving selection body beneath stationary labels. Both edges retain velocity when redirected, and ink changes as the lens reaches the label. Resizing and reduced motion settle directly.
- AFL's operator scoreboard gives totals greater emphasis, with supporting goals/behinds and the original scoring commands. The floating navigation, header, stoppage editor and contextual actions share the new surface family.
- Resume, updater, club finder, colour picker, crop, delivery and score-edit interfaces receive compatible glass shells and integrated window controls. Cricket decisions and player menus use the same material hierarchy. Three-dot menus open from their source, support keyboard navigation, and retire visually without delaying scoring commands.
- Buttons illuminate at the pointer, compress on press and recover with spring motion. Growth is bounded to avoid overlap. Search fields own a single focus indicator; a second feedback layer no longer adds an extra underline.

## Material implementation

This is an original WPF adaptation, not Apple's platform renderer. An embedded PS2 shader refracts only the background, adds directional edge highlights and responds to the pointer. Foreground text is not filtered. It requires no runtime compiler, new package or SDK installation.

In-window surfaces sample a dedicated spatial backdrop. Separate popovers can take a bounded, blurred snapshot of the owner once when opened. A material never captures an ancestor containing itself, avoiding recursive rendering. Hidden/unloaded popovers release captured content. There is no continuous window screenshot loop or perpetual material animation.

Opaque information surfaces remain available. A software material fallback handles unavailable shader support; high contrast uses system colours. Reduced motion bypasses movement. The current test machine reported WPF rendering tier 2 and loaded the embedded shader successfully. This is functional validation, not a frame-rate benchmark across all GPUs.

Native review caught and corrected transparent category buttons being tinted pale, duplicated input focus decoration, and contrast problems in the window caption. Existing tests that required the old menu placement and minimum-height property were updated to check the new anchored placement and actual reachable card size. Relative self-test paths are resolved to absolute paths so image fixtures load correctly.

## Verification

- Debug and Release builds: zero warnings and errors.
- Native design/material/layout suite: 189 checks, zero failures.
- Operator interaction suite: 49 checks, zero failures.
- Cricket workflows: 294 checks, zero failures.
- Startup: one main window, no startup exception, correct home and essentials/advanced navigation.
- AFL stress: 120,000 operations and 120,067 assertions, zero failures.
- All 48 public-rendering source hashes match the baseline captured before this pass.

Reports and native captures are in `artifacts/apple-glass-refinement/`. Final material/focus captures are in `final-native/`; the complete workflow, startup and stress reports are named `verified-*.json`. Tests use process-isolated preferences, recovery and diagnostics. No actual updater package was installed.

The public AFL/cricket/training output windows, scorebugs, presentations, score animations, overlays and screensaver rendering were not redesigned. Existing scoring algorithms, persistence schemas, target framework and package dependencies remain intact. The project adds the compiled shader as a resource.

## References

- [Apple: Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/) — selective functional materials, lensing, illumination and continuity from controls into menus.
- [Apple: Adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass) — material hierarchy and accessibility alternatives.
- [Apple: Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/310/) — desktop navigation and control hierarchy.
- [Microsoft: WPF ShaderEffect](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.shadereffect?view=windowsdesktop-10.0) — background effect integration.

Installed Apple Design, MicroKit, Transitions and WPF references were applied to the existing architecture. No Apple font or SF Symbols assets are bundled.

## Review in the app

1. Hover and press the sports tiles; resize home to a short or portrait window.
2. Open What's New, search/filter it, then close and immediately reopen it.
3. Switch settings categories and appearance/density, including rapid reversals.
4. Switch live tabs rapidly, resize, score and undo; hit targets should remain stable.
5. Open/cancel cricket player menus and decision sheets, plus colour/club/updater interfaces.
6. Confirm the public output presentations still look as before.
