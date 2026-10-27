# Linting rules

The validator (`scripts/dmd.py lint`) reproduces the upstream `design.md`
linter exactly: the same nine rules, severities, thresholds, and finding
messages. Model-phase errors (invalid color, invalid unit, bad font weight,
nesting too deep) are emitted before the rules. Output is JSON:
`{ "findings": [...], "summary": { "errors", "warnings", "infos" } }`. Exit
code is `1` when there is at least one error, else `0`.

| Rule                 | Severity | What it checks |
| -------------------- | -------- | -------------- |
| `broken-ref`         | error    | A `{path}` reference doesn't resolve to any defined token. (Same rule also emits a **warning** for unrecognized component sub-tokens.) |
| `missing-primary`    | warning  | Colors exist but there is no `primary` — agents will auto-generate one. |
| `contrast-ratio`     | warning  | A component's resolved `backgroundColor`/`textColor` pair is below WCAG AA (4.5:1). |
| `orphaned-tokens`    | warning  | A color is defined but never referenced by any component (MD3 standard families are exempt). |
| `token-summary`      | info     | Count of tokens per section. |
| `missing-sections`   | info     | `spacing` or `rounded` absent while other tokens exist. |
| `missing-typography` | warning  | Colors exist but no typography tokens. |
| `section-order`      | warning  | Markdown sections out of canonical order. |
| `unknown-key`        | warning  | A top-level YAML key is within edit-distance 2 of a schema key (e.g. `colours` → `colors`). |

## Contrast algorithm (WCAG)

Per-channel relative luminance on sRGB (`r,g,b` in 0–255):

```
s   = c / 255
lin = s/12.92                      if s <= 0.03928
      ((s + 0.055)/1.055) ** 2.4   otherwise
L   = 0.2126*lin(r) + 0.7152*lin(g) + 0.0722*lin(b)
ratio = (max(L1,L2) + 0.05) / (min(L1,L2) + 0.05)
```

Luminance uses the opaque RGB (alpha is ignored), which is why translucent
overlay surfaces (e.g. `#ffffff1a` glass) report ~1.0:1 against white text.
The ratio is printed to two decimals. Threshold = 4.5:1.

## Interpreting results

- **Errors** block export and should be fixed before shipping the file. The
  most common is a broken `{reference}` — check the token actually exists and
  the path is spelled correctly.
- **`contrast-ratio` warnings** are intentional signals: either the pair is a
  real accessibility problem, or it's a translucent surface where the literal
  ratio is meaningless. Use judgment; don't blindly "fix" glass surfaces.
- **`orphaned-tokens`** often just means the palette is richer than the
  components section documents — usually fine for a design *reference*.
- **`section-order` / `unknown-key`** are quick, safe fixes.

## Verifying fidelity against upstream

If Node is available, you can confirm the bundled validator matches the
official CLI:

```bash
diff <(npx -y @google/design.md lint DESIGN.md) \
     <(python scripts/dmd.py lint DESIGN.md)
```

The skill does **not** depend on this — the Python engine is self-contained.
This is only a confidence check.
