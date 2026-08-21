# Score Grid Design System

The Score Grid design system provides a complete, theme-able foundation for the Roche Scoreboard operator interface redesign.

## Architecture

The design system consists of eight ResourceDictionary files organized in `DesignSystem/`:

1. **Tokens.xaml** — Invariant design constants (spacing, radii, font sizes)
2. **LightTheme.xaml** — Light theme colors and semantic brushes
3. **DarkTheme.xaml** — Dark theme colors and semantic brushes
4. **ComfortableDensity.xaml** — Comfortable density spacing and control heights
5. **CompactDensity.xaml** — Compact density spacing and control heights
6. **Typography.xaml** — Text styles (Display, Title, Heading, Body, etc.)
7. **Icons.xaml** — Icon glyphs from Segoe UI Symbol
8. **Components.xaml** — Button, TextBox, and CheckBox styles

## Merge Order

Dictionaries are merged in `App.xaml` in this exact order:

```xml
<ResourceDictionary.MergedDictionaries>
	<!-- 1. Invariant tokens -->
	<ResourceDictionary Source="DesignSystem/Tokens.xaml" />

	<!-- 2. Theme (Light or Dark) -->
	<ResourceDictionary Source="DesignSystem/LightTheme.xaml" />

	<!-- 3. Density (Comfortable or Compact) -->
	<ResourceDictionary Source="DesignSystem/ComfortableDensity.xaml" />

	<!-- 4. Typography, icons, components -->
	<ResourceDictionary Source="DesignSystem/Typography.xaml" />
	<ResourceDictionary Source="DesignSystem/Icons.xaml" />
	<ResourceDictionary Source="DesignSystem/Components.xaml" />
</ResourceDictionary.MergedDictionaries>
```

## Theme and Density Switching

Runtime theme and density switching is handled by `ThemeService`:

- **ThemeService** maintains direct references to the active theme and density dictionaries
- Switching replaces the exact resource dictionary at its original index
- No filename-based Contains matching — uses stable object references
- Settings are persisted to `%MyDocuments%\Roche_Scoreboard\ux_preferences.json`

### Preference Persistence

Theme and density preferences are stored in `UxPreferencesData`:

```json
{
  "theme": "light",
  "density": "comfortable"
}
```

Enum values serialize as camel-case strings using `JsonStringEnumConverter`.

## Design Gallery

The design gallery is accessible via the `--design-gallery` command-line argument only:

```
Roche_Scoreboard.exe --design-gallery
```

Gallery features:
- Complete typography demonstration
- All button states (hover, pressed, focus, disabled)
- Input components with enabled/disabled states
- Theme color swatches
- Icon glyphs from Segoe UI Symbol
- Runtime theme and density toggle buttons

## Resource Naming Convention

All design-system resources use the `ScoreGrid.` prefix to avoid collisions with existing local resources in production XAML files such as `MainWindow.xaml`.

Examples:
- `ScoreGrid.Brush.BrandPrimary`
- `ScoreGrid.Button.Primary`
- `ScoreGrid.Text.Heading`

## Accessibility

All component templates include:

- Keyboard focus indication (2px border with Focus color)
- Hover and pressed states
- Disabled states with appropriate contrast
- Semantic foreground/background pairs for Success, Warning, Danger, and Accent states

## Typography

Typography styles using Segoe UI:

- **Display** — 36pt Bold
- **Title** — 28pt SemiBold
- **Heading** — 22pt SemiBold
- **Subheading** — 18pt SemiBold
- **Body** — 14pt Regular (wraps)
- **Body Strong** — 14pt SemiBold
- **Caption** — 12pt Regular (secondary text color)
- **Overline** — 10pt SemiBold
- **Code** — 14pt Consolas

## Component Styles

### Buttons

- **ScoreGrid.Button.Primary** — Brand primary, white text
- **ScoreGrid.Button.Secondary** — Surface with border
- **ScoreGrid.Button.Danger** — Danger color, white text

All buttons support hover, pressed, keyboard focus, and disabled states.

### Inputs

- **ScoreGrid.TextBox** — Standard text input with focus border
- **ScoreGrid.CheckBox** — Custom template with check mark icon

## Future Work

The following components are not yet implemented:

- Radio buttons
- Combo boxes / dropdown lists
- Sliders
- Progress indicators
- Custom tooltip template
- Tab controls
- Data grid theming
- Animation infrastructure

Reduced-motion handling is deferred pending animation implementation.

## Production Boundaries

This design system does NOT modify:

- MainWindow.xaml or its code-behind
- Match management, scoring, or clock logic
- Undo or match history
- External output window
- Cricket mode
- Media or VLC behavior
- Cloud services or update handling

The design system is isolated for gallery testing and future opt-in adoption.
