**Changed Files**
- [DualWriteMapBrowserViewModel.cs](c:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs:78): added an internal constructor overload so the Testify settings flow can be exercised against an injected `TestifyConfigurationStore` in tests.
- [DualWriteMapBrowserViewModel.Testify.cs](c:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.Testify.cs:30): moved `_testifyConfigStore` initialization to the constructor-backed seam.
- [DualWriteMapBrowserViewModel.TestifySettings.cs](c:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.TestifySettings.cs:155): removed `ConfigureAwait(false)` from the WPF view-model settings load/save path so property updates stay on the captured context.
- [DualWriteMapBrowserViewModelTestifySettingsTests.cs](c:/Users/ben.jones/Repos/toolbAX/tests/FoToolbox.Tests/DualWriteMapBrowserViewModelTestifySettingsTests.cs:13): added a regression test covering selected-map load, switching to a second map, save, and reload through a fresh view-model instance.

**Validation Results**
- `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj -c Release --no-restore --filter SaveAndReload_PersistsSelectedMapSettingsAcrossViewModelInstances`
  Result: build/test discovery succeeded, but execution aborted because this machine is missing the x64 `Microsoft.AspNetCore.App 8.0` runtime for `testhost.exe`.
- `dotnet build .\FoToolbox.sln -c Release`
  Result: passed with 0 warnings and 0 errors.

**Assumptions / Blockers**
- The repo already contained the Testify settings UI work from the prior Ralph iteration; this slice focused on the missing test seam and runtime-safe async behavior.
- Full test execution is still blocked locally by the missing x64 ASP.NET Core 8 runtime.
- I did not touch `.ralph/tasks.json` or `.ralph/progress.md`.

**Known Limitations / Follow-up**
- After installing the x64 `Microsoft.AspNetCore.App 8.0` runtime, rerun the new targeted test and then the broader `dotnet test` suite.
- Manual WPF smoke verification is still pending for the toolbar-to-panel path in the running app.

```json
{
  "selectedTaskId": "T1",
  "requestedStatus": "in_progress",
  "progressNote": "Added a testable TestifyConfigurationStore injection seam for DualWriteMapBrowserViewModel, removed ConfigureAwait(false) from the WPF Testify settings load/save path, and added a regression test covering per-map settings load, map switching, save, and reload across a fresh view-model instance. Release solution build passed.",
  "blocker": "Targeted test execution is blocked on this machine because testhost.exe requires the x64 Microsoft.AspNetCore.App 8.0 runtime, which is not installed.",
  "validationRan": "dotnet test .\\tests\\FoToolbox.Tests\\FoToolbox.Tests.csproj -c Release --no-restore --filter SaveAndReload_PersistsSelectedMapSettingsAcrossViewModelInstances; dotnet build .\\FoToolbox.sln -c Release"
}
```