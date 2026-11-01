# Superpowers instruction import

Source: https://github.com/obra/superpowers
Version: **7.0.1**; immutable revision **2ce8aa15d05bd57213e6f746f953aafb7ded7a8b**.
See `manifest.json` for archive and per-file SHA-256, and `LICENSE` for the upstream MIT licence.

## Installed and discoverable as repository files

12 selected skills, 41 original Markdown files (definitions, references and review templates):

- `../../using-superpowers/SKILL.md`: index/conventions and platform references.
- `../../brainstorming/SKILL.md`: requirements/design preparation; optional visual server omitted.
- `../../writing-plans/SKILL.md`: implementation plans and self-review.
- `../../systematic-debugging/SKILL.md`: reproduce, trace and isolate failures.
- `../../test-driven-development/SKILL.md`: regression-first implementation discipline.
- `../../verification-before-completion/SKILL.md`: evidence before completion claims.
- `../../requesting-code-review/SKILL.md` and its reviewer template.
- `../../receiving-code-review/SKILL.md`: evaluate and resolve review evidence.
- `../../subagent-driven-development/SKILL.md`: reference only for its script-dependent workflow.
- `../../executing-plans/SKILL.md`: reference only for its script-dependent workflow.
- `../../using-git-worktrees/SKILL.md` and `../../finishing-a-development-branch/SKILL.md`: dependency references, subject to the active task's branch/commit/publication restrictions.

The three excluded upstream skills are parallel dispatch, writing new skills and plugin diagnosis: they are not prerequisites for the selected current workflow. Existing Roche/.NET guidance supplies domain checks and test review; imported ECC skills already overlap planning, TDD, verification and review. Superpowers adds structured workflow/reviewer templates, not another application toolchain.

321 pre-existing skill directories and all their files were preserved byte for byte. There are now **333** top-level repository `SKILL.md` definitions. Definitions retain upstream names/content. Read the relevant files through the repository guide; `superpowers:name` references map to `.agents/skills/name/SKILL.md` here. The cloud executor skills-provider catalogue currently reports zero entries, including the old 321; automatic host Skills-menu enumeration is therefore **not verified**. Files and relative Markdown dependencies are directly readable.

## Authority and supported capabilities

This is an instruction/reference import, **not the marketplace plugin installation**. The current official README recommends the Codex marketplace for the full plugin. No callable marketplace installer for Superpowers is exposed in this task. No hooks, scripts, binaries, plugin/agent/MCP config, credentials, telemetry, global settings or visual-companion server were installed or run.

Use definitions selectively. User/developer instructions and Roche project guidance remain authoritative. Existing approval, model/tool schemas, branch and single-writer constraints override upstream examples; do not copy their model names or configuration snippets blindly. This import does not authorise agents, commits, worktrees, pushes, merges, services or executable helpers. It adds no runtime orchestration capability. Existing supplied collaboration tools may be used only when permitted by the active task.

## Pending executable component — do not run

Version 7.0.1's full execution workflow requires omitted shell helpers. Exact source paths and SHA-256 are in `omitted-scripts.json`. The following are **upstream command forms**, not executable approved steps:

- `bash .agents/skills/subagent-driven-development/scripts/sdd-workspace <plan-file>`: creates a per-plan directory under this repo's `.superpowers/sdd/` and a create-only ignore file.
- `bash .agents/skills/subagent-driven-development/scripts/task-brief <plan-file> <task-number>`: extracts a task brief.
- `bash .agents/skills/executing-plans/scripts/task-start <plan-file> <task-number>`: prepares the task workspace/brief.
- `bash .agents/skills/executing-plans/scripts/task-done <plan-file> <task-number> <base-commit> -- <explicit-test-command>`: executes the supplied command and appends a completion ledger after success.
- `bash .agents/skills/subagent-driven-development/scripts/review-package <plan-file> <base-commit> <head-commit>`: writes committed-range logs/diffs into the plan workspace. It requires a nonempty committed range; it does **not** review the current dirty working tree.

These files are absent deliberately. Copying/reviewing the helpers for potential execution, selecting actual arguments and action-time execution approval are separate steps. Current app work is uncommitted and commits/publication are held; do not manufacture commits or review only the old preview range to satisfy a helper. Continue reviewing the actual current working-tree diff using existing supported tools.

Optional brainstorming `start-server.sh`, `server.cjs`, browser helper and remote image/logo features remain omitted and inactive. They are not prerequisites for text planning/debugging/TDD/verification.

## Validation

Per-file source/destination hashes match; every selected definition has frontmatter; internal Markdown files were copied together. Links/commands to omitted scripts, other platform integrations and upstream-only documents remain documented limitations. No global registration/restart/config mutation was performed. The existing checkout is unchanged apart from these added instruction files, attribution and a short AGENTS.md discovery link.
