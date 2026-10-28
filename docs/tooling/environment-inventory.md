# Roche Scoreboard environment tooling inventory

Observed 28 October 2026 in `/workspace/RocheScoreboard`, Linux cloud executor.
The inventory sections below record the state **before installation**. The final
section records the subsequently approved, pinned Markdown-only installation.
No packages, executable plugins, agent frameworks, credentials, permissions or
persistent integrations were installed or changed. App refinement batch 2 continues.

## Pre-install local state

| Item | Verified location / state |
| --- | --- |
| Active checkout | `/workspace/RocheScoreboard`, branch `codex/mac-download-preview`, HEAD `6cdadb4bf4bab9d0439a6bf8158dad43d85c39dd`, uncommitted batches preserved |
| Project instructions | `/workspace/RocheScoreboard/.github/copilot-instructions.md` exists; current root `AGENTS.md`, `.agents/` and `.codex/` are absent |
| Executor skill registry | `skills.list(authority=executor)` returned **zero skills**, no warnings |
| .NET SDK | `/workspace/.tools/dotnet/dotnet`, version **10.0.402** |
| Shared engines | `/workspace/RocheScoreboard/macos/RocheScoreboard.Core/RocheScoreboard.Core.csproj` compiles authoritative Windows models directly |
| Native port tests | `/workspace/RocheScoreboard/macos/RocheScoreboard.Mac.Tests/`, xUnit + Avalonia.Headless, real source controllers/adapters and decoded video |
| Mac translation | `/workspace/RocheScoreboard/macos/tools/translate_production.py`, `audit_parity.py`; generated files carry hashes |
| Build provenance / verification | `macos/tools/write_build_provenance.py`, `verify_preview.py`, `bundle_vlc.py`; architecture/source/input verification already exists |
| Git / GitHub CLI | `/usr/local/bin/git`, `/usr/bin/gh`; existing workflow access has been used successfully, no new authentication added |
| Existing CI | `.github/workflows/mac-download-preview.yml`, `mac-demo.yml`, `publish-release.yml`; release workflow is separate and must not be triggered for previews |
| Linux video | `/workspace/.tools/vlc/usr/lib/x86_64-linux-gnu/`, matching plugin tree available |
| Browser testing | `/usr/bin/chromium`, Python `playwright` launcher under `/opt/codex/runtimes/codex-primary-runtime/dependencies/python/bin/`; useful for web remote UI, not native Cocoa acceptance |
| General runtimes | Python 3, Node, npm and uv are installed under `/opt/codex/runtimes/codex-primary-runtime/dependencies/` |
| PowerShell / graphify CLI | `pwsh` and `graphify` are not on PATH |
| Native Mac/Windows desktop | Unavailable in this Linux executor; cross-builds and headless tests do not provide those desktops |

## Skills already provided through this chat

The cloud catalogue has **44 skills**: 15 Codex Security, five Product Design,
21 Data Analytics, two Cloud Environment/onboarding and one Impeccable skill.
These are callable cloud packages, not files installed into this checkout.

Relevant supplied packages:

- `impeccable:impeccable` — refinement, accessibility, motion and native UI review.
- `product-design:audit` and related design workflows — evidence-based visual review.
- `cloud-environment:cloud-environment-runtime` — runtime/network diagnosis.
- `cloud-environment-onboarding:setup` — reusable environment setup.
- Codex Security workflows — available for requested security review, not a
  replacement for ordinary correctness testing.

Important limitation: the Impeccable launcher path
`/workspace/RocheScoreboard/.codex/skills/impeccable/scripts/impeccable` does not
exist. Its cloud instructions can be read, but this checkout does not contain
the launcher/binary or a project PRODUCT.md/DESIGN.md. Do not describe it as
fully installed locally or silently run a downloaded binary.

The Data/office/connectors do not fill a C#/Avalonia testing gap. Extra messaging,
drive and calendar integrations are unnecessary for this app task.

## Existing repository-owned skills omitted from this checkout

Git objects for the verified Windows baseline
`636140761a9348b5030de731d3d91e5f741c1cbf` already contain **23 SKILL.md files**:
22 under `.agents/skills/` and graphify under `.codex/skills/`. They are **not**
present in the active working tree. This is a recovery/source-selection option,
not evidence that all 23 are appropriate or active.

Two physical reference copies exist outside the active checkout:

- `/tmp/roche-windows-6361407/.agents/skills/dotnet-wpf-modern/SKILL.md`
- `/tmp/roche-windows-6361407/.codex/skills/graphify/SKILL.md`
- Their routing instructions: `/tmp/roche-windows-6361407/AGENTS.md`.

The WPF skill is modern .NET guidance with .NET 8/9 examples. It is not an
Avalonia porting skill and must not trigger a framework/theme/MVVM rewrite or
downgrade this .NET 10 project. Its referenced testing/accessibility skills
are not installed simply because their names appear in its text.

Other baseline skill names include animation/performance reviews, Apple design,
break-ui, design-md, emil-design-eng, prototype and web/mobile-specific tools.
Several target React/Expo/Swift and would be redundant or inapplicable to the
shared WPF/Avalonia code. Inspect source, licence, scripts and fit before selecting.
Graphify has a reference file but no executable here; restoring its Markdown
alone would not install the engine or refresh a graph.

## Real gaps and useful next choices

| Priority | Gap | Useful preparation / selection |
| --- | --- | --- |
| 1 | Persistent project context for the actual port | Repo skill documenting authoritative Windows models, generated-screen workflow, native overrides, exact commands, preview boundaries and native-acceptance limits |
| 1 | C#/Avalonia lifecycle expertise | An audited Avalonia/.NET skill for ownership, focus, DPI/work areas, bindings, timers and dispatcher/input behavior; preserve existing architecture |
| 1 | Rules/state regression discipline | Project skill for explicit transition matrices, rejected-operation immutability, reversible checkpoints, save/reload, authoritative MCC edition/competition distinctions and deterministic model tests |
| 1 | Native UI QA | Repo guidance using existing headless captures, real pointer/keyboard actions and small-window tests, plus a separate real-Mac checklist; browser automation cannot replace this |
| 2 | CI and artifact handoff | Repo guidance for exact source SHA, preview-only workflow side effects, signing/provenance checks, correct architecture and authenticated artifact links; reuse gh and existing scripts |
| 2 | Brand/design consistency | Select only material that preserves Score Grid identity, current shared controls/motion and operator readability; existing Impeccable/Product Design guidance already covers much of this |
| 3 | Broad web/agent automation | No demonstrated requirement for Agent Reach, external agents or extra service connectors in this batch; audit a concrete candidate before adding execution/credentials/dependencies |

## Installation mechanism and boundaries reviewed before installation

For approved, audited **repository skills**, the writable local destination is:

`/workspace/RocheScoreboard/.agents/skills/<skill-name>/SKILL.md`

Vendor only the selected version and necessary referenced files, retain licence
notices/provenance, and record the upstream commit/hash. Recovering owner-repository
material can use `git show 6361407:<path>` rather than executing an external
installer. Add narrowly scoped routing in a reviewed root `AGENTS.md` if needed;
preserve current architecture and explicit user authorisation. No such files have
been written by this inventory.

File installation is possible through the executor. **Automatic discovery and
activation must be checked after installation** in this host; the current executor
registry is empty. No claim is made that a Markdown file installs scripts, native
tools, plugins or persistent agents. Cloud `skills.read/list` are read/discovery
tools, not installers. The available plugin suggestion tool is constrained to
specifically requested recommended plugins; it is not a general installer for
arbitrary coding tools.

Before any candidate with executable code: inspect the pinned source/licence,
dependencies, network behavior, credential handling and requested permissions.
Do not run global install commands, add credentials, enable external-agent
communication or create another task as a side effect. Candidate selection is
pending with the managing thread; none has been installed yet.

## Subsequently approved installation — completed in this repository

The managing thread selected Microsoft's official `dotnet/skills` at commit
`586f13f8d898a60583a3364f146a3b6f5a763164`. Its GitHub commit identity and selected
Git blob IDs were checked. All downloaded content was staged under
`/tmp/roche-dotnet-skills-586f13f`, inspected before activation, and contained only
Markdown plus the root MIT licence. No executable/config/hook payload was present.

Installed paths:

- `/workspace/RocheScoreboard/.agents/skills/test-gap-analysis/SKILL.md`
- `/workspace/RocheScoreboard/.agents/skills/assertion-quality/SKILL.md`
- `/workspace/RocheScoreboard/.agents/skills/test-analysis-extensions/SKILL.md`
  plus its complete `extensions/` catalogue, including `dotnet.md`.
- `/workspace/RocheScoreboard/.agents/skills/roche-scoreboard-development/SKILL.md`
  — original, concise project guidance for parity, UI QA, cricket/state invariants,
  evidence limits and CI handoff.

`/workspace/RocheScoreboard/AGENTS.md` now routes agents to these exact files and
to the existing `.github/copilot-instructions.md`. No existing skill or instruction
file was overwritten. The source pin, 16 upstream Markdown SHA-256 hashes, source
paths and preserved root licence are recorded under
`/workspace/RocheScoreboard/.agents/skills/_provenance/dotnet-skills/`.
All hashes and relative Markdown dependency links passed validation.

No supported installer/creator helper was advertised in this task's skill/tool
catalogue, so installation used the reviewed repository-local file flow; the
Roche-specific guide was authored directly. No dependencies or external installer
were executed. Agent Reach, Impeccable binaries/hooks, legacy CI helpers, external
agents and global configuration were not installed.

**Discovery limit:** the live executor skill registry still returns zero entries
after installation. It has not dynamically refreshed. Project routing is verified
through the new root `AGENTS.md` and existing files; use those paths in this task.
Do not claim that the host's UI skill catalogue or another computer already
contains the additions. They remain local/uncommitted in this environment until
an authorised commit/push transfers them elsewhere.

The new test-review skills were applied to a focused source-only review of four
batch-2 regressions. They identified missing immediate run/extras and non-default
fielder assertions; these were strengthened. No mutation-survival or line-coverage
percentage is inferred from that static review or from a green suite.

## Additional personal/global inventory in this cloud executor

Checked the actual home `/home/agent` and known runtime roots. No personal custom
skill definitions were found under `/home/agent/.agents/skills` (absent), or the
two workspace-global `.agents/skills` / `.codex/skills` roots (absent).

`/home/agent/.codex/skills/builtins/` and `/opt/codex/skills/builtins/` each contain
the same four provider skills: **documents, pdf, presentations, spreadsheets**.
Their definitions match byte-for-byte but are separate files, not symlinks.
This is four distinct built-in skill names, not eight personal skills. They are
separate from the four newly installed repository-local skills and the 44 cloud
catalogue entries.

Verified dependencies for those existing provider skills: all checked relative
Markdown references and operation-marker helpers exist at their actual local
roots; Poppler `pdftotext`/`pdftoppm`, the bundled `soffice` launcher, `python-docx`,
`pypdf`, `reportlab`, `pdfplumber` and Pillow are present. The Node
`@oai/artifact-tool` module resolves under the configured primary runtime. The
obsolete Python `artifact_tool` module is absent; current presentation guidance
uses the JavaScript API, so that absence does not prove a missing dependency.
Some provider examples hardcode `/root/.codex/...`; those prefixes were not
accessed or copied and must be resolved to the actual permitted skill root when
that workflow is used. No provider helper or document-authoring script was run.

## User PC transfer — verified and installed

The user attached the actual text bundle to this task as Sediment file
`file_0000000051e0820bbf6296658a4c6501`. Its 1,977,105 bytes and SHA-256
`7aa4c2d689e4a1e3050adbdb1a0a6237d5f163a85b4c9819b6fbcff8342284d5`
match the expected transfer. No Library download or desktop automation was needed.

**317 distinct skills installed** under `.agents/skills/`: five direct personal,
311 canonical English ECC and one Taste skill. The four pre-existing project
skills remain intact: **321 direct SKILL.md directories**. Seventeen identical
compatibility definitions (111 duplicate files overall) were folded into their
canonical destinations with exact source-to-installed hashes. There were no
existing-name collisions or overwrites. All 617 manifest rows / 618 files were
verified; source metadata anomalies in 118 linked-reference rows are documented,
not silently treated as valid group/skill labels.

See [the import guide](../../.agents/skills/_provenance/personal-skill-transfer/IMPORT.md),
[the complete installed catalogue](../../.agents/skills/_provenance/personal-skill-transfer/installed-catalogue.csv)
and [the exact file receipt](../../.agents/skills/_provenance/personal-skill-transfer/import-receipt.json).
Root AGENTS.md routes project discovery to that catalogue; an unchanged host live
catalogue is not evidence that files are missing. Existing cloud/plugin skills
remain supplied by their providers rather than copied or configured anew.

This is instruction/reference availability, not fully operational tool parity.
Twenty-eight skills explicitly list omitted scripts/assets; other external
prerequisites remain unvalidated. Three Markdown relative links need the supplied
installed map; 45 are absent/illustrative. No credentials, sessions, settings,
MCP servers, executable scripts, hooks, binaries or external-agent access were
imported. Licences and supplied provenance are preserved; unknown pins remain
unknown. Earlier upstream staging was comparison-only and was not installed.
