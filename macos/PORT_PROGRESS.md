# Mac 5.0 port checkpoint — local implementation complete

Windows baseline: origin/master 636140761a9348b5030de731d3d91e5f741c1cbf (5.0.0).
Final Linux headless suite: **71 passed, 0 failed, 0 skipped**, with native LibVLC.
Translation audit: 49 XAML surfaces, 246 C# references, 530 UI event bindings.
All 364 imported Windows files match the baseline commit.

Final preview packages and archive checks are saved under artifacts/macos-5-preview;
see artifact-manifest.json for the completed archive checks.
See PORT_VERIFICATION.md and PLATFORM_BEHAVIOR_AUDIT.md for implementation and limits.
Existing research and user work remain intact. Only the separate Mac preview branch
and its artifact workflow are authorized; no master merge, deployment or release.

Native Mac acceptance is not complete. The executor is Linux; Cocoa input, the
reported AFL freeze, Retina/mixed displays, fullscreen/modal behavior and native
integrations require the Mac acceptance matrix. Glass uses the source software
fallback rather than the Windows DirectX shader. Verified release packaging stays
guarded in port-state.json. graphify update could not run because the CLI is absent.
