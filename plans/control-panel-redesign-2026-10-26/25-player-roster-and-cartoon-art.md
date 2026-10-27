# Player roster, cartoon artwork and stronger liquid selection

27 October 2026. Operator interface review; no release or version change.

## Player workflow corrections

The remove controls were native Buttons but both handlers tested for Border senders. Their valid Click events were ignored. Both handlers now resolve the bound CricketPlayer from the source FrameworkElement and remove that exact record from the appropriate observable roster.

Home and away now share the same compact row structure: ellipsised player name, a 36-pixel three-dot edit action and a separate 36-pixel remove action. Inline captain/keeper buttons and duplicate keeper glyphs have been removed. The three dots opens the integrated player form directly, populated with current names and roles. Add and edit share validation, cancel behaviour and navigation-generation protection. Editing a player that has already left the roster cannot recreate it. Blank names cannot be saved, captain selection clears the previous captain, and multiple wicketkeepers remain supported.

Name changes notify FullName and DisplayName bindings, preserving the same player instance and native row instead of refreshing the entire roster. No persistence schema or match scoring rule changes were introduced.

## Visual and motion changes

Teams uses paired club shields; Players uses people, including in the cricket wizard. The shared selection geometry has a more pronounced travelling neck, asymmetric leading/trailing curvature and a bounded vertical contraction. The lens remains a single connected paint-only body driven by the existing velocity-aware springs. Retargeting, immediate commands, finite rendering loops, reduced motion and the stable resting capsule are retained. No second competing hover controller was added.

Sports-selection resources are now flatter two-dimensional AI-generated editorial cartoons: restrained ink outlines, limited flat shadowing, simple turf and sporting equipment, no cinematic stadium lighting or realistic textures. AFL, cricket and training all use the same illustration direction. Text remains native. Images are bundled, cached and frozen; they need no network at runtime. Only active v2 images are embedded, reducing artwork payload from about 5.2 MB to 4.0 MB rather than shipping both sets. Source selections and generation direction are in the artwork README.

## Research and skills

Used the installed Apple design and WPF reference skills plus built-in image generation. [Apple Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/) informed immediate response, interruption and continuity. This is a WPF adaptation, not a native Apple rendering implementation. No UI framework migration or additional dependency was introduced.

## Verification

Native evidence is in `artifacts/player-roster-liquid/`. Checks cover actual routed edit/remove clicks for both squads, populated edit forms, saving in place, immediate name binding updates, cancellation, role-only-in-form placement, distinct wizard icons, artwork resource loading, connected lens deformation/resting state, existing rapid navigation and reduced-motion behaviour. Screenshots of the actual native home, roster, add/edit sheets and moving tab lens were inspected. Broader operator, startup and cricket workflow suites were also run; see verification-summary.json for final results. Counts overlap and are not unique scenarios. Automated checks and screenshot review do not establish a guaranteed frame rate on every machine or replace the user's assessment of the artwork and motion.

Public presentation sources were not changed in this pass. Comparison with the earlier historical baseline still finds only the same three prior cricket overlay transition source changes recorded in earlier review notes. Real appearance preferences retain their pre-test hash.
