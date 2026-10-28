# Preview refresh — 28 October 2026

This refresh includes refinement batches [1](2026-10-28-batch-1.md),
[2](2026-10-28-batch-2.md), [3](2026-10-28-batch-3.md) and
[4](2026-10-28-batch-4.md), plus the repository-local skill import described in
[the environment inventory](../tooling/environment-inventory.md).

The user has authorised committing these changes and pushing to
`codex/mac-download-preview` to produce refreshed GitHub Actions previews.
Historical batch reports retain their original local-only status; this delivery
authorisation supersedes those publication notes. Master, releases and deployment
are outside this refresh.

## Included

- Shared cricket scoring/correction/recovery fixes, explicit abandoned/no-result
  outcomes and a reversible non-striker run-out before delivery.
- Training duration validation, visible errors and cancelled stale finish callbacks.
- Cricket Extras/boundaries routing, selection clearance/focus, distinct wizard
  icons, scorebug-only overlays and opaque presentation handoffs.
- Separate chart series for all recorded team innings, follow-on ordinals,
  partial overs and honest gaps for corrected/incomplete ball history.
- Shared fluid selection, operator-window/sheet and menu arrival effects, with
  immediate commands and reduced-motion behavior.
- 321 repository skill entry points: 317 imported personal/ECC/Taste definitions
  plus four existing Microsoft/Roche project skills. Original text, licences,
  hashes, source/pin attribution and omitted-dependency notes are preserved.
  These are instruction/reference files; no third-party runtime, hooks, sessions,
  credentials or global configuration is installed by this source archive.
  Scoped `.agents/.gitattributes` preserves original line endings and byte hashes
  in Git/source ZIPs rather than normalising the transferred text.

## Verification and delivery

The final local Linux/Skia/headless run passed **182/182**, with native LibVLC
enabled and zero skips. Windows Release cross-build passed with zero warnings
or errors; self-contained Apple Silicon and Intel cross-publish succeeded.
The source contract has 49 XAML surfaces, 249 C# files and 530 declared bindings.
Existing Mac adapter warning instances remain documented in the batch reports.

Pushing the preview branch automatically starts `mac-download-preview.yml` with
read-only repository-content permissions. It runs the full suite on native
architecture-matched Mac runners, bundles VLC/.NET, verifies the preview ZIP and
exact source commit, checks the ad-hoc app signature, and uploads app/source
archives plus checksums and manifests. It does not create a GitHub release.
The matching Actions run/artifact records are the delivery receipt; a Linux
cross-build alone is not a downloadable/signed bundle verification.

Both architecture artifacts include a matching `git archive HEAD` source ZIP,
including the imported skills. Local build directories/caches, private transfer
archives and the unrelated uncommitted research directory are excluded.

`Prepare Roche Scoreboard.command` invokes the existing `sign-mac.sh` on the
adjacent app and then opens it. That helper applies local ad-hoc signatures with
the .NET JIT entitlement to bundled files and the app, then performs deep/strict
signature verification. It does not remove quarantine attributes, change global
Gatekeeper settings or install credentials. Packaging supplies executable bits.
The helper is not run on the user's device by this task. Builds are not notarised.

## Still pending

Mac-runner headless tests and package checks are distinct from hands-on desktop
acceptance. The original roughly 30-second AFL input freeze needs reproduction
on an actual Mac, including video/motion and prolonged mouse/keyboard interaction.
Retina/multiple-monitor layout, popup/focus/window behavior, screen-capture
permission/eyedropper and the requested dramatic motion need native GUI review.
Preview-only gates and `mac_acceptance_verified: false` remain in force.

Competition-specific player eligibility, interruption/result criteria and rule
profiles remain the decisions recorded in batch 2. This delivery changes no
credentials, security settings, master branch, release or deployment.
