# Native Mac acceptance

The code migration covers the production launcher, all operator panels, output
presentations, dialogs and resource dictionaries. The default entry opens the
production application. The original Windows source remains the specification.
A preview build is available for native review; it is not a verified release.

## Evidence already available

The Linux headless run passed 36 checks, including the unchanged scoring engines,
the production launcher and three-step AFL setup, scoring/undo/clock and all four
console tabs, original operator mode checks, all six goal styles, ticker/overlay
priority, training controls and actual LibVLC frame decoding with transport,
volume, mute, speed and seek. Captures are generated under `ROCHE_CAPTURE_DIR`.
Headless checks do not exercise macOS window management or native integrations.

## Run on both Apple silicon and Intel

1. Extract the matching preview ZIP; run `Prepare Roche Scoreboard.command`.
   Confirm the app opens without an SDK or separate VLC installation, including
   after moving it to Applications. Confirm the Dock icon and menus.
2. Compare every screen with Windows at the same client size, theme and density.
   Compare gradients, clipping, icons, typography, button/input states, tooltips,
   dialogs, overlays and animation timing. Install the same original typefaces
   on both systems where available. The repository supplies no original font
   files; embedded Inter is the fallback for unavailable families.
3. Complete AFL setup and exercise Essentials/Advanced and all four tabs:
   scoring, corrections, undo, quarter changes, siren rules, clock direction,
   finals/extra time, shortcut editing, announcements and all output routes.
   Compare all six goal styles and behind animations, compact/classic layouts,
   every automatic/manual overlay and its priority, break rotation, stats,
   scoreworm, weather, screensaver and intro transitions.
4. Complete cricket setup, roster ordering and player selection. Exercise runs,
   wickets/dismissal details, all extras, batter/bowler changes, strike changes,
   undo, over/innings transitions, targets, summary and cricket overlays.
5. Exercise training timer add/edit/delete/reorder, presets, title editing,
   start/pause/reset/skip, time adjustments, final countdown, completion holds
   and next-timer transitions. Verify shortcuts while a text field has focus.
6. Exercise team library search/download/favourites and presets; raster/SVG/SVGZ
   imports, crop/transparent trimming and all colour controls. Test the screen
   eyedropper permission grant/denial on multiple Retina displays. TIFF uses
   Mac ImageIO; Windows WDP/JPEG-XR imports require conversion to PNG on Mac.
7. Play real audio/video, loop and seek, vary speed, mute/volume, change source,
   trigger goal-video takeover, return to the live output and compare the live
   preview. Check playback after pause, minimize, repeated output close/reopen
   and app shutdown. Check video overlays and that no native video surface
   obscures controls.
8. Resize, move and close/reopen each independent output. Compare configured
   physical dimensions and calibration at 100% and Retina scaling, and after
   moving between monitors. Native output borders/title bars are allowed.
9. Save/load matches, presets, settings and recovery snapshots; restart after
   an interrupted match, recover or discard, and verify paths/Unicode names.
   Check dark/light themes, comfortable/compact density and reduced motion.
10. With the existing Supabase configuration, pair the AFL and training cloud
    remote, test commands/snapshots/reconnect/unpair and show the QR code. Check
    weather fetch, forecast refresh and network-error presentation.
11. Use a disposable installed app to exercise a newer signed Mac release ZIP:
    architecture selection, progress/cancel, invalid digest/archive/signature,
    install permissions, restart and rollback. Preview/Demo assets are excluded
    from automatic updates. Never point an update check at a production match
    while doing this acceptance work.

Record Mac model, OS version, resolved fonts, captures, observed failures and
fixes. Only then mark `mac_acceptance_verified`, completed screens/features and
`ready_to_package` in `port-state.json`. Release packaging remains guarded until
those fields are complete. Public distribution signing/notarisation is separate
from the included local ad-hoc signing helper.
