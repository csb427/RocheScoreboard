# Fluid motion and installed design skills

26–27 October 2026. The user requested continued research, useful new Codex skills and a stronger iPhone interaction feel, particularly the travelling selection body and compact/comfortable transitions. Version remains 4.1.3; no commit, push or release.

## Installed and verified

Installed through the Codex Skill Installer into the user's global Codex skills folder. Existing project skills were preserved. Each installation contains its entry point, supporting files, upstream MIT licence and an `installation-source.json` recording its pinned source revision and entry-point checksum. Automatic skill discovery becomes available on the next turn; the installed references were read directly during this implementation.

| Skill | Source | Revision | Role |
|---|---|---|---|
| `apple-liquid-glass` | [DanielJamesTronca/apple-liquid-glass-skill](https://github.com/DanielJamesTronca/apple-liquid-glass-skill) | `324b668c511556df6e5d85b082e2029a6e4ccb5a` | Apple-native platform reference: functional material hierarchy, navigation, accessibility and verification. Its Swift audit/API workflow is not a WPF implementation. |
| `fluid-design` | [kemiljk/fluid-design](https://github.com/kemiljk/fluid-design) | `667d1189f30a3484413958ee5307af2f06da82d5` | Physical continuity, interruption, momentum, layout transitions and reduced motion. |
| `design-motion-principles` | [kylezantos/design-motion-principles](https://github.com/kylezantos/design-motion-principles) | `4a9ca879f24a361f4dca4174fe2da0f67b5ddee3` | Context-sensitive motion construction and review, production polish, expression and accessibility. |

The reviewed alternatives included additional SwiftUI API catalogs, workflow-testing guidance and another Apple Design skill with a duplicate name. This pass adds focused capabilities without replacing the existing Apple Design, MicroKit, Transitions or WPF guidance. No app runtime package, Apple SDK or web framework was installed.

Installation evidence: `artifacts/apple-skill-refinement/installed-skills.json`. Review copies are under `skill-review/` in the same folder.

## Motion interpretation

The user has already chosen expressive Apple-like interaction. Production polish and physical continuity guide the implementation, with more playful deformation on the selection surface. Scoring commands continue to commit immediately. The native WPF implementation follows the existing architecture and operator-only resource boundary.

Apple's [Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/) and [Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/) were used alongside the new references. The relevant ideas are continuity, meaningful input response and a functional layer that moves organically. The implementation uses original WPF geometry and the existing material renderer.

## Implemented

### A connected travelling selection surface

Live tabs and segmented settings now share a connected liquid-body renderer. The leading and trailing edges retain their existing independent springs. The surface subtly stretches, changes its end-cap proportions, narrows through its middle and relaxes into its resting capsule. Only the painted background deforms; native labels, commands and hit targets retain their positions.

Surface width is itself continuous. Without that extra state, switching between pixel-rounded segment widths could change the travel cap by a pixel and make a reversal jump. Directional shape bias also changes continuously through zero velocity, avoiding an abrupt left/right silhouette flip.

Cached frozen brushes provide the material palette. Each redraw creates a small frozen vector geometry; there is no live screen-capture loop or layout-property animation. Solid-surface preference uses a flat fill, reduced motion goes directly to the selected state, and high contrast uses system highlight colours. Theme changes invalidate the surface through a dynamic resource dependency.

### Selection lifecycle fixes

- Clearing or removing the selected choice removes the retired highlight.
- Removed radio controls release the selection-specific local text colour.
- Reinserted choices and generated tab containers recover once layout is available.
- Segmented-label ink follows the actual painted body's bounds rather than an unclamped travel interval.
- Settled controls release their rendering callbacks and residual velocities. The live lens stops once edge error is below a small subpixel tolerance.

### Density changes resize glyphs

The preceding reflow used the TextBlock rectangle to derive its scale. A stretched label can keep the same rectangle width when its font changes, making character widths jump despite a vertical animation. The transition now records the current visible type scale, including a partly completed preceding transition. Left/centre/right text alignment and transform origin are included in the optical offset.

Native text, focus and selection remain live. The temporary visual transforms retire cleanly after repeated reversals. Fixed-size typography no longer stretches merely because its containing box changed width.

## Verification and evidence

Native checks cover 512 varied geometry poses, a single connected frozen body, container bounds, continuous reversal, no moving hit targets, clear/remove/reinsert behaviour, generated containers, no idle render loop, solid/glass preferences, reduced motion and density glyph-width continuity.

The initial focused run identified and corrected the segment-width reversal and selection cleanup cases. The generated-tab check explicitly selects its newly added item; the renderer does not take ownership of application selection. The final focused run passed 85 checks.

| Verification | Result | Evidence under `artifacts/apple-skill-refinement/` |
|---|---|---|
| Focused Apple interaction checks | 85 checks, zero failures | `focused-verified/apple-interaction-self-test.json` |
| Full native Control Centre checks | 273 checks, zero failures | `verified-native/control-centre-self-test.json` |
| Operator workflow checks | 49 checks, zero failures | `final-operator.json` |
| Debug cricket regression workflows | 350 checks, zero failures | `final-verified-debug-workflows.json` |
| AFL reliability stress | 120,000 operations; 120,067 assertions; zero failures | `final-afl-stress.json` |
| Normal startup | One main window; correct sport selection; no startup exception | `final-startup.json` |

These suites overlap; their check counts should not be added together as unique coverage.

Final Debug and Release builds both completed with zero warnings and zero errors. `git diff --check` passed. All three installed skill entry points match their recorded checksums.

Native UI tests produced actual rendered motion frames (`liquid-tabs-travel-*.png`, `liquid-segments-travel.png`) and a narrow layout capture. They are under the final native report directory in `artifacts/apple-skill-refinement/`. These are captures of the WPF implementation, so app review does not depend on the earlier inline visualization.

The broad workflow run also identified fixed-delay assumptions in existing cricket output tests. The cancellation and handoff checks now wait for their actual completion states with the existing bounded helper, retaining their original assertions. Public output implementation files are unchanged.

The public-rendering source baseline contains 48 SHA-256 hashes captured before this pass. Essential scoring geography, match algorithms, output presentations, overlays, fonts, package dependencies and target framework remain within their existing boundaries. Automated checks establish measured behaviour on this machine; visual feel and discoverability still require the user's review.

## Review in the app

1. Switch adjacent and distant tabs, then reverse halfway through the motion. Selection should respond immediately while the body continues from its current pose.
2. Repeatedly switch compact/comfortable. Letter width and height should resize together and settle sharply.
3. Switch theme and glass/solid preferences, then enable reduced motion. Selection and text must remain clear.
4. Resize setup, settings and live tabs. Check for stale highlights, clipped labels or moving click targets.
5. Score, undo and navigate normally; confirm public output retains its previous design.
