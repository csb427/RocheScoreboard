# Roche Scoreboard — revised Windows test preview

**Snapshot: 5.0.0-windows-feedback-20261101-v2.** Includes the first Windows hands-on feedback fixes on top of the previous261db075 preview. Previous downloads and master are unchanged. This is source for testing, not a precompiled installer or a public release.

## Run on Windows

1. Extract the complete ZIP into a new folder, separate from earlier test copies.
2. Use Visual Studio2026/current .NET10-capable version with **.NET desktop development** and the **.NET10 SDK**.
3. Open **Roche_Scoreboard.slnx**, set **Roche_Scoreboard** as startup project, choose **Debug**, restore NuGet, then **F5**.

[Detailed launch steps](START-WINDOWS-HERE.md) · [Known limitations](KNOWN-LIMITATIONS.md) · [Verification](TEST-VERIFICATION.md)

## Changes in this revision

- Selection fills follow rounded card/button geometry rather than always forming capsules; tab motion is preserved.
- First setup page removes the blank Back slot; Training has more noticeable hover feedback.
- Thin full-width cricket delivery rows; Edit reveals on hover or row keyboard focus. The latest row clears its over header.
- Tighter wicket/FOR spacing; larger team-name, clock and weather text fitted inside unchanged panels.
- Extras and Innings Boundaries are normal Cricket Overlays controls under Match Context and Recent Play, with distinct content and scorebug-only eligibility.

45 combined affected-workflow checks passed, plus5 final activation checks (46 distinct cases across both runs). Windows Release/Debug cross-builds each have zero warnings/errors. **Intermittent native Windows first-use animation skipping remains unresolved.** Linux translated UI tests and cross-builds are not native Windows acceptance.

The app still uses its normal Documents/Roche_Scoreboard data folder: a separate source folder does not isolate matches/settings. Preserve that data before testing destructive actions if needed. No operator saves, credentials, build caches or private diagnostic history are included. Companion source/tests and repository-local skills retain existing licences/provenance; they do not install services automatically.

ZIPs have no .git; About/build revision may correctly show unknown/local. A cloned build can identify its actual commit. Tests/builds ran before this publication; no clean-commit or native launch claim is made retroactively. SOURCE-SNAPSHOT.json lists the included files and hashes.
