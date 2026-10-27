# Sculpted glass and customisation

27 October 2026. Implemented in the native WPF operator interface.

## What changed

The previous material had too little scene detail to refract, a blue-grey wash that flattened dark controls, and similar optical strength on different surfaces. This pass changes those ingredients together. It uses original vector artwork and the existing WPF architecture; it does not add an Apple framework, web overlay or runtime dependency.

- The operator backdrop has a curved light ribbon and secondary reflection shape. Its colours and tonal variation remain visible through the glass. It is static when settled.
- Glass has stronger perpendicular edge refraction, directional rim light, restrained core shading and a small painted shadow. Labels stay native, sharp and outside the blur/shader pass.
- Dedicated decorative backdrops remain live behind the material. Hardware-supported frosting blurs only that background, avoiding an owner-window screenshot loop. Genuine dialog/menu backplates retain the guarded capture path.
- Pointer reflection eases into and out of the surface; press feedback remains immediate. Retargeting and unloading retire the finite animation clocks.
- Shared dark materials are neutral charcoal. Semantic button colours retain sufficient opacity for their text.

## Customisation

**Settings → Appearance → Glass & backdrop** is shared by global, AFL, cricket and training settings workspaces.

Glass intensity offers Soft, Balanced and Vivid. Backdrop offers Mono, Colour and Neutral. All nine combinations apply immediately and synchronize between loaded settings surfaces. New preferences default to Vivid and Colour. Existing saved choices are respected. Restore defaults resets only these two choices, preserving glass enablement and reduced motion.

Older preference files without the new fields retain their transparency/motion choices and receive the new material defaults. Invalid enum values recover to valid defaults. Saving continues through the existing atomic file writer.

Backdrop changes use a finite crossfade. Rapid reversal starts from the visible blend; it does not snap back or recursively retain old drawings. The cache holds at most the three current scene variants. Reduced motion applies the destination immediately. High contrast uses system colours; disabling glass removes the shader. No ongoing idle renderer was added.

## Structure and interaction

Sports selection is a Control Centre-style mosaic: AFL and cricket occupy the two main tiles, with a wide training module below. Tall narrow windows stack the modules; extremely short windows use a three-across fallback so the commands remain reachable. Settings and What's New remain floating controls. Arrow-key navigation follows the visible arrangement and activation remains immediate.

Setup navigation now pairs a quiet Back action with a prominent Continue/Start Match action. It reuses the existing named text elements, live bindings and event handlers. Original vector direction symbols participate in the shared finite interaction response. Frequent live scoring targets retain their established geography.

## Research and boundaries

This pass revisited the installed Apple design and motion guidance and the user's dark Control Centre and floating Apple Music references. The primary research was Apple's [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/) and [Get to know the new design system](https://developer.apple.com/videos/play/wwdc2025/356/). Their separation of content and controls informed the foreground/material boundary; the WPF implementation is an adaptation.

Public output windows, scorebugs, presentations, overlay scheduling, match algorithms and broadcast fonts were not redesigned. All 48 public rendering source files match the preceding refinement's hashes. The user's actual appearance preferences were also checked unchanged after isolated QA. No version bump, commit, push or release was performed.

## Verification

Current reports and actual native screenshots are under `artifacts/sculpted-glass/`. `verification-summary.json` records final build and regression results; earlier failed capture reports remain separately labelled. The intermediate material-image check exposed a screenshot cropping issue: rendering a centred child directly produced black crops. Capturing the whole native window and then cropping its actual bounds fixed the evidence collection without weakening the appearance assertions.

The current complete native suite passes 383 checks. Coverage includes all nine appearance combinations, legacy/invalid preferences, synchronized controls, native foreground baselines, visibly distinct material presets, bounded rapid backdrop reversals, hover interruption, fallback modes, lifecycle cleanup and small-window command access. Match-workflow and reliability results are recorded separately; overlapping suites are not unique coverage counts.

| Final check | Result |
|---|---|
| Complete native Control Centre suite, current Debug build | 383 checks; zero failures |
| Cricket workflows | 350 checks; zero failures |
| Operator workflows | 49 checks; zero failures |
| AFL reliability stress | 120,000 operations; 120,067 assertions; zero failures |
| Startup | One main window, sports selection visible, no startup exception |
| Debug and Release builds | Zero warnings and zero errors |
| Public rendering comparison | 48 files checked; zero changes |
| Actual saved appearance preferences | SHA-256 unchanged after isolated tests |
| Whitespace validation | Passed |
| Project knowledge graph | Refreshed using scoped AST-only update |

The screenshots show the real application rather than the earlier browser study. They verify bounds and final visual states; they cannot certify the user's subjective perception of fluidity or guarantee performance on every GPU. The saved Mono choice remains respected on this machine. Try Vivid with Colour to review the more expressive material direction.

## Review sequence

1. Open Appearance → Glass & backdrop. Compare all three intensities over Colour, then Mono and Neutral; check readability and live updates.
2. Hover, press and rapidly reverse tab/segmented selections. Check that the response settles without moving the click target.
3. Resize sports selection through wide, tall/narrow and very short windows. Open each mode and return immediately.
4. Navigate AFL and cricket setup forwards and backwards. Check the new action hierarchy and retained values.
5. Enable reduced motion and disable glass separately. Restore defaults and confirm those accessibility choices remain unchanged.
6. Operate a match and switch presentations. The public output design and scoring behaviour should remain familiar.
