---
name: design-md
description: >-
  Create, validate, update, preview, and export DESIGN.md files — Google's
  DESIGN.md format (google-labs-code/design.md, "Built on Google's DESIGN.md
  spec") that gives AI coding agents a structured, persistent understanding of a
  design system. Use this skill WHENEVER the user mentions DESIGN.md, design.md,
  design tokens for AI agents, a design system file for a coding agent, Stitch
  design specs, getdesign.md, or asks to lint / validate / check / diff / export
  design tokens, build a DESIGN.md from scratch or from an existing
  project/codebase, update or refresh a DESIGN.md, generate a live preview of a
  design system, or borrow/adapt a DESIGN.md from a known brand (Stripe, Linear,
  Notion, Vercel, etc.). It bundles a self-contained Python engine so NO npm/CLI
  install is needed — validation, contrast checks, token-reference resolution,
  diff, and Tailwind/DTCG export all run offline via bundled scripts. Reach for
  it even if the user just says "make a design system for my coding agent".
---

# DESIGN.md toolkit

DESIGN.md is a format that describes a visual identity to coding agents: YAML
design tokens (the normative values) plus markdown prose (the rationale).
Normally you'd `npm install @google/design.md` to lint/diff/export. **This skill
removes that dependency** — everything runs through bundled, pure-Python scripts
that faithfully reproduce the official CLI's behavior (verified byte-for-byte on
the official examples). No Node, no network, no install.

## When to use which workflow

| The user wants to…                                  | Go to            |
| --------------------------------------------------- | ---------------- |
| Make a new DESIGN.md from a brand brief / from scratch | **Create**       |
| Make one by analyzing an existing project/codebase  | **Create from project** |
| Check / lint / validate a DESIGN.md                 | **Validate**     |
| Compare two versions / check for regressions        | **Diff**         |
| Turn tokens into Tailwind or DTCG                    | **Export**       |
| See the design system rendered visually             | **Live preview** |
| Start from a known brand's design system            | **Catalog**      |
| Edit / refresh an existing DESIGN.md                | **Update**       |

Scripts live in `scripts/`. Run them with `python` (or `python3`). All read/write
plain files and print JSON. Read `references/spec.md` before authoring tokens by
hand, and `references/linting-rules.md` to interpret findings.

## Core principle: always validate

Whatever you produce or change, **run the linter and resolve errors before
presenting it**. A DESIGN.md with a broken token reference will mislead the
agent that consumes it. After writing or editing a file:

```bash
python scripts/dmd.py lint path/to/DESIGN.md
```

Exit code is non-zero if there are errors. Show the user the summary and fix any
errors (broken refs, invalid colors/units). Warnings (contrast, orphaned tokens)
are judgment calls — explain them rather than blindly "fixing" them.

---

## Create (from scratch)

1. **Gather the brand input.** Ask the user for what you don't know: brand mood
   / adjectives, primary + accent colors, font choices, and any key components
   (buttons, cards). If they're vague, propose a coherent palette and confirm.
   Consider offering a catalog starting point (see **Catalog**).
2. **Write a token spec JSON** describing colors, typography, rounded, spacing,
   components, and optional per-section `prose`. See the shape at the top of
   `scripts/scaffold.py`.
3. **Generate the file** (emits canonical section order + correctly-quoted YAML
   and validates automatically):

   ```bash
   python scripts/scaffold.py new --from-json spec.json --out DESIGN.md
   ```
4. **Review the lint summary** it prints. Fix errors, then refine the prose so a
   reader understands *why* each token exists.

By default DESIGN.md is created at the **project root** (that's where agents look
for it). Confirm the location with the user if ambiguous.

## Create from project

When the user wants a DESIGN.md that reflects an existing codebase:

```bash
python scripts/scaffold.py scan PROJECT_DIR --json > scan-spec.json
```

This read-only scan surfaces the most-used hex colors, CSS custom properties,
font families, and border-radii (it skips `node_modules`, build dirs, binaries).
Treat the result as a **draft**: review it with the user, rename `color-1` to
semantic names (`primary`, `surface`, …), add typography roles and components,
then feed it to `scaffold.py new`. Don't ship a raw scan — it has no semantics
or rationale yet.

## Validate

```bash
python scripts/dmd.py lint DESIGN.md          # JSON findings + summary
```

Reproduces all nine upstream rules with identical messages, including WCAG
contrast on component color pairs and token-reference resolution. See
`references/linting-rules.md` to interpret each finding. For a confidence check,
if Node happens to be installed you can diff against `npx -y @google/design.md
lint` — but this is never required.

## Diff

```bash
python scripts/dmd.py diff OLD.md NEW.md
```

Reports token-level `added`/`removed`/`modified` per category and a `regression`
boolean (true when the new file has more errors or warnings). Useful before
committing changes or in review.

## Export

```bash
python scripts/dmd.py export --format css-tailwind DESIGN.md > theme.css
python scripts/dmd.py export --format json-tailwind DESIGN.md > tailwind.theme.json
python scripts/dmd.py export --format dtcg DESIGN.md > tokens.json
```

`css-tailwind` = Tailwind v4 `@theme` block; `json-tailwind` (alias `tailwind`)
= Tailwind v3 `theme.extend`; `dtcg` = W3C Design Tokens. Export refuses to run
if the file has errors — lint first.

## Live preview

Render the design system to a **self-contained, offline HTML file** (color
swatches, type scale, components, spacing/radius scales, prose):

```bash
python scripts/preview.py DESIGN.md -o DESIGN.preview.html
```

Then share it with `present_files` so the user can open it in their browser. The
HTML has no scripts and no remote assets and is injection-hardened (see
`references/security.md`). This is the local equivalent of getdesign.md's
preview pages. Offer it whenever the user would benefit from *seeing* the system,
not just reading tokens.

## Catalog (start from an existing design)

74 reference DESIGN.md analyses (Stripe, Linear, Notion, Vercel, Claude, Apple,
Discord, …) are indexed in `assets/catalog-index.json`. Browse offline:

```bash
python scripts/catalog.py list
python scripts/catalog.py search fintech
python scripts/catalog.py url stripe
```

To actually use one, **fetch its `raw_url` with the `web_fetch` tool**, then
either save it as a starting point or adapt its patterns. Be tasteful: borrow
structure and ideas, but don't paste a brand's exact identity into an unrelated
product. After adapting, always lint the result.

## Update (edit an existing file)

For token changes that should preserve the human-written prose and re-impose
canonical section order:

```bash
python scripts/scaffold.py update DESIGN.md --from-json changes.json
```

`changes.json` is a partial spec — only the token groups / prose sections you
want to override. Existing tokens and prose are kept; the merged result is
re-validated. (Prose is treated as human-owned and not rewritten, so refresh any
hex values you quote in prose yourself.) For small surgical edits, editing the
file directly and re-running `lint` is also fine.

---

## Self-test

To confirm the engine is healthy on a new machine:

```bash
python scripts/dmd.py selftest      # 28 offline checks, no Node/network
```

## Notes

- The format is at version **`alpha`** and under active development; if upstream
  changes, the rule list and messages in `scripts/dmd.py` + `references/` are the
  single source of truth to update.
- Tokens are normative; prose is for humans and the consuming agent. A good
  DESIGN.md has both — don't ship tokens with empty rationale.
