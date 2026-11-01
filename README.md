# Roche Scoreboard — Windows test preview

**Test source snapshot: 5.0.0-local-20261101-v1.** This branch contains the current integrated application source beyond the older a007d8c preview. It is for testing and has not been merged into master or published as a release.

## Run on Windows

1. Download this branch/commit as a ZIP and extract into a new folder outside your existing checkout.
2. Use Visual Studio2026 (or a current version supporting .NET10), with **.NET desktop development** and the **.NET10 SDK**.
3. Open **Roche_Scoreboard.slnx**, set Roche_Scoreboard as startup project, choose Debug, restore NuGet packages, then press **F5**.

This is source, not a precompiled Windows installer. The project already targets win-x64. See [START-WINDOWS-HERE.md](START-WINDOWS-HERE.md) for details and the first acceptance checks.

A separate source folder does not isolate saved app data: Windows still uses its normal Documents/Roche_Scoreboard folder. Back that folder up if you want to preserve existing matches/settings before testing resets or corrections. No operator saves are included.

## What is included

Current Windows source/assets and companion Mac/web/shared source/tests match the prepared local snapshot byte for byte. Repository-local instruction/reference sets retain their licences/provenance; they do not automatically install runtime services. Bulky research history, private diagnostic records, built binaries/caches and credentials are omitted. See [SOURCE-SNAPSHOT.json](SOURCE-SNAPSHOT.json).

Recent Linux/Avalonia affected-workflow aggregate: **137/137 passed**. The frozen source restored offline and Windows Debug cross-built with **zero warnings/errors**. Those are not native WPF GUI/hardware acceptance. No new Mac CI/package run is claimed.

See [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md) and [TEST-VERIFICATION.md](TEST-VERIFICATION.md). The older a007d8c preview remains unchanged.

ZIPs omit .git, so About/build revision may correctly show unknown/local. Git-cloned builds can report this branch's actual revision. The prepared tests/build were performed before publication; no clean-commit/native-launch claim is retroactively made.
