# 07 — Source inventory, research and integrity

26 October 2026. Static inspection and design work only. This is not a runtime audit, measured performance report or claim that the existing app has passed new tests.

## Inspected application structure

The current worktree was at `a52f049` with substantial existing uncommitted work. The analysis did not treat that work as a clean release snapshot. The production AFL workspace is MainWindow; older standalone views should not be mistaken for the complete operator UI.

| Area | Source evidence used for planning |
|---|---|
| AFL live scoring and history | `Roche_Scoreboard/MainWindow.xaml`: match strip around 149, Match around 268, paired team controls around 396, log around 513 |
| AFL operator Output / messages / Media | `MainWindow.xaml`: Output around 556, eight ticker slots around 861, Media around 1022 |
| AFL live settings and reset | `MainWindow.xaml`: Settings around 1193, reset surface around 2352 |
| Cricket operator shell | `Roche_Scoreboard/Views/CricketControlPanel.xaml`: run keypad around 207, delivery log around 287, Scorecard around 301, Output around 302, Settings around 332, toss around 409 |
| Training | `Roche_Scoreboard/Views/TrainingControlPanel.xaml`: timer transport, queue and builder around 175–240; it is a session workspace, not a cricket tab shell |
| Shared motion | `Roche_Scoreboard/Services/OperatorMotion.cs`: current role timings around 9–22; reuse/revise this seam deliberately rather than add another parallel animator |
| Shared surface entry | `Roche_Scoreboard/Views/SharedSurfaceMotion.cs`: entry/reset sequence around 45 |
| Disclosure | `Roche_Scoreboard/Services/DisclosureMotion.cs`: measured height animation around 33; distinguish bounded disclosure work from whole-workspace reflow |
| Presentation-button templates | `Roche_Scoreboard/Resources/OperatorControls.xaml`: replacement template around 203–210, considered with named parts expected by `CricketActionMotion.cs` |
| Cricket action feedback | `Roche_Scoreboard/Services/CricketActionMotion.cs`: action paint/feedback around 58–72; existing feedback ownership must remain singular |
| Settings reflow | `Roche_Scoreboard/Views/SettingsWorkspace.cs`: current responsive branch around 110–130 |

The read-only workflow inventory also covered sports home/recovery, both setup wizards, club/colour/crop tools, player selection, wicket/extras and delivery correction, scorecard transactions, supplied revised target/overs, innings completion, training queue operations, update/recovery prompts and native-alert seams. Their design coverage is captured in 02/04. These are source-located workflow facts, not proof that every path was exercised.

The graph file and wiki were absent at inspection (`graphify-out/graph.json` and `graphify-out/wiki/index.md`). No graph was installed/generated. No code changed, so no graph update was required. Read-only specialist audits were used under the improve-animations workflow; no agent was asked to implement or modify source.

## Vetted static design seams

| Priority | Evidence / limitation | Design response to evaluate later |
|---|---|---|
| High | Shared surface entry can reset a visual to a prescribed entry pose | Retarget from currently displayed state when a surface is interrupted or reused; test rapid reversals |
| High | Shared and specialised action feedback depend on ownership/template parts | One feedback owner and a verified template contract; no duplicate white selection rectangles |
| High | Current logs include nonvirtualized/rebuilt collections of visual rows | A later history refactor needs stable item identity, bounded scrolling and focus/scroll preservation; virtualization is not a visual-only claim |
| Medium | Measured-height disclosure animation can affect nearby layout | Reserve frequent scoring targets; constrain layout work to a bounded surface |
| Medium | Several components have their own timing/effect roles | Consolidate operator motion semantics through the existing seam, with deliberate per-role differences |
| Medium | Existing responsive helpers solve some dimensions but cannot prove every editor fits | Test explicit window classes, text scaling, field caret and footer access; avoid globally scaling a fixed canvas |
| Feasibility | Native WPF does not automatically supply Apple's material system | Prototype sculpted static/vector material first; treat backdrop/refraction as an optional measured experiment |

No finding here is a reported runtime diagnosis of the user's historical twitching. Actual frame traces, recordings and reproduction will be required before attributing a specific runtime cause.

## Primary research and how it was used

The following sources support general design/engineering principles. Exact Roche layouts, values and choreography in 09/10 are design proposals, not copies of Apple implementation values.

| Source | Design use |
|---|---|
| [Apple — Designing Fluid Interfaces, WWDC18](https://developer.apple.com/videos/play/wwdc2018/803/) | Response, continuity, redirectable interaction and the need for interactive demonstrations |
| [Apple — Meet Liquid Glass, WWDC25](https://developer.apple.com/videos/play/wwdc2025/219/) | Distinguish interactive material from content; legibility and accessibility variants |
| [Apple — Get to know the new design system, WWDC25](https://developer.apple.com/videos/play/wwdc2025/356/) | Shape relationships, concentricity, grouping and the relationship between source and task surface |
| [Apple — Build a SwiftUI app with the new design, WWDC25](https://developer.apple.com/videos/play/wwdc2025/323/) | Interactive controls and connected material transitions as references; its APIs do not run in WPF |
| [Apple Support — Use and customise Control Centre](https://support.apple.com/en-au/guide/iphone/iph59095ec58/ios) | Control grouping and expansion; translate touch-only discovery to explicit desktop controls |
| [Microsoft — Windows typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography) | Windows system typography rather than bundling Apple fonts |
| [Microsoft — WPF BlurEffect](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.blureffect?view=windowsdesktop-10.0) | Target-texture blur is not a ready-made backdrop material |
| [Microsoft — WPF Popup](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/popup) | Separate native-window boundary matters for effects, focus and material assumptions |
| [W3C — Contrast minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) | Contrast targets and composited-background evaluation as design references |
| [W3C — Animation from interactions](https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions.html) | Reduced nonessential motion as an alternative; no desktop WCAG certification is claimed |
| [Transitions.dev](https://transitions.dev/) | Reference families for menus, checks, tabs, switches and notifications; native adaptation only |

Apple HIG pages for materials/motion/accessibility were also located, but some return JavaScript shells to the research tool. The readable WWDC transcripts above were the substantive source. The Apple reference is the user's supplied Control Centre/Liquid Glass direction, not a claim to reproduce every feature of the latest Apple OS.

The MicroKit site was not accessible through the research tool. Its existing local repository was available: README, interaction definitions, and specific Blur Glide Menu / Sliding Content Tabs / Scrub Number Field metadata were inspected. Local Transitions skill recipes for source-menu morph, sliding tabs and switches were also read. No web framework, package or copied component was installed. A numeric drag control would need an explicit typed alternative and is not proposed for high-risk live scoring.

## Skills used

- Apple design: interaction continuity, source relationships, physical motion, materials and reduced modes.
- Improve animations: read-only inventory, specialist audit, evidence vetting and planning artifacts.
- Animation performance and animation vocabulary: distinguish an attractive effect from its rendering cost and describe the intended motion precisely.
- MicroKit UI and Transitions.dev: pattern selection and adaptation to WPF, with deliberate exclusions of unrelated promotional effects.
- Modern WPF guidance: platform feasibility and reusable controls; no framework/architecture migration implied.
- Image generation: visual boards and storyboards only. Prompt record is included in this package.

Available skills were selected by relevance. Not every installed skill was executed, and no claim is made to have interactively tested every effect in each external gallery.

## Integrity verification

The same read-only hash check was run before design work and after the revised package was authored:

| Check | Before | After |
|---|---|---|
| Application source/config files selected | 248 | 248 |
| Aggregate SHA-256 | `C016AE9DBCBA78F1C35EB2028E441335C6A667E24914202CF3D1F07C1E8FB13B` | `C016AE9DBCBA78F1C35EB2028E441335C6A667E24914202CF3D1F07C1E8FB13B` |

Selection: recursively under Roche_Scoreboard, excluding bin/obj, extensions `.cs`, `.xaml`, `.csproj`, `.json`, `.resx`, `.manifest`, sorted by full path. The aggregate hashes relative paths plus each file's SHA-256. It establishes equality of that selected source/config set, not a claim that unrelated pre-existing Git changes vanished.

Initial artifacts were design documents and concept copies under `plans/`; generated originals remain in the tool's generated-images folder. Subsequent stage progression added a separate browser design study in the thread's visualization directory; see 12/13. No application code or settings were modified. No full-app build, app launch, installation, update, commit, push or release was performed. The root DESIGN.md was not replaced. Public output received no implementation changes.
