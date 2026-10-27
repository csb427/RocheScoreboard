# DESIGN.md Specification (condensed reference)

Targets spec **version `alpha`** of Google's `design.md` format
(`google-labs-code/design.md`). This file is the authoritative reference for
the skill; the bundled `scripts/dmd.py spec` command emits a machine-readable
version of the same content.

## What a DESIGN.md is

A single file that describes a visual identity to coding agents. It has two
layers:

1. **YAML front matter** — machine-readable design *tokens*, delimited by `---`
   fences at the top of the file. (Fenced ```` ```yaml ```` blocks in the body
   are also merged in.) Tokens are the normative values.
2. **Markdown body** — human-readable rationale organized into `##` sections.
   Prose explains *why* the values exist and how to apply them.

## Token schema (top-level keys)

```
version: <string>      # optional, current: "alpha"
name: <string>
description: <string>  # optional
colors:      { <token-name>: <Color> }
typography:  { <token-name>: <Typography> }
rounded:     { <scale-level>: <Dimension> }
spacing:     { <scale-level>: <Dimension | number> }
components:  { <component-name>: { <sub-token>: <string | reference> } }
```

`version, name, description, colors, typography, rounded, spacing, components`
are the only keys the spec and exporters read. Other top-level keys are
preserved but ignored (and may trigger the `unknown-key` lint warning if they
look like a typo of a schema key).

## Token types

| Type            | Format                                                                 | Example |
| --------------- | ---------------------------------------------------------------------- | ------- |
| Color           | Any CSS color: hex, `rgb()`, `hsl()`, `hwb()`, `oklch()`, `lab()`, named | `"#1A1C1E"`, `"oklch(62% 0.18 250)"` |
| Dimension       | number + unit; **standard units are `px`, `em`, `rem`** only           | `48px`, `1.5rem`, `-0.02em` |
| Token Reference | `{path.to.token}`                                                      | `{colors.primary}` |
| Typography      | object: `fontFamily`, `fontSize`, `fontWeight`, `lineHeight`, `letterSpacing`, `fontFeature`, `fontVariation` | see below |

Colors should start with `#` per the prose, but the validator accepts any
parseable CSS color. `rounded` and typography dimensions that use a non-standard
unit (anything other than px/em/rem) are reported as **errors**. `lineHeight`
may be a unitless number (a multiplier).

## Section order

Markdown `##` sections may be omitted, but those present must appear in this
order (aliases resolve to the canonical name):

| # | Section           | Aliases            |
| - | ----------------- | ------------------ |
| 1 | Overview          | Brand & Style      |
| 2 | Colors            |                    |
| 3 | Typography        |                    |
| 4 | Layout            | Layout & Spacing   |
| 5 | Elevation & Depth | Elevation          |
| 6 | Shapes            |                    |
| 7 | Components        |                    |
| 8 | Do's and Don'ts   |                    |

## Component tokens

Components map a name to a group of sub-token properties. Valid sub-tokens, in
canonical order:

```
backgroundColor, textColor, typography, rounded, padding, size, height, width
```

```yaml
components:
  button-primary:
    backgroundColor: "{colors.tertiary}"
    textColor: "{colors.on-tertiary}"
    rounded: "{rounded.sm}"
    padding: 12px
  button-primary-hover:        # variants are separate entries with a related name
    backgroundColor: "{colors.tertiary-container}"
```

An unknown component property is accepted but raises a `warning`.

## Consumer behavior for unknown content

| Scenario                          | Behavior                              |
| --------------------------------- | ------------------------------------- |
| Unknown section heading           | Preserve; do not error                |
| Unknown color/typography token    | Accept if the value is valid          |
| Unknown component property        | Accept with a warning                 |
| Duplicate top-level YAML key      | Recoverable warning; empty design system |

## Export targets (interoperability)

DESIGN.md tokens are inspired by the W3C Design Token Format. The `export`
command converts them to:

- **`json-tailwind`** — Tailwind v3 `theme.extend` JSON (alias: `tailwind`).
- **`css-tailwind`** — Tailwind v4 `@theme { ... }` block with CSS custom
  properties (`--color-*`, `--font-*`, `--text-*`, `--leading-*`, `--tracking-*`,
  `--font-weight-*`, `--radius-*`, `--spacing-*`).
- **`dtcg`** — W3C Design Tokens Format Module (`tokens.json`).

## Minimal valid example

```markdown
---
name: Heritage
colors:
  primary: "#1A1C1E"
  on-primary: "#ffffff"
typography:
  h1: { fontFamily: Public Sans, fontSize: 3rem, fontWeight: 700 }
  body-md: { fontFamily: Public Sans, fontSize: 1rem }
rounded: { sm: 4px, md: 8px }
spacing: { sm: 8px, md: 16px }
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    rounded: "{rounded.sm}"
---

## Overview
Architectural minimalism meets journalistic gravitas.

## Colors
- **Primary (#1A1C1E):** deep ink for headlines and core text.
```
