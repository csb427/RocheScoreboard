# Publishing Roche Scoreboard

The public download is a self-contained, multi-file Windows x64 package. Do not upload the EXE by itself and do not use GitHub's automatically generated source ZIP as the app download.

## Recommended: publish through GitHub

1. Update `<Version>` in `Roche_Scoreboard/Roche_Scoreboard.csproj` and update the in-app changelog.
2. Commit and push the release-ready source to the default branch.
3. Open **Actions → Publish Windows release → Run workflow**.
4. Enter the matching tag, such as `v4.1.0`, and run it.

The workflow restores dependencies, publishes the self-contained app, checks the EXE version and required runtime files, validates the full LibVLC plugin tree, scans out the previous Windows App SDK manifest conflict, runs the packaged startup self-test, creates the ZIP and checksum, then publishes both as GitHub release assets.

The stable asset contract is:

`Roche-Scoreboard-v<version>-Windows-x64.zip`

The in-app updater prefers this name and validates the complete portable folder before replacing an installed version.

## Local package check

From PowerShell at the repository root:

```powershell
.\Build-Release.ps1
```

Validated output is written to `artifacts/release`. Upload both the Windows ZIP and its SHA-256 text file if a release must be created manually.
