# Roche Scoreboard Copilot Instructions

## Project

Roche Scoreboard is a C# WPF Windows application for operating live AFL scoreboards and training timers.

The redesigned operator interface must be usable by untrained volunteers, parents, students, children and strangers without prior instruction.

## Redesign scope

Included:

- Launcher
- AFL match setup
- AFL operator console
- Display and ticker-message controls
- Media controls
- Settings
- Training Timers
- Dialogs, validation, errors, loading and feedback
- Light and dark themes
- Responsive operator-window layouts

Excluded:

- Cricket mode
- The appearance and layout of the external scoreboard/output window

Do not delete or break excluded functionality. Cricket should remain in the codebase but should not appear in the redesigned launcher.

## Non-negotiable safeguards

- Preserve all existing match calculations and business logic.
- Preserve MatchManager, ScoreEvent, MatchStorage and GameStateService behaviour.
- Preserve clock, quarter, undo and score-event editing behaviour.
- Preserve autosave and resume-match behaviour.
- Preserve updater, media, weather, overlay and cloud-remote functionality.
- Preserve output-window updates even though its UI is not being redesigned.
- Preserve Training Timer behaviour.
- Do not rewrite unrelated services during UI tasks.
- Do not silently remove existing features.
- Do not modify cricket files unless required to keep the solution compiling.
- Never replace working behaviour with placeholders.
- Do not perform a broad rewrite of MainWindow and its code-behind in one task.

## Workflow

Before changing code:

1. Inspect all relevant XAML, code-behind, models and services.
2. State which files will change.
3. Explain how existing event handlers and state will be preserved.
4. Identify regression risks.
5. Present a bounded implementation plan.
6. Wait for approval before implementing when Plan mode is available.

After changing code:

1. Build the complete solution.
2. Fix all warnings or errors caused by the change.
3. Report all changed files.
4. Report any behaviour that could not be tested.
5. Provide a manual test checklist.
6. Do not claim a feature works without build or runtime evidence.

## Score Grid design system

Use semantic resources instead of hard-coded colours.

Core light-theme colours:

- Brand primary: `#0A57FF`
- Brand hover: `#0849D8`
- Brand pressed: `#073CB1`
- Navy 900: `#0B1D3A`
- Navy 800: `#10294D`
- Light background: `#F7F9FC`
- Raised surface: `#FFFFFF`
- Subtle surface: `#E6F1FF`
- Border: `#D6DEE8`
- Primary text: `#0B1D3A`
- Secondary text: `#5E6B7D`
- Success: `#22C88A`
- Warning: `#FFB020`
- Danger: `#E53935`
- Focus: `#0A57FF`

Core dark-theme colours:

- Background: `#081629`
- Raised surface: `#0B1D3A`
- Alternate surface: `#10294D`
- Border: `#263C58`
- Primary text: `#F7F9FC`
- Secondary text: `#A8B6C8`

Use `DynamicResource` for theme-dependent values.

## Typography and spacing

- Prefer Inter when available, with Segoe UI as the Windows fallback.
- Use tabular numerals for clocks and scores.
- Do not shrink text to make responsive layouts fit.
- Use a four-pixel spacing system: 4, 8, 12, 16, 24 and 32.
- Default corner radius: 8.
- Default border: 1 pixel.
- Focus ring: 2 pixels with clear contrast.
- Default control height: 36.
- Important scoring controls: at least 44 high.
- Ordinary interface text should generally not be smaller than 12.

## Responsive layouts

Use these initial content-width states:

- Full: 1400 or wider
- Standard: 1050–1399
- Compact: 760–1049
- Minimum supported window: approximately 760×560

Full:

- Two team scoring cards
- Full match-history panel

Standard:

- Two team scoring cards
- Narrower match-history panel

Compact:

- One active team card at a time
- Both scores remain visible in the header
- Match history becomes a drawer
- Start Clock and Undo remain sticky

Do not scatter uncoordinated width checks throughout different views. Use a shared responsive-layout mechanism.

## Interaction rules

- Scoring takes effect immediately.
- Never ask for confirmation before adding a score.
- Highlight the new history event.
- Keep Undo immediately available.
- Allow score events to be edited from match history.
- Confirm destructive actions only.
- Use descriptive confirmation buttons such as `Reset Match`, not `Yes`.
- Cancel receives default focus for destructive confirmations.
- Escape cancels dialogs.
- Enter activates only a safe primary action.
- No sound feedback.

## Keyboard shortcuts

Preserve:

- `A`: Home goal
- `S`: Home behind
- `K`: Away goal
- `L`: Away behind
- `Space`: Start or pause clock
- `Ctrl+Z`: Undo

Keyboard focus must always be visible. All mouse actions must have a keyboard-accessible equivalent.

## Accessibility

- Normal text contrast: at least 4.5:1.
- Large text contrast: at least 3:1.
- Interactive component and focus contrast: at least 3:1.
- Do not communicate state through colour alone.
- Use an icon, label or shape alongside colour.
- Keep controls comfortably spaced.
- Support Windows display scaling without clipping.
- Respect reduced-motion settings.
- Do not use flashing animation.

## Product identity

The exact product name is `Roche Scoreboard`.

Use the Score Grid modular-cell identity. Do not restore the former red-and-black identity. Red is reserved for destructive actions and errors.

The final logo geometry must be supplied as a deliberate vector asset. Do not substitute a generic grid icon as the final logo.

