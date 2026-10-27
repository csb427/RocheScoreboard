# Coordinated motion and generated sport artwork

27 October 2026. Operator-only implementation following the hover/selection review.

## Shape and feedback corrections

`Border.CornerRadius` does not clip transformed WPF children to the same outline. The sports-card bodies now have an explicit, size-aware rounded clipping geometry. The image, hover wash and edge share that boundary. The extra full-card travelling glass slab no longer follows the pointer between rows; it could paint across a card's outline during its travel.

Training's hover transform was still connected to the old hidden icon. It now drives the visible training-card body, like AFL and cricket. All three use the same finite retargetable spring. The native launch buttons no longer run a second generic feedback controller over the card-specific controller. Commands still dispatch immediately.

The sports grid now stretches into its actual star row instead of setting a fixed height from estimated toolbar/header sizes. The estimate could crop Training's lower corners when the real heading occupied more space. Native layout checks compare the grid with its allocated slot, and short-window card actions remain covered by the existing size tests.

Player selection previously combined a rounded row with a generic rectangular adorner and a whole-list confirmation flash. The player row now owns its rounded hover surface and drawn selection check. The list container does not add a second flash. Generic legacy focus feedback is restricted to actual keyboard navigation.

Club presets use a smaller 152 × 62 tile with one name and a contained crest. The abbreviation and inset white crest box have been removed. Selection draws and pops the tick on the retained tile. Refreshing the same preset does not cancel that draw. A single shared component owns preset feedback; the generic white inset adorner and the separate legacy cricket hover-scale handlers are removed.

## Motion coordination and response

The operator's page stages now participate in shared surface entry, including setup, global settings and cricket/training consoles. The AFL tab handler also reveals its selected content through the same component. The existing cricket content transition remains its owner. Entries have a more visible unequal-axis expansion and short travel, while reduced motion produces an immediate stable state. No input waits for the visual transition.

The liquid tab lens no longer snaps to its target when a container-generation notification arrives during motion. Layout corrections retarget an active lens from its visible state. Its trailing edge has a slower response than its leading edge to make the connected liquid stretch more evident.

Idle controls no longer subscribe to a rendering event just because they load or re-enter a screen. Card motion stops when settled and cancels on hide/unload. Training no longer carries a separate full-card drop-shadow effect. These changes remove redundant work rather than adding a longer input delay.

## Generated artwork

The previous hand-built vector fields have been replaced with three AI-generated, stylised background paintings, using the built-in image-generation tool. Selected outputs and the final prompt set are recorded in [the artwork directory](../../Roche_Scoreboard/Resources/SportArtwork/README.md). The PNGs are embedded in the application and decoded once at 1536 pixels wide. No external file path, network request or API key is required at runtime.

Artwork keeps its aspect ratio at different card dimensions. A runtime opacity mask blends its dark left edge into the card background, avoiding a visible vertical image boundary. Text remains native WPF text. This artwork is exclusive to sports selection; public output screens and their presentations have not been redesigned.

## Guidance used

Applied Apple design, the animation implementation skill, image generation and the project WPF reference. The architecture remains WPF; no web animation framework or replacement theme system was added.

- [Apple motion guidance](https://developer.apple.com/design/human-interface-guidelines/motion): clear, optional, interruptible feedback.
- [Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/): direct response and continuity when redirecting motion.
- [Apple UI responsiveness](https://developer.apple.com/documentation/xcode/understanding-user-interface-responsiveness): distinguish delayed input from uneven rendering.
- [Microsoft WPF transforms](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/transforms-overview): use render transforms for visual movement without layout work.

## Verification

Native reports and screenshots live in `artifacts/coordinated-operator-motion/`. The verification summary records final build and suite results. Coverage includes generated resource loading, all three visible hover transforms, rounded clipping, rapid cross-card movement and render-loop retirement, smaller name-only presets, retained tick animation, and player selection's rounded feedback owner. Broader operator and cricket workflows are also run. Suites overlap, so counts are not unique coverage.

Native screenshots are inspected in addition to property checks. Local rendering-event observations are not monitor FPS and do not guarantee identical performance on every laptop. No release, version bump, commit or push is included in this pass.

Final Debug and Release builds each passed with zero warnings and errors. The final Release interface suite passed 386 checks, the focused run passed 197, cricket workflows passed 350, and the separate operator suite passed 49, all with zero failures. Startup checks passed. The final screenshots include 320 × 240 sports selection, each sport's active hover, compact preset selection and rounded player selection. The real appearance-preference file retained its original hash. Graphify and the whitespace check completed successfully.
