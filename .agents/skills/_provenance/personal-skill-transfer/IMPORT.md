# Personal skills imported into Roche Scoreboard

On 28 October 2026, the user attached `roche-skill-transfer-text.zip` directly to
this task. Its **1,977,105 bytes** and SHA-256
`7aa4c2d689e4a1e3050adbdb1a0a6237d5f163a85b4c9819b6fbcff8342284d5`
match the previously recorded private transfer metadata. The attached bytes,
rather than an upstream substitute or shared desktop path, were imported.

## Installation and discovery

- **317 distinct skills** installed as `.agents/skills/<skill>/SKILL.md`:
  five direct personal skills, 311 canonical English ECC skills, one Taste skill.
- The four existing Microsoft/Roche project skills were preserved. There are
  **321 direct skill directories** in this checkout. Root `AGENTS.md` points here
  for project discovery even when a host's live catalogue has not refreshed.
- **17 identical compatibility definitions** were folded into their canonical
  directories. Across all content, **111 duplicate files** were folded; every
  supplied file maps to byte-identical installed content in `import-receipt.json`.
  Two extra illustrative YAML/JSON references were retained inside the relevant
  skill's `references/` directory. They are examples, not active configuration.
- **Zero existing-name collisions**, overwrites or imported executables. All 617
  manifest rows and 618 archive files (including the manifest) were verified.
  Paths, case collisions, traversal, symlinks, executable flags, file types and
  UTF-8 contents were checked before copying. The HTML reference has no inline JS
  or event handlers and was not opened/executed.

Use `installed-catalogue.csv` to find a skill by name, source, pin and dependency
notes. Read only relevant definitions. Original skill texts remain unchanged;
these local notes qualify availability without modifying the user's originals.

## Limits and provenance

This is an **instruction/reference import**. It does not install plugin runtimes,
agents, scripts, binaries, browser sessions, MCP servers, settings or credentials.
Do not execute copied commands or expand task permissions merely because a skill
suggests doing so. Host tools and the user's current instructions take precedence.

**28 skills explicitly list omitted runtime/assets.** The remaining 289 have no
listed omitted assets; their external prerequisites are not thereby verified.
Examples: Playwright's CLI wrapper is absent; Apple Liquid Glass's audit script
is absent and actual Apple UI work needs an Apple SDK; SF Symbols needs Apple
tools and has a source-absent `design/animation-patterns/symbol-effects.md` link;
Taste's JavaScript/assets are omitted. Framework-specific React/Swift examples
are reference material for this C#/Avalonia application.

Sources/pins and exclusions are preserved in `skill-dependencies.csv`. ECC is
pinned to `25278e7d7b6cf6e6a4e7a6c8e10f14ff22f0eb5b`; three direct personal skills
have recorded immutable revisions. Playwright and SF Symbols lack recorded
source provenance. Taste is recorded as local version 2.1.5 without a commit pin.
No provenance or licence was invented. Supplied licences remain with their files;
ECC's root MIT licence is retained at `ecc/LICENSE` here.

The source manifest has **118 malformed group/skill metadata rows** for linked
references: the `group` cell contains a Windows-style source path and `skill` is
empty. All those paths, sizes and SHA-256 fields verify. Their original rows are
preserved; the explicit installed file map provides the resolved locations.
The separate per-skill dependency inventory has all 317 named records.

`relative-link-audit.csv` checks 306 Markdown relative-link occurrences:
258 resolve directly, three resolve at an explicitly mapped installed location,
and 45 are absent from the approved bundle or illustrative paths. It does not
pretend Markdown prose/code paths or external services are validated. Read the
file map for relocated references; do not download or install missing helpers
automatically. The original ZIP remains the uploaded source of record.

No app code, global settings, repository commits or publication were performed
by this import. App refinement and native Mac acceptance are separate work.
