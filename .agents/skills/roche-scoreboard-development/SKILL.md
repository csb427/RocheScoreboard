---
name: roche-scoreboard-development
description: Implement and review Roche Scoreboard changes while preserving shared Windows/Avalonia behavior, operator accessibility, reversible scoring and honest build/runtime evidence.
---

# Roche Scoreboard development

## Source and parity

- `Roche_Scoreboard/` is the authoritative Windows interface and shared models.
  The Mac Core project compiles those models directly; do not fork rules engines.
- Update the original controllers/views, native adapters or translator as appropriate.
  Never hand-edit `macos/RocheScoreboard.Mac/Native/Generated/`.
- After source/view changes, regenerate with `python3 macos/tools/translate_production.py`
  and check `python3 macos/tools/audit_parity.py --check-translation`.
  Use `--write-contract` only after intentional source inventory changes.
- Follow `macos/README.md`, `macos/PLATFORM_BEHAVIOR_AUDIT.md` and current refinement
  reports. Preserve the existing shared styles/motion and operator preferences;
  avoid architecture/theme rewrites or a second animation system.

## Rules, state and recovery

- Verify changed cricket rules against an official current MCC/ICC/competition
  source, recording the edition and clauses. The latest verified work uses MCC's
  2017 Code, fourth edition, effective 1 October 2026; recheck when rules change.
- Keep competition choices explicit: adopted code, counted extras, free hits,
  player eligibility, minimum overs, tie-breaks and awarded/no-result outcomes.
  Do not invent DLS calculations or infer an official decision from a rain break.
- Trace transitions: legal/illegal/undelivered balls, wickets/strike, over endings,
  retirement/replacement, completion, corrections, reset, interruption and repeats.
- Rejected/stale operations should preserve score/history/revisions and emit no
  scoring events. Capture confirmation context; revalidate after every await.
- Keep unusual operations explainable and reversible. Preserve checkpoint/history
  boundaries and corrected figures across save/reload and outcome reopening.
- Exercise both valid transitions and rejected/no-op paths. Isolate test files and
  app data with `ROCHE_DATA_HOME`; never use personal/operator saves as fixtures.

## UI review

- Inspect current rendered evidence before changing design. Retain the Score Grid
  identity and current native fallback limits; no decorative scope expansion.
- Check pointer and keyboard activation, visible focus, Escape/cancel, modal owner,
  repeated/interrupted navigation, nested scrolling, popup edges and long names.
- Test small/scaled windows, bounds restoration and separate compact 386×193 output.
  Primary/cancel/close actions must remain reachable; scroll rather than clip.
- Use existing motion/reduced-motion handling. Animations and video must yield to
  input; test callbacks and subscriptions during reset, hide, close and reload.

## Verification and delivery

- When authorised, use `dotnet test macos/RocheScoreboard.Mac.slnx -c Release` and
  Windows builds with `-p:EnableWindowsTargeting=true` on Linux. This executor's SDK
  is `/workspace/.tools/dotnet/dotnet`; established cache paths are in task reports.
- Native video tests require `ROCHE_NATIVE_MEDIA=1` and a matching VLC runtime.
  Capture TRX, source hashes, exact commands and screenshots. Review warnings;
  do not suppress failures or remove controls/handlers to obtain a pass.
- Distinguish model tests, Linux headless UI, Mac-runner headless tests,
  cross-publish/package/signature checks and actual Mac desktop acceptance.
  A package or stress pass does not establish that the reported AFL freeze is fixed.
- For authorised CI previews, inspect workflow side effects/permissions, confirm
  the exact pushed SHA, and monitor to completion. Verify architectures, source
  provenance, tests/signatures and genuine authenticated artifact URLs. Do not
  invent public links or treat workspace paths as downloads.
- Keep master, release and deployment boundaries from the current user request.
  Use the existing GitHub CLI/workflows; no legacy CI helper or external agent is required.
