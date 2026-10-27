# design-md — a DESIGN.md toolkit skill (no CLI required)

![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)
![Python: stdlib only](https://img.shields.io/badge/Python-stdlib_only-3776AB.svg)
![No CLI required](https://img.shields.io/badge/CLI-not_required-success.svg)
![DESIGN.md spec](https://img.shields.io/badge/DESIGN.md-alpha-black.svg)

Create, validate, update, preview, and export **DESIGN.md** files — entirely
through a self-contained skill. **No `npm install`, no Node, no network.**

> **DESIGN.md** is a format ([google-labs-code/design.md](https://github.com/google-labs-code/design.md),
> *"Built on Google's DESIGN.md spec"*) that gives AI coding agents a structured,
> persistent understanding of a design system: machine-readable **design tokens**
> in YAML front matter + human-readable **rationale** in Markdown prose.

This is an agent **skill** — a `SKILL.md` plus bundled scripts — usable by *any*
agentic coding tool that supports skills (Claude / Cowork / Claude Code, Cursor,
GitHub Copilot, and others). Point your agent at a request like *"make a
DESIGN.md for my app"* or *"is my design tokens file valid?"* and it runs the
bundled scripts to do the work. Because the scripts are plain Python with no
dependencies, they also work **standalone** — straight from your terminal or in
CI, with no agent at all.

![Live preview of a DESIGN.md rendered by the skill](assets/preview.png)

> *The self-contained HTML live preview (`python scripts/preview.py DESIGN.md`)
> renders the palette, type scale, components, and shape scales straight from the
> tokens — no build step, no remote assets.*

---

## Why this exists

The official `@google/design.md` CLI is great, but using it means installing an
npm package and running a Node binary (with some Windows `.md`-association
footguns). For an agent that just needs to **author, check, and convert** a
DESIGN.md, that's friction.

This skill removes the dependency. The entire toolchain is reimplemented in
**pure Python (standard library only)** and runs offline:

- ✅ **No CLI install** — `npx @google/design.md` is not needed.
- ✅ **No Node, no npm, no internet** — scripts use only the Python stdlib.
- ✅ **Faithful** — output is verified **byte-for-byte against the official CLI**
  on the official example files (lint, diff, and all export formats) and ships a
  28-check self-test.
- ✅ **More than validation** — it also *creates* a DESIGN.md from scratch or by
  scanning an existing project, *updates* one, renders a *live HTML preview*, and
  offers a *catalog* of 74 reference design systems.

If you happen to have Node installed, you can still cross-check against the real
CLI (`diff <(npx -y @google/design.md lint DESIGN.md) <(python scripts/dmd.py lint DESIGN.md)`)
— but you never need to.

---

## Install

Installing a skill is just copying a folder. Download (or clone) this repo and
drop the `design-md` folder into your tool's skills directory, so you end up
with:

```
<your skills folder>/
└── design-md/
    ├── SKILL.md
    ├── scripts/
    ├── references/
    └── assets/
```
e.g:. `.claude/skills/design-md/**`, `.cursor/skills/design-md/**`, `.github/skills/design-md/**` and other agent.
```bash
git clone https://github.com/jeftarmascarenhas/design.md.git
# then copy the folder into your skills directory, e.g.:
cp -r design.md ~/.your-agent/skills/design-md
```

That's it — reload your agent and it will discover the skill via `SKILL.md`. The
skills folder location depends on your tool (Claude / Cowork / Claude Code,
Cursor, Copilot, and others all read `SKILL.md`), but the convention is the same
everywhere: one folder per skill. No build step and no dependencies — the scripts
use only the Python standard library (Python 3.8+).

> Optional sanity check: `python scripts/dmd.py selftest` runs 28 offline checks.

---

## How to use it — just ask your agent

Once the skill is installed, you don't run anything by hand. Describe what you
want in your agent's chat in plain language; the skill triggers automatically and
the agent calls the right script for you. For example:

- *"Create a DESIGN.md for my meditation app — soft sage greens, off-white
  background, rounded corners, friendly sans-serif, with a primary button."*
- *"Build a DESIGN.md from this project."* (the agent scans your codebase for
  colors, fonts, and radii and drafts one)
- *"Is my DESIGN.md valid? Something feels off with the button."* (it lints and
  pinpoints the broken token reference + a fix)
- *"What changed between these two versions of my design system?"* (a token-level
  diff with a regression check)
- *"Export my tokens to a Tailwind v4 theme."* (or `json-tailwind` / W3C `dtcg`)
- *"Show me a visual preview of this design system."* (a self-contained HTML page)
- *"Start my design system from Stripe's."* (browses the 74-system catalog)

You never memorize flags — the agent reads `SKILL.md` and picks the right tool.

### What's covered

Creating a DESIGN.md from a brief or by scanning a project, validating it
(the nine upstream lint rules — broken references, WCAG AA contrast on component
color pairs, missing primary/typography, orphaned tokens, section order, unknown
keys — with identical messages and severities), diffing two versions with a
regression flag, exporting to `json-tailwind` (Tailwind v3), `css-tailwind`
(Tailwind v4), or `dtcg` (W3C Design Tokens), rendering a self-contained HTML
live preview, and browsing a catalog of reference design systems.

### Optional: run the scripts yourself (standalone / CI)

You don't need this for normal use — it's here because the scripts are plain,
dependency-free Python and double as a tiny CLI. Handy for CI gates (the linter
exits non-zero on errors) or quick local checks:

```bash
python scripts/dmd.py lint DESIGN.md                       # validate (JSON findings)
python scripts/dmd.py diff OLD.md NEW.md                   # token diff + regression
python scripts/dmd.py export --format css-tailwind DESIGN.md > theme.css
python scripts/preview.py DESIGN.md                        # self-contained HTML preview
```

---

## The format in 30 seconds

```markdown
---
name: Heritage
colors:
  primary: "#1A1C1E"
  on-primary: "#ffffff"
typography:
  h1: { fontFamily: Public Sans, fontSize: 3rem, fontWeight: 700 }
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

Tokens are normative; prose explains *why*. See [`references/spec.md`](references/spec.md)
for the full reference and [`references/linting-rules.md`](references/linting-rules.md)
for how to read findings.

---

## Repository layout

```
design-md/
├── SKILL.md                 # how Claude uses the skill (workflows)
├── scripts/
│   ├── dmd.py               # core engine: parse / lint / diff / export / spec / selftest
│   ├── scaffold.py          # create / update / scan-a-project
│   ├── preview.py           # self-contained HTML live preview
│   ├── catalog.py           # offline catalog index browser
│   └── selftest.py          # 28 offline checks
├── references/
│   ├── spec.md              # condensed DESIGN.md spec (v alpha)
│   ├── linting-rules.md     # the 9 rules + WCAG contrast math
│   └── security.md          # safety properties to preserve
├── assets/
│   ├── examples/            # 3 official example DESIGN.md files (fixtures)
│   └── catalog-index.json   # 74 reference design systems (slug → fetch URL)
└── evals/                   # test prompts + fixtures for evaluating the skill
```

---

## Security

The scripts are offline and dependency-free. Project scanning is read-only,
bounded, and never follows symlinks out of the target. The HTML preview escapes
all document values, validates anything entering a `style` attribute against an
allow-list, ships a strict `Content-Security-Policy`, and contains no JavaScript
or remote assets. Details in [`references/security.md`](references/security.md).
Never put secrets in a DESIGN.md — it's meant to be committed and shared.

---

## Status & compatibility

The DESIGN.md format is at version **`alpha`** and evolving. This skill tracks
that version; if upstream changes, `scripts/dmd.py` and `references/` are the
single source of truth to update. Fidelity is verified against the official CLI
release used at build time.

## Attribution

- The DESIGN.md **format/spec** and the three files under `assets/examples/` are
  from [google-labs-code/design.md](https://github.com/google-labs-code/design.md),
  Apache-2.0.
- The catalog index references analyses from
  [VoltAgent/awesome-design-md](https://github.com/VoltAgent/awesome-design-md)
  (MIT) and [getdesign.md](https://getdesign.md); it links to them and does not
  redistribute their contents.

This project is independent and not affiliated with or endorsed by Google.

## License

Licensed under the **Apache License 2.0** — see [`LICENSE`](LICENSE) and
[`NOTICE`](NOTICE). You're free to use, modify, and distribute this skill,
including commercially, provided you retain the license and attribution notices.

```
Copyright 2026 Jeftar Mascarenhas
Licensed under the Apache License, Version 2.0.
```
