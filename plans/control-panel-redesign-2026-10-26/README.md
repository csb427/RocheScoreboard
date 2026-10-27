# Roche Scoreboard — Control Centre redesign

26 October 2026 · Native WPF implementation and review

## Current implementation

The user subsequently authorized implementation. Documents 01–14 preserve the planning and prototype stages; statements there that application code is untouched describe those earlier stages. Documents [15](15-native-implementation.md) through [25](25-player-roster-and-cartoon-art.md) record the native implementation. Document 26 records the separately requested cricket output pass; Document 25 is the latest operator-only review pass, including its verification evidence and remaining review limits.

Public-output redesign remains excluded. Documents 22–23 record the narrow cricket overlay-transition fixes specifically requested during review. The native operator interface is now the review surface; the earlier browser study is no longer required to review these changes.

## The direction

A black sporting workspace with a much stronger iOS Control Centre influence: sculpted glass action tiles, floating capsules, clear selection lenses and physical, interruptible interactions. The initial understated dashboard direction has been superseded following user review. Scoring keeps its familiar broad geography.

The approved brief for this proposal is:

- Keep the accepted Control Centre direction, refining it to glass for floating navigation/menus/sheets, quieter satin scoring/action buttons and matte information. The high-gloss first boards are an exploration rather than an all-controls requirement.
- Preserve the broad geography of essential AFL and cricket scoring controls. Individual controls can move where that improves clarity.
- Full layout freedom for sports selection, setup, settings, scorecard, navigation and operator pop-ups.
- Public output windows and presentation screens are EXCLUDED. Their layouts, typography, overlays, animations, scheduling, timing, standby, video and fallback behaviour remain unchanged. The operator Output tab is in scope as a control interface only.
- Strong, coherent animation and interactivity; colour supports accessibility, status and team identity.
- Detailed specification and visual concept boards preceded the authorized native implementation.

## Read this proposal

| Document | What it decides |
|---|---|
| [26 Cricket output order, charts and milestones](26-cricket-broadcast-order-charts-and-milestones.md) | Appearance order, extras forms, delayed boundary counts, milestones, scoreworm and Manhattan in the cricket broadcast style |
| [25 Player roster and cartoon artwork](25-player-roster-and-cartoon-art.md) | Working remove/edit actions, roles confined to player forms, distinct wizard icons, flatter generated cartoons and stronger liquid selection |
| [24 Coordinated motion and generated sport artwork](24-coordinated-motion-and-generated-art.md) | Rounded hover boundaries, Training motion, smaller preset tiles with retained tick animation, player selection feedback, page entry and bundled generated illustrations |
| [23 Illustrated launch and connected interactions](23-illustrated-launch-and-connected-interactions.md) | Illustrated sports selection, integrated player entry, shared club presets, selection contrast, retained scorecard views and coordinated cricket overlay backgrounds |
| [22 Response, performance and form refinement](22-response-performance-and-form-refinement.md) | Mouse/keyboard focus separation, scorecard reuse, cached transitions, native selection variants, AFL settings migration and current native checks |
| [21 Sculpted glass and customisation](21-sculpted-glass-and-customisation.md) | Stronger optics, live frosted backdrops, material preferences, sports mosaic and setup action hierarchy |
| [20 Materials, setup and selection corrections](20-materials-setup-and-selection-corrections.md) | Shared team editor, full-card format selection, category icon bounds and local glass sampling |
| [19 Fluid motion and installed skills](19-fluid-motion-and-installed-skills.md) | Reviewed skill installations, connected liquid selection, glyph-scale density transitions and lifecycle checks |
| [18 iPhone controls and navigation](18-iphone-controls-and-navigation.md) | Shared search, direct range/switch feedback, native command menus, club/player actions and setup handoff stability |
| [17 Apple interaction refinement](17-apple-interaction-refinement.md) | Latest native pass: semantic symbol motion, density reflow, setup structure, release browser and final checks |
| [16 Liquid Glass refinement](16-liquid-glass-refinement.md) | Native material renderer, shared operator components and preceding verification |
| [15 Native implementation](15-native-implementation.md) | Initial implementation and scope boundaries |
| [14 Fluid motion and coordinated resizing](14-fluid-motion-and-coordinated-resizing.md) | Latest discussion: liquid selection shape, press response, connected surfaces and animated density/type changes; explicitly not yet implemented |
| [12 Controls, information and adaptation](12-controls-information-and-adaptation.md) | Latest refinement: Apple character beyond glass, symbol consistency and adaptive structure |
| [13 Interactive study and verification](13-interactive-study-and-verification.md) | First isolated operator interaction study, actual browser checks and explicit limits |
| [11 Review refinement and first demonstration](11-review-refinement-and-first-demonstration.md) | Latest user feedback, material balance, proportion/navigation adjustments and the recommended first interaction sequence |
| [09 Revised Control Centre direction](09-control-centre-direction.md) | Start here: current visual direction, material/geometry decisions and operator-only boundary |
| [10 Motion, VFX and micro-interactions](10-motion-vfx-specification.md) | Stronger Apple-inspired interaction choreography, optical effects, interruption and cross-screen mapping |
| [01 Foundations](01-foundations.md) | Materials, colour, typography, iconography, spacing, component states and responsive structure |
| [02 Navigation and screen coverage](02-navigation-and-coverage.md) | Every current surface family, proposed destinations, shared versus sport-specific controls |
| [03 Motion and interaction](03-motion-and-interaction.md) | Interaction taxonomy, timing, physical behaviour, interruption, focus, scroll and performance contracts |
| [04 Screen specifications](04-screen-specifications.md) | Setup, live operation, scorecards, output studio, media, settings, training and every decision family |
| [06 Delivery and review](06-delivery-and-review.md) | Implementation sequencing for a later authorized phase, acceptance gates, stress scenarios and review questions |
| [07 Source inventory and research](07-source-inventory-and-research.md) | Static source evidence, existing seams, official research, limitations and unchanged-source evidence |
| [08 Concept boards](08-concept-boards.md) | Revised Control Centre boards, image prompts and interpretation notes |

## What the concepts show

1. AFL live scoring: sculpted team actions, paired scoring commands and bounded history.
2. Cricket live scoring: the familiar keypad, team context and anchored player actions in the same material family.
3. Home and application settings: Control Centre tiles, coloured category icons and grouped preferences.
4. Setup and integrated decisions: symmetric team editors and a common selection/sheet language.
5. Motion atlas: press, selection lens, switch, menu morph and sheet storyboards.

The images are visual proposals, not screenshots of a working implementation. Their small labels, logos and sample data are illustrative. The written state, accessibility and scoring specifications take precedence over generated image details. Real club assets must come from the existing catalog/user choices, not the illustrative monograms.

## Key decisions

- Keep one shared component and motion vocabulary across sports, with different commands where the sport requires them.
- Keep match status and public-output status distinct. A running match does not imply that its scorebug is currently on air.
- Keep frequently used scoring targets stationary. Show immediate press feedback; score/timer commits never wait for decorative animation.
- Preserve all current domain capabilities. Hide uncommon actions behind clear secondary paths rather than deleting unusual match cases.
- Use one feedback owner per control and one presentation controller per transient surface.
- Keep team colours on identities and selected team actions. Keep most settings, tables and containers neutral.
- Make global defaults and live-match adjustments visibly different scopes.
- Scope operator resources explicitly so new implicit styles, fonts, materials and motion cannot leak into public windows. Public output is a separate future task, not a delivery phase of this package.

## Deliberate design proposals, not existing features

The unified notification centre, consolidated sheet host, consistent performance/material fallback, revised navigation and any new training-session recovery are proposals. Existing recovery and updater logic should be reused, not assumed to already provide the complete proposed experience. DLS remains supplied revised overs/target entry; this proposal does not introduce a DLS calculation engine.

## Review checkpoints

Review the stronger Control Centre material direction first; then scoring proportions; then sheet/navigation behaviour and motion demonstrations. The next useful artifact after review is an isolated interactive operator-design prototype, if requested. It should not be wired into live scoring or shipped as the application.

Documents 09 and 10 are authoritative for revised styling and motion where older wording differs. Document 05 is a WITHDRAWN early draft, retained only to explain scope history; it is not an implementation instruction. Earlier generated public-output concepts are not part of the current proposal.

Latest refinement: the user likes the stronger Apple direction and floating navigation, and requested continued stage progression. Documents 12/13 record a first isolated interactive operator study after the static boards. This is design work with mock data; application implementation remains untouched. Documents 09–11 remain the foundation, with 12/13 authoritative for the newest control/information refinements and verification status.

## Planning baseline

Source inspected at commit `a52f049` with an existing dirty working tree. This is not a clean-commit snapshot. The 248 application source/configuration files selected for the integrity check had aggregate SHA-256 `C016AE9DBCBA78F1C35EB2028E441335C6A667E24914202CF3D1F07C1E8FB13B` before design work. Final verification is recorded in document 07.
