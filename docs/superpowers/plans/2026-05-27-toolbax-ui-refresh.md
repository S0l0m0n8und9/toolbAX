# toolBax UI Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify visual language between the WPF host shell and plugin views, surface the active profile globally, modernize ProfilesView, decouple icons from the view-model, introduce a standardized PluginToolbar — without breaking the plugin contract.

**Architecture:** Sharp 2px design tokens shared across host + plugins. Title-bar `ProfileChip` for active profile + inline switching. Rich status bar with live connection state. Updater consolidated into a title-bar overflow menu. Icons resolved from a `ResourceDictionary` keyed by an optional manifest field. New `AppShellViewModel` owns cross-cutting shell state.

**Tech Stack:** WPF (.NET 9 SDK, .NET 8 target framework `net8.0-windows`), C# 12, xUnit for tests, `Microsoft.Extensions.Logging`, `System.Text.Json`. Pure WPF — no new external dependencies.

**Spec:** `docs/superpowers/specs/2026-05-27-toolbax-ui-refresh-design.md`

---

## File Map

**New files**

| Path | Responsibility |
|---|---|
| `src/FoToolbox.Host/Themes/Spacing.xaml` | `Fo.Space.*` Double + Thickness tokens |
| `src/FoToolbox.Host/Themes/Icons.xaml` | `Geometry` resources (`Icon.Profiles`, `Icon.Query`, …) |
| `src/FoToolbox.Host/Controls/PluginToolbar.cs` | Custom `ItemsControl` with internal `WrapPanel` |
| `src/FoToolbox.Host/Controls/PluginToolbar.xaml` | Default template + `Fo.Toolbar.Button` style |
| `src/FoToolbox.Host/Controls/ProfileChip.cs` | `Control` with `Profiles`/`ActiveProfile`/`ConnectionStatus` DPs |
| `src/FoToolbox.Host/Controls/ProfileChip.xaml` | Default template (dot + label + caret + popup) |
| `src/FoToolbox.Host/Controls/StatusPip.cs` | `Control` with `State` DP, animates on `Busy` |
| `src/FoToolbox.Host/Controls/StatusPip.xaml` | Default template + state-conditional styling |
| `src/FoToolbox.Host/Controls/ToolbarSpacer.cs` | Zero-content `FrameworkElement` |
| `src/FoToolbox.Host/Plugins/IconResourceResolver.cs` | Static helper: manifest → Geometry, with fallback chain |
| `src/FoToolbox.Host/ViewModels/AppShellViewModel.cs` | Cross-cutting shell state |
| `src/FoToolbox.Host/ViewModels/ConnectionStatus.cs` | Enum `Unknown`/`Ok`/`Warning`/`Error` |
| `src/FoToolbox.Host/ViewModels/ConnectionTestedEventArgs.cs` | Event payload |
| `src/FoToolbox.SDK/Plugins/IPluginBusyState.cs` | Optional busy-state opt-in |
| `tests/FoToolbox.Tests/IconResourceResolverTests.cs` | Resolver fallback chain tests |
| `tests/FoToolbox.Tests/AppShellViewModelTests.cs` | Busy aggregation + connection state tests |

**Renamed files**

| From | To |
|---|---|
| `src/FoToolbox.Host/Themes/Fluent.Light.xaml` | `src/FoToolbox.Host/Themes/Fluent.Theme.xaml` |

**Modified files**

| Path | What changes |
|---|---|
| `src/FoToolbox.Host/App.xaml` | Reference renamed theme + new dictionaries |
| `src/FoToolbox.Host/MainWindow.xaml` | New title bar, status bar, removed update bar |
| `src/FoToolbox.Host/MainWindow.xaml.cs` | Wire `AppShellViewModel`, profile-change subscription |
| `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs` | Remove `IconPathFor`; consume `IconResourceResolver`; expose `Shell` |
| `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs` | Add `TabSelection` enum prop + `IsActive(envId)` helper; publish `ConnectionTested` |
| `src/FoToolbox.Host/Views/ProfilesView.xaml` | Rewritten: list + tabbed detail + sticky `PluginToolbar` |
| `src/FoToolbox.Host/Themes/Fluent.Controls.xaml` | Add `Fo.Toolbar.Button`, `Fo.ChipButton`, `Fo.OverflowButton` styles |
| `src/FoToolbox.SDK/Plugins/FoPluginManifest.cs` | Add optional `Icon` property |
| `plugins/QueryBuilder/PluginManifest.json` | Add `"icon": "Query"` |
| `plugins/TableEntityBrowser/PluginManifest.json` | Add `"icon": "TableEntity"` |
| `plugins/DualWriteMapBrowser/PluginManifest.json` | Add `"icon": "DualWrite"` |
| `plugins/ODataPostBuilder/PluginManifest.json` | Add `"icon": "ODataPost"` |
| `plugins/QueryBuilder/QueryBuilderView.xaml` | Adopt `PluginToolbar`; CornerRadius via token |
| `plugins/TableEntityBrowser/TableEntityBrowserView.xaml` | Adopt `PluginToolbar`; CornerRadius via token |
| `plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml` | Adopt `PluginToolbar`; CornerRadius via token |
| `plugins/ODataPostBuilder/ODataPostBuilderView.xaml` | Adopt `PluginToolbar`; CornerRadius via token |

---

## Common verification commands

These appear repeatedly in steps below:

```powershell
# Build (run from repo root)
dotnet build .\FoToolbox.sln -c Debug

# Run tests (run from repo root)
dotnet test .\FoToolbox.sln -c Debug --filter FullyQualifiedName~<TestClass>
```

Smoke launch: `dotnet run --project src\FoToolbox.Host -c Debug`

---

## Task 1: Rename Fluent.Light.xaml → Fluent.Theme.xaml

**Files:**
- Rename: `src/FoToolbox.Host/Themes/Fluent.Light.xaml` → `src/FoToolbox.Host/Themes/Fluent.Theme.xaml`
- Modify: `src/FoToolbox.Host/App.xaml`

- [ ] **Step 1: Rename the theme file**

```powershell
git mv src\FoToolbox.Host\Themes\Fluent.Light.xaml src\FoToolbox.Host\Themes\Fluent.Theme.xaml
```

- [ ] **Step 2: Update App.xaml to reference the new path**

Edit `src/FoToolbox.Host/App.xaml` — change line 10:

From:
```xml
<ResourceDictionary Source="Themes/Fluent.Light.xaml" />
```

To:
```xml
<ResourceDictionary Source="Themes/Fluent.Theme.xaml" />
```

- [ ] **Step 3: Update the in-file comment**

Edit `src/FoToolbox.Host/App.xaml` — change the comment above the merged dictionary from `<!-- Fluent-inspired baseline theme (dark). -->` to `<!-- Warm-dark terminal theme. -->`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS. (Plugins reference brushes by `DynamicResource` keys, not by file path, so no plugin breaks.)

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\Themes\Fluent.Theme.xaml src\FoToolbox.Host\App.xaml
git commit -m "ui: rename Fluent.Light.xaml to Fluent.Theme.xaml (was misnamed)"
```

---

## Task 2: Add Themes/Spacing.xaml

**Files:**
- Create: `src/FoToolbox.Host/Themes/Spacing.xaml`
- Modify: `src/FoToolbox.Host/App.xaml`

- [ ] **Step 1: Create Spacing.xaml**

Write `src/FoToolbox.Host/Themes/Spacing.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <!-- Spacing scale (Double) -->
    <sys:Double x:Key="Fo.Space.2">2</sys:Double>
    <sys:Double x:Key="Fo.Space.4">4</sys:Double>
    <sys:Double x:Key="Fo.Space.6">6</sys:Double>
    <sys:Double x:Key="Fo.Space.8">8</sys:Double>
    <sys:Double x:Key="Fo.Space.12">12</sys:Double>
    <sys:Double x:Key="Fo.Space.16">16</sys:Double>
    <sys:Double x:Key="Fo.Space.24">24</sys:Double>

    <!-- Common thickness presets -->
    <Thickness x:Key="Fo.Margin.Card">8</Thickness>
    <Thickness x:Key="Fo.Padding.Card">12</Thickness>
    <Thickness x:Key="Fo.Margin.FormRow">0,10,0,0</Thickness>
    <Thickness x:Key="Fo.Margin.ToolbarItem">0,0,6,0</Thickness>
</ResourceDictionary>
```

- [ ] **Step 2: Merge Spacing.xaml into App.xaml resources**

Edit `src/FoToolbox.Host/App.xaml`. Add the new dictionary between `Fluent.Theme.xaml` and `Fluent.Controls.xaml`:

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Themes/Fluent.Theme.xaml" />
    <ResourceDictionary Source="Themes/Spacing.xaml" />
    <ResourceDictionary Source="Themes/Fluent.Controls.xaml" />
</ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\Themes\Spacing.xaml src\FoToolbox.Host\App.xaml
git commit -m "ui: add Spacing.xaml design tokens (Fo.Space.*)"
```

---

## Task 3: Add Themes/Icons.xaml and extract icon path data

**Files:**
- Create: `src/FoToolbox.Host/Themes/Icons.xaml`
- Modify: `src/FoToolbox.Host/App.xaml`

- [ ] **Step 1: Create Icons.xaml with current icon geometries**

Write `src/FoToolbox.Host/Themes/Icons.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!--
      Icon geometries (24x24 viewBox). Approximate Lucide equivalents.
      Used by host nav rail/tab bar via IconResourceResolver, keyed "Icon.{Name}".
    -->
    <Geometry x:Key="Icon.Profiles">M12 20s8-2.7 8-7V5l-8-3-8 3v8c0 4.3 8 7 8 7z</Geometry>
    <Geometry x:Key="Icon.Query">M12 2C7.6 2 4 3.3 4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5c0-1.7-3.6-3-8-3zM4 12c0 1.7 3.6 3 8 3s8-1.3 8-3</Geometry>
    <Geometry x:Key="Icon.DualWrite">M3 6v14l6-3 6 3 6-3V3l-6 3-6-3-6 3zM9 3v14M15 6v14</Geometry>
    <Geometry x:Key="Icon.TableEntity">M4 4v16a2 2 0 0 1 2-2h14V2H6a2 2 0 0 0-2 2zM6 18h14</Geometry>
    <Geometry x:Key="Icon.ODataPost">M4 7l5 5-5 5M12 17h8</Geometry>
    <Geometry x:Key="Icon.Plugin">M9 2v6M15 2v6M7 8h10v4a5 5 0 0 1-10 0zM12 17v5</Geometry>
    <Geometry x:Key="Icon.Settings">M12 1l2.5 4.5L19 6l-1.5 5L21 14l-3.5 3.5L18 22l-4.5-2.5L9 22l.5-4.5L5 14l3.5-3.5L8 6l4.5 0.5z</Geometry>
</ResourceDictionary>
```

- [ ] **Step 2: Merge Icons.xaml into App.xaml resources**

Edit `src/FoToolbox.Host/App.xaml`. Add after `Spacing.xaml`:

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Themes/Fluent.Theme.xaml" />
    <ResourceDictionary Source="Themes/Spacing.xaml" />
    <ResourceDictionary Source="Themes/Icons.xaml" />
    <ResourceDictionary Source="Themes/Fluent.Controls.xaml" />
</ResourceDictionary.MergedDictionaries>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\Themes\Icons.xaml src\FoToolbox.Host\App.xaml
git commit -m "ui: extract icon path data into Icons.xaml resource dictionary"
```

---

## Task 4: Add Icon field to FoPluginManifest (SDK)

**Files:**
- Modify: `src/FoToolbox.SDK/Plugins/FoPluginManifest.cs`

- [ ] **Step 1: Add the optional Icon property**

Edit `src/FoToolbox.SDK/Plugins/FoPluginManifest.cs`. After the `Capabilities` property block (line 35), add:

```csharp
    /// <summary>
    /// Optional icon resource key. Resolved via the host's Icons.xaml resource dictionary
    /// (key format: "Icon.{value}"). When absent, the host falls back to a name-based
    /// heuristic. Existing plugins do not need to set this field.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS. No plugin manifest needs updating (the field is nullable).

- [ ] **Step 3: Commit**

```powershell
git add src\FoToolbox.SDK\Plugins\FoPluginManifest.cs
git commit -m "sdk: add optional Icon field to FoPluginManifest"
```

---

## Task 5: Add IconResourceResolver (TDD)

**Files:**
- Create: `src/FoToolbox.Host/Plugins/IconResourceResolver.cs`
- Create: `tests/FoToolbox.Tests/IconResourceResolverTests.cs`

The resolver maps a manifest (or a free-form name) to a `Geometry`. Resolution order:
1. `manifest.Icon != null` → `"Icon." + manifest.Icon` lookup.
2. Heuristic on `manifest.Name` (preserves today's `IconPathFor` behaviour).
3. Default `"Icon.Plugin"`.

To keep the resolver testable without spinning up a WPF `Application`, we accept a `Func<string, Geometry?>` lookup delegate at the call site. In production it's `Application.Current.TryFindResource(key) as Geometry`. In tests we inject a dictionary.

- [ ] **Step 1: Write the failing tests**

Create `tests/FoToolbox.Tests/IconResourceResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Media;
using FoToolbox.Host.Plugins;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.Tests;

public class IconResourceResolverTests
{
    private static FoPluginManifest Manifest(string name, string? icon = null) => new()
    {
        Id = "test." + name.ToLowerInvariant(),
        Name = name,
        Version = "0.0.1",
        MinSdk = "0.2.0",
        Icon = icon,
    };

    private static Geometry FromPath(string path) => Geometry.Parse(path);

    [Fact]
    public void Resolve_ExplicitIcon_FindsResourceByKey()
    {
        var profiles = FromPath("M0 0 L 1 1");
        Geometry? Lookup(string key) => key == "Icon.Profiles" ? profiles : null;

        var result = IconResourceResolver.Resolve(Manifest("Anything", icon: "Profiles"), Lookup);

        Assert.Same(profiles, result);
    }

    [Fact]
    public void Resolve_NameHeuristic_FallsBackWhenIconKeyAbsent()
    {
        var query = FromPath("M2 2 L 3 3");
        Geometry? Lookup(string key) => key == "Icon.Query" ? query : null;

        var result = IconResourceResolver.Resolve(Manifest("Query Builder"), Lookup);

        Assert.Same(query, result);
    }

    [Fact]
    public void Resolve_UnknownExplicitIcon_FallsThroughToNameHeuristicThenDefault()
    {
        var plugin = FromPath("M9 9 L 1 1");
        Geometry? Lookup(string key) => key == "Icon.Plugin" ? plugin : null;

        var result = IconResourceResolver.Resolve(Manifest("WeirdName", icon: "NotAKnownIcon"), Lookup);

        Assert.Same(plugin, result);
    }

    [Fact]
    public void Resolve_AllLookupsMiss_ReturnsNull()
    {
        Geometry? Lookup(string key) => null;
        var result = IconResourceResolver.Resolve(Manifest("Whatever"), Lookup);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Profiles", "Icon.Profiles")]
    [InlineData("Query Builder", "Icon.Query")]
    [InlineData("DualWrite Map Browser", "Icon.DualWrite")]
    [InlineData("Table Entity Browser", "Icon.TableEntity")]
    [InlineData("Some Metadata Tool", "Icon.TableEntity")]
    [InlineData("OData POST Builder", "Icon.ODataPost")]
    public void Resolve_NameHeuristic_PicksExpectedKey(string name, string expectedKey)
    {
        var marker = FromPath("M5 5 L 5 6");
        Geometry? Lookup(string key) => key == expectedKey ? marker : null;

        var result = IconResourceResolver.Resolve(Manifest(name), Lookup);

        Assert.Same(marker, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\FoToolbox.sln -c Debug --filter FullyQualifiedName~IconResourceResolverTests`
Expected: FAIL — `IconResourceResolver` does not exist.

- [ ] **Step 3: Implement IconResourceResolver**

Create `src/FoToolbox.Host/Plugins/IconResourceResolver.cs`:

```csharp
using System;
using System.Windows.Media;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.Host.Plugins;

internal static class IconResourceResolver
{
    private const string DefaultKey = "Icon.Plugin";

    public static Geometry? Resolve(FoPluginManifest manifest, Func<string, Geometry?> lookup)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));

        if (!string.IsNullOrWhiteSpace(manifest.Icon))
        {
            var explicitGeom = lookup("Icon." + manifest.Icon);
            if (explicitGeom is not null) return explicitGeom;
        }

        var heuristicKey = HeuristicKeyFor(manifest.Name);
        var heuristicGeom = lookup(heuristicKey);
        if (heuristicGeom is not null) return heuristicGeom;

        return lookup(DefaultKey);
    }

    public static Geometry? Resolve(string name, Func<string, Geometry?> lookup)
    {
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));
        return lookup(HeuristicKeyFor(name)) ?? lookup(DefaultKey);
    }

    private static string HeuristicKeyFor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return DefaultKey;

        if (Contains(name, "Profile")) return "Icon.Profiles";
        if (Contains(name, "Query")) return "Icon.Query";
        if (Contains(name, "Dual")) return "Icon.DualWrite";
        if (Contains(name, "POST")) return "Icon.ODataPost";
        if (Contains(name, "Table") || Contains(name, "Entity") || Contains(name, "Metadata"))
            return "Icon.TableEntity";

        return DefaultKey;
    }

    private static bool Contains(string s, string fragment) =>
        s.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\FoToolbox.sln -c Debug --filter FullyQualifiedName~IconResourceResolverTests`
Expected: PASS — all 9 tests green (4 named + 5 from the Theory).

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\Plugins\IconResourceResolver.cs tests\FoToolbox.Tests\IconResourceResolverTests.cs
git commit -m "feat: add IconResourceResolver with manifest/heuristic/default fallback chain"
```

---

## Task 6: Switch MainWindowViewModel to use IconResourceResolver

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`
- Modify: `src/FoToolbox.Host/MainWindow.xaml`

The view-model currently exposes `IconPath` (string) on `PluginEntry`. We replace it with `IconGeometry` (Geometry) and remove `IconPathFor`. Bindings in `MainWindow.xaml` switch from `Path Data="{Binding IconPath}"` to `Path Data="{Binding IconGeometry}"`.

- [ ] **Step 1: Update PluginEntry to expose IconGeometry**

Edit `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`. Replace the entire `PluginEntry` class (lines 17-25) with:

```csharp
internal sealed class PluginEntry
{
    public required string Name { get; init; }
    public required UserControl Control { get; init; }
    public LoadedPlugin? Loaded { get; init; }

    /// <summary>Resolved icon geometry for the left rail / tab bar.</summary>
    public Geometry? IconGeometry { get; init; }
}
```

Add `using System.Windows.Media;` to the file's using directives.

- [ ] **Step 2: Replace IconPathFor with resolver calls**

In the same file, in `MainWindowViewModel.LoadPlugins`:

- Delete the entire `IconPathFor` static method (lines 144-151).
- Replace `IconPath = IconPathFor("Profiles"),` with:

```csharp
IconGeometry = IconResourceResolver.Resolve("Profiles", LookupGeometry),
```

- Replace `IconPath = IconPathFor(plugin.Manifest.Name),` with:

```csharp
IconGeometry = IconResourceResolver.Resolve(plugin.Manifest, LookupGeometry),
```

Add a private static helper at the bottom of `MainWindowViewModel`:

```csharp
private static Geometry? LookupGeometry(string key)
{
    if (Application.Current is null) return null;
    return Application.Current.TryFindResource(key) as Geometry;
}
```

Add `using System.Windows;` and `using System.Windows.Media;` and `using FoToolbox.Host.Plugins;` to the file if not already present.

- [ ] **Step 3: Update MainWindow.xaml bindings**

Edit `src/FoToolbox.Host/MainWindow.xaml`. Find the two `Path` elements that bind `Data="{Binding IconPath}"` (one in the left rail at ~line 86, one in the tab bar at ~line 137) and change both to:

```xml
<Path Data="{Binding IconGeometry}"
      Fill="Transparent"
      StrokeThickness="1.5"
      StrokeLineJoin="Round"
      StrokeStartLineCap="Round"
      StrokeEndLineCap="Round">
```

(Keep the rest of the styling unchanged.)

- [ ] **Step 4: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 5: Smoke launch**

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Icons render in the left rail and tab bar exactly as before. Close the window.

- [ ] **Step 6: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs src\FoToolbox.Host\MainWindow.xaml
git commit -m "refactor: route plugin icons through IconResourceResolver (no behavior change)"
```

---

## Task 7: Add ConnectionStatus + ConnectionTestedEventArgs

**Files:**
- Create: `src/FoToolbox.Host/ViewModels/ConnectionStatus.cs`
- Create: `src/FoToolbox.Host/ViewModels/ConnectionTestedEventArgs.cs`

- [ ] **Step 1: Create ConnectionStatus.cs**

```csharp
namespace FoToolbox.Host.ViewModels;

internal enum ConnectionStatus
{
    Unknown,
    Ok,
    Warning,
    Error,
}
```

- [ ] **Step 2: Create ConnectionTestedEventArgs.cs**

```csharp
using System;

namespace FoToolbox.Host.ViewModels;

internal sealed class ConnectionTestedEventArgs : EventArgs
{
    public required string EnvironmentId { get; init; }
    public required ConnectionScope Scope { get; init; }
    public required bool Success { get; init; }
    public required DateTimeOffset TestedAt { get; init; }
    public string? Detail { get; init; }
}

internal enum ConnectionScope
{
    FinanceAndOperations,
    Dataverse,
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\ConnectionStatus.cs src\FoToolbox.Host\ViewModels\ConnectionTestedEventArgs.cs
git commit -m "feat: introduce ConnectionStatus enum and ConnectionTested event payload"
```

---

## Task 8: Add IPluginBusyState to SDK

**Files:**
- Create: `src/FoToolbox.SDK/Plugins/IPluginBusyState.cs`

- [ ] **Step 1: Create the interface**

```csharp
using System.ComponentModel;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional opt-in for plugins that want their busy state surfaced in the host status bar.
/// Plugins that do not implement this interface are treated as idle by the shell.
/// </summary>
public interface IPluginBusyState : INotifyPropertyChanged
{
    bool IsBusy { get; }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 3: Commit**

```powershell
git add src\FoToolbox.SDK\Plugins\IPluginBusyState.cs
git commit -m "sdk: add IPluginBusyState opt-in interface"
```

---

## Task 9: AppShellViewModel (TDD)

**Files:**
- Create: `src/FoToolbox.Host/ViewModels/AppShellViewModel.cs`
- Create: `tests/FoToolbox.Tests/AppShellViewModelTests.cs`

`AppShellViewModel` does not hold business logic — it observes plugins + connection tests and aggregates state for the title bar / status bar. Profile activation is delegated to the existing `ProfileStore` via a callback; we keep the shell thin.

- [ ] **Step 1: Write the failing tests**

Create `tests/FoToolbox.Tests/AppShellViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using FoToolbox.Host.ViewModels;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.Tests;

public class AppShellViewModelTests
{
    private sealed class StubBusy : IPluginBusyState
    {
        private bool _busy;
        public bool IsBusy
        {
            get => _busy;
            set
            {
                if (_busy == value) return;
                _busy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void IsBusy_DefaultsToFalse()
    {
        var shell = new AppShellViewModel();
        Assert.False(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_TrueWhenAnyRegisteredPluginBusy()
    {
        var shell = new AppShellViewModel();
        var a = new StubBusy();
        var b = new StubBusy();
        shell.RegisterPluginBusy(a);
        shell.RegisterPluginBusy(b);

        a.IsBusy = true;

        Assert.True(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_FalseWhenAllPluginsIdle()
    {
        var shell = new AppShellViewModel();
        var a = new StubBusy { IsBusy = true };
        shell.RegisterPluginBusy(a);
        Assert.True(shell.IsBusy);

        a.IsBusy = false;

        Assert.False(shell.IsBusy);
    }

    [Fact]
    public void IsBusy_RaisesPropertyChangedOnTransition()
    {
        var shell = new AppShellViewModel();
        var stub = new StubBusy();
        shell.RegisterPluginBusy(stub);

        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        stub.IsBusy = true;
        stub.IsBusy = true; // no-op
        stub.IsBusy = false;

        Assert.Equal(new[] { nameof(AppShellViewModel.IsBusy), nameof(AppShellViewModel.IsBusy) }, raised);
    }

    [Fact]
    public void OnConnectionTested_SuccessSetsOkAndTimestamp()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        var when = DateTimeOffset.UtcNow;
        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = when,
        });

        Assert.Equal(ConnectionStatus.Ok, shell.ConnectionStatus);
        Assert.Equal(when, shell.LastPingAt);
    }

    [Fact]
    public void OnConnectionTested_FailureSetsError()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = false,
            TestedAt = DateTimeOffset.UtcNow,
            Detail = "401",
        });

        Assert.Equal(ConnectionStatus.Error, shell.ConnectionStatus);
    }

    [Fact]
    public void OnConnectionTested_IgnoresEventsForInactiveProfile()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile(envId: "PROD", name: "PROD-NZ");

        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "DEV",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(ConnectionStatus.Unknown, shell.ConnectionStatus);
        Assert.Null(shell.LastPingAt);
    }

    [Fact]
    public void SetActiveProfile_NullClearsConnectionState()
    {
        var shell = new AppShellViewModel();
        shell.SetActiveProfile("PROD", "PROD-NZ");
        shell.OnConnectionTested(new ConnectionTestedEventArgs
        {
            EnvironmentId = "PROD",
            Scope = ConnectionScope.FinanceAndOperations,
            Success = true,
            TestedAt = DateTimeOffset.UtcNow,
        });

        shell.SetActiveProfile(null, null);

        Assert.Null(shell.ActiveProfileName);
        Assert.Equal(ConnectionStatus.Unknown, shell.ConnectionStatus);
        Assert.Null(shell.LastPingAt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\FoToolbox.sln -c Debug --filter FullyQualifiedName~AppShellViewModelTests`
Expected: FAIL — `AppShellViewModel` does not exist.

- [ ] **Step 3: Implement AppShellViewModel**

Create `src/FoToolbox.Host/ViewModels/AppShellViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.Host.ViewModels;

/// <summary>
/// Cross-cutting shell state surfaced by the title bar and status bar:
/// active profile, aggregate busy, connection status, and last successful ping time.
/// </summary>
internal sealed class AppShellViewModel : INotifyPropertyChanged
{
    private readonly List<IPluginBusyState> _busyPlugins = new();
    private bool _isBusy;
    private string? _activeProfileEnvId;
    private string? _activeProfileName;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;
    private DateTimeOffset? _lastPingAt;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveProfileEnvId
    {
        get => _activeProfileEnvId;
        private set
        {
            if (_activeProfileEnvId == value) return;
            _activeProfileEnvId = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveProfileName
    {
        get => _activeProfileName;
        private set
        {
            if (_activeProfileName == value) return;
            _activeProfileName = value;
            OnPropertyChanged();
        }
    }

    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        private set
        {
            if (_connectionStatus == value) return;
            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? LastPingAt
    {
        get => _lastPingAt;
        private set
        {
            if (_lastPingAt == value) return;
            _lastPingAt = value;
            OnPropertyChanged();
        }
    }

    public event EventHandler? NavigateToProfilesRequested;

    public void RaiseNavigateToProfiles() =>
        NavigateToProfilesRequested?.Invoke(this, EventArgs.Empty);

    public void RegisterPluginBusy(IPluginBusyState busy)
    {
        if (busy is null) return;
        if (_busyPlugins.Contains(busy)) return;
        _busyPlugins.Add(busy);
        busy.PropertyChanged += OnPluginBusyChanged;
        RecomputeIsBusy();
    }

    public void UnregisterPluginBusy(IPluginBusyState busy)
    {
        if (busy is null) return;
        if (!_busyPlugins.Remove(busy)) return;
        busy.PropertyChanged -= OnPluginBusyChanged;
        RecomputeIsBusy();
    }

    public void SetActiveProfile(string? envId, string? name)
    {
        if (string.Equals(_activeProfileEnvId, envId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_activeProfileName, name, StringComparison.Ordinal))
        {
            return;
        }
        ActiveProfileEnvId = envId;
        ActiveProfileName = name;
        ConnectionStatus = ConnectionStatus.Unknown;
        LastPingAt = null;
    }

    public void OnConnectionTested(ConnectionTestedEventArgs e)
    {
        if (e is null) return;
        if (!string.Equals(_activeProfileEnvId, e.EnvironmentId, StringComparison.OrdinalIgnoreCase))
        {
            // Test result for a profile that isn't the active one - ignore.
            return;
        }

        ConnectionStatus = e.Success ? ConnectionStatus.Ok : ConnectionStatus.Error;
        LastPingAt = e.Success ? e.TestedAt : LastPingAt;
    }

    private void OnPluginBusyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPluginBusyState.IsBusy))
        {
            RecomputeIsBusy();
        }
    }

    private void RecomputeIsBusy() =>
        IsBusy = _busyPlugins.Any(p => p.IsBusy);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\FoToolbox.sln -c Debug --filter FullyQualifiedName~AppShellViewModelTests`
Expected: PASS — all 8 tests green.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\AppShellViewModel.cs tests\FoToolbox.Tests\AppShellViewModelTests.cs
git commit -m "feat: AppShellViewModel owns cross-cutting shell state (busy/profile/connection)"
```

---

## Task 10: Expose Shell on MainWindowViewModel and register busy plugins

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add Shell to MainWindowViewModel**

Edit `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`. Inside `MainWindowViewModel`, add a public field after `Plugins`:

```csharp
public AppShellViewModel Shell { get; } = new();
```

In `LoadPlugins`, after the existing loop that adds plugins to `Plugins`, register busy-state subscriptions:

```csharp
foreach (var entry in Plugins)
{
    if (entry.Loaded?.Instance is IPluginBusyState busy)
    {
        Shell.RegisterPluginBusy(busy);
    }
}
```

Add `using FoToolbox.SDK.Plugins;` if not present.

- [ ] **Step 2: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS. No plugin currently implements `IPluginBusyState`, so the loop is a no-op (correct fallback).

- [ ] **Step 3: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs
git commit -m "feat: expose AppShellViewModel on MainWindowViewModel and register plugin busy states"
```

---

## Task 11: Add Fo.Toolbar.Button style

**Files:**
- Modify: `src/FoToolbox.Host/Themes/Fluent.Controls.xaml`

- [ ] **Step 1: Append the toolbar button style**

Edit `src/FoToolbox.Host/Themes/Fluent.Controls.xaml`. Insert this style just before the closing `</ResourceDictionary>` tag:

```xml
<!-- Compact, borderless toolbar button (used inside PluginToolbar). -->
<Style x:Key="Fo.Toolbar.Button" TargetType="{x:Type Button}">
    <Setter Property="Foreground" Value="{DynamicResource Fo.TextBrush}" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="10,4" />
    <Setter Property="MinHeight" Value="28" />
    <Setter Property="Margin" Value="0,0,6,0" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type Button}">
                <Border x:Name="Bd"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="{DynamicResource Fo.CornerRadius.Control}"
                        SnapsToDevicePixels="True">
                    <ContentPresenter Margin="{TemplateBinding Padding}"
                                      HorizontalAlignment="Center"
                                      VerticalAlignment="Center"
                                      RecognizesAccessKey="True"
                                      TextElement.Foreground="{TemplateBinding Foreground}" />
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fo.ControlHoverBrush}" />
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fo.ControlPressedBrush}" />
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                        <Setter TargetName="Bd" Property="BorderBrush" Value="{DynamicResource Fo.AccentBrush}" />
                        <Setter TargetName="Bd" Property="BorderThickness" Value="1" />
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.55" />
                        <Setter Property="Cursor" Value="Arrow" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 3: Commit**

```powershell
git add src\FoToolbox.Host\Themes\Fluent.Controls.xaml
git commit -m "ui: add Fo.Toolbar.Button compact borderless style"
```

---

## Task 12: PluginToolbar + ToolbarSpacer controls

**Files:**
- Create: `src/FoToolbox.Host/Controls/ToolbarSpacer.cs`
- Create: `src/FoToolbox.Host/Controls/PluginToolbar.cs`
- Create: `src/FoToolbox.Host/Themes/Generic.xaml` (if not present) **OR** extend `Fluent.Controls.xaml` with the default template

We will use `Fluent.Controls.xaml` (already merged into App) to host the default template instead of introducing a `Themes/Generic.xaml`. This avoids fiddling with `ThemeInfo` attribute on the assembly.

- [ ] **Step 1: Create ToolbarSpacer**

```csharp
using System.Windows;

namespace FoToolbox.Host.Controls;

/// <summary>
/// Flexible spacer for use inside <see cref="PluginToolbar"/>. Children placed after a spacer
/// dock toward the right edge when used inside a DockPanel; in WrapPanel mode the spacer
/// has no effect (acceptable; right alignment is approximate in wrap mode).
/// </summary>
public sealed class ToolbarSpacer : FrameworkElement
{
    public ToolbarSpacer()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Width = double.NaN;
        MinWidth = 8;
    }
}
```

- [ ] **Step 2: Create PluginToolbar**

```csharp
using System.Windows;
using System.Windows.Controls;

namespace FoToolbox.Host.Controls;

/// <summary>
/// Standardized plugin toolbar: a 36px-tall items container with consistent styling.
/// Plugins host this as the top region of their UserControl.
/// </summary>
public sealed class PluginToolbar : ItemsControl
{
    static PluginToolbar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PluginToolbar),
            new FrameworkPropertyMetadata(typeof(PluginToolbar)));
    }
}
```

- [ ] **Step 3: Add default template to Fluent.Controls.xaml**

Add at the top of `Fluent.Controls.xaml` (or just before the closing tag — order does not matter for `x:Key`-less default styles). Add this `xmlns` to the `ResourceDictionary` root if not present:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls"
```

Then add the style:

```xml
<Style TargetType="{x:Type c:PluginToolbar}">
    <Setter Property="Background" Value="{DynamicResource Fo.Ink1Brush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Fo.HairBrush}" />
    <Setter Property="BorderThickness" Value="0,0,0,1" />
    <Setter Property="MinHeight" Value="36" />
    <Setter Property="Padding" Value="8,4" />
    <Setter Property="ItemsPanel">
        <Setter.Value>
            <ItemsPanelTemplate>
                <WrapPanel Orientation="Horizontal" VerticalAlignment="Center" />
            </ItemsPanelTemplate>
        </Setter.Value>
    </Setter>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type c:PluginToolbar}">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        Padding="{TemplateBinding Padding}"
                        SnapsToDevicePixels="True">
                    <ItemsPresenter />
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Setter Property="ItemContainerStyle">
        <Setter.Value>
            <Style TargetType="{x:Type ContentPresenter}">
                <Setter Property="Margin" Value="0" />
            </Style>
        </Setter.Value>
    </Setter>
    <!-- Implicit button style for direct Button children inside this toolbar. -->
    <Setter Property="Resources">
        <Setter.Value>
            <ResourceDictionary>
                <Style TargetType="{x:Type Button}" BasedOn="{StaticResource Fo.Toolbar.Button}" />
                <Style TargetType="{x:Type Separator}">
                    <Setter Property="Background" Value="{DynamicResource Fo.HairBrush}" />
                    <Setter Property="Width" Value="1" />
                    <Setter Property="Height" Value="20" />
                    <Setter Property="Margin" Value="6,0,6,0" />
                </Style>
            </ResourceDictionary>
        </Setter.Value>
    </Setter>
</Style>
```

Note: WPF's `Fo.Toolbar.Button` style was added with `x:Key`. The `BasedOn={StaticResource Fo.Toolbar.Button}` reference above turns it into the implicit `Button` style scoped only inside `PluginToolbar`. Outside the toolbar, `Button` keeps the default look from Task 0 (the existing 32px chrome button).

- [ ] **Step 4: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\Controls\PluginToolbar.cs src\FoToolbox.Host\Controls\ToolbarSpacer.cs src\FoToolbox.Host\Themes\Fluent.Controls.xaml
git commit -m "feat: add PluginToolbar custom control with default style"
```

---

## Task 13: StatusPip control

**Files:**
- Create: `src/FoToolbox.Host/Controls/StatusPip.cs`
- Modify: `src/FoToolbox.Host/Themes/Fluent.Controls.xaml`

- [ ] **Step 1: Create the StatusPip class**

```csharp
using System.Windows;
using System.Windows.Controls;

namespace FoToolbox.Host.Controls;

public enum PipState
{
    Idle,
    Busy,
    Ok,
    Warning,
    Error,
}

public sealed class StatusPip : Control
{
    static StatusPip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusPip),
            new FrameworkPropertyMetadata(typeof(StatusPip)));
    }

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PipState), typeof(StatusPip),
        new PropertyMetadata(PipState.Idle));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatusPip),
        new PropertyMetadata(string.Empty));

    public PipState State
    {
        get => (PipState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
```

- [ ] **Step 2: Add default template to Fluent.Controls.xaml**

Append to `Fluent.Controls.xaml`:

```xml
<Style TargetType="{x:Type c:StatusPip}">
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Foreground" Value="{DynamicResource Fo.SubtleTextBrush}" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type c:StatusPip}">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Ellipse x:Name="Dot" Width="6" Height="6"
                             Fill="{DynamicResource Fo.SubtleTextBrush}"
                             VerticalAlignment="Center" />
                    <TextBlock x:Name="Lbl"
                               Margin="6,0,0,0"
                               VerticalAlignment="Center"
                               FontSize="{DynamicResource Fo.FontSize.Caption}"
                               Foreground="{TemplateBinding Foreground}"
                               Text="{TemplateBinding Label}" />
                </StackPanel>
                <ControlTemplate.Triggers>
                    <Trigger Property="State" Value="Busy">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.AccentBrush}" />
                        <Trigger.EnterActions>
                            <BeginStoryboard x:Name="PulseStoryboard">
                                <Storyboard RepeatBehavior="Forever" AutoReverse="True">
                                    <DoubleAnimation Storyboard.TargetName="Dot"
                                                     Storyboard.TargetProperty="Opacity"
                                                     From="0.4" To="1.0" Duration="0:0:1" />
                                </Storyboard>
                            </BeginStoryboard>
                        </Trigger.EnterActions>
                        <Trigger.ExitActions>
                            <StopStoryboard BeginStoryboardName="PulseStoryboard" />
                        </Trigger.ExitActions>
                    </Trigger>
                    <Trigger Property="State" Value="Ok">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.SuccessBrush}" />
                    </Trigger>
                    <Trigger Property="State" Value="Warning">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.WarningBrush}" />
                    </Trigger>
                    <Trigger Property="State" Value="Error">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.ErrorBrush}" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\Controls\StatusPip.cs src\FoToolbox.Host\Themes\Fluent.Controls.xaml
git commit -m "feat: add StatusPip control with Idle/Busy/Ok/Warning/Error states"
```

---

## Task 14: ProfileChip control

**Files:**
- Create: `src/FoToolbox.Host/Controls/ProfileChip.cs`
- Modify: `src/FoToolbox.Host/Themes/Fluent.Controls.xaml`

- [ ] **Step 1: Create the ProfileChip class**

```csharp
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FoToolbox.Host.ViewModels;

namespace FoToolbox.Host.Controls;

public sealed class ProfileChip : Control
{
    static ProfileChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ProfileChip),
            new FrameworkPropertyMetadata(typeof(ProfileChip)));
    }

    public static readonly DependencyProperty ProfilesProperty = DependencyProperty.Register(
        nameof(Profiles), typeof(IEnumerable), typeof(ProfileChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActiveProfileNameProperty = DependencyProperty.Register(
        nameof(ActiveProfileName), typeof(string), typeof(ProfileChip),
        new PropertyMetadata("No profile"));

    public static readonly DependencyProperty ConnectionStatusProperty = DependencyProperty.Register(
        nameof(ConnectionStatus), typeof(ConnectionStatus), typeof(ProfileChip),
        new PropertyMetadata(ConnectionStatus.Unknown));

    public static readonly DependencyProperty SetActiveProfileCommandProperty = DependencyProperty.Register(
        nameof(SetActiveProfileCommand), typeof(ICommand), typeof(ProfileChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty OpenProfilesCommandProperty = DependencyProperty.Register(
        nameof(OpenProfilesCommand), typeof(ICommand), typeof(ProfileChip),
        new PropertyMetadata(null));

    public IEnumerable? Profiles
    {
        get => (IEnumerable?)GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public string ActiveProfileName
    {
        get => (string)GetValue(ActiveProfileNameProperty);
        set => SetValue(ActiveProfileNameProperty, value);
    }

    public ConnectionStatus ConnectionStatus
    {
        get => (ConnectionStatus)GetValue(ConnectionStatusProperty);
        set => SetValue(ConnectionStatusProperty, value);
    }

    public ICommand? SetActiveProfileCommand
    {
        get => (ICommand?)GetValue(SetActiveProfileCommandProperty);
        set => SetValue(SetActiveProfileCommandProperty, value);
    }

    public ICommand? OpenProfilesCommand
    {
        get => (ICommand?)GetValue(OpenProfilesCommandProperty);
        set => SetValue(OpenProfilesCommandProperty, value);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (OpenProfilesCommand?.CanExecute(null) == true)
        {
            OpenProfilesCommand.Execute(null);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 2: Add default template to Fluent.Controls.xaml**

Append to `Fluent.Controls.xaml`:

```xml
<Style TargetType="{x:Type c:ProfileChip}">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderBrush" Value="{DynamicResource Fo.HairBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="8,3" />
    <Setter Property="MinHeight" Value="22" />
    <Setter Property="MaxWidth" Value="220" />
    <Setter Property="Foreground" Value="{DynamicResource Fo.TextBrush}" />
    <Setter Property="FontFamily" Value="{DynamicResource Fo.FontFamily.Mono}" />
    <Setter Property="FontSize" Value="11" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="{x:Type c:ProfileChip}">
                <Grid>
                    <Border x:Name="Bd"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="{DynamicResource Fo.CornerRadius.Control}"
                            Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Ellipse x:Name="Dot" Width="6" Height="6"
                                     Fill="{DynamicResource Fo.SubtleTextBrush}"
                                     VerticalAlignment="Center" />
                            <TextBlock x:Name="Lbl"
                                       Margin="6,0,6,0"
                                       VerticalAlignment="Center"
                                       Text="{TemplateBinding ActiveProfileName}"
                                       TextTrimming="CharacterEllipsis"
                                       Foreground="{TemplateBinding Foreground}" />
                            <Path Width="8" Height="5"
                                  Data="M 0 0 L 8 0 L 4 5 Z"
                                  Fill="{TemplateBinding Foreground}"
                                  VerticalAlignment="Center" />
                        </StackPanel>
                    </Border>
                    <Popup x:Name="DropPopup"
                           Placement="Bottom"
                           StaysOpen="False"
                           AllowsTransparency="True"
                           IsOpen="{Binding IsChecked, ElementName=Toggle, Mode=TwoWay}">
                        <Border Background="{DynamicResource Fo.SurfaceBrush}"
                                BorderBrush="{DynamicResource Fo.HairBrush}"
                                BorderThickness="1"
                                MinWidth="200"
                                MaxHeight="320">
                            <ScrollViewer VerticalScrollBarVisibility="Auto">
                                <ItemsControl ItemsSource="{TemplateBinding Profiles}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Button Style="{StaticResource Fo.Toolbar.Button}"
                                                    HorizontalContentAlignment="Left"
                                                    HorizontalAlignment="Stretch"
                                                    Content="{Binding Environment.Name}"
                                                    Command="{Binding DataContext.SetActiveProfileCommand,
                                                              RelativeSource={RelativeSource AncestorType={x:Type c:ProfileChip}}}"
                                                    CommandParameter="{Binding}" />
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </Border>
                    </Popup>
                    <ToggleButton x:Name="Toggle"
                                  Background="Transparent"
                                  BorderThickness="0"
                                  Opacity="0">
                        <ToggleButton.Template>
                            <ControlTemplate TargetType="{x:Type ToggleButton}">
                                <Border Background="Transparent" />
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property="ConnectionStatus" Value="Ok">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.SuccessBrush}" />
                    </Trigger>
                    <Trigger Property="ConnectionStatus" Value="Warning">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.WarningBrush}" />
                    </Trigger>
                    <Trigger Property="ConnectionStatus" Value="Error">
                        <Setter TargetName="Dot" Property="Fill" Value="{DynamicResource Fo.ErrorBrush}" />
                    </Trigger>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Bd" Property="Background" Value="{DynamicResource Fo.ControlHoverBrush}" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\Controls\ProfileChip.cs src\FoToolbox.Host\Themes\Fluent.Controls.xaml
git commit -m "feat: add ProfileChip title-bar control with status dot + popup"
```

---

## Task 15: Wire ProfileChip into the title bar

**Files:**
- Modify: `src/FoToolbox.Host/MainWindow.xaml`
- Modify: `src/FoToolbox.Host/MainWindow.xaml.cs`
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs`

We expose `Profiles` and a `SetActiveProfileCommand` on `ProfilesViewModel` (it already has the active-profile mutation logic in `SetActiveCommand`; we add a parameterized command alongside it) and pass them into the chip via the existing `ProfilesView.DataContext`. The chip lives in `MainWindow.xaml`, so we bind it to `_profilesView.DataContext`.

- [ ] **Step 1: Add SetActiveProfileByItemCommand to ProfilesViewModel**

In `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs`, locate the property declaration block (around the other `ICommand` properties). Add:

```csharp
public ICommand SetActiveProfileByItemCommand { get; }
```

In the constructor, after the existing command instantiations, add:

```csharp
SetActiveProfileByItemCommand = new RelayCommand<ProfileItem?>(async item =>
{
    if (item is null) return;
    Selected = item;
    await SetActiveAsyncCore();
});
```

If `RelayCommand<T>` does not already exist in this codebase, add a simple version at the bottom of the file:

```csharp
internal sealed class RelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    public RelayCommand(Func<T?, Task> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute((T?)parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
```

(Replace `SetActiveAsyncCore()` with the actual private method backing the existing parameterless `SetActiveCommand`. If that logic is inline in a lambda, refactor it into a `private async Task SetActiveAsyncCore()` method first, then call it from both commands.)

- [ ] **Step 2: Add Profiles property pass-through if needed**

`ProfilesViewModel.Profiles` already exists as an `ObservableCollection<ProfileItem>`. No change.

- [ ] **Step 3: Wire ActiveProfile updates to AppShellViewModel**

In `MainWindow.xaml.cs`, locate `ApplyProfileAsync` (around line 92). After the `result = await _bootstrapper.ApplyProfileAsync(...)` line, push the active profile into the shell:

```csharp
_vm.Shell.SetActiveProfile(
    bundle.FoEnvironment.Id,
    bundle.FoEnvironment.Name);
```

Add `using FoToolbox.Host.ViewModels;` if absent.

- [ ] **Step 4: Render the chip in MainWindow.xaml title bar**

Edit `src/FoToolbox.Host/MainWindow.xaml`. Add the namespace to the Window root element:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls"
```

In the title bar's `DockPanel`, between the brand StackPanel (currently `DockPanel.Dock="Left"`) and the plugin count `TextBlock` (currently `DockPanel.Dock="Right"`), add (the chip docks Right so it sits between brand and count):

```xml
<TextBlock DockPanel.Dock="Right"
           Text="{Binding PluginCountDisplay}"
           VerticalAlignment="Center"
           Margin="12,0,0,0"
           FontFamily="{DynamicResource Fo.FontFamily.Mono}"
           FontSize="11"
           Foreground="{DynamicResource Fo.DimTextBrush}" />

<c:ProfileChip DockPanel.Dock="Right"
               VerticalAlignment="Center"
               Profiles="{Binding ProfilesViewModelHost.Profiles}"
               ActiveProfileName="{Binding Shell.ActiveProfileName, TargetNullValue='No profile'}"
               ConnectionStatus="{Binding Shell.ConnectionStatus}"
               SetActiveProfileCommand="{Binding ProfilesViewModelHost.SetActiveProfileByItemCommand}" />
```

`ProfilesViewModelHost` is a new pass-through property we add next. (Delete the OLD plugin count TextBlock node — replace it with the one above which we placed adjacent to the chip.)

- [ ] **Step 5: Add ProfilesViewModelHost to MainWindowViewModel**

In `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`, add a settable property:

```csharp
private ProfilesViewModel? _profilesViewModelHost;
public ProfilesViewModel? ProfilesViewModelHost
{
    get => _profilesViewModelHost;
    set
    {
        if (_profilesViewModelHost == value) return;
        _profilesViewModelHost = value;
        OnPropertyChanged();
    }
}
```

In `MainWindow.xaml.cs`, inside `LoadPluginsAsync`, after constructing `_profilesView`, push the VM into the host:

```csharp
_vm.ProfilesViewModelHost = (ProfilesViewModel)_profilesView.DataContext;
```

- [ ] **Step 6: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Title bar shows the chip with active profile name (or "No profile" if none). Right-click to navigate to Profiles tab (this will be wired in a later task — for now confirm no crash). Close the window.

- [ ] **Step 7: Commit**

```powershell
git add src\FoToolbox.Host\MainWindow.xaml src\FoToolbox.Host\MainWindow.xaml.cs src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs src\FoToolbox.Host\ViewModels\ProfilesViewModel.cs
git commit -m "ui: wire ProfileChip into MainWindow title bar"
```

---

## Task 16: Wire NavigateToProfilesRequested handler

**Files:**
- Modify: `src/FoToolbox.Host/Controls/ProfileChip.cs`
- Modify: `src/FoToolbox.Host/MainWindow.xaml.cs`

Wire the chip's right-click → shell's `RaiseNavigateToProfiles()` → MainWindow code-behind subscribes and calls `EnsureProfilesTabVisible()`.

- [ ] **Step 1: Bind ProfileChip.OpenProfilesCommand**

In `src/FoToolbox.Host/MainWindow.xaml`, augment the `<c:ProfileChip>` element with:

```xml
OpenProfilesCommand="{Binding NavigateToProfilesCommand}"
```

- [ ] **Step 2: Expose NavigateToProfilesCommand on MainWindowViewModel**

In `src/FoToolbox.Host/ViewModels/MainWindowViewModel.cs`, add:

```csharp
public ICommand NavigateToProfilesCommand { get; }
```

In the constructor:

```csharp
NavigateToProfilesCommand = new AsyncCommand(() =>
{
    Shell.RaiseNavigateToProfiles();
    return Task.CompletedTask;
});
```

- [ ] **Step 3: Subscribe in MainWindow.xaml.cs**

In `MainWindow` constructor, after `_vm = new MainWindowViewModel();`, add:

```csharp
_vm.Shell.NavigateToProfilesRequested += (_, __) => Dispatcher.Invoke(EnsureProfilesTabVisible);
```

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Right-clicking the chip navigates to the Profiles tab.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\MainWindow.xaml src\FoToolbox.Host\MainWindow.xaml.cs src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs
git commit -m "ui: ProfileChip right-click navigates to Profiles tab"
```

---

## Task 17: Refresh status bar

**Files:**
- Modify: `src/FoToolbox.Host/MainWindow.xaml`

- [ ] **Step 1: Replace the status bar markup**

Edit `src/FoToolbox.Host/MainWindow.xaml`. Replace the entire `<!-- 03 · Status bar -->` `<Border Grid.Row="3" ...>` block (currently lines 222-269) with:

```xml
<Border Grid.Row="3"
        Background="{DynamicResource Fo.Ink0Brush}"
        BorderBrush="{DynamicResource Fo.HairBrush}"
        BorderThickness="0,1,0,0">
    <DockPanel>

        <!-- Active plugin -->
        <Border DockPanel.Dock="Left"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="0,0,1,0"
                Padding="10,0">
            <TextBlock Text="{Binding Selected.Name, FallbackValue=''}"
                       VerticalAlignment="Center"
                       FontFamily="{DynamicResource Fo.FontFamily.Mono}"
                       FontSize="10.5"
                       Foreground="{DynamicResource Fo.SubtleTextBrush}" />
        </Border>

        <!-- Active profile echo -->
        <Border DockPanel.Dock="Left"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="0,0,1,0"
                Padding="10,0">
            <c:StatusPip State="{Binding ShellPipState}"
                         Label="{Binding Shell.ActiveProfileName, TargetNullValue='No profile'}" />
        </Border>

        <!-- Busy indicator -->
        <Border DockPanel.Dock="Left"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="0,0,1,0"
                Padding="10,0"
                Visibility="{Binding Shell.IsBusy, Converter={StaticResource BoolToVis}}">
            <c:StatusPip State="Busy" Label="working..." />
        </Border>

        <!-- Update channel (right) -->
        <Border DockPanel.Dock="Right"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="1,0,0,0"
                Padding="10,0"
                Visibility="{Binding ShowUpdaterUi, Converter={StaticResource BoolToVis}}">
            <TextBlock Text="{Binding UpdateChannel}"
                       VerticalAlignment="Center"
                       FontFamily="{DynamicResource Fo.FontFamily.Mono}"
                       FontSize="10.5"
                       Foreground="{DynamicResource Fo.DimTextBrush}" />
        </Border>

        <!-- Update staged star -->
        <Border DockPanel.Dock="Right"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="1,0,0,0"
                Padding="10,0"
                Visibility="{Binding HasStagedUpdate, Converter={StaticResource BoolToVis}}">
            <TextBlock VerticalAlignment="Center"
                       FontFamily="{DynamicResource Fo.FontFamily.Mono}"
                       FontSize="10.5"
                       Foreground="{DynamicResource Fo.AccentBrush}">
                <Run Text="&#x2605; update ready" />
            </TextBlock>
        </Border>

        <!-- Last connection ping (right) -->
        <Border DockPanel.Dock="Right"
                BorderBrush="{DynamicResource Fo.HairBrush}"
                BorderThickness="1,0,0,0"
                Padding="10,0"
                Visibility="{Binding Shell.HasLastPing, Converter={StaticResource BoolToVis}}">
            <TextBlock Text="{Binding Shell.LastPingDisplay}"
                       VerticalAlignment="Center"
                       FontFamily="{DynamicResource Fo.FontFamily.Mono}"
                       FontSize="10.5"
                       Foreground="{DynamicResource Fo.SubtleTextBrush}" />
        </Border>

        <Grid />
    </DockPanel>
</Border>
```

- [ ] **Step 2: Add ShellPipState, HasLastPing, LastPingDisplay**

Add to `MainWindowViewModel.cs`:

```csharp
public PipState ShellPipState => Shell.ConnectionStatus switch
{
    ConnectionStatus.Ok => PipState.Ok,
    ConnectionStatus.Warning => PipState.Warning,
    ConnectionStatus.Error => PipState.Error,
    _ => PipState.Idle,
};
```

Add `using FoToolbox.Host.Controls;`. Then subscribe in the constructor:

```csharp
Shell.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(AppShellViewModel.ConnectionStatus))
    {
        OnPropertyChanged(nameof(ShellPipState));
    }
};
```

Add to `AppShellViewModel.cs`:

```csharp
public bool HasLastPing => _lastPingAt.HasValue;

public string LastPingDisplay
{
    get
    {
        if (!_lastPingAt.HasValue) return string.Empty;
        var delta = DateTimeOffset.UtcNow - _lastPingAt.Value;
        if (delta < TimeSpan.FromSeconds(45)) return "conn just now";
        if (delta < TimeSpan.FromMinutes(1)) return "conn 1m ago";
        if (delta < TimeSpan.FromHours(1)) return $"conn {(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromDays(1)) return $"conn {(int)delta.TotalHours}h ago";
        return $"conn {(int)delta.TotalDays}d ago";
    }
}
```

Notify both whenever `LastPingAt` changes — in the `LastPingAt` setter, add:

```csharp
OnPropertyChanged(nameof(HasLastPing));
OnPropertyChanged(nameof(LastPingDisplay));
```

Add a `DispatcherTimer` ticking every 30 seconds in `AppShellViewModel` to refresh `LastPingDisplay`:

```csharp
private readonly System.Windows.Threading.DispatcherTimer _pingTimer;

public AppShellViewModel()
{
    _pingTimer = new System.Windows.Threading.DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(30),
    };
    _pingTimer.Tick += (_, __) => OnPropertyChanged(nameof(LastPingDisplay));
    _pingTimer.Start();
}
```

- [ ] **Step 3: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Status bar shows plugin name | profile name with pip | (no busy or ping yet). Resize window — segments remain readable. Close.

- [ ] **Step 4: Commit**

```powershell
git add src\FoToolbox.Host\MainWindow.xaml src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs src\FoToolbox.Host\ViewModels\AppShellViewModel.cs
git commit -m "ui: refresh status bar with profile pip, busy indicator, and last-ping display"
```

---

## Task 18: Publish ConnectionTested from ProfilesViewModel and route to Shell

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs`
- Modify: `src/FoToolbox.Host/MainWindow.xaml.cs`

- [ ] **Step 1: Add ConnectionTested event**

In `ProfilesViewModel.cs` near the existing `event PropertyChangedEventHandler? PropertyChanged;`, add:

```csharp
public event EventHandler<ConnectionTestedEventArgs>? ConnectionTested;
```

- [ ] **Step 2: Raise the event from Test FO/CE commands**

Locate the implementations of `TestFoConnectionCommand` and `TestCeConnectionCommand` (search for `TestFoConnection`). At the end of the success path of each (where today the VM sets a `Status` string), raise:

```csharp
ConnectionTested?.Invoke(this, new ConnectionTestedEventArgs
{
    EnvironmentId = Selected!.Environment.Id,
    Scope = ConnectionScope.FinanceAndOperations, // or .Dataverse for the CE test
    Success = true,
    TestedAt = DateTimeOffset.UtcNow,
});
```

In each method's catch block (the failure path), raise the same event with `Success = false` and `Detail = ex.Message`. Wrap in try/finally so the event always fires.

- [ ] **Step 3: Subscribe in MainWindow.xaml.cs**

In `LoadPluginsAsync`, right after `_profilesView ??= new ProfilesView(...);`, add:

```csharp
if (_profilesView.DataContext is ProfilesViewModel profilesVm)
{
    profilesVm.ConnectionTested += (_, args) => Dispatcher.Invoke(() => _vm.Shell.OnConnectionTested(args));
}
```

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Open Profiles tab → click `Test FO connection` on an existing profile → on success, status bar pip turns green, `conn just now` appears at the right edge.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\ProfilesViewModel.cs src\FoToolbox.Host\MainWindow.xaml.cs
git commit -m "feat: ProfilesViewModel publishes ConnectionTested; shell receives it"
```

---

## Task 19: Title-bar overflow menu (updater + about) and delete the dedicated update bar

**Files:**
- Modify: `src/FoToolbox.Host/MainWindow.xaml`

- [ ] **Step 1: Replace the update bar with an overflow button + menu**

Edit `src/FoToolbox.Host/MainWindow.xaml`.

a. Delete the entire `<!-- 02 · Update bar -->` block (currently `Grid.Row="2"`).

b. Change the row definitions: remove the `RowDefinition Height="Auto"` that was for the update bar. The remaining grid is title (32), content (*), status (22). Update the row indexes of the content (`Grid.Row="1"`) and status (`Grid.Row="2"`) to match.

c. Add the overflow button to the title bar. In the title bar's `DockPanel`, before the chip, add a `Button` docked Right with a `ContextMenu`:

```xml
<Button DockPanel.Dock="Right"
        Content="&#x22EF;"
        FontFamily="{DynamicResource Fo.FontFamily.Mono}"
        FontSize="14"
        Width="28"
        Height="22"
        Margin="6,0,0,0"
        VerticalAlignment="Center"
        Style="{StaticResource Fo.Toolbar.Button}">
    <Button.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Check updates"
                      Command="{Binding CheckUpdatesCommand}"
                      Visibility="{Binding ShowUpdaterUi, Converter={StaticResource BoolToVis}}"
                      IsEnabled="{Binding CanCheckUpdates}" />
            <MenuItem Header="Apply update"
                      Command="{Binding ApplyUpdateCommand}"
                      Visibility="{Binding ShowUpdaterUi, Converter={StaticResource BoolToVis}}"
                      IsEnabled="{Binding HasStagedUpdate}" />
            <MenuItem Header="Rollback to previous"
                      Command="{Binding RollbackUpdateCommand}"
                      Visibility="{Binding ShowUpdaterUi, Converter={StaticResource BoolToVis}}"
                      IsEnabled="{Binding HasRollbackUpdate}" />
            <Separator Visibility="{Binding ShowUpdaterUi, Converter={StaticResource BoolToVis}}" />
            <MenuItem Header="About toolBax" Command="{Binding ShowAboutCommand}" />
        </ContextMenu>
    </Button.ContextMenu>
    <Button.Triggers>
        <EventTrigger RoutedEvent="Button.Click">
            <BeginStoryboard>
                <Storyboard>
                    <!-- click opens the ContextMenu via code-behind below -->
                </Storyboard>
            </BeginStoryboard>
        </EventTrigger>
    </Button.Triggers>
</Button>
```

Replace the empty Storyboard trigger with a `Click` handler:

```xml
<Button ... Click="OnOverflowButtonClick">
```

(Remove the `Button.Triggers` block; we route via code-behind for clarity.)

- [ ] **Step 2: Implement OnOverflowButtonClick in MainWindow.xaml.cs**

Add:

```csharp
private void OnOverflowButtonClick(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement fe && fe.ContextMenu is not null)
    {
        fe.ContextMenu.PlacementTarget = fe;
        fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        fe.ContextMenu.IsOpen = true;
    }
}
```

- [ ] **Step 3: Add ShowAboutCommand**

In `MainWindowViewModel.cs`:

```csharp
public ICommand ShowAboutCommand { get; }
```

In the constructor:

```csharp
ShowAboutCommand = new AsyncCommand(() =>
{
    var version = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "(unknown)";
    System.Windows.MessageBox.Show(
        $"toolBax\nversion {version}\nchannel {UpdateChannel}",
        "About toolBax",
        System.Windows.MessageBoxButton.OK,
        System.Windows.MessageBoxImage.Information);
    return Task.CompletedTask;
});
```

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Title bar has `⋯` button. Click → menu appears. With updater env-vars unset, only "About toolBax" is visible. Dedicated update bar is gone. Status bar should no longer be pushed up by the update bar.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\MainWindow.xaml src\FoToolbox.Host\MainWindow.xaml.cs src\FoToolbox.Host\ViewModels\MainWindowViewModel.cs
git commit -m "ui: replace dedicated update bar with title-bar overflow menu"
```

---

## Task 20: Add TabSelection + IsActive to ProfilesViewModel

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs`

- [ ] **Step 1: Add ProfilesTab enum**

At the bottom of `ProfilesViewModel.cs` (outside the class) — or in a new file `src/FoToolbox.Host/ViewModels/ProfilesTab.cs`:

```csharp
namespace FoToolbox.Host.ViewModels;

internal enum ProfilesTab
{
    FoEnvironment,
    CeEnvironment,
    Auth,
}
```

- [ ] **Step 2: Add SelectedTab property to ProfilesViewModel**

```csharp
private ProfilesTab _selectedTab = ProfilesTab.FoEnvironment;
public ProfilesTab SelectedTab
{
    get => _selectedTab;
    set
    {
        if (_selectedTab == value) return;
        _selectedTab = value;
        OnPropertyChanged();
    }
}
```

- [ ] **Step 3: Add IsActive helper**

```csharp
public bool IsActive(ProfileItem? item) =>
    item is not null &&
    !string.IsNullOrWhiteSpace(_activeEnvId) &&
    string.Equals(_activeEnvId, item.Environment.Id, StringComparison.OrdinalIgnoreCase);
```

`_activeEnvId` already exists at line 39 of the file. We expose `IsActive` so the new XAML can data-trigger off it (via a value converter or `MultiBinding`).

- [ ] **Step 4: Add ActiveEnvironmentId public read-only property**

```csharp
public string? ActiveEnvironmentId => _activeEnvId;
```

Raise `OnPropertyChanged(nameof(ActiveEnvironmentId))` at every site that mutates `_activeEnvId` (lines 193, 259, 263, 341, 389 per the prior survey). For each, after the assignment, add `OnPropertyChanged(nameof(ActiveEnvironmentId));`.

- [ ] **Step 5: Build**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

- [ ] **Step 6: Commit**

```powershell
git add src\FoToolbox.Host\ViewModels\ProfilesViewModel.cs
git commit -m "feat: ProfilesViewModel exposes SelectedTab + IsActive(item) + ActiveEnvironmentId"
```

---

## Task 21: Rewrite ProfilesView.xaml

**Files:**
- Modify: `src/FoToolbox.Host/Views/ProfilesView.xaml`

This is the largest single XAML change in the plan. The code-behind (`ProfilesView.xaml.cs`) stays — it owns the password-box handlers, which can't bind directly.

- [ ] **Step 1: Back up the current view for reference**

```powershell
copy src\FoToolbox.Host\Views\ProfilesView.xaml src\FoToolbox.Host\Views\ProfilesView.xaml.bak
```

(Remove the `.bak` at the end of the task before committing.)

- [ ] **Step 2: Replace the contents**

Overwrite `src/FoToolbox.Host/Views/ProfilesView.xaml` with:

```xml
<UserControl x:Class="FoToolbox.Host.Views.ProfilesView"
             x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:c="clr-namespace:FoToolbox.Host.Controls"
             xmlns:vm="clr-namespace:FoToolbox.Host.ViewModels"
             xmlns:models="clr-namespace:FoToolbox.Core.Models;assembly=FoToolbox.Core"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             d:DesignHeight="780"
             d:DesignWidth="1200">
    <UserControl.Resources>
        <Style x:Key="VisibleWhenFoClientSecret" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.FoPrincipal.AuthMode}" Value="{x:Static models:AuthMode.ClientSecret}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenFoBearerToken" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.FoPrincipal.AuthMode}" Value="{x:Static models:AuthMode.BearerToken}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenFoCertificate" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.FoPrincipal.AuthMode}" Value="{x:Static models:AuthMode.Certificate}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenCeClientSecret" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.DataversePrincipal.AuthMode}" Value="{x:Static models:AuthMode.ClientSecret}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenCeBearerToken" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.DataversePrincipal.AuthMode}" Value="{x:Static models:AuthMode.BearerToken}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenCeCertificate" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.DataversePrincipal.AuthMode}" Value="{x:Static models:AuthMode.Certificate}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <Style x:Key="CardBorder" TargetType="{x:Type Border}">
            <Setter Property="Background" Value="{DynamicResource Fo.SurfaceBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource Fo.BorderBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="{DynamicResource Fo.CornerRadius.Card}" />
            <Setter Property="Padding" Value="{DynamicResource Fo.Padding.Card}" />
            <Setter Property="Margin" Value="0,0,0,8" />
        </Style>
    </UserControl.Resources>

    <Grid Margin="{DynamicResource Fo.Margin.Card}">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <!-- Left: profile list -->
        <Border Grid.Column="0" Style="{StaticResource CardBorder}" Margin="0,0,8,0">
            <DockPanel>
                <DockPanel DockPanel.Dock="Top" Margin="0,0,0,8">
                    <TextBlock Text="Profiles"
                               FontWeight="SemiBold"
                               FontSize="{DynamicResource Fo.FontSize.SubHeading}"
                               VerticalAlignment="Center"
                               DockPanel.Dock="Left" />
                    <Button DockPanel.Dock="Right"
                            Content="+"
                            Command="{Binding AddProfileCommand}"
                            Width="28" MinHeight="24" Padding="0"
                            Style="{StaticResource Fo.Toolbar.Button}" />
                </DockPanel>

                <ListBox DockPanel.Dock="Top"
                         Margin="0,0,0,8"
                         ItemsSource="{Binding Profiles}"
                         SelectedItem="{Binding Selected, Mode=TwoWay}"
                         MinHeight="600"
                         ScrollViewer.HorizontalScrollBarVisibility="Auto"
                         ScrollViewer.VerticalScrollBarVisibility="Auto">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal">
                                <Ellipse Width="6" Height="6"
                                         VerticalAlignment="Center"
                                         Margin="0,0,8,0">
                                    <Ellipse.Style>
                                        <Style TargetType="Ellipse">
                                            <Setter Property="Fill" Value="Transparent" />
                                            <Setter Property="Stroke" Value="{DynamicResource Fo.SubtleTextBrush}" />
                                            <Setter Property="StrokeThickness" Value="1" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding Environment.Id, Converter={x:Null}}" Value="">
                                                    <!-- placeholder -->
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </Ellipse.Style>
                                </Ellipse>
                                <TextBlock Text="{Binding Environment.Name}"
                                           VerticalAlignment="Center" />
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <Button DockPanel.Dock="Bottom"
                        Content="Delete"
                        Command="{Binding DeleteProfileCommand}"
                        HorizontalAlignment="Stretch" />
            </DockPanel>
        </Border>

        <!-- Right: tabbed detail with sticky bottom toolbar -->
        <Grid Grid.Column="1">
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>

            <TabControl Grid.Row="0"
                        BorderThickness="0"
                        Background="Transparent"
                        SelectedIndex="{Binding SelectedTab, Converter={x:Null}}">
                <!-- TabControl SelectedIndex/Item could be wired to enum via a converter;
                     for v1 we let WPF default-select index 0 and don't enforce binding. -->

                <TabItem Header="FO Environment">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <StackPanel Margin="{DynamicResource Fo.Margin.Card}">
                            <Border Style="{StaticResource CardBorder}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="170" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto" />
                                        <RowDefinition Height="Auto" />
                                        <RowDefinition Height="Auto" />
                                        <RowDefinition Height="Auto" />
                                    </Grid.RowDefinitions>

                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Name" VerticalAlignment="Center" />
                                    <TextBox Grid.Row="0" Grid.Column="1"
                                             Text="{Binding Selected.Environment.Name, UpdateSourceTrigger=PropertyChanged}" />

                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Base URL" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                    <TextBox Grid.Row="1" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                             Text="{Binding Selected.Environment.BaseUrl, UpdateSourceTrigger=PropertyChanged}" />

                                    <TextBlock Grid.Row="2" Grid.Column="0" Text="Tenant ID" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                    <TextBox Grid.Row="2" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                             Text="{Binding Selected.Environment.TenantId, UpdateSourceTrigger=PropertyChanged}" />

                                    <TextBlock Grid.Row="3" Grid.Column="0" Text="Default company" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                    <TextBox Grid.Row="3" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                             Text="{Binding Selected.Environment.DefaultCompany, UpdateSourceTrigger=PropertyChanged}" />
                                </Grid>
                            </Border>
                        </StackPanel>
                    </ScrollViewer>
                </TabItem>

                <TabItem Header="CE / Dataverse">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <StackPanel Margin="{DynamicResource Fo.Margin.Card}">
                            <Border Style="{StaticResource CardBorder}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="170" />
                                        <ColumnDefinition Width="*" />
                                    </Grid.ColumnDefinitions>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto" />
                                        <RowDefinition Height="Auto" />
                                    </Grid.RowDefinitions>

                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="Base URL" VerticalAlignment="Center" />
                                    <TextBox Grid.Row="0" Grid.Column="1"
                                             Text="{Binding Selected.DataverseEnvironment.BaseUrl, UpdateSourceTrigger=PropertyChanged}" />

                                    <TextBlock Grid.Row="1" Grid.Column="0" Text="Tenant ID" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                    <TextBox Grid.Row="1" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                             Text="{Binding Selected.DataverseEnvironment.TenantId, UpdateSourceTrigger=PropertyChanged}" />
                                </Grid>
                            </Border>
                        </StackPanel>
                    </ScrollViewer>
                </TabItem>

                <TabItem Header="Auth">
                    <ScrollViewer VerticalScrollBarVisibility="Auto">
                        <Grid Margin="{DynamicResource Fo.Margin.Card}">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="8" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>

                            <!-- FO auth card -->
                            <Border Grid.Column="0" Style="{StaticResource CardBorder}">
                                <StackPanel>
                                    <TextBlock Text="FO Authentication"
                                               FontWeight="SemiBold"
                                               FontSize="{DynamicResource Fo.FontSize.SubHeading}"
                                               Margin="0,0,0,8" />

                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="140" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                        </Grid.RowDefinitions>

                                        <TextBlock Grid.Row="0" Grid.Column="0" Text="Client ID" VerticalAlignment="Center" />
                                        <TextBox Grid.Row="0" Grid.Column="1"
                                                 Text="{Binding Selected.FoPrincipal.ClientId, UpdateSourceTrigger=PropertyChanged}" />

                                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Auth mode" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                        <ComboBox x:Name="FoAuthModeComboBox" Grid.Row="1" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}" MinWidth="220"
                                                  ItemsSource="{Binding AuthModeValues}"
                                                  SelectedItem="{Binding Selected.FoPrincipal.AuthMode, Mode=TwoWay}" />

                                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Client secret" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenFoClientSecret}" />
                                        <Grid Grid.Row="2" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenFoClientSecret}">
                                            <PasswordBox x:Name="FoClientSecretBox" />
                                        </Grid>

                                        <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding FoStoredCredentialStatus}" Foreground="{DynamicResource Fo.SubtleTextBrush}" FontSize="{DynamicResource Fo.FontSize.Small}" Margin="0,5,0,0"
                                                   Style="{StaticResource VisibleWhenFoClientSecret}" />

                                        <TextBlock Grid.Row="4" Grid.Column="0" Text="Bearer token" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenFoBearerToken}" />
                                        <Grid Grid.Row="4" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenFoBearerToken}">
                                            <PasswordBox x:Name="FoBearerTokenBox" />
                                        </Grid>

                                        <TextBlock Grid.Row="5" Grid.Column="0" Text="Retrieve token" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenFoBearerToken}" />
                                        <Grid Grid.Row="5" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenFoBearerToken}">
                                            <Button Content="Get FO bearer token"
                                                    HorizontalAlignment="Left"
                                                    MinWidth="170"
                                                    Command="{Binding AcquireFoBearerTokenCommand}" />
                                        </Grid>

                                        <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding FoStoredCredentialStatus}" Foreground="{DynamicResource Fo.SubtleTextBrush}" FontSize="{DynamicResource Fo.FontSize.Small}" Margin="0,5,0,0"
                                                   Style="{StaticResource VisibleWhenFoBearerToken}" />

                                        <TextBlock Grid.Row="7" Grid.Column="0" Text="Cert thumbprint" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenFoCertificate}" />
                                        <Grid Grid.Row="7" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenFoCertificate}">
                                            <TextBox Text="{Binding Selected.FoPrincipal.CertThumbprint, UpdateSourceTrigger=PropertyChanged}" />
                                        </Grid>
                                    </Grid>
                                </StackPanel>
                            </Border>

                            <!-- CE auth card -->
                            <Border Grid.Column="2" Style="{StaticResource CardBorder}">
                                <StackPanel>
                                    <TextBlock Text="CE/Dataverse Authentication"
                                               FontWeight="SemiBold"
                                               FontSize="{DynamicResource Fo.FontSize.SubHeading}"
                                               Margin="0,0,0,8" />

                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="140" />
                                            <ColumnDefinition Width="*" />
                                        </Grid.ColumnDefinitions>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="Auto" />
                                        </Grid.RowDefinitions>

                                        <TextBlock Grid.Row="0" Grid.Column="0" Text="Client ID" VerticalAlignment="Center" />
                                        <TextBox Grid.Row="0" Grid.Column="1"
                                                 Text="{Binding Selected.DataversePrincipal.ClientId, UpdateSourceTrigger=PropertyChanged}" />

                                        <TextBlock Grid.Row="1" Grid.Column="0" Text="Auth mode" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}" />
                                        <ComboBox x:Name="CeAuthModeComboBox" Grid.Row="1" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}" MinWidth="220"
                                                  ItemsSource="{Binding AuthModeValues}"
                                                  SelectedItem="{Binding Selected.DataversePrincipal.AuthMode, Mode=TwoWay}" />

                                        <TextBlock Grid.Row="2" Grid.Column="0" Text="Client secret" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenCeClientSecret}" />
                                        <Grid Grid.Row="2" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenCeClientSecret}">
                                            <PasswordBox x:Name="CeClientSecretBox" />
                                        </Grid>

                                        <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding CeStoredCredentialStatus}" Foreground="{DynamicResource Fo.SubtleTextBrush}" FontSize="{DynamicResource Fo.FontSize.Small}" Margin="0,5,0,0"
                                                   Style="{StaticResource VisibleWhenCeClientSecret}" />

                                        <TextBlock Grid.Row="4" Grid.Column="0" Text="Bearer token" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenCeBearerToken}" />
                                        <Grid Grid.Row="4" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenCeBearerToken}">
                                            <PasswordBox x:Name="CeBearerTokenBox" />
                                        </Grid>

                                        <TextBlock Grid.Row="5" Grid.Column="0" Text="Retrieve token" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenCeBearerToken}" />
                                        <Grid Grid.Row="5" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenCeBearerToken}">
                                            <Button Content="Get CE bearer token"
                                                    HorizontalAlignment="Left"
                                                    MinWidth="170"
                                                    Command="{Binding AcquireCeBearerTokenCommand}" />
                                        </Grid>

                                        <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding CeStoredCredentialStatus}" Foreground="{DynamicResource Fo.SubtleTextBrush}" FontSize="{DynamicResource Fo.FontSize.Small}" Margin="0,5,0,0"
                                                   Style="{StaticResource VisibleWhenCeBearerToken}" />

                                        <TextBlock Grid.Row="7" Grid.Column="0" Text="Cert thumbprint" VerticalAlignment="Center" Margin="{DynamicResource Fo.Margin.FormRow}"
                                                   Style="{StaticResource VisibleWhenCeCertificate}" />
                                        <Grid Grid.Row="7" Grid.Column="1" Margin="{DynamicResource Fo.Margin.FormRow}"
                                              Style="{StaticResource VisibleWhenCeCertificate}">
                                            <TextBox Text="{Binding Selected.DataversePrincipal.CertThumbprint, UpdateSourceTrigger=PropertyChanged}" />
                                        </Grid>
                                    </Grid>
                                </StackPanel>
                            </Border>
                        </Grid>
                    </ScrollViewer>
                </TabItem>
            </TabControl>

            <!-- Sticky bottom action bar -->
            <c:PluginToolbar Grid.Row="1">
                <Button Content="Refresh" Command="{Binding RefreshCommand}" />
                <Button Content="Save" Command="{Binding SaveCommand}" />
                <Button Content="Set active" Command="{Binding SetActiveCommand}" />
                <Separator />
                <Button Content="Test FO connection" Command="{Binding TestFoConnectionCommand}" />
                <Button Content="Test CE connection" Command="{Binding TestCeConnectionCommand}" />
                <c:ToolbarSpacer />
                <TextBlock Text="{Binding Status}"
                           VerticalAlignment="Center"
                           Margin="8,0,0,0"
                           Foreground="{DynamicResource Fo.SubtleTextBrush}"
                           FontSize="{DynamicResource Fo.FontSize.Small}"
                           TextTrimming="CharacterEllipsis"
                           MaxWidth="380" />
            </c:PluginToolbar>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 3: Delete the backup**

```powershell
remove-item src\FoToolbox.Host\Views\ProfilesView.xaml.bak
```

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Open Profiles tab. Three tabs visible: FO Environment, CE / Dataverse, Auth. Auth tab shows two columns at default size. Sticky toolbar at bottom always visible. Save/Test buttons work. Edit a field, switch tabs, switch back — edit persists.

- [ ] **Step 5: Commit**

```powershell
git add src\FoToolbox.Host\Views\ProfilesView.xaml
git commit -m "ui: rewrite ProfilesView as list + tabbed detail with sticky toolbar"
```

---

## Task 22: Migrate QueryBuilder to PluginToolbar + sharp radii

**Files:**
- Modify: `plugins/QueryBuilder/QueryBuilderView.xaml`
- Modify: `plugins/QueryBuilder/PluginManifest.json`

The plugin assemblies do NOT have a project reference to `FoToolbox.Host`. Plugins instead use `DynamicResource` keys that get resolved at runtime through the merged Application resources. To use `<c:PluginToolbar>` from a plugin XAML, we expose it via a host-provided namespace alias.

The simplest path: tell each plugin XAML to import the host namespace via `xmlns:c="clr-namespace:FoToolbox.Host.Controls;assembly=FoToolbox.Host"`. WPF resolves this at runtime, and the host assembly is always loaded.

- [ ] **Step 1: Update QueryBuilderView.xaml**

Edit `plugins/QueryBuilder/QueryBuilderView.xaml`:

a. Add the host controls namespace to the `UserControl` root:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls;assembly=FoToolbox.Host"
```

b. Replace the entity-explorer toolbar (the `WrapPanel` inside the top `StackPanel` of the left `Border`) with the standardized toolbar. Find this block (around lines 36-50):

```xml
<WrapPanel DockPanel.Dock="Right">
    <Button Content="Load Entities" ... />
    <Button Content="Refresh" ... />
</WrapPanel>
```

Replace with:

```xml
<c:PluginToolbar DockPanel.Dock="Right" Background="Transparent" BorderThickness="0" Padding="0">
    <Button Content="Load Entities"
            Command="{Binding LoadEntitiesCommand}"
            IsEnabled="{Binding IsLoadingEntities, Converter={StaticResource NotBool}}" />
    <Button Content="Refresh"
            Command="{Binding RefreshEntitiesCommand}"
            IsEnabled="{Binding IsLoadingEntities, Converter={StaticResource NotBool}}" />
</c:PluginToolbar>
```

c. Change every `CornerRadius="6"` to `CornerRadius="{DynamicResource Fo.CornerRadius.Card}"` (currently used by the two outer `Border` cards in this file).

- [ ] **Step 2: Update plugins/QueryBuilder/PluginManifest.json**

```json
{
  "id": "fo.querybuilder",
  "name": "Query Builder",
  "version": "0.1.0",
  "minSdk": "0.2.0",
  "capabilities": [ "OData.Read" ],
  "icon": "Query"
}
```

- [ ] **Step 3: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: Query Builder tab opens. Toolbar uses unified styling. Card corners are sharp 2px. Functionality unchanged.

- [ ] **Step 4: Commit**

```powershell
git add plugins\QueryBuilder\QueryBuilderView.xaml plugins\QueryBuilder\PluginManifest.json
git commit -m "ui(querybuilder): adopt PluginToolbar + sharp radii"
```

---

## Task 23: Migrate TableEntityBrowser

**Files:**
- Modify: `plugins/TableEntityBrowser/TableEntityBrowserView.xaml`
- Modify: `plugins/TableEntityBrowser/PluginManifest.json`

- [ ] **Step 1: Add namespace + replace top toolbar**

Edit `plugins/TableEntityBrowser/TableEntityBrowserView.xaml`. Add to root:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls;assembly=FoToolbox.Host"
```

Replace the top `<Grid Grid.Row="0" Margin="8">` (lines 22-56) with:

```xml
<c:PluginToolbar Grid.Row="0">
    <Button Content="Load Tables" Command="{Binding LoadTablesCommand}"
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <Button Content="Load Entities" Command="{Binding LoadEntitiesCommand}"
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <Button Content="Refresh Entities" Command="{Binding RefreshEntitiesCommand}"
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <Button Content="Refresh All" Command="{Binding RefreshAllCommand}"
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <Separator />
    <Button Content="Import Tables" Command="{Binding ImportTablesCommand}"
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <Button Content="Save Import Template" Command="{Binding SaveImportTemplateCommand}"
            ToolTip="Creates a starter JSON file for the Import Tables feature."
            IsEnabled="{Binding IsBusy, Converter={StaticResource NotBool}}" />
    <c:ToolbarSpacer />
    <TextBlock Text="Working..." VerticalAlignment="Center"
               Foreground="{DynamicResource Fo.SubtleTextBrush}"
               Margin="0,0,8,0"
               Visibility="{Binding IsBusy, Converter={StaticResource BoolToVisibility}}" />
    <ProgressBar Width="140" Height="10" IsIndeterminate="True"
                 Visibility="{Binding IsBusy, Converter={StaticResource BoolToVisibility}}" />
</c:PluginToolbar>
```

- [ ] **Step 2: Replace CornerRadius**

In the same file, change every `CornerRadius="6"` to `CornerRadius="{DynamicResource Fo.CornerRadius.Card}"`.

- [ ] **Step 3: Update PluginManifest.json**

```json
{
  ...existing fields...
  "icon": "TableEntity"
}
```

(Preserve existing keys; only add `"icon"`.)

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: TableEntityBrowser tab opens. Toolbar buttons render in unified style with a separator. Functionality unchanged.

- [ ] **Step 5: Commit**

```powershell
git add plugins\TableEntityBrowser\TableEntityBrowserView.xaml plugins\TableEntityBrowser\PluginManifest.json
git commit -m "ui(tableentitybrowser): adopt PluginToolbar + sharp radii"
```

---

## Task 24: Migrate DualWriteMapBrowser

**Files:**
- Modify: `plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml`
- Modify: `plugins/DualWriteMapBrowser/PluginManifest.json`

- [ ] **Step 1: Add namespace + replace top WrapPanel**

Edit `plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml`. Add to root:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls;assembly=FoToolbox.Host"
```

Replace the top `<Border Grid.Row="0" ...>` block (currently lines 21+) with a `c:PluginToolbar` containing the same buttons, separators between logical groups, and the right-side busy indicator. Specifically, replace the children of the existing `<StackPanel>` with toolbar buttons:

```xml
<c:PluginToolbar Grid.Row="0">
    <Button Content="Load Dual-write Maps" Command="{Binding LoadMapsCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Button Content="Refresh Count Setup" Command="{Binding RefreshCountSetupCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Button Content="Validate Counts" Command="{Binding ValidateCountsCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Separator />
    <Button Content="Prepare Testify" Command="{Binding PrepareTestifyCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Button Content="Run Testify" Command="{Binding RunTestifyCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Button Content="Testify Settings" Command="{Binding OpenTestifySettingsCommand}" IsEnabled="{Binding IsNotLoading}" />
    <Separator />
    <CheckBox Content="Exact CE Count (slower)"
              IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
              VerticalAlignment="Center" />
    <Button Content="Clear" Command="{Binding ClearCommand}" IsEnabled="{Binding IsNotLoading}" />
    <c:ToolbarSpacer />
    <!-- Preserve any right-side status content the existing markup had -->
</c:PluginToolbar>
```

Reproduce the existing right-side StackPanel (busy indicator, progress, etc.) after `<c:ToolbarSpacer />`.

- [ ] **Step 2: Replace CornerRadius**

Change every `CornerRadius="6"` to `CornerRadius="{DynamicResource Fo.CornerRadius.Card}"` in this file.

- [ ] **Step 3: Update PluginManifest.json**

Add `"icon": "DualWrite"`.

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: DualWriteMapBrowser tab opens. Toolbar wraps to a second row at narrow widths. Functionality unchanged.

- [ ] **Step 5: Commit**

```powershell
git add plugins\DualWriteMapBrowser\DualWriteMapBrowserView.xaml plugins\DualWriteMapBrowser\PluginManifest.json
git commit -m "ui(dualwrite): adopt PluginToolbar + sharp radii"
```

---

## Task 25: Migrate ODataPostBuilder

**Files:**
- Modify: `plugins/ODataPostBuilder/ODataPostBuilderView.xaml`
- Modify: `plugins/ODataPostBuilder/PluginManifest.json`

- [ ] **Step 1: Add namespace + introduce PluginToolbar**

The current OData view uses a single top StackPanel with a Button + status label (~line 51-55). It is small but for consistency replace it with `c:PluginToolbar`:

```xml
xmlns:c="clr-namespace:FoToolbox.Host.Controls;assembly=FoToolbox.Host"
```

Replace the StackPanel with:

```xml
<c:PluginToolbar Grid.Row="1" Grid.ColumnSpan="5" Margin="0,12,0,0">
    <Button Content="Load Entities" Command="{Binding LoadEntitiesCommand}" />
    <c:ToolbarSpacer />
    <TextBlock VerticalAlignment="Center"
               Text="{Binding EntityLoadStatus}"
               TextWrapping="NoWrap"
               TextTrimming="CharacterEllipsis"
               MaxWidth="600"
               Foreground="{DynamicResource Fo.SubtleTextBrush}"
               FontSize="{DynamicResource Fo.FontSize.Small}" />
</c:PluginToolbar>
```

Adjust the existing `Grid.RowDefinitions` if needed to host the new toolbar above the existing content (introduce a new row at index 1 and shift the rest, or repurpose the existing `RowDefinition Height="Auto"`).

- [ ] **Step 2: No CornerRadius="6" present in this view** — skip radius migration here.

- [ ] **Step 3: Update PluginManifest.json**

Add `"icon": "ODataPost"`.

- [ ] **Step 4: Build and smoke-launch**

Run: `dotnet build .\FoToolbox.sln -c Debug`
Expected: BUILD SUCCESS.

Run: `dotnet run --project src\FoToolbox.Host -c Debug`
Expected: OData POST Builder tab opens. Toolbar at top. Functionality unchanged.

- [ ] **Step 5: Commit**

```powershell
git add plugins\ODataPostBuilder\ODataPostBuilderView.xaml plugins\ODataPostBuilder\PluginManifest.json
git commit -m "ui(odatapost): adopt PluginToolbar + manifest icon"
```

---

## Task 26: Full-app smoke test and final polish pass

**Files:**
- None (verification only)

- [ ] **Step 1: Run full build + tests**

Run: `dotnet build .\FoToolbox.sln -c Release`
Expected: BUILD SUCCESS.

Run: `dotnet test .\FoToolbox.sln -c Release`
Expected: ALL TESTS PASS.

- [ ] **Step 2: End-to-end smoke**

Run: `dotnet run --project src\FoToolbox.Host -c Release`

Walk through:
1. Title bar shows brand, profile chip (or "No profile"), ⋯ button, plugin count.
2. Left rail icons render for every plugin.
3. Click each tab — content loads.
4. Open Profiles. Add a new profile. Configure FO env + auth. Save. Set active. The chip in title bar updates.
5. Click `Test FO connection`. Status bar pip turns green; `conn just now` appears.
6. Click profile chip → popup lists profiles → switch to another → status bar profile name updates, pip resets to gray.
7. Right-click chip → navigates to Profiles tab.
8. Click ⋯ overflow → if updater env-vars set, all four items appear; otherwise only "About toolBax". Click About → message box shows version.
9. Resize window to minimum (1280x820). PluginToolbar wraps to second row in plugins with many buttons. ProfilesView Auth tab still legible.
10. Confirm: no `Fluent.Light.xaml` reference remains; no `IconPathFor` method exists; no update bar exists between content and status.

- [ ] **Step 3: Final commit (no code changes; just to mark completion)**

If any tiny polish is needed (margins/colors that look off in the actual app), apply in this step. Otherwise, skip.

```powershell
git log --oneline -20
```

Expected: All 25 task commits visible.

---

## Self-Review

**1. Spec coverage:**
- §4 architecture file layout → covered by Tasks 1-3, 7-9, 11-14, 20 (rename + new files).
- §4.2 VM split → Task 9 (AppShellViewModel), Task 10 (Shell on MainWindowViewModel), Task 20 (ProfilesViewModel additions).
- §4.3 icon system → Tasks 3, 4, 5, 6.
- §4.4 IPluginBusyState → Task 8 (interface), Task 10 (registration).
- §5 visual tokens → Tasks 1, 2, 3, plus per-plugin migrations in 22-25.
- §6.1 ProfileChip → Task 14, wired in 15-16.
- §6.2 PluginToolbar → Tasks 11-12.
- §6.3 StatusPip → Task 13.
- §6.4 status bar → Task 17.
- §6.5 title bar + overflow → Tasks 15, 16, 19.
- §6.6 update bar removed → Task 19.
- §6.7 ProfilesView rewrite → Task 21.
- §7 data flow (`ConnectionTested` plumbing) → Tasks 7, 18.
- §8 error handling (empty profile, missing icon resource) → covered by IconResourceResolver tests in Task 5 and AppShellViewModelTests in Task 9.
- §11 migration ordering — tasks numbered 1-26 follow the spec's 10-phase rollout with finer granularity.

**2. Placeholder scan:**
- Task 15 step 1 mentions "Replace `SetActiveAsyncCore()` with the actual private method backing the existing parameterless `SetActiveCommand`. If that logic is inline in a lambda, refactor it into a `private async Task SetActiveAsyncCore()` method first" — this is a small refactor instruction with concrete steps, not a placeholder.
- Task 24 step 1 says "Reproduce the existing right-side StackPanel" — this requires the engineer to read the source file (which they have). Acceptable: the existing markup is small (10 lines) and verbatim reproduction is unambiguous.
- Task 25 step 1 says "Adjust the existing `Grid.RowDefinitions` if needed" — this is conditional based on what they observe in the file. Acceptable.
- No `TBD`, `TODO`, or vague "add error handling" markers.

**3. Type consistency:**
- `AppShellViewModel.SetActiveProfile(envId, name)` — declared in Task 9 with two string? params, used identically in Tasks 15 (step 3) and the tests in Task 9.
- `IconResourceResolver.Resolve(manifest, lookup)` and `Resolve(name, lookup)` — both used in Task 6 with consistent signatures.
- `ConnectionTestedEventArgs.EnvironmentId/Scope/Success/TestedAt/Detail` — declared in Task 7, consumed in Task 18 step 2 and tests in Task 9 — identical names.
- `ProfileChip.SetActiveProfileCommand` and `OpenProfilesCommand` — declared in Task 14, bound in Task 15 step 4 and Task 16 step 1.
- `PipState` enum values `Idle/Busy/Ok/Warning/Error` — declared in Task 13, consumed in Tasks 14, 17.

No mismatches found.

---

## Out of scope

Per the spec §13, the following are deferred and not part of this plan:

- Light theme
- Ctrl+K command palette
- Notification drawer / toast host
- Real Settings screen
- Tab close button / drag-to-reorder
- Per-plugin theming
