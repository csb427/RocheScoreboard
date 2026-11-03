# Windows refinement preview verification

Final Windows source/assets match the locally cross-built working revision. No app/test source changed after final verification. Publication adds documentation/manifest only.

| Scope / source epoch | Result |
| --- | --- |
| Final navigation/input affected suite |79 passed,0failed/skipped/aborted;4m06s |
| Final Windows Release / Debug |0warnings/errors;24.31s /25.07s |
| Earlier scorecard/wicket/history continuation |76passed; separate earlier epoch |
| Earlier workflow refinement |104passed; separate earlier epoch |
| Earlier contextual wicket/rules/ownership pass |106distinct passing cases across3invocations |
| Native translated product/test build |190inherited warnings,0errors;30.78s |
| Translation/parity |49XAML /307C# /575declared UI bindings |

Counts overlap and must not be added together or presented as one full-suite result. Final79 scopes: Home20, unified setup18, recovery9, clock choices12, settings keyboard4, navigation3, AFL history6, post-dialog input7. The Windows-only programmatic QA assertion compiles but was not executed as a native GUI test.

Input baselines reproduced fresh scoring loss under WPF-equivalent preview order: AFL pointer/Ctrl+1 navigation2fail, cricket dialog close6fail. Scoped native translation now preserves preview order for the tested guard handlers. Physical OS input, Windows compositor/UIA/DPI and cold-first-open timing still need native acceptance. Explicit routed repeat/position metadata checks do not prove OS double-click/touch classification.

A first oversized follow-up filter selected unrelated tests from partial classes; it was intentionally stopped and receives no aggregate pass credit. Final79 uses exact workflow method selectors/small traits. Failed baselines, fixture corrections and private diagnostics were retained locally; assertions were not weakened to force a pass.

No new complete historical regression suite, native Windows GUI certification, Mac CI/package acceptance, release or deployment is claimed. SOURCE-SNAPSHOT.json records byte hashes of included files; local source provenance does not fabricate a clean-commit identity for earlier dirty builds.
