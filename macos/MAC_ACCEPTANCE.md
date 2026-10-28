# Native Mac acceptance

The code migration covers the production launcher, all operator panels, output
presentations, dialogs and resource dictionaries. The default entry opens the
production application. The original Windows source remains the specification.
A preview build is available for native review; it is not a verified release.

## Evidence already available

See [PORT_VERIFICATION.md](PORT_VERIFICATION.md) for the current Windows 5.0
baseline, exact Linux results, captures and local artifact checks. Headless checks
exercise shared rules, source screens and software rendering. They do not exercise
macOS window management, screen permissions, Cocoa event delivery or native input.

The prior AFL mouse/keyboard freeze after roughly 30 seconds is **not confirmed
fixed on Mac**. A 35-second Linux animation/input stress check complements the
existing Background scheduling, video upload and input diagnostics protections.

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

## Window and input acceptance matrix for this port

Run on a small laptop and Retina display; repeat after connecting/disconnecting
an external display and changing scale. Include left/above-primary displays.

- Launch and restore the console and every dialog: all frame bounds inside the
  available work area, even after a saved monitor disappears. Check OS menu bar,
  Dock and display notches; physical desktop positions and logical sizes differ.
- Resize to 1280×720, 800×500, and approximately 480×320 client areas. Scroll every
  setup step, settings section, roster, event editor, delivery sheet and long list.
  Reach primary, cancel and close actions by pointer and Tab/Shift-Tab.
- Maximize, enter/exit native fullscreen, minimize and restore. Check minimum sizes,
  restored normal size, preserved output separation and no constraint oscillation.
- Open an owned modal from each output/control context. Check owner interaction is
  blocked by the native modal mechanism, Escape/Return, focus restoration, repeated
  open/cancel and closing the owner while a child is open.
- Open player menus, search results and context menus at every screen edge. Confirm
  native popup flipping/sliding and keyboard dismissal without trapped focus.
- Use 90-character team/player names, many roster entries, long messages and empty
  names. Confirm labels retain visible text and actions remain reachable by scrolling.
- Set each output to **386×193 physical pixels** at normal and Retina scale; move
  between differently scaled displays, then close/reopen it and restore saved bounds.
- Enter AFL and operate for at least 10 minutes with clocks, ticker, repeated goal
  animations and video overlays. Exercise mouse and keyboard every 10 seconds,
  including after 30 seconds, switching tabs, returning home and starting again.
  Repeat with Reduce Motion, Increase Contrast and glass preferences changed.
- If input stops, retain `input-diagnostics.log` and `crash.log`, and record active
  screen, video, elapsed time, whether menus/Tab/Return respond and window focus.

Windows DirectX glass refraction is replaced by the source's software fallback;
compare selected-tab ink, text contrast, material layers and all animation states.
