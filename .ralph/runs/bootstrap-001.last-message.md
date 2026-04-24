Changed files:
- [DualWriteMapBrowserView.xaml](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml)
- [DualWriteMapBrowserViewModel.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs)
- [DualWriteMapBrowserViewModel.TestifySettings.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.TestifySettings.cs)
- [TestifySettingsTextSerializer.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/TestifySettingsTextSerializer.cs)
- [AssemblyInfo.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/AssemblyInfo.cs)
- [TestifyConfigurationStoreTests.cs](C:/Users/ben.jones/Repos/toolbAX/tests/FoToolbox.Tests/TestifyConfigurationStoreTests.cs)

The change adds a `Testify Settings` toolbar button and an inline WPF per-map settings panel in `DualWriteMapBrowser`. The panel binds to the selected map, loads from `TestifyConfigurationStore` on selection/open, and saves `omitCreateFields`, `preferredCreateValues`, `cePollTimeoutMinutes`, and `allowPartialEnumCoverage` back through the existing store path. I also added a small text serializer for the multiline editor format and test access for the plugin internals.

Validation results:
- `dotnet build .\FoToolbox.sln -c Release` succeeded.
- `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests` built successfully but could not run because this machine is missing the x64 `Microsoft.AspNetCore.App` 8.0 runtime required by `testhost.exe`.

Assumptions or blockers:
- `AGENTS.md` was not present at the repo root, so I used the Ralph files and repository code as the source of truth.
- I treated “panel inside DualWriteMapBrowser” as an inline expandable editor rather than a separate modal dialog.
- Test execution is currently blocked by the local runtime environment, not by compile errors.

Known limitations or follow-up work:
- Saving updates the persisted config immediately, but existing preflight/run state is not recomputed automatically; the UI prompts the user to run `Prepare Testify` again.
- The new persistence tests are in place but still need to be executed once the required x64 .NET 8 ASP.NET runtime is installed.
- A quick manual WPF smoke test is still warranted for the selection-change/load-save flow.

```json
{
  "selectedTaskId": "T1",
  "requestedStatus": "done",
  "progressNote": "Added a Testify Settings toolbar entry and inline per-map WPF editor bound to the selected map, with load/save through TestifyConfigurationStore. Release solution build passed. Targeted test assembly builds, but test execution is blocked on this machine by a missing x64 Microsoft.AspNetCore.App 8.0 runtime.",
  "validationRan": "dotnet build .\\FoToolbox.sln -c Release",
  "blocker": "Targeted test execution could not run because testhost.exe requires the x64 Microsoft.AspNetCore.App 8.0 runtime, which is not installed on this machine.",
  "needsHumanReview": true
}
```