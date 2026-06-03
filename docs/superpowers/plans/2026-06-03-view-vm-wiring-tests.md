# Headless View↔VM Wiring Tests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `FoToolbox.UiTests` project that mounts every host view and plugin tool control offscreen and asserts each one constructs without throwing and produces zero WPF data-binding errors.

**Architecture:** Each test runs on an STA dispatcher thread via `Xunit.StaFact`'s `[WpfFact]`/`[WpfTheory]`. A view is built through its real production lifecycle (`InitializeAsync` → `CreateTool` for plugins; real ViewModel for host views), mounted in an invisible `HwndSource` (so `Loaded` fires, styles apply, and bindings evaluate), and checked against a `TraceListener` attached to `PresentationTraceSources.DataBindingSource`. No `Application` singleton is shared across threads — pack:// scheme is registered once and theme dictionaries are merged at each mount root.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, xUnit 2.9.3, `Xunit.StaFact` (MIT), CPM (`Directory.Packages.props`).

**Spec:** `docs/superpowers/specs/2026-06-03-view-vm-wiring-tests-design.md`

---

## File Structure

| File | Responsibility |
| --- | --- |
| `tests/FoToolbox.UiTests/FoToolbox.UiTests.csproj` | New test project; references Host + all 7 plugins. |
| `tests/FoToolbox.UiTests/AssemblyInfo.cs` | `DisableTestParallelization` (global trace listener is shared). |
| `tests/FoToolbox.UiTests/Infrastructure/WpfTestRuntime.cs` | One-time process-wide pack:// scheme registration. |
| `tests/FoToolbox.UiTests/Infrastructure/OffscreenHost.cs` | Mounts a `FrameworkElement` offscreen; forces layout; pumps dispatcher. |
| `tests/FoToolbox.UiTests/Infrastructure/BindingErrorScope.cs` | Collects WPF binding-trace errors for the duration of a test. |
| `tests/FoToolbox.UiTests/Infrastructure/FakePluginContext.cs` | Seeded fake implementing `IPluginContext` + Write/Dataverse/Navigation. |
| `tests/FoToolbox.UiTests/ViewCase.cs` | Record describing one mountable view + optional `WarmUp`. |
| `tests/FoToolbox.UiTests/ViewRegistry.cs` | The list of plugin + host view cases. |
| `tests/FoToolbox.UiTests/ViewWiringTests.cs` | The `[WpfTheory]` over the registry + window `[WpfFact]`s. |
| `tests/FoToolbox.UiTests/SelfTests/HarnessSelfTests.cs` | Proves the harness catches a deliberately-broken binding. |
| `src/FoToolbox.Host/AssemblyInfo.cs` | Add `InternalsVisibleTo("FoToolbox.UiTests")` (ProfilesView ctor is internal). |
| `Directory.Packages.props` | Add `Xunit.StaFact` version (CPM). |
| `.github/workflows/ci.yml` | New isolated `ui-tests` job. |
| `FoToolbox.sln` | Add the new project. |

---

## Task 1: Scaffold the project and prove `Xunit.StaFact` + WPF runs

**Files:**
- Create: `tests/FoToolbox.UiTests/FoToolbox.UiTests.csproj`
- Create: `tests/FoToolbox.UiTests/AssemblyInfo.cs`
- Create: `tests/FoToolbox.UiTests/SmokeTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `FoToolbox.sln`

- [ ] **Step 1: Add the `Xunit.StaFact` package version (CPM)**

In `Directory.Packages.props`, under the `<!-- Test -->` group, add:

```xml
    <PackageVersion Include="Xunit.StaFact" Version="1.1.18" />
```

(If `dotnet restore` reports a newer 1.x patch, take it — `Xunit.StaFact` 1.x targets xUnit v2, which matches `xunit` 2.9.3.)

- [ ] **Step 2: Create the project file**

Create `tests/FoToolbox.UiTests/FoToolbox.UiTests.csproj` (mirrors `tests/FoToolbox.Tests/FoToolbox.Tests.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$(MSBuildProjectDirectory)\..\..\build\fotoolbox.snk</AssemblyOriginatorKeyFile>
    <NoWarn>$(NoWarn);CS8002</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\FoToolbox.Core\\FoToolbox.Core.csproj" />
    <ProjectReference Include="..\\..\\src\\FoToolbox.SDK\\FoToolbox.SDK.csproj" />
    <ProjectReference Include="..\\..\\src\\FoToolbox.Host\\FoToolbox.Host.csproj" />
    <ProjectReference Include="..\\..\\plugins\\HelloPlugin\\HelloPlugin.csproj" />
    <ProjectReference Include="..\\..\\plugins\\QueryBuilder\\QueryBuilder.csproj" />
    <ProjectReference Include="..\\..\\plugins\\TableEntityBrowser\\TableEntityBrowser.csproj" />
    <ProjectReference Include="..\\..\\plugins\\ODataPostBuilder\\ODataPostBuilder.csproj" />
    <ProjectReference Include="..\\..\\plugins\\DualWriteMapBrowser\\DualWriteMapBrowser.csproj" />
    <ProjectReference Include="..\\..\\plugins\\DualWriteOperations\\DualWriteOperations.csproj" />
    <ProjectReference Include="..\\..\\plugins\\DualWriteCompare\\DualWriteCompare.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Xunit.StaFact" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Disable test parallelization**

Create `tests/FoToolbox.UiTests/AssemblyInfo.cs`:

```csharp
using Xunit;

// The WPF binding TraceListener and the process pack:// registration are global state,
// so UI tests must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

- [ ] **Step 4: Add the smoke test**

Create `tests/FoToolbox.UiTests/SmokeTests.cs`:

```csharp
using System.Windows.Controls;
using Xunit;

namespace FoToolbox.UiTests;

public class SmokeTests
{
    [WpfFact]
    public void Wpf_controls_can_be_constructed_on_the_test_thread()
    {
        var button = new Button { Content = "ok" };
        Assert.Equal("ok", button.Content);
    }
}
```

- [ ] **Step 5: Add the project to the solution**

Run: `dotnet sln .\FoToolbox.sln add .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 6: Build and run the smoke test**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release`
Expected: 1 test, PASS. (Confirms `[WpfFact]` runs WPF objects on an STA thread.)

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props FoToolbox.sln tests/FoToolbox.UiTests/FoToolbox.UiTests.csproj tests/FoToolbox.UiTests/AssemblyInfo.cs tests/FoToolbox.UiTests/SmokeTests.cs
git commit -m "test(ui): scaffold FoToolbox.UiTests with Xunit.StaFact smoke test"
```

---

## Task 2: `WpfTestRuntime` + `OffscreenHost`

**Files:**
- Create: `tests/FoToolbox.UiTests/Infrastructure/WpfTestRuntime.cs`
- Create: `tests/FoToolbox.UiTests/Infrastructure/OffscreenHost.cs`
- Test: `tests/FoToolbox.UiTests/Infrastructure/OffscreenHostTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/FoToolbox.UiTests/Infrastructure/OffscreenHostTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests.Infrastructure;

public class OffscreenHostTests
{
    private sealed class Model { public string Title { get; set; } = "hello"; }

    [WpfFact]
    public void Mount_evaluates_bindings_against_the_data_context()
    {
        var text = new TextBlock { DataContext = new Model() };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Title)));

        using var host = OffscreenHost.Mount(text);

        Assert.Equal("hello", text.Text);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter OffscreenHostTests`
Expected: FAIL — `OffscreenHost` does not exist (compile error).

- [ ] **Step 3: Implement `WpfTestRuntime`**

Create `tests/FoToolbox.UiTests/Infrastructure/WpfTestRuntime.cs`:

```csharp
using System.Windows;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Registers the WPF pack:// URI scheme exactly once per process so Host theme
/// ResourceDictionaries can be loaded by absolute pack URI. A single Application is
/// created purely for the registration side-effect and is never accessed again, so its
/// thread affinity is irrelevant.
/// </summary>
internal static class WpfTestRuntime
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsurePackSchemeRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
            _registered = true;
        }
    }
}
```

- [ ] **Step 4: Implement `OffscreenHost`**

Create `tests/FoToolbox.UiTests/Infrastructure/OffscreenHost.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Mounts a WPF element in an invisible HwndSource so Loaded fires, styles apply, and
/// data bindings evaluate — without showing a window. Host theme dictionaries are merged
/// at the mount root so StaticResource lookups resolve without an Application.Resources.
/// </summary>
internal sealed class OffscreenHost : IDisposable
{
    private static readonly string[] ThemeDictionaries =
    {
        "pack://application:,,,/FoToolbox.Host;component/Themes/Fluent.Theme.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Spacing.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Icons.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Fluent.Controls.xaml",
    };

    private readonly HwndSource _source;

    private OffscreenHost(HwndSource source) => _source = source;

    public static OffscreenHost Mount(FrameworkElement element)
    {
        WpfTestRuntime.EnsurePackSchemeRegistered();

        var root = new Border();
        foreach (var uri in ThemeDictionaries)
        {
            root.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
        }
        root.Child = element;

        var parameters = new HwndSourceParameters("FoToolbox.UiTests.Offscreen")
        {
            Width = 1280,
            Height = 1024,
            WindowStyle = 0, // WS_VISIBLE not set => never displayed
        };
        var source = new HwndSource(parameters) { RootVisual = root };

        root.Measure(new Size(1280, 1024));
        root.Arrange(new Rect(0, 0, 1280, 1024));
        root.UpdateLayout();

        var host = new OffscreenHost(source);
        host.PumpToIdle();
        return host;
    }

    public void PumpToIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose() => _source.Dispose();
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter OffscreenHostTests`
Expected: PASS — `text.Text` is `"hello"`, proving layout + binding evaluation occurred offscreen.

- [ ] **Step 6: Commit**

```bash
git add tests/FoToolbox.UiTests/Infrastructure/WpfTestRuntime.cs tests/FoToolbox.UiTests/Infrastructure/OffscreenHost.cs tests/FoToolbox.UiTests/Infrastructure/OffscreenHostTests.cs
git commit -m "test(ui): add WpfTestRuntime and OffscreenHost mounting helper"
```

---

## Task 3: `BindingErrorScope` + harness self-test

**Files:**
- Create: `tests/FoToolbox.UiTests/Infrastructure/BindingErrorScope.cs`
- Create: `tests/FoToolbox.UiTests/SelfTests/HarnessSelfTests.cs`

- [ ] **Step 1: Write the failing self-tests**

Create `tests/FoToolbox.UiTests/SelfTests/HarnessSelfTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests.SelfTests;

public class HarnessSelfTests
{
    private sealed class Model { public string Title { get; set; } = "ok"; }

    [WpfFact]
    public void Scope_catches_a_broken_binding_path()
    {
        var text = new TextBlock { DataContext = new Model() };
        // "Nope" does not exist on Model => WPF emits a data-binding error.
        text.SetBinding(TextBlock.TextProperty, new Binding("Nope"));

        using var scope = new BindingErrorScope();
        using var host = OffscreenHost.Mount(text);
        host.PumpToIdle();

        Assert.NotEmpty(scope.Errors);
    }

    [WpfFact]
    public void Scope_reports_no_errors_for_a_correct_binding()
    {
        var text = new TextBlock { DataContext = new Model() };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Title)));

        using var scope = new BindingErrorScope();
        using var host = OffscreenHost.Mount(text);
        host.PumpToIdle();

        Assert.Empty(scope.Errors);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter HarnessSelfTests`
Expected: FAIL — `BindingErrorScope` does not exist (compile error).

- [ ] **Step 3: Implement `BindingErrorScope`**

Create `tests/FoToolbox.UiTests/Infrastructure/BindingErrorScope.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Data;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Captures WPF data-binding trace output (PresentationTraceSources.DataBindingSource)
/// for the lifetime of the scope. Any captured message indicates a binding failure.
/// </summary>
internal sealed class BindingErrorScope : IDisposable
{
    private readonly CollectingTraceListener _listener = new();
    private readonly SourceLevels _previousLevel;

    public BindingErrorScope()
    {
        PresentationTraceSources.Refresh();
        _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
    }

    public IReadOnlyList<string> Errors => _listener.Messages;

    public void Dispose()
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
    }

    private sealed class CollectingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message!);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message!);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter HarnessSelfTests`
Expected: PASS (2 tests). This proves the suite fails when bindings break — it isn't green because it detects nothing.

- [ ] **Step 5: Commit**

```bash
git add tests/FoToolbox.UiTests/Infrastructure/BindingErrorScope.cs tests/FoToolbox.UiTests/SelfTests/HarnessSelfTests.cs
git commit -m "test(ui): add BindingErrorScope with broken-binding self-test"
```

---

## Task 4: `FakePluginContext` (seeded)

**Files:**
- Create: `tests/FoToolbox.UiTests/Infrastructure/FakePluginContext.cs`
- Test: `tests/FoToolbox.UiTests/Infrastructure/FakePluginContextTests.cs`

Reference implementation: the `FakeContext` / `FakeODataClient` / `FakeCatalogService` already in `tests/FoToolbox.Tests/QueryBuilderPluginTests.cs`. Reuse those exact member shapes; this task consolidates them and extends with the Write/Dataverse/Navigation interfaces plus seeded metadata.

- [ ] **Step 1: Write the failing test**

Create `tests/FoToolbox.UiTests/Infrastructure/FakePluginContextTests.cs`:

```csharp
using System.Linq;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests.Infrastructure;

public class FakePluginContextTests
{
    [Fact]
    public async Task Context_exposes_all_optional_capabilities_and_seeded_metadata()
    {
        var ctx = new FakePluginContext();

        Assert.IsAssignableFrom<IPluginContextWrite>(ctx);
        Assert.IsAssignableFrom<IPluginContextDataverse>(ctx);
        Assert.IsAssignableFrom<IPluginContextNavigation>(ctx);

        var metadata = await ctx.Catalog.GetODataMetadataAsync(ctx.CurrentEnv, CatalogRefreshMode.CacheFirst);
        Assert.Contains(metadata.Entities, e => e.Name == "Customers");
    }
}
```

(If the `CatalogRefreshMode` member is named differently, use the value used by `FakeCatalogService` in `QueryBuilderPluginTests.cs`.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter FakePluginContextTests`
Expected: FAIL — `FakePluginContext` does not exist (compile error).

- [ ] **Step 3: Implement `FakePluginContext`**

Create `tests/FoToolbox.UiTests/Infrastructure/FakePluginContext.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Seeded fake plugin context implementing every optional capability so any plugin's
/// CreateTool succeeds and bindings have realistic data to resolve against.
/// </summary>
internal sealed class FakePluginContext :
    IPluginContext, IPluginContextWrite, IPluginContextDataverse, IPluginContextNavigation
{
    private static readonly DateTime SeedTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public FakePluginContext()
    {
        CurrentEnv = new FoEnvironment("env", "Contoso", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        OData = new SeededODataClient();
        Catalog = new SeededCatalogService();
        Logger = NullLogger.Instance;
        ODataWrite = new NoopWriteClient();
    }

    public FoEnvironment CurrentEnv { get; set; }
    public IODataClient OData { get; }
    public ICatalogService Catalog { get; }
    public ILogger Logger { get; }

    // IPluginContextWrite
    public IODataWriteClient ODataWrite { get; }

    // IPluginContextDataverse
    public bool HasDataverseProfile => false;
    public DataverseEnvironment? CurrentDataverseEnv => null;
    public HttpClient? DataverseHttp => null;

    // IPluginContextNavigation
    public bool TryNavigateTo(string targetPluginId, IReadOnlyDictionary<string, string> parameters) => false;

    private static ODataEntity Customers => new(
        "Customers",
        new[]
        {
            new ODataProperty("AccountNumber", "Edm.String", false),
            new ODataProperty("Name", "Edm.String", true),
        },
        Array.Empty<ODataNavigationProperty>());

    private static ODataMetadata Metadata => new(new[] { Customers }, Array.Empty<ODataEnumType>(), null);

    private static TableCatalog Tables => new("contoso", "Contoso", SeedTime, Array.Empty<TableInfo>());

    private sealed class SeededODataClient : IODataClient
    {
        public async IAsyncEnumerable<ODataPage> StreamAsync(
            QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var rows = new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["AccountNumber"] = "US-001", ["Name"] = "Contoso Retail" },
                new Dictionary<string, object?> { ["AccountNumber"] = "US-002", ["Name"] = "Fabrikam" },
            };
            yield return new ODataPage(rows, NextLink: null, ODataCount: rows.Length);
        }
    }

    private sealed class NoopWriteClient : IODataWriteClient
    {
        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
            => Task.FromResult(new ODataWriteResponse(200, "{}", new Dictionary<string, string>()));
    }

    private sealed class SeededCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(Tables);

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(Metadata);

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, Tables, Metadata, SeedTime));

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
```

> **If `ICatalogService` has more members than the four above**, implement them by mirroring the canonical `FakeCatalogService` in `tests/FoToolbox.Tests/QueryBuilderPluginTests.cs` (return the seeded `Tables`/`Metadata` shown here). The compiler error from Step 2 lists any missing members.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter FakePluginContextTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/FoToolbox.UiTests/Infrastructure/FakePluginContext.cs tests/FoToolbox.UiTests/Infrastructure/FakePluginContextTests.cs
git commit -m "test(ui): add seeded FakePluginContext with all optional capabilities"
```

---

## Task 5: `ViewCase`, `ViewRegistry` (7 plugins) and the binding-error theory

**Files:**
- Create: `tests/FoToolbox.UiTests/ViewCase.cs`
- Create: `tests/FoToolbox.UiTests/ViewRegistry.cs`
- Create: `tests/FoToolbox.UiTests/ViewWiringTests.cs`

- [ ] **Step 1: Write the `ViewCase` record**

Create `tests/FoToolbox.UiTests/ViewCase.cs`:

```csharp
using System;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FoToolbox.UiTests;

/// <summary>
/// One mountable view. <see cref="Factory"/> builds the control through its real
/// production lifecycle. <see cref="WarmUp"/> is an optional hook to trigger a primary
/// load command so seeded data flows into item/data-template bindings; most cases leave
/// it null and rely on constructor/InitializeAsync loads settling during the pump.
/// </summary>
internal sealed record ViewCase(
    string Name,
    Func<Task<UserControl>> Factory,
    Action<object?>? WarmUp = null)
{
    public override string ToString() => Name;
}
```

- [ ] **Step 2: Write the `ViewRegistry` (plugins only for now)**

Create `tests/FoToolbox.UiTests/ViewRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using FoToolbox.SDK.Plugins;
using FoToolbox.UiTests.Infrastructure;

namespace FoToolbox.UiTests;

internal static class ViewRegistry
{
    public static IReadOnlyDictionary<string, ViewCase> All { get; } =
        Build().ToDictionary(c => c.Name, StringComparer.Ordinal);

    private static IEnumerable<ViewCase> Build()
    {
        yield return Plugin("QueryBuilder", () => new QueryBuilderPlugin.QueryBuilderPlugin());
        yield return Plugin("ODataPostBuilder", () => new ODataPostBuilderPlugin.ODataPostBuilderPlugin());
        yield return Plugin("TableEntityBrowser", () => new TableEntityBrowserPlugin.TableEntityBrowserPlugin());
        yield return Plugin("DualWriteMapBrowser", () => new DualWriteMapBrowserPlugin.DualWriteMapBrowserPlugin());
        yield return Plugin("DualWriteOperations", () => new DualWriteOperationsPlugin.DualWriteOperationsPlugin());
        yield return Plugin("DualWriteCompare", () => new DualWriteComparePlugin.DualWriteComparePlugin());
        yield return Plugin("Hello", () => new HelloPlugin.HelloFoToolPlugin());
        // Host views are added in Task 6.
    }

    private static ViewCase Plugin(string name, Func<IFoToolPlugin> create) =>
        new(name, async () =>
        {
            var plugin = create();
            await plugin.InitializeAsync(new FakePluginContext());
            return plugin.CreateTool();
        });
}
```

- [ ] **Step 3: Write the theory test**

Create `tests/FoToolbox.UiTests/ViewWiringTests.cs`:

```csharp
using System.Threading.Tasks;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests;

public class ViewWiringTests
{
    // Case names are strings (serializable) so xUnit gets one discoverable test per view
    // and no xUnit1045 non-serializable-data warning (which CI treats as an error).
    public static TheoryData<string> ViewCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ViewRegistry.All.Keys)
        {
            data.Add(name);
        }
        return data;
    }

    [WpfTheory]
    [MemberData(nameof(ViewCaseNames))]
    public async Task View_constructs_and_has_no_binding_errors(string caseName)
    {
        var view = ViewRegistry.All[caseName];

        using var scope = new BindingErrorScope();
        var control = await view.Factory();       // construct + lifecycle (throws => fail)
        using var host = OffscreenHost.Mount(control);
        view.WarmUp?.Invoke(control.DataContext);  // optional seeded-data load
        host.PumpToIdle();

        Assert.True(
            scope.Errors.Count == 0,
            $"'{caseName}' produced {scope.Errors.Count} binding error(s):\n" + string.Join("\n", scope.Errors));
    }
}
```

- [ ] **Step 4: Run the theory**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter ViewWiringTests`
Expected: 7 cases. **If all pass, continue.** If a case fails, read the binding errors in the assertion message and either (a) fix the binding in the offending plugin's XAML if it's a real bug, or (b) if it is a genuine environment artifact, add the case name to a `Quarantine` set with a comment and a tracking note, and assert it is skipped (see Task 7's quarantine mechanism). **Do not weaken the assertion for all views.**

- [ ] **Step 5: Commit**

```bash
git add tests/FoToolbox.UiTests/ViewCase.cs tests/FoToolbox.UiTests/ViewRegistry.cs tests/FoToolbox.UiTests/ViewWiringTests.cs
git commit -m "test(ui): assert all 7 plugin tool controls have zero binding errors"
```

---

## Task 6: Host views (`ProfilesView` + `PluginConsentWindow`)

**Files:**
- Modify: `src/FoToolbox.Host/AssemblyInfo.cs`
- Modify: `tests/FoToolbox.UiTests/ViewRegistry.cs`
- Modify: `tests/FoToolbox.UiTests/ViewWiringTests.cs`

- [ ] **Step 1: Grant the UI test project access to Host internals**

`ProfilesView`'s constructor is `internal`. In `src/FoToolbox.Host/AssemblyInfo.cs`, add below the existing line:

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FoToolbox.UiTests")]
```

- [ ] **Step 2: Add the `ProfilesView` case to the registry**

In `tests/FoToolbox.UiTests/ViewRegistry.cs`, add these usings at the top:

```csharp
using System.IO;
using FoToolbox.Host.ViewModels;
using FoToolbox.Host.Views;
using Microsoft.Extensions.Logging.Abstractions;
```

Add this case to `Build()` after the plugin cases (before the closing comment):

```csharp
        yield return new ViewCase("ProfilesView", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "fotoolbox-uitests");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".db");
            // ProfilesView.Loaded auto-runs RefreshCommand against this empty temp store.
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            return Task.FromResult<UserControl>(new ProfilesView(vm));
        });
```

- [ ] **Step 3: Run the theory (now 8 cases)**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter ViewWiringTests`
Expected: 8 cases. `ProfilesView` builds a real `ProfilesViewModel` (SQLite at a temp path) and auto-loads on `Loaded`. Apply the same fix-or-quarantine rule from Task 5 Step 4 if it fails.

- [ ] **Step 4: Add the `PluginConsentWindow` construction test**

`PluginConsentWindow` is a `Window`, not a `UserControl`, so it is tested separately. Add these usings to `tests/FoToolbox.UiTests/ViewWiringTests.cs`:

```csharp
using System.Windows;
using FoToolbox.Host.Plugins;
using FoToolbox.Host.Views;
using FoToolbox.UiTests.Infrastructure;
```

Add this test method to the `ViewWiringTests` class:

```csharp
    [WpfFact]
    public void PluginConsentWindow_constructs_with_no_binding_errors()
    {
        using var scope = new BindingErrorScope();

        var window = new PluginConsentWindow(
            new PluginConsentRequest("Demo.Plugin.dll", @"C:\plugins\Demo.Plugin.dll", "abc123def456"));

        // Reparent the window content so it can be measured/bound offscreen without Show().
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        using var host = OffscreenHost.Mount(content);
        host.PumpToIdle();

        Assert.True(scope.Errors.Count == 0, string.Join("\n", scope.Errors));
    }
```

- [ ] **Step 5: Run the host-view tests**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --filter ViewWiringTests`
Expected: 8 theory cases + 1 window fact = 9 tests, all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FoToolbox.Host/AssemblyInfo.cs tests/FoToolbox.UiTests/ViewRegistry.cs tests/FoToolbox.UiTests/ViewWiringTests.cs
git commit -m "test(ui): cover ProfilesView and PluginConsentWindow"
```

---

## Task 7: Quarantine mechanism

A view that proves genuinely flaky (not a real bug) must be skippable explicitly, with the assertion intact for everything else.

**Files:**
- Modify: `tests/FoToolbox.UiTests/ViewWiringTests.cs`

- [ ] **Step 1: Add the quarantine set and apply it in the theory**

In `tests/FoToolbox.UiTests/ViewWiringTests.cs`, add a static field to the class:

```csharp
    // View case names temporarily excluded from the binding-error assertion. Each entry
    // MUST have a one-line reason and a tracking note. Empty by default — keep it empty.
    private static readonly IReadOnlySet<string> Quarantine = new HashSet<string>(System.StringComparer.Ordinal)
    {
        // e.g. "SomeView", // flaky offscreen render on CI image — tracked in issue #NN
    };
```

Add this guard as the first lines of `View_constructs_and_has_no_binding_errors`:

```csharp
        Assert.SkipWhen(Quarantine.Contains(caseName), $"'{caseName}' is quarantined.");
```

(`Assert.SkipWhen` requires xUnit 2.9+, which the repo uses. If unavailable, use `Xunit.SkipException` from `Xunit.StaFact` patterns or early-`return` with a logged skip.)

- [ ] **Step 2: Build and run the full UI suite**

Run: `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release`
Expected: all tests PASS (quarantine set is empty, so nothing is skipped).

- [ ] **Step 3: Commit**

```bash
git add tests/FoToolbox.UiTests/ViewWiringTests.cs
git commit -m "test(ui): add explicit quarantine mechanism for flaky views"
```

---

## Task 8: Isolated CI job

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add a separate `ui-tests` job**

In `.github/workflows/ci.yml`, add a new job alongside `build-test` (do not modify `build-test`):

```yaml
  ui-tests:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: |
            **/*.csproj
            Directory.Build.props

      - name: Restore
        run: dotnet restore .\FoToolbox.sln

      - name: Build UI tests
        run: dotnet build .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --no-restore

      - name: Run UI tests
        run: dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release --no-build --blame-hang --blame-hang-timeout 150s --blame-hang-dump-type mini --results-directory ${{ github.workspace }}\UiTestResults

      - name: Upload UI test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: ui-test-results
          path: ${{ github.workspace }}\UiTestResults
          if-no-files-found: ignore
```

- [ ] **Step 2: Verify the workflow is valid YAML and the job builds locally**

Run: `dotnet build .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj -c Release`
Expected: build succeeds (mirrors the CI step). Visually confirm the new `ui-tests` job is a sibling of `build-test` (same indentation under `jobs:`).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run FoToolbox.UiTests in an isolated job"
```

- [ ] **Step 4: Full local gate before pushing**

Run: `dotnet test .\FoToolbox.sln -c Release`
Expected: the whole solution (unit + UI tests) passes. Note: locally `TreatWarningsAsErrors` is off; CI turns it on, so confirm there are no new analyzer warnings (especially xUnit1045 on `[MemberData]`) by building with `dotnet build .\FoToolbox.sln -c Release -p:CI=true`.

---

## Self-Review

**Spec coverage:**
- §1 Goal & contract (construct + zero binding errors) → Task 5/6 assertions. ✓
- §2 Project & deps (`FoToolbox.UiTests`, `Xunit.StaFact`, CPM, `DisableTestParallelization`) → Task 1. ✓
- §3 Harness components: `BindingErrorScope` → Task 3; `OffscreenHost` → Task 2; `FakePluginContext` → Task 4. The spec's `UiTestApplicationFixture` is realized as `WpfTestRuntime` + per-mount theme dictionaries (Task 2) to avoid the `Application` thread-affinity trap — same intent (themes resolve), better mechanism. ✓
- §4 View registry + `WarmUp` → Task 5 (`ViewCase`/`ViewRegistry`/theory). `WarmUp` ships as an available, unused hook; loads are triggered by ctor/`InitializeAsync` and settle during the pump — consistent with the spec's accepted limitation. ✓
- §5 CI separate job + quarantine → Tasks 8 and 7. ✓
- §6 Out of scope (`DualWriteSignInWindow`, visual, E2E) → not referenced; honored. ✓
- "Testing the harness itself" → Task 3 self-tests. ✓

**Placeholder scan:** No TBD/TODO. The two conditional notes (extra `ICatalogService` members in Task 4; `Assert.SkipWhen` availability in Task 7) point to a concrete fallback with a named reference, not unfinished work.

**Type consistency:** `ViewCase(Name, Factory, WarmUp)` is defined in Task 5 and consumed unchanged in Tasks 5–7. `OffscreenHost.Mount`/`PumpToIdle`/`Dispose` (Task 2) used consistently in Tasks 3, 5, 6. `BindingErrorScope.Errors` (Task 3) used in Tasks 5, 6. `FakePluginContext` (Task 4) used in Task 5. Plugin type names match the source (`namespace.Class`, e.g. `QueryBuilderPlugin.QueryBuilderPlugin`, `HelloPlugin.HelloFoToolPlugin`).
