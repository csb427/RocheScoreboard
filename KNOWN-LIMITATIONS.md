# Current testing feedback: native acceptance still required

This revision fixes two concrete sport-entry causes: animations targeting detached legacy XAML wrappers after the team editor rebuilt them, and first-use form work consuming animation time before a frame was presented. Both sports show intermediate motion in Linux translated renderer tests. **Native Windows first-use/cutout/smoothness is not certified fixed.** Verify the exact current Git revision, control and motion preference on Windows.

The cricket panel uses natural content height and removes empty card stretching; unused background can remain in unusually tall windows. Scoring controls were not inflated solely to fill it.

History is a proposal only. Reset still clears the existing active match/recovery under its prior semantics; no match archive is created.

# Test preview limitations

- **Native Windows WPF GUI is not yet validated.** Linux/Avalonia tests and Windows cross-compilation do not prove physical pointer/keyboard/tunnel ordering, modal/focus/capture, UIA/Narrator/IME, font/DPI/high-contrast, compositor or monitor behavior. Embedded VLC HWND, hardware-to-software fallback, audible output and monitor reconnect require actual Windows/hardware testing.
- **Historical Linux synthetic-font crash remains open.** An accumulated113-case host passed17 cases before the18th began and the process crashed in native stream-copy/synthetic-glyph code; an earlier group crashed after39 passes. The passing24-window diagnostic and recent137 aggregate do not establish a causal fix. The matched recorded18-row causal experiment has not run. The older unknown host abort also remains unresolved.
- **Default portable selector scrolling remains open.** Four same-input-turn batter/bowler opening/reopening tests at700x450/420x320 had their published viewport overridden to5px by Avalonia SelectingItemsControl.AutoScrollToSelectedItemIfNecessary. A separate15-case passing run disabled that mechanism in four test fixtures only: it was not a production fix or a native Windows regression demonstration. The137 aggregate excludes this selector class. Physical wheel timing and native WPF equivalent behavior remain unrun.
- **Original Mac input freeze remains unverified.** The Mac port is for demonstration; no new native Mac/packaging acceptance is supplied here.
- **Not every cricket profile or unusual circumstance is certified.** Debug Ctrl+Alt+D after entering cricket offers13 curated practice situations plus animation previews/restore. It is not an exhaustive simulator. Competition-specific replacement rules require an adopted profile; no DLS calculation engine is implemented. Unknown data is suppressed or explained rather than guessed.
- Apple-inspired surfaces are software rendering, not native Apple materials or proof of exact reference-video fidelity. Native visual judgement and actual OS accessibility notifications remain acceptance work.
- No credentials, operator saves or executable runtime installer are provided. Cloud/phone transport needs its existing authorised setup; local desktop launch does not require that setup.
