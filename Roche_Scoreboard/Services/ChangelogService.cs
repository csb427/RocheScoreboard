using System;
using System.Collections.Generic;

namespace Roche_Scoreboard.Services;

/// <summary>
/// Provides an in-app changelog shown on the sport selection screen.
/// Add a one-liner per change to the current version's entry.
/// </summary>
internal static class ChangelogService
{
    private static readonly IReadOnlyList<ChangelogVersion> RawVersions =
    [
        new("5.0.0", "2026-10-28",
        [
            "[New] Full cricket scoring for limited-overs and multi-innings matches, with toss, lineups, extras and wickets",
            "[New] Editable cricket scorecards and delivery history, innings controls and revised overs or chase targets",
            "[New] Cricket batting and bowling cards, scoreworm, Manhattan and automatic match overlays",
            "[New] Four, six, wicket, half-century and century celebrations",
            "[New] Redesigned control panel across sport selection, setup, live scoring, settings and match prompts",
            "[New] Liquid-glass navigation, animated controls, team theming and layouts that adapt to smaller windows",
            "[New] Cricket club search includes Victorian Premier and VSDCA teams, club logos and saved presets",
            "[Fixed] Safer match recovery, more stable navigation and consistent shared output sizing"
        ]),
        new("4.1.3", "2026-09-05",
        [
            "[Fixed] Goal-run text is centred and drought timers are larger with consistent scorebug typography",
            "[Fixed] Gold team-direction arrows flash clearly without stretching or moving overlay text",
            "[Fixed] Stoppage time uses one clear live editor with smooth cancellation and shared match clock controls",
            "[New] Stoppage editor keyboard adjustments: Up/Down changes by one, Shift changes by ten",
            "[Fixed] Automatic overlays recover after clock corrections and match restoration",
            "[Fixed] Informational overlays stay clear of the final 30 seconds of a quarter",
            "[Fixed] Overlay scheduling handles invalid animation durations and clears old activity records",
            "[Fixed] Safer saved-window and training-timer recovery, with lighter club-logo processing"
        ]),
        new("4.1.2", "2026-09-04",
        [
            "[New] Distance scorebug removes the top bar to give team names and fixed-capacity scores more room, with wall time integrated beside the match clock",
            "[New] Essentials view gives volunteer operators a simpler Match-and-Setup workspace while keeping specialist tools one click away",
            "[Fixed] Goal-run and drought takeovers now keep their featured team visible, centre larger text and show a pulsing team-direction marker",
            "[Fixed] Last-score and team-drought timers reserve stable space for every supported time without clipping or shifting",
            "[Fixed] The count-up stoppage clock now supports live quick adjustments and a clearly separated editor without changing the scoreboard clock",
            "[Fixed] Ticker messages use a larger overlay-height bar, clearer saved slots and lighter live editing without per-keystroke disk or cloud work",
            "[Fixed] Distance layout now supports live scoring, quarter changes, clock modes, finals colours, overlays, animations and remote controls without switching layouts",
            "[Fixed] Ticker slots validate saved content, recover cleanly and animate the output lane without sudden score-panel jumps",
            "[Fixed] Quarter and timer numerals keep one clear broadcast-signage size without shifting as the clock ticks",
            "[Fixed] Essentials is the default volunteer workspace, clearly labels the current mode and remembers intentional Advanced use",
            "[Fixed] GitHub releases now provide a permanent one-click Windows download plus built-in extraction and dependency guidance",
            "[Fixed] Essentials now behaves as a true two-tab workflow with clear Match and Setup labels and intuitive Ctrl+1/Ctrl+2 navigation",
            "[Fixed] Compact Distance outputs protect more width for the quarter clock while presentation headers are larger and consistent",
            "[Fixed] Presentation score bars use a compact quarter-and-live-minute display, leaving more room for fixed-size scores and logos",
            "[New] Eight reusable ticker slots provide individual on-air, colour, highlight and clear controls",
            "[New] Goal-run and playing-time drought stories carry across quarters and remain available for manual viewing",
            "[New] A neutral animated sports screensaver appears before setup and after reset, with optional club image or video",
            "[New] Team stories use centred oversized figures, larger two-line context and calm interruption-safe motion",
            "[New] Quarter breaks use full-height team-colour fields, centred goals.behinds and totals, plus a rotating large break label and wall clock",
            "[Fixed] Recent scores use clear club-colour cards, crest identity and a full-card pulse for the newest event",
            "[Fixed] Manual and automatic overlays now move their text, bars and blurred identity backgrounds as one transition",
            "[Fixed] TV Signal is the default goal animation and the selected style now survives setup and match restore",
            "[Fixed] Long team names stay still and trim cleanly, while drought timers drop unnecessary leading zeroes",
            "[Fixed] Compact output sizes protect live scores, larger weather values, team identity and smart contrast",
            "[Fixed] Match navigation, internal scrolling and output controls stay in place during focus, hover and clicks at smaller window sizes",
            "[Fixed] Setup navigation restores pointer input immediately after backing out, including rapid select-and-return actions",
            "[Fixed] Match Log now fills the available scoring column beneath Remote Scoring",
            "[New] Output controls add one-click overlay cancellation and clearer animated ticker switches",
            "[New] Team colours use an app-native colour wheel, hex entry, defaults and shared recent-colour history",
            "[Fixed] Tickers reserve team-name rows, match statistics prioritise the data area, and weather uses clearer broadcast-safe typography",
            "[Fixed] Application preferences are separate from live AFL match settings, with simpler match, controls, scoreboard, automation, assets and system categories",
            "[Fixed] Quarter state now survives scoreless periods, post-siren scores, corrections, restarts and accidental repeated End Quarter commands",
            "[Fixed] Match recovery, presets, keybinds and layouts use atomic saves with automatic backup recovery if a file is interrupted or damaged",
            "[Fixed] Rapid presentation switching no longer crashes Match Stats, and Margin Flow keeps every axis value fully visible",
            "[Fixed] Goal-run and drought scheduling now resets cleanly, prioritises live events and uses smoother ordered quarter-break folds",
            "[Fixed] Match Log stays independently scrollable, control-panel clicks no longer resize nearby controls, and break scoring is evenly centred",
            "[Fixed] Rapid navigation, repeated presentation changes and Windows clock corrections no longer leave stale animations, shifted controls or stalled overlays",
            "[Fixed] Score animations now recover from missing media callbacks, bound rapid-input backlogs and ignore stale display-window events",
            "[Fixed] Updates now reject incomplete or unsafe packages, recover from stalled downloads and restore the complete previous release if installation or relaunch fails",
            "[Fixed] Remote controls always converge on the latest match state during rapid scoring or weak network conditions",
            "[Fixed] Weather refreshes cancel stale requests so an older location can no longer overwrite the current venue",
            "[New] Match recovery and software update prompts now provide clearer decisions, saved-match context and accessible keyboard-safe controls",
            "[Fixed] Updates show download, verification, extraction and restart stages without freezing the control panel",
            "[Fixed] Saved matches self-repair from the last valid recovery copy, and discarding a recovery now requires confirmation"
        ]),
        new("4.1.1", "2026-08-29",
        [
            "[Fixed] Quarter Breakdown, Margin Flow and Match Stats now keep identical typography and layout through repeated switching, scoring and output resizing",
            "[Fixed] The scorebug moves its quarter and timer to the bottom only for horizontally thin output shapes, not simply smaller windows",
            "[Fixed] Compact overlays protect score readability, simplify secondary detail and keep the top information bar from overlapping"
        ]),
        new("4.1.0", "2026-08-29",
        [
            "[New] Australian Club Library now includes every AFL and VAFA club plus all APS and AGSV member schools",
            "[New] Official club marks are cached as transparent PNGs with edge-aware background cleanup",
            "[New] Team search adds league, state, saved-team and sorting controls, remembers its filters, and keeps clearer selection motion",
            "[New] Goal moments use distinct broadcast styles with automatic text contrast",
            "[Fixed] +6 and +1 cards now keep their numerals visible above the scorebug swipe in every team colour",
            "[Fixed] The main scorebug information bar is larger while score rows remain fixed-capacity and space-efficient",
            "[Fixed] AFL workspaces, setup defaults, venue weather and recent team presets are remembered so repeat matches need fewer clicks",
            "[Fixed] Windows 10 and 11 releases use a verified self-contained package with one clear GitHub download",
            "[Fixed] Scoring runs use the latest quarter sequence, recognise dominant bursts with limited replies and trigger during timing stoppages",
            "[Fixed] Match Log score cards now carry clear home and away team colouring",
            "[Fixed] Smart contrast now checks full gradients and hover states across Match Log cards and every AFL presentation",
            "[Fixed] Goal animation choice is remembered, with TV used as the reliable default and missing-video fallback",
            "[Fixed] Timing stoppages stay in the operator-selected state when setup or Settings is reopened",
            "[Fixed] Repeated presentation selections no longer layer old transitions, and compact screens protect logos, totals and overlays",
            "[Fixed] Quarter Breakdown, Margin Flow and Match Stats now retain one stable live layout instead of replaying stale refresh states"
        ]),
        new("4.0.0", "2026-08-22",
        [
            "[New] Australian Club Library adds fast team search, league and state filters, verified club identity data, one-click home or away setup and favourites that flow directly into saved team presets; the first verified catalogue includes every AFL club",
            "[New] Club logos are downloaded only when selected, cached locally as transparent PNG files and passed through an edge-connected background remover when a source image has an opaque mat, preserving white details inside the crest",
            "[Fixed] Every built-in goal animation now uses the GOAL wordmark, numeric +6 and +1 feedback is rendered above its swipe layer on the scorebug and every presentation, and quarter-break labels expand to fill the centre scorebar cell",
            "[Redesigned] The AFL operator tabs now form four true edge-to-edge equal-width segments with a full-width selected rail and compact labels at narrower window sizes",
            "[New] Finals presentation system adds a consistent restrained-gold broadcast palette across the live scorebug, quarter breakdown, margin flow, statistics and weather without covering the output in a global tint",
            "[Redesigned] AFL scorebug establishes a distance-first hierarchy with dominant total scores, protected logo space, responsive team names and fixed-capacity score numerals that never jump in size as values change",
            "[Redesigned] Quarter Breakdown presents true quarter-by-quarter scoring with permanent cells, 1ST–4TH labels, current-quarter emphasis and strong team-colour separation",
            "[Improved] Presentation typography now uses the largest safe fixed-capacity sizing across quarter totals, goals.behinds, statistics, weather, graph axes and cricket data; decorative microtext has been removed or promoted into readable labels",
            "[Redesigned] Margin Flow uses independent dynamic ranges above and below zero, sparse coloured axis labels, quarter markers and team-tinted graph halves without redundant scoring-zone labels",
            "[Improved] Every AFL presentation now shares a live bottom score bar with maximum-size triple-digit totals, protected logos, quarter state and a continuously updating match clock; unreadable duplicate goals.behinds micro-text has been removed",
            "[Redesigned] Quarter Score overlay replaces six cramped goals, behinds and points cells with large team abbreviations and triple-digit-safe quarter totals while the complete live score remains visible below",
            "[Improved] Scorebug weather, forecast, rain and recent-score overlays show fewer, larger information groups so labels remain readable on compact physical scoreboards instead of shrinking to fit dense rows",
            "[Improved] AFL presentation layouts are pixel-aware and reflow across changing output dimensions instead of relying on a fixed 386 × 193 surface",
            "[Redesigned] Match Stats and Weather use the same presentation chrome, score hierarchy and contrast rules; fixed-size team abbreviations and proportional weather rows prevent unreadable or clipped text",
            "[New] The five-minute warning is a dedicated full-width slide-down alert that preserves the live quarter, timer, teams and scores beneath it",
            "[Redesigned] AFL Match Control, Output Studio, Media Desk and Match Setup share one navigation and card system with a persistent live match-state bar",
            "[Redesigned] AFL control-panel navigation now uses a responsive four-destination dock with compact labels, optional context subtitles and equal-width ownership so tabs no longer clip or drift outside the window",
            "[Redesigned] Built-in goal moments are now five clearly different packages: Signal television sweeps, Volt charge strobes, Epic team-identity drama, Pulse score-first restraint and Scoreboard retro lamp flicker; custom video remains unchanged",
            "[Improved] Every full-screen AFL presentation now mirrors live scoring with a team-colour +6 or +1 wipe, while goals add a stronger two-beat light response without hiding the quarter or match clock",
            "[Improved] Margin Flow now uses traditional straight horizontal runs and 90-degree score switches with square joins, sparse milestone markers and the existing independent team-side axis scaling",
            "[Improved] Buttons, toggles, tabs, option reveals and setup transitions now use one consistent responsive motion system with clearer hover, press and selection feedback",
            "[Redesigned] Global Settings has denser category navigation, clearer grouping, stronger contrast and improved responsive behaviour across narrow and large windows",
            "[New] What’s New centre provides a searchable, filterable release history with version navigation and a concise current-release summary",
            "[Improved] Auto-update checks distinguish failures from up-to-date results, select compatible release packages explicitly and verify downloaded package integrity before installation",
            "[Fixed] Public-output switching and manual overlays recover and synchronise the live scorebug before firing, so presentation changes update immediately instead of leaving stale state",
            "[Fixed] Saved matches are only deleted through an explicit Discard action, Cricket tab selection no longer hits invalid animation scopes, and presentation empty/error states fail visibly instead of showing fabricated data",
            "[Fixed] Match Setup feature cards use high-contrast vector-style symbols and readable idle-state labels instead of dark colour-emoji glyphs disappearing into black panels",
            "[Fixed] The Match Control result chip now identifies a scoreless draw instead of rendering as an unexplained empty outlined box",
            "[Redesigned] Output Studio is a clearer broadcast workspace with larger presentation controls, a dedicated ticker editor, useful one-click message templates, an on-air count and an intentional empty state",
            "[Improved] Ticker edits, highlights and removals now use one synchronised update path so the live output, cloud remote and saved match state cannot drift apart",
            "[New] Appearance includes a safe goal-effect preview plus live score-capacity, responsive-output and smart-contrast checks, making presentation choices easier to validate before going on air",
            "[Fixed] Training timer titles are length-limited and legacy recent timers are sanitised, preventing malformed saved labels from overrunning the operator console",
            "[New] Cricket Match Centre adds live phase, run-rate trend, projection range, wickets in hand, chase equation, momentum, recent overs and fall-of-wickets analysis",
            "[Improved] Cricket is now a first-class saved-match and cloud-remote mode with live innings recovery, dedicated remote state and delivery, undo, selection and next-innings commands",
            "[Fixed] Cricket scoring now follows runs/wickets convention, preserves prior innings scorecards, restores exact pre-ball state on undo and prevents scoring through unresolved batter or bowler selections",
            "[Fixed] Cricket no-balls, wides, balls faced, maidens, dot-ball percentage, powerplays and multi-innings results now use consistent cricket scoring rules",
            "[Improved] Cricket setup validates playable squads, uses readable vector-style format/weather symbols and presents stronger idle-state contrast"
        ]),
        new("3.4.0", "2026-05-10",
        [
            "[New] Scoreboard control panel countdown for Count Up matches — the setup wizard now offers a default-on countdown option, the control panel shows an independent stoppage timer once the game starts, and the scoreboard clock auto-pauses when that timer reaches zero without auto-ending the quarter",
            "[Improved] Control panel countdown flow and messaging — the countdown card and toggle were polished for clearer operator guidance, smoother visibility transitions, and tighter placement alongside the existing goal/behind and team score controls",
            "[Fixed] Settings navigation and keybind panel compile errors — restored the missing All category pill and added the keybind capture click handler so the main window builds cleanly again",
        ]),
        new("3.3.0", "2026-05-09",
        [
            "[Removed] Music Mode \u2014 the GSMTC-driven now-playing screen, its WASAPI loopback FFT visualiser, the presentation card, the break-screen rotation entry and all associated audio/session services have been removed in full",
            "[New] 5-minute warning overlay completely redesigned in broadcast style \u2014 red\u2192black radial vignette replaces the flat linear backdrop, real diagonal hazard stripes scroll across the panel via a tiled DrawingBrush + diagonal cut mask, a white scanline sweeps across every 1.6s, the border glow pulses on a sine curve, and a bright white entry flash wipes 0\u21920.9\u21920 in 420ms when the overlay first appears",
            "[Improved] 5-minute warning overlay typography \u2014 primary \u00225 MIN\u0022 line bumped to 1.85\u00d7 with heavier 34px drop shadow; subtitle \u0022WARNING\u0022 tightened to 0.78\u00d7 with extra letter spacing (5px), amber #FFC84A tint and flanking \u26A0 chevrons; whole letter group sits inside an italic SkewTransform (-8\u00B0) with a very subtle 1.00 \u2194 1.025 / 820ms heartbeat scale so motion is felt without making the text wobble",
            "[Improved] 5-minute warning hazard stripes restricted to thin top + bottom caution bands via a vertical opacity-mask, with thin amber #FFB020 accent lines just below each band; the centre of the panel stays a clean red vignette so the white \u00225 MIN\u0022 punches and remains the hero",
        ]),
        new("3.2.2", "2026-05-08",
        [
            "[New] Training mode \u2014 \u201cup next\u201d bar redesigned: bigger, evenly-sized 96 px label/title text and a punchy 112 px orange countdown timer; long titles ellipsise cleanly and the whole bar is now centred vertically within its strip instead of crowding the screen edge",
            "[New] Training mode \u2014 active timer enlarged to a bolder 420 px digit block with a brighter blue glow, kept perfectly centred via the existing flex-spacer rows so it never collides with the up-next bar",
            "[Fixed] Training mode \u2014 \u201cup next\u201d bar text was invisible on first show. The bar previously animated from Height=0 with ClipToBounds=True, which caused WPF to bake a 0-height child measure pass and skip drawing the text. The bar is now always measured at its natural size and toggled via Visibility + opacity fade, so the text appears the instant the bar reveals",
            "[Fixed] Training mode \u2014 up-next bar was being vertically clipped by the 1920\u00d71080 canvas because the timer/title/segment stack exceeded the canvas height; sizes rebalanced so everything renders fully on screen",
            "[Fixed] Remote scoring website \u2014 embedded Kestrel host now resolves wwwroot from Environment.ProcessPath instead of AppContext.BaseDirectory, so the single-file build no longer points the static-file server at the per-launch %TEMP%\\.net extraction folder; ContentRootPath was wrong too, which is why the \u201cerror generating link\u201d message appeared on every fresh install",
            "[Fixed] Auto-update apply path \u2014 updater now renames the running EXE to .old before robocopy runs. Windows allows renaming an in-use executable even when overwrite would fail, so a lingering AV/prefetch handle can no longer cause robocopy to silently \u201csucceed\u201d while leaving the user on the previous version. Adds post-copy verification with .old fallback, Unblock-File to strip Mark-of-the-Web from the new EXE (so SmartScreen doesn\u2019t block the relaunch), and queued cleanup of the .old backup once it\u2019s unlocked",
            "[Fixed] Weather presentation screen \u2014 weather icons rendered nearly black on the dark background because Segoe UI Emoji\u2019s colour bitmap ignores Foreground; the U+FE0F emoji-presentation selector is now stripped from the snapshot icon and the TextBlock falls back to Segoe UI Symbol monochrome with a bright amber Foreground + glow",
            "[Fixed] Settings screen \u2014 weather location combo box rendered black-on-dark; switched to white text on a dark input background, and brightened the dim status/helper label colours (#3A5A80, #3A5070, #5080AA, #30363D, #484F58, etc.) to readable complementary tints",
            "[Fixed] Settings screen \u2014 WEB ACCESS card text was very dark green on near-black; status, section labels and helper text bumped to brighter greens (#7FE39B / #9DD9AC) for proper contrast",
        ]),
        new("3.2.1", "2026-05-07",
        [
            "[Fixed] Goal animation pipeline rebuilt \u2014 score now stays on the old value until the goal animation (default or MP4) has finished, then the +6 / +1 swipe rises and the digits flip in sync with the swipe reveal",
            "[Fixed] Away-side custom MP4 sometimes failed to appear on the scoreboard \u2014 VideoPlayerControl no longer inherits the previous clip's suspend bookkeeping, so each fresh goal video always starts from frame 0",
            "[Fixed] Back-to-back goal clicks no longer desync \u2014 score events are queued, the per-side lock arms BEFORE the model mutation so MatchChanged can\u2019t leak the new score onto the scoreboard early, and the next animation only starts after the prior swipe slide-out completes",
            "[Fixed] Race in VideoPlayerControl.SetVideoSource where calling VLC.Stop() then immediately reassigning Media occasionally killed the next clip; the source swap now pauses, replaces Media cleanly, and lets Play() start the new clip",
            "[Improved] Expanded scorebug \u2014 team-name bar is a touch taller so the team font auto-scales up, while goal / behind / total cell heights are unchanged so the score digits keep their original size",
        ]),
        new("3.2.0", "2026-05-06",
        [
            "[New] Video playback diagnostics & telemetry layer — structured log file at %LOCALAPPDATA%\\Roche_Scoreboard\\video.log, in-memory ring buffer surfaced on the VIDEO tab, and per-event severity (Info/Warn/Error) across source-set, Play/Pause/Stop, watchdog and decoder fallback paths",
            "[New] LibVLC native runtime validation at startup — App.OnStartup now calls VlcRuntime.ValidateOrThrow() so missing or broken native VLC libraries surface as a clear startup diagnostic instead of failing silently when the first clip is loaded",
            "[New] Frozen-playback watchdog — the player monitors VLC time progression after Play() and, if the stream stalls for 4 seconds, transparently retries once with software decoding before reporting failure to the operator",
            "[New] Isolated playback test mode — StartIsolatedPlaybackTest / StopIsolatedPlaybackTest let a clip be looped through the takeover surface in isolation for diagnosing render or decode issues without touching the live scoreboard state",
            "[New] Emergency VLC-only fallback window — a borderless, opaque, top-left, software-decoded EmergencyVideoWindow can be engaged if embedded WPF hosting becomes unstable, bypassing the WPF composition pipeline entirely",
            "[New] VIDEO tab telemetry overlay — playback mode, hardware vs software decode state, takeover state and recent diagnostic entries are now shown above the live preview mirror",
            "[Fixed] Switching to the video surface no longer auto-plays — pressing ‘Show on Scoreboard’ now displays the loaded clip paused on its current frame; the operator must explicitly press Play (or Restart) to start broadcast playback",
            "[Improved] VlcRuntime hardened — initialisation errors are captured and exposed via IsAvailable / InitializationError, and Instance now throws fast with a clear message instead of failing later inside the player control",
        ]),
        new("3.1.1", "2026-05-05",
        [
            "[Fixed] Auto-updater now actually replaces the installed exe — for the single-file self-contained build, AppContext.BaseDirectory points at a per-launch %TEMP%\\.net extraction folder, so the previous updater was robocopying the new release into that throwaway directory and the real Roche_Scoreboard.exe on disk was never touched (next launch just re-extracted the old bundle). The install directory is now derived from Environment.ProcessPath, so the update is applied to the genuine install location and persists across relaunches",
            "[Fixed] First-launch update prompt — App.OnStartup was awaiting the GitHub check on the UI thread, but as soon as it suspended the dispatcher created MainWindow and ran ShowDeferredAlerts BEFORE the network call returned, so UpdateAvailable was still false and the prompt never appeared. The check now runs as a background task exposed via App.UpdateCheckTask, and MainWindow awaits that task before deciding to show the prompt",
        ]),
        new("3.1.0", "2026-05-04",
        [
            "[Fixed] Custom video playback no longer spawns a separate floating VLC window — the player now correctly embeds inside the scoreboard display in the top-left; loading a clip just cues the source and the native VLC surface is only attached once the operator switches to the video screen so libvlc can host into the real child HWND instead of falling back to its own top-level window",
            "[Fixed] Scoreboard display window is now truly borderless — Windows 11's residual rounded corners, 1px accent border and thin top caption strip on WindowStyle=None windows are stripped via a zero-thickness WindowChrome, so the scorebug sits flush in the top-left with no bars, gaps or curves",
            "[Fixed] Auto-update detection — the app was querying the wrong GitHub repository name (Roche_Scoreboard instead of RocheScoreboard), so the startup update prompt never appeared and the Settings ‘Check for Updates’ button reported ‘failed to download update’; the API target now matches the actual release repo and updates are picked up immediately",
            "[New] Recent Scores overlay — the most recent score now pulses continuously for the full duration of the overlay (sine-eased opacity flash + stronger team-coloured glow) so the freshest entry is unmistakable at a glance",
            "[Improved] Recent Scores GOAL / BEHIND text is now noticeably larger inside each row — Viewbox margin removed and reference frame tightened so the labels scale up to fill the row height; GOAL and BEHIND still render at identical heights and neither clips the row",
        ]),
        new("3.0.1", "2026-05-03",
        [
            "[New] Training Mode — inline edit queued timers: each queue row now has a ✎ button that swaps the row into an editable title + mm:ss form with ✓ Save / ✕ Cancel (Enter saves, Esc cancels); editing the active timer preserves elapsed progress and re-baselines the running stopwatch on the fly",
            "[New] Training Mode — AUTO-ADVANCE toggle in the queue header: when on (default) the next timer starts automatically at the end of the strobe; when off the session pauses at the end of every interval and waits for the operator to press Start (or Space)",
        ]),
        new("3.0.0", "2026-05-02",
        [
            "[New] Display windows now default to the opaque, GPU-composited path on new installs — the single biggest video framerate win; operators who previously chose per-pixel transparency keep their saved choice (toggle with Ctrl+Shift+T on the display window)",
            "[New] High-framerate video playback — MP4s are now transcoded to a fastdecode-friendly H.264 profile (baseline, no B-frames, ref=1, no CABAC/deblock, 30fps) and the player surface releases its decoder whenever it's off screen, lifting playback from stuttery ~15fps to a consistent 30–60fps",
            "[New] Goal-video overlay sits on an opaque black backdrop so the layered display window no longer per-pixel alpha-blends every video frame",
            "[Improved] MediaElement tuned for throughput — ScrubbingEnabled off, LowQuality bitmap scaling and aliased edges to keep the GPU/CPU off the critical decode path",
            "[Fixed] Weather overlay temperatures no longer render a question mark next to the digits — degree symbols are now emitted as encoding-safe escapes with a Bahnschrift/Segoe UI/Arial font fallback chain",
            "[Fixed] Quarter-break presentation screens no longer show a stale 00:00 timer next to the QT/HT/3QT label — the shared bottom bar hides the timer automatically when the clock is stopped at zero",
            "[Improved] Scoreworm overhaul — cleaner rendering and consistent layout against the redesigned bottom score bar",
        ]),
        new("2.8.0", "2026-05-01",
        [
            "[New] Scoring Run engine redesigned as a broadcast-style contextual evaluator \u2014 instead of a single backward streak scan, the engine now evaluates multiple rolling windows simultaneously (event windows of 4/6/8/10/12/16/20/24/30 scores AND match-time windows of 4/8/12/18/25/35 minutes) and ranks every candidate to pick the most meaningful, broadcast-worthy run rather than the first one it stumbles on",
            "[New] Five-tier scoring-run classification \u2014 Burst (18\u20130 type flurries), Standard (24\u20136), Sustained (36\u201312), Major (42\u201318) and Mega (48\u201312+); each tier has its own minimum points, opposition cap and dominance ratio so short bursts and long-term dominance trends are both detected with appropriate evidence",
            "[New] Scoring runs now tolerate realistic opposition scoring \u2014 a run no longer resets the moment the opposition kicks a behind or even a goal; the engine continues to evaluate dominance across rolling windows and only fades or resets when the opposition meaningfully responds, wins recent windows or significantly reduces the run margin",
            "[Improved] Scoring-run anti-spam tightened \u2014 once \u2018XAVIER SCORING RUN 18\u20130\u2019 has been shown, the same overlay will not re-trigger at 19\u20130 or 20\u20130; a re-show now requires either a tier escalation or a meaningful (\u002B12 point) growth, or the full 5-minute same-team cooldown to elapse",
            "[New] 10-second post-animation delay before the scoring-run graphic shows \u2014 the overlay now waits for goal animations, the +6 swipe, active mp4 video AND any other on-screen overlay to fully clear before appearing, so it lands as a follow-up commentary card rather than interrupting live scoring excitement",
            "[Improved] Cross-team scoring-run cooldown shortened to 45s so genuine momentum flips land close to when they happen, while same-team cooldown extended to 5 minutes to suppress repetitive triggering",
            "[Preserved] Scoring-run overlay visual identity unchanged \u2014 same team-name + \u2018Scoring Run\u2019 wording, same value, same elapsed-time treatment; all improvements are in detection intelligence and scheduling, not branding",
        ]),
        new("2.7.1", "2026-04-30",
        [
            "[Fixed] Recent Scores overlay no longer overflows at small scorebug sizes (~400×200) — width floor reduced from 320px to 110px so the overlay scales down with the actual score column instead of forcing the panels off-screen",
            "[Fixed] Recent Scores overlay now fully hides the team name, goal count and behind count panels while it is on-screen — those cells fade to zero opacity so only the Total score panels and team logo panel remain visible behind the overlay (previously leftover digits and name letters could peek through)",
            "[Improved] Recent Scores rows now stretch to fill the overlay width with equal-sized GOAL and BEHIND boxes (single 28pt label) and tighter padding/margins so all 5 entries fit cleanly at small sizes",
            "[Improved] Recent Scores header height reduced from 34px to 22px with a smaller 14pt label inside a downscaling Viewbox so the header strip stays proportional on tiny scorebugs",
            "[Improved] Scores This Quarter overlay simplified to a boxes-only layout — visible team-name headers removed and the Q1/Q2/Q3/Q4 score boxes now scale up via a Both-direction Viewbox to fill the freed space",
            "[Improved] Lead Changes overlay content wrapped in a downscaling Viewbox so the bar text fits cleanly at ~400×200",
        ]),
        new("2.7.0", "2026-04-29",
        [
            "[New] TRAINING MODE \u2014 brand new third sport mode for stopwatches, drills and interval sessions; selectable from the top-right tile on the sport selection screen with its own amber-glow neon styling matching the AFL/Cricket card techniques",
            "[New] Training control panel \u2014 add multiple titled timers to a queue, drag rows directly to reorder (with a glowing blue insertion line) or use the Up/Down buttons, remove individually or Clear All, with live ACTIVE card showing remaining time at 84pt and a session-total readout that ticks down across the whole queue",
            "[New] Training transport \u2014 Start / Pause toggle, Reset active interval, Skip to next, plus quick \u002B1:00 / \u002B0:10 buttons that now correctly extend the running timer on the fly without disturbing the queue (previously only worked while paused)",
            "[New] Training quick presets \u2014 one-click WARM-UP 5:00, DRILL 3:00, REST 1:00, SPRINT 0:30, MATCH PLAY 10:00, COOL DOWN 5:00 to drop common intervals straight into the queue",
            "[New] Interval session builder \u2014 enter Rounds / Work / Rest seconds and one click expands the queue into a Tabata-style WORK \u2194 REST sequence (e.g. 8 rounds \u00d7 20s work / 10s rest)",
            "[New] Training scorebug \u2014 huge centred countdown (420pt source font, scales down via Viewbox) with the title above in a neon-bordered chip and a live progress bar that fills as the timer winds down; status pill shifts blue \u2192 green \u2192 amber \u2192 red as the state changes",
            "[New] LAST 10s pulse \u2014 the timer drop-shadow glow pulses once per second across the final 10 seconds so the operator and athletes can see the wind-down at a glance",
            "[New] Whole-screen finish flash \u2014 when an interval hits zero the entire scorebug strobes red\u002Fblack at 420ms cadence so a finish is unmistakable from any distance, with a FINISHED status pill on the active card",
            "[New] UP NEXT bar \u2014 the bottom of the scorebug slides up a strip showing the next timer\u0027s title and duration whenever a queued timer exists; collapses smoothly when the queue is exhausted, and never overlaps the timer thanks to a flexible spacer-row layout that always keeps the centre block centred in the available space",
            "[New] NOW STARTING preview \u2014 during the finish flash a green-accented bar at the bottom shows the next timer already ticking down live in the background, so the operator sees the new clock running before the swap",
            "[New] Smooth interval transitions \u2014 finished timer holds for ~7.5s while the next interval pre-stages live at the bottom; transition uses a clean in-place crossfade (450ms) instead of a vertical slide, with the new active timer fading in pre-populated with the next clock\u0027s live values",
            "[New] Session complete overlay \u2014 once the queue is exhausted, a full-screen SESSION COMPLETE banner fades in over a 10-second extended red\u002Fblack strobe, then everything fades back out to the ready state",
            "[New] Auto-advance \u2014 the next queued timer auto-starts the moment the transition finishes, so a multi-interval session runs hands-free end-to-end",
            "[New] Operator console UX \u2014 Training control panel redesigned with a top status bar (brand chip, live SESSION TOTAL, prominent Exit), a NOW PLAYING readout at 42pt title \u002F 84pt clock with a status pill, an oversized START button (20pt) for the primary action, and grouped builder cards (Add Timer \u002F Quick Presets \u002F Interval Builder) with coloured accent strips and helper text on each",
            "[New] Operator keyboard shortcuts \u2014 Space toggles Start\u002FPause, S skips, R resets the active timer, Esc exits Training Mode; Enter while typing the title or any numeric field adds the timer; numeric inputs reject non-digit keystrokes; on-screen shortcut reference card included",
            "[New] Drag-to-reorder queue \u2014 grab any queue row and drag to move it; a glowing 3px blue insertion line floats above the rows showing exactly where it will land; the source row dims while dragging; releasing fires a single MoveTimer with no per-row re-animation cascade so the rebuild is glitch-free",
            "[Improved] Custom button template \u2014 every Training-mode action button now uses a custom ControlTemplate that respects Background\u002FForeground\u002FBorderBrush, so the saturated button colours actually paint instead of being hidden behind the system Aero grey-white chrome (text was reading as light-on-light before)",
            "[Improved] Resizable control panels \u2014 every sport mode\u0027s control panel (AFL, Cricket, Training) is now wrapped in a downscaling Viewbox so it scales down proportionally when the host window is smaller than its design size, and the main window MinWidth\u002FMinHeight have been dropped (860\u00d7580 \u2192 420\u00d7320) so the app can be operated on small laptops or low-res screens with every control still visible and clickable",
            "[Improved] Sport selection screen entry animation now also fades and drops the new TRAINING MODE tile in from above, with hover scale and glow ramp matching the AFL\u002FCricket card hover treatment",
            "[New] Robust crash- and shutdown-safe save \u2014 the live game state (including the running stopwatch) is now flushed every 5 seconds while the clock is running, on every clock pause, on Windows session-end (logoff\u002Fshutdown\u002Frestart) and on a terminating unhandled exception, so a crash, force-kill, power loss or session-end never loses more than a few seconds of clock progress",
            "[Fixed] On a clean close mid-quarter, the saved-game ElapsedInQuarter no longer lags behind the live stopwatch \u2014 the auto-save and crash-path saves all read the live MatchManager.ToState() so the persisted clock matches the on-screen clock to within the auto-save interval",
            "[New] Crash-handler save uses Dispatcher.Invoke to marshal onto the UI thread, so a save attempt during a background-thread crash is still safe and completes before process termination",
            "[Improved] Scoring Run overlay layout \u2014 the run value (e.g. 20-0), SCORING RUN label and elapsed time now sit on a single horizontal line, with the value bumped to 84pt, label to 48pt and elapsed time to 44pt for high-impact broadcast readability",
            "[Improved] Scoring Run overlay colour contrast \u2014 all three text elements now run through ContrastHelper.GetReadableTextColor against the actual gradient mid-point so the overlay reads cleanly against any team primary colour (yellow, lime and cyan no longer collapse into the team-tinted background)",
            "[Improved] Scoring Run elapsed time gets bright pure-white treatment with a soft black drop-shadow instead of the previous muted grey, so the live counter is unmistakably legible at distance",
        ]),
        new("2.6.3", "2026-04-27",
        [
            "[New] Pause-aware match clock \u2014 scoring run, scoreless and team-drought elapsed timers now freeze the instant the game clock is paused and resume from where they left off when it restarts, so 'TIME SINCE LAST SCORE' and scoring-run elapsed track real game time instead of wall-clock time across pauses, breaks, save/restore and app restarts",
            "[New] Drought / scoreless milestone detection (e.g. 'scoreless every 4 min from 8:00') is now match-clock-driven so a milestone only fires after that many minutes of actual game time, not wall-clock time",
            "[New] Recent Scores overlay redesigned for clarity \u2014 stripped of time stamps, running scores and score-type icons; each row is now a sharp neon team-coloured rectangle with the team's primary colour as both the border AND the glow, so every panel is unmistakably team-dedicated",
            "[New] Recent Scores score-type label (GOAL / BEHIND) now renders in the team's primary colour reinforcing team identity; goals and behinds share the same neon styling so the row chrome doesn't compete with the score-type text",
            "[New] Quarter Scores overlay caption replaced with a single-line, readable 'SCORES THIS QUARTER' centre chip; team scores pushed to the outer left and right edges of the bar with the centre caption sitting cleanly between them",
            "[Improved] Bottom score bar enlarged across every break/rotation screen \u2014 logos 48 \u2192 60px, team abbreviation source font 32 \u2192 40pt (Viewbox cap 38 \u2192 50), G/B chip digits 28 \u2192 34pt, total score source font 44 \u2192 56pt, centre quarter/timer 18\u002F11 \u2192 22\u002F14pt for vastly better long-distance readability",
            "[Improved] Break / Score Worm / Stats team-name boxes unified into a single 220 \u00d7 44 single-line strip across all three rotation screens \u2014 same dimensions, same logo placement, same 28pt source font; long names auto-shrink via Viewbox to fit one line in the wider 220px column instead of being cropped or wrapped",
            "[Improved] Bottom-bar host row enlarged from 80 \u2192 100px on every rotation screen (Quarter Summary, Score Worm, Stats, Weather) so the bigger score-bar content has clean breathing space and the bar reads identically as the rotation cycles through",
        ]),
        new("2.6.2", "2026-04-26",
        [
            "[New] Update Available alert fully redesigned in scoreboard style \u2014 sharp edges, neon double-border, glowing icon badge, side-by-side INSTALLED/LATEST version chips with team-style accents, sectioned WHAT'S NEW panel, neon outline buttons and a glowing progress bar",
            "[New] Game In Progress alert is now genuinely team-dedicated \u2014 split home/away gradient stripe across the top, team-coloured borders + glows on each team block, full match readout (big totals, G.B line, quarter, elapsed clock, margin) reconstructed from the saved snapshot, all colours pulled from the saved game state",
            "[Improved] Quarter Summary screen polish pass \u2014 team-name boxes lengthened, all score cells get more breathing room, all four Q-label cells render at identical size (no more uneven Q1/Q2/Q3/Q4 widths)",
            "[Improved] Stats screen visual consistency \u2014 all four stat bars now identical height, side value text bumped to 40pt with wider 100px columns, team-name boxes wrapped in a Viewbox so the full team name fills the header at 24pt",
            "[Improved] Bottom score bar redesigned with broadcast-style team-coloured G/B chips \u2014 rounded outline borders, team-coloured tinted backgrounds, glowing goals chip, larger digits (28pt) and G/B suffix labels",
            "[Improved] Score bar abbreviation slot bumped to 32pt source font with a 38px Viewbox cap so team abbreviations read clearly across every screen",
            "[Improved] Break-screen bottom bar height raised to 96px (from the standard 80px) so the Quarter Summary bar is more prominent without clipping at smaller window sizes \u2014 every other screen keeps the standard 80px bar",
            "[Improved] Full team names (instead of abbreviations) now render in the dedicated team-name boxes on the Stats, Score Worm and Quarter Summary screens; long names auto-shrink via Viewbox caps so they always fit",
            "[Fixed] Score Worm graph now redraws when the host control resizes \u2014 the worm adapts to new window dimensions instead of staying on stale pixel coordinates from the first Populate",
            "[Fixed] Weather screen condition text (e.g. 'Mostly Cloudy with Showers') wraps to a second line instead of being clipped by the card width",
        ]),
        new("2.6.1", "2026-04-25",
        [
            "[New] Tiered scoring-run detection \u2014 four parallel tiers (Burst, Standard, Sustained, Mega) with their own thresholds so runs of every shape qualify: tight 2-goal flurries, classic 18-1 stretches, long 30+ point quarter-spanning runs, and overwhelming 48+ point dominance",
            "[Improved] Scoring-run decision making \u2014 picks the run with current momentum (tier \u2192 freshness \u2192 ratio) instead of raw net dominance, so a fresh 12-0 burst beats a stale 30-9 run from earlier",
            "[Improved] Scoring-run cooldown is now match-clock-based, allows tier escalation to override the same-team cooldown, and uses a much shorter cross-team cooldown so momentum flips land close to when they happen",
            "[Improved] Recent-tail guard tightened to a 4-minute match-clock window (independent of the scan window) so a sustained run has to still be hot right now, not just hot historically",
            "[Improved] Single-event \u201Cruns\u201D and sub-goal growth re-shows are now correctly rejected",
            "[Improved] All presentation screens (stats, weather, score worm, intro, break, cricket scorebug, cricket summary, presentation score bar) now adapt to any window dimensions without distortion \u2014 things shift around the screen instead of stretching or condensing",
        ]),
        new("2.6.0", "2026-04-25",
        [
            "[Improved] Scoring run detection now uses the match clock instead of the wall clock \u2014 runs that span multiple quarters, paused matches, saved-game restores, app restarts and crashes are now correctly identified (e.g. a 35-8 run from Q1 02:00 to Q2 04:00 is detected even after closing and reopening the app)",
            "[Improved] Scoring runs land within ~3 seconds of the triggering goal instead of being delayed up to 3 minutes by the informational-overlay cooldown",
            "[New] Event-driven overlays (scoring runs, lead changes, droughts, scoreless) now take priority over scheduled overlays \u2014 if a scheduled overlay is on-screen when an event-driven moment fires, it fades smoothly so the time-sensitive overlay can show immediately",
            "[New] All 7 scheduled overlays (stats, win prob, quarter scores, last 5 scores, weather, forecast, rain) now have equal chance of appearing and rotate evenly \u2014 uniform interval, uniform priority, and a small randomisation jitter for tied candidates so no overlay is structurally favoured",
            "[New] Narrow-mode responsive layout \u2014 when the scorebug width drops past a threshold, the left clock column collapses and the bottom bar swaps from the scrolling marquee to a full-width quarter+clock readout so the timer stays visible and readable",
            "[New] Start/Pause button shows the next quarter dynamically between quarters \u2014 \u0022\u25B6 Start Q2\u0022, \u0022\u25B6 Start Q3\u0022, \u0022\u25B6 Start Q4\u0022 \u2014 instead of a generic \u0022Start\u0022",
        ]),
        new("2.5.3", "2026-04-23",
        [
            "[Fixed] Stats overlay no longer fires within 5 seconds of resuming a saved game \u2014 the overlay scheduler now seeds a 100-second early-buffer marker on resume so the viewer gets a clean beat of scorebug-only after pressing Resume",
            "[Fixed] Weather icons now render in full colour across the scorebug overlays and weather screen \u2014 forced Segoe UI Emoji on every icon TextBlock so WPF picks the COLR/CPAL coloured glyphs instead of falling back to monochrome",
            "[Fixed] Scoring run with zero opposition points (18-0, 20-0, 30-0 etc.) no longer absorbs an unrelated earlier opposition score \u2014 a clean-run guard breaks the backward scan and extension pass the moment opposition history is encountered, so a true 18-0 displays as 18-0 not 18-6",
        ]),
        new("2.5.2", "2026-04-23",
        [
            "[Fixed] Critical random crash during matches \u2014 win probability Monte Carlo was iterating the live event list on worker threads while the UI added score events, causing an InvalidOperationException that tore the process down",
            "[Fixed] Win probability engine now snapshots the event list to an immutable array before background simulation, eliminating the cross-thread mutation crash",
            "[New] Global exception handlers installed \u2014 Dispatcher, AppDomain and TaskScheduler unhandled exceptions are now logged to %LOCALAPPDATA%\\Roche_Scoreboard\\crash.log and survivable ones are swallowed instead of terminating the app mid-match",
            "[Improved] Win probability engine significantly smarter \u2014 scoring-shot intensity (rewards attacking pressure even with inaccurate kicking), recency-weighted events via 12-min exponential decay, pressure regime (field-position battle), in-front inertia, comeback drive, clutch late-game regime and Q4 fatigue modelling",
            "[Improved] Solid 90-second cooldown between the end of one overlay and the start of the next \u2014 applies to queued, event-driven and informational overlays",
            "[Improved] Scoreless and team-drought overlays fade out smoothly the instant a goal is scored so stale \u0022time since last score\u0022 info doesn\u0027t linger after scoring resumes",
            "[Improved] Lead-changes overlay now suppresses automatically when the current margin is more than 4 goals \u2014 stops showing \u00225 lead changes!\u0022 at 30-point blowouts",
        ]),
        new("2.5.1", "2026-04-23",
        [
            "[Improved] Recent Scores panel now auto-shrinks text when 6+ rows are displayed (5 scores + quarter-split header) so content never clips inside the cells",
            "[Improved] Scoring run detection smarter \u2014 added recent-tail guard (last 4 events must be \u226570% dominated by the run team) so cooled-off runs like 18-1 \u2192 18-7 no longer trigger the overlay",
            "[Improved] Scoring run backward scan now stops on two consecutive opponent scores at the tail, preventing random earlier opposition goals from being absorbed into totals (e.g. a 20-2 run no longer inflates to 20-8)",
            "[Improved] Scoring run dominance threshold tightened from 2.5\u00d7 to 2.9\u00d7 so borderline cases like 18-7 (2.57\u00d7) and 20-8 (2.5\u00d7) are rejected, while 38-13 (2.92\u00d7) still qualifies",
            "[Improved] Scoring run opponent cap reduced from 25 to 18 points so the opponent can never be as dominant as the run team within the window",
            "[Improved] Weather forecast, weather stats and rain forecast overlay heights raised from 110px to 130px so large text no longer clips",
            "[Improved] +6 and +1 goal swipe text enlarged from 56pt to 80pt for much higher visibility when a score lands",
            "[Improved] Recent Scores panel text bumped in roomy mode \u2014 header 26pt, GOAL/BEHIND 26pt, score type icon 28pt, team badge 26pt with wider padding, time 18pt, running score 22pt",
        ]),
        new("2.5.0", "2026-04-23",
        [
            "[New] Scores-This-Quarter overlay fully redesigned \u2014 sharp neon boxes, team-coloured borders on every cell, Total boxes with team-coloured drop-shadow glow, separate GOALS and BEHINDS boxes, centre QTR chip with cyan border and glow",
            "[New] Scoring run detection now has a two-phase scan \u2014 strong runs (\u226518 pts, dominance \u22652.5\u00d7) keep absorbing earlier contiguous events past the 30-min window so long dominant runs show their true length",
            "[New] Recent Scores side panel text sizes overhauled \u2014 header 20pt, team badge 20pt with wider padding, GOAL/BEHIND text 20pt, score type icon 22pt, running score 17pt bright white, time text 15pt",
            "[New] Weather forecast, weather stats and rain forecast overlays redesigned with much larger text \u2014 forecast times/temps 20/30pt, rain % 26pt, wx stats labels 18pt and values 46pt",
            "[New] Scoring run overlay restructured \u2014 huge 72pt score on the left, tight stacked 36pt label + 24pt detail on the right, eliminates negative space",
            "[Improved] Overlays now allowed to show while the countdown timer is paused (break-screen suppression still applies)",
            "[Improved] Solid 2.5-second cooldown between consecutive queued overlays so each one gets a clear beat of scorebug-only time",
            "[Improved] Smooth animated exit when an overlay is cancelled by the next one or by the end of a quarter \u2014 no more hard-snap transitions",
            "[Improved] Scoring run thresholds broadened \u2014 dominance ratio 3.0\u2192 2.5\u00d7, opponent cap 12\u2192 25 pts so runs like 36-12, 38-13 and 50-20 now register",
            "[Improved] Scoring run cross-quarter gap widened to 20 min wall-clock so runs spanning a quarter break (e.g. Q1 28\u2032 \u2192 Q2 7\u2032) are no longer truncated",
            "[Improved] Scoring run concentration window widened to 30 min, redundancy escape now has three paths (concentrated burst, strong dominance \u22654\u00d7, large absolute run \u226530pts)",
            "[Improved] Scoreless overlay first milestone 8min \u2192 10min (repeats every 5 min)",
            "[Improved] Scoreless and drought overlays now mutually exclusive \u2014 if the game is fully scoreless, only the scoreless overlay shows (not both)",
            "[Fixed] Drought overlay double-firing \u2014 milestone now consumed atomically when detected so rapid clock ticks can't enqueue the same drought multiple times",
            "[Fixed] Scoring run overlay not triggered for runs spanning Q1 28\u2032 into Q2 7\u2032 due to too-tight cross-quarter gap limit",
        ]),
        new("2.4.0", "2025-07-19",
        [
            "[Improved] Scoring run detection now scans both teams and picks the stronger run, not just the last scorer",
            "[Improved] Scoring run detection broadened — 30-event / 30-minute lookback window captures longer game-spanning runs",
            "[Improved] Scoring run spans quarter breaks (up to 5 min gap) so cross-quarter momentum isn't lost",
            "[Improved] Overlay scheduling overhauled — all 7 overlays (stats, win prob, quarter scores, last 5 scores, forecast, weather, rain) now rotate evenly",
            "[Improved] Weather and rain overlay priority boosted so they actually appear during matches",
            "[Improved] Overlay minimum intervals reduced (3–5 min) and target overlays per quarter doubled for better rotation",
            "[Improved] All overlay display durations increased (12–16 seconds) for easier reading",
            "[Improved] Break screen slideshow interval increased from 10s to 18s per screen",
            "[Improved] Scoreless and team drought overlays now trigger at 10 minutes then every 5 minutes",
            "[Improved] All percentages in stats overlay and stats screen now display as whole numbers (no decimals)",
            "[Improved] Last 5 scores overlay text contrast improved — team badge text auto-adapts to light/dark team colours",
            "[Improved] Last 5 scores time and score text brightened for better readability on dark background",
            "[Improved] Break screen titles standardised — consistent font size, weight, and family across all screens",
            "[Improved] Weather icons diversified with more distinct and colourful emoji for each condition",
        ]),
        new("2.3.0", "2025-07-18",
        [
            "[New] Game state persistence — in-progress matches are auto-saved and can be restored if the app is closed mid-game",
            "[New] Recent Scores overlay — shows last 5 scores with quarter splits, team-coloured borders, and goal/behind icons",
            "[New] RecentScores added to the overlay scheduling rotation system",
            "[New] Custom styled resume game dialog replaces system MessageBox, shown after app loads",
            "[New] Score event time editing — adjust the game time of any score from the match log editor",
            "[Improved] Recent Scores items styled with team primary/secondary colour borders, left accent strip, and glow effects",
            "[Improved] Smooth animated transitions for overlay expand/collapse — columns, name strips, and G/B panels fade in/out together",
            "[Improved] Total score text size stays consistent during Recent Scores overlay",
            "[Improved] Update and resume-game alerts now show sequentially after the app window is visible, with update taking priority",
        ]),
        new("2.2.1", "2025-07-17",
        [
            "[Improved] All overlay bars redesigned with bold 2px coloured borders, #0D1117 backgrounds, and vibrant team-coloured text with glow effects",
            "[Improved] Score worm full-screen redesigned to match neo design language with team-tinted backgrounds and bold borders",
            "[Improved] Bottom score bar on break screens redesigned with team-colour section borders, gold quarter label, cyan timer, and glow effects",
            "[Improved] Stats bar corners now sharp (no rounding) to match neo design",
            "[Improved] Team logos now auto-trimmed to remove transparent padding for tighter circle fit",
            "[Improved] App title now shows version number (e.g. Roche Scoreboard v2.2.1) across all windows and taskbar"
        ]),
        new("2.2.0", "2025-07-16",
        [
            "[Improved] Bold 2px coloured borders on every card, cell, and header across all presentation screens",
            "[Improved] Subtle dark backgrounds (#0D1117) behind borders for high-contrast neo aesthetic",
            "[Improved] All text now uses vibrant, meaningful colours — gold titles, cyan clocks, team-coloured values",
            "[Improved] Team headers redesigned: subtle dark bg with bold team-colour borders instead of solid fill",
            "[Improved] Quarter labels now cyan-bordered with vibrant cyan text instead of grey",
            "[Improved] Weather card dividers tinted to match card theme (amber/green) instead of neutral grey",
            "[Improved] Score cell borders now full-opacity team primary colour at 2px thickness",
            "[Improved] Stat bar borders now full-opacity blended team colour at 2px thickness"
        ]),
        new("2.1.3", "2025-07-15",
        [
            "[New] Scores This Quarter overlay shows quarter scoring breakdown in stats-bar style",
            "[New] QTR SCORES manual overlay button added to Game Analysis panel",
            "[Fixed] Stats bar corner radius now correctly rounded on outer edges only",
            "[Fixed] Scoring run detection now recognises dominant runs over longer periods (e.g. 20-0 in 10 minutes)",
            "[Improved] Drought overlay text colour now adjusts for contrast against darkened team background"
        ]),
        new("2.1.2", "2025-07-14",
        [
            "[Improved] Match log team name text now adjusts for contrast against dark background",
            "[Improved] Scoreless and team drought overlays now trigger at 8 minutes then every 4 minutes",
            "[Improved] Drought overlay text changed to 'TIME SINCE LAST {TEAM} SCORE'",
            "[New] Continue quarter button allows resuming a quarter after accidentally ending it",
            "[New] Live clock adjustment buttons (±1s, ±10s) and set time fields",
            "[Fixed] Auto-updater now correctly downloads .exe release assets instead of source code",
            "[Fixed] Home team logo no longer pulses on score or button presses"
        ]),
        new("2.1.0", "2025-07-13",
        [
            "[Improved] Scoring buttons now show keyboard shortcut hints (Ctrl+G/B, Alt+G/B)",
            "[Improved] Match result text now says 'defeated by' at full time instead of 'leads by'",
            "[Improved] Header margin indicator is now tinted with the leading team's colour",
            "[Improved] Last event text in the header now shows actual team names instead of generic Home/Away",
            "[Improved] Undo button now visually dims when no events exist to undo",
            "[Improved] Clipboard copy buttons now show brief 'Copied!' feedback",
            "[Improved] Scoring panel colours now have a minimum brightness floor for very dark team colours",
            "[Improved] End Quarter button now asks for confirmation before ending Q4 (full time is irreversible)",
            "[Improved] Web state broadcast now includes event count, last event, and match result",
            "[Fixed] Start/Pause and Full Time button brushes are now cached — eliminates per-frame allocations",
            "[Fixed] Quarter snapshot array now uses proper nullable typing instead of null! suppression",
            "[Fixed] Eye dropper preview now reuses a single brush instead of allocating one per mouse move",
            "[Fixed] ContrastHelper.GetReadableTextBrush now returns a frozen brush — prevents cross-thread issues",
            "[Fixed] WeatherService catch clauses narrowed from bare catch to specific exception types",
            "[Changed] QuarterSnapshot converted to a record for value equality and reduced boilerplate",
            "[Changed] EndQuarter clock-stop logic simplified — removed duplicated stopwatch reset",
            "[Changed] Modernised collection initialisers to use C# 12 collection expressions across models",
            "[Improved] Scorebug window now adapts layout and element sizes responsively on resize instead of uniformly stretching"
        ]),
        new("2.0.2", "2025-07-12",
        [
            "[Fixed] App could become unlaunchable after dismissing an update prompt on close",
            "[Fixed] Hardened startup update check so network or parse failures never block launch"
        ]),
        new("2.0.1", "2025-07-11",
        [
            "[New] Match log panels expand on hover to reveal edit controls",
            "[Changed] Redesigned match strip header with cinematic gradient and broadcast typography",
            "[Changed] Redesigned tab bar as full-width broadcast channel selectors",
            "[Changed] Changelog now centred at bottom of sport selection screen with hover expand"
        ]),
        new("2.0.0", "2025-07-11",
        [
            "Added auto-updater — checks GitHub for new releases on launch and close",
            "Added in-app changelog on the sport selection screen",
            "Redesigned control panel navigation — clean text signpost tabs with accent underline",
            "Redesigned match strip — thin integrated bar with compact layout"
        ]),
        new("1.0.0", "2025-06-20",
        [
            "AFL scoreboard with live scorebug, summary, and stats screens",
            "Cricket support with full scorebug and match management",
            "Live weather overlays with forecast, temperature, and rain data",
            "Custom goal video animations per team",
            "Web remote control and live view with QR codes",
            "Automatic stat overlays, scoring run detection, and lead change alerts",
            "Win probability engine with live overlay",
            "Team logo crop and positioning controls",
            "Ticker message system with scrolling bottom bar",
            "Initial release of Roche Scoreboard"
        ])
    ];

    /// <summary>
    /// Changelog copy is deliberately concise. This normalization also keeps
    /// future entries to the two user-facing categories: New and Fixed.
    /// </summary>
    public static IReadOnlyList<ChangelogVersion> Versions { get; } = RawVersions
        .Select(version => version with
        {
            Changes = version.Changes
                .Select(Simplify)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray()
        })
        .ToArray();

    private static string Simplify(string change)
    {
        string trimmed = change.Trim();
        bool fixedItem = trimmed.StartsWith("[Fixed]", StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith("[Removed]", StringComparison.OrdinalIgnoreCase);
        int close = trimmed.IndexOf(']');
        string text = close >= 0 ? trimmed[(close + 1)..].Trim() : trimmed;
        int dash = text.IndexOf(" — ", StringComparison.Ordinal);
        if (dash > 28) text = text[..dash];
        if (text.Length > 150)
        {
            int cut = text.LastIndexOf(' ', 150);
            text = text[..(cut > 80 ? cut : 150)].TrimEnd(',', ';', ':') + "…";
        }
        return $"[{(fixedItem ? "Fixed" : "New")}] {text.TrimEnd('.', ';')}";
    }
}

/// <summary>
/// A single version entry in the changelog.
/// </summary>
internal sealed record ChangelogVersion(string Version, string Date, IReadOnlyList<string> Changes);
