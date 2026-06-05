# Headless UI testing

Headless UI testing is the **reason Avalonia was chosen** over WPF/WinUI for this tool. It must run
in CI with no display server. **Get this green on an empty view before building screens** — it's the
de-risking step.

> Spec, not compiled output. Pin `Avalonia.Headless.XUnit` to match your Avalonia 12.x and adjust
> APIs to the live package.

## Packages (`toolBax.App.Tests`)

```xml
<PackageReference Include="Avalonia.Headless.XUnit" Version="12.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
```

## Harness setup

A headless `Application` for tests (no platform windowing, no GPU):

```csharp
// TestApp.cs
public class TestApp : Application {
    public override void Initialize() => Styles.Add(new FluentTheme());
}

// AssemblyInfo.cs  — registers the headless Avalonia app for all [AvaloniaFact]/[AvaloniaTheory]
[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
public class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
```

`[AvaloniaFact]` runs the test body on the Avalonia UI thread with a live dispatcher, so you can
new up Views, set `DataContext`, pump layout, raise input, and assert on the visual tree — all
headless. Use `Dispatcher.UIThread.RunJobs()` to flush queued work between act/assert.

## Two layers of tests

1. **ViewModel logic** (no view) — fast, the bulk of coverage. Plain xUnit, fakes for services.
2. **View + binding smoke** (`[AvaloniaFact]`) — instantiate the View with a VM, confirm it renders
   and key controls bind/react. Catches XAML binding breaks the VM tests can't.

---

## Sample 1 — Operations eligibility + confirm gate (VM logic)

Proves the non-negotiables: eligibility is state-aware, and **no mutation happens without confirm**.

```csharp
public class DualWriteOpsEligibilityTests {
    static DualWriteOpsViewModel MakeVm(FakeGateway gw, FakeDialogs dlg) { /* seed from DW_OPS_MAPS */ }

    [Fact]
    public void Pause_excludes_already_paused_maps() {
        var vm = MakeVm(new FakeGateway(), new FakeDialogs(confirm: true));
        foreach (var m in vm.Maps) m.IsChecked = true;          // check all
        var pause = vm.Actions.Single(a => a.Id == "pause");
        // seed has 2 running-eligible + 1 paused among checked → only running maps count
        Assert.Equal(vm.Maps.Count(m => m.State == MapState.Running), vm.EligibleCount(pause));
        Assert.True(vm.CanRun(pause));
    }

    [Fact]
    public async Task RunAction_does_NOT_mutate_when_confirm_cancelled() {
        var gw = new FakeGateway();
        var vm = MakeVm(gw, new FakeDialogs(confirm: false));    // user hits Cancel
        vm.Maps.First(m => m.State == MapState.Running).IsChecked = true;
        await vm.RunActionCommand.ExecuteAsync(vm.Actions.Single(a => a.Id == "pause"));
        Assert.Equal(0, gw.SubmitCount);                        // gateway never called
    }

    [Fact]
    public async Task Confirmed_pause_settles_maps_to_Paused() {
        var gw = new FakeGateway();                             // simulates verb → result over polls
        var vm = MakeVm(gw, new FakeDialogs(confirm: true));
        var running = vm.Maps.Where(m => m.State == MapState.Running).ToList();
        foreach (var m in running) m.IsChecked = true;
        await vm.ExecuteActionAsync(vm.Actions.Single(a => a.Id == "pause"), running);
        Assert.Equal(1, gw.SubmitCount);
        Assert.All(running, m => Assert.Equal(MapState.Paused, m.State));
        Assert.False(vm.IsBusy);
        Assert.Contains(vm.Log, l => l.Text.Contains("action=5"));   // pause code
    }
}
```

`FakeGateway.SubmitActionAsync` records the call and returns a request id;
`GetStatusAsync` returns `InProgress` for the first N-1 calls (moving one map per call from the
verb state to the result state) then `Succeeded` — mirroring `data.js`'s simulation, so the polling
loop is exercised without real time/network.

---

## Sample 2 — Operations view renders + live banner is permanent (`[AvaloniaFact]`)

```csharp
public class DualWriteOpsViewTests {
    [AvaloniaFact]
    public void View_renders_and_live_banner_is_not_closable() {
        var view = new DualWriteOpsView { DataContext = MakeVm() };
        var window = new Window { Content = view, Width = 1280, Height = 800 };
        window.Show();                          // headless: no real window, but layout runs
        Dispatcher.UIThread.RunJobs();

        var banner = view.GetVisualDescendants().OfType<InfoBar>().Single();
        Assert.True(banner.IsOpen);
        Assert.False(banner.IsClosable);
        Assert.Equal(InfoBarSeverity.Warning, banner.Severity);

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.NotEqual(0, grid.GetVisualDescendants().OfType<DataGridRow>().Count());
    }

    [AvaloniaFact]
    public void Stop_button_disabled_when_no_eligible_selection() {
        var vm = MakeVm();                      // nothing checked
        var view = new DualWriteOpsView { DataContext = vm };
        new Window { Content = view }.Show();
        Dispatcher.UIThread.RunJobs();
        var stop = FindButtonByContent(view, "Stop");
        Assert.False(stop.IsEnabled);
    }
}
```

## CI

Runs on a plain Linux/Windows runner with **no display** — that's the payoff. Example:

```yaml
- uses: actions/setup-dotnet@v4
  with: { dotnet-version: '10.0.x' }
- run: dotnet test toolBax.App.Tests --configuration Release
```

No `xvfb`, no virtual desktop, no RDP-kept-alive agent (all of which WPF/WinUI UI automation would
need). If this job can't stay green, the framework choice isn't paying off — fix it before adding
screens.

## Coverage targets for the vertical slice

- **Operations:** eligibility per action; confirm-cancel = no mutation; confirm-accept settles
  states + logs the right `action=` code; busy flag toggles; select-all tri-state.
- **Profiles:** Save persists via `IProfileStore`; SetActive moves `ActiveId`; Test commands set the
  status line; DI tab ROPC↔Interactive switches required fields; bearer/interactive SignIn calls the
  broker. Fake `IInteractiveAuthBroker`/`ISecretProtector` so no real MSAL/DPAPI in tests.
