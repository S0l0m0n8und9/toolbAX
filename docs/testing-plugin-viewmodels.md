# Testing plugin view models

Plugin tools are WPF `UserControl`s, but their logic lives in view models (the `*View.xaml.cs`
code-behind is just `InitializeComponent()`). Test the **view model**, not the control — that keeps
tests headless (no visible desktop session) and fast.

Two shared helpers in `FoToolbox.TestHelpers` make this easy:

- **`FakePluginContext`** — an in-memory `IPluginContext` (current environment, an empty OData client,
  a catalog, and a null logger). It defaults to the seeded `FakeCatalogService` (one `Customers`
  entity from `TestCatalogBuilder`); pass a custom `ICatalogService` to drive a specific flow.
- **`UiThreadTestHost.Run(async () => { ... })`** — runs the test body on a dedicated STA thread whose
  awaited continuations are pumped back onto that same thread, so the view model sees one stable "UI
  thread" (the WPF dispatcher model).

## Why the UI-thread host matters

A view model that does `await someService.LoadAsync()` and then mutates a UI-bound collection (or a
`ICollectionView`) **must resume on the UI thread**. Under a real dispatcher, a misplaced
`ConfigureAwait(false)` resumes the continuation on a thread-pool thread and WPF throws on the
cross-thread collection change — which the view model often turns into a misleading "load failed".

`UiThreadTestHost` reproduces that thread affinity headlessly, so the bug surfaces as a failing test
instead of only in the running app. (See issue #37.)

## Pattern

```csharp
[Fact]
public void LoadEntities_populates_entities_from_the_catalog()
{
    UiThreadTestHost.Run(async () =>
    {
        var vm = new ODataPostBuilderViewModel(new FakePluginContext());

        await vm.LoadEntitiesCommand.ExecuteAsync(CancellationToken.None);

        Assert.Equal("Loaded 1 entities.", vm.EntityLoadStatus);
        Assert.Contains(vm.Entities, e => e.Name == "Customers");
    });
}
```

To exercise threading/concurrency, pass a catalog that completes asynchronously (on a pool thread) or
that blocks on a gate — see `DeferredCatalog` in `ODataPostBuilderViewModelTests`.

## Tips

- Prefer `AsyncRelayCommand.ExecuteAsync(...)` over `Execute(...)` in tests so you can `await` the flow.
- If a view model opens a store keyed off `ProfilePaths.ResolveProfileDbPath()` in its constructor,
  set `FOTOOLBOX_APPDATA_DIR` to a temp directory first to isolate it (parallelization is disabled
  assembly-wide via `TestAssemblyInfo.cs`, so mutating the env var for the test's duration is safe).
- Seed catalog/metadata shapes through `TestCatalogBuilder` so they stay consistent across suites.
