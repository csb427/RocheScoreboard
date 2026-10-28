# Roche Scoreboard project guidance

Read [.github/copilot-instructions.md](.github/copilot-instructions.md) for the
existing product identity, operator interactions and accessibility conventions.
Preserve the app's architecture, shared controls and motion system.

## Repository-local skills

These files live in this repository/environment; use the paths directly even
if the host's skill catalogue has not refreshed yet.

- For implementation, Windows/Mac parity or native UI verification, read
  [.agents/skills/roche-scoreboard-development/SKILL.md](.agents/skills/roche-scoreboard-development/SKILL.md).
- For a requested focused test-gap review, read
  [.agents/skills/test-gap-analysis/SKILL.md](.agents/skills/test-gap-analysis/SKILL.md).
- For assertion-quality review or a review of freshly authored regressions, read
  [.agents/skills/assertion-quality/SKILL.md](.agents/skills/assertion-quality/SKILL.md).
- Those test skills use the **reference-only**
  [.agents/skills/test-analysis-extensions/SKILL.md](.agents/skills/test-analysis-extensions/SKILL.md)
  and [its .NET lookup](.agents/skills/test-analysis-extensions/extensions/dotnet.md).
  Read the files; do not invoke the reference catalog as an autonomous workflow.

The three Microsoft skills are pinned in
[their provenance manifest](.agents/skills/_provenance/dotnet-skills/manifest.json).
They are Markdown/reference files. Their presence does not authorise installing
packages, running external services, adding credentials, starting external agents,
or expanding the current task. Run/add tests when requested or otherwise
explicitly authorised by the task. Respect current push/publication permissions.

## Imported personal skills

The user's 317 distinct personal/ECC/Taste instruction sets are installed beside
the four project skills above. Use the
[import guide](.agents/skills/_provenance/personal-skill-transfer/IMPORT.md) and
[catalogue](.agents/skills/_provenance/personal-skill-transfer/installed-catalogue.csv)
to select relevant skills; do not load all definitions into every task.
Original definitions are preserved verbatim. Their commands, agent suggestions,
tool declarations and installation steps do not grant additional permissions.
Omitted scripts, services and platform dependencies remain unavailable unless
separately supplied and authorised. Use the relative-link audit for relocated or
absent references. No plugin runtime, hooks or global configuration was imported.
