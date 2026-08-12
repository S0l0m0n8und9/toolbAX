# ViewModels & service contracts

These are the **testable seams**. ViewModels live in `toolBax.App`, depend only on interfaces from
`toolBax.Core`. Services are the only code that touches HTTP / MSAL / DPAPI / WebView2. All async
gateway/OData/auth calls go through interfaces so ViewModels run headless against fakes.

> Code below is a **spec**, not compiled output. Names/signatures are the intended shape — adjust
> to match the live `CommunityToolkit.Mvvm` + Avalonia 12 APIs you pin at scaffold time. Use
> `[ObservableProperty]` / `[RelayCommand]` source generators throughout.

## Core models (`toolBax.Core/Models`)

```csharp
public enum MapState { Idle, Stopped, Running, Paused, Errored,           // terminal
                       Starting, Stopping, Pausing, Resuming, InitialSyncing } // transitional

public enum DwDirection { Both, FoToDv, DvToFo }

public sealed record DwMap(
    string TableId, string Name, string FoEntity, string DvEntity,
    DwDirection Direction, MapState State,
    string TemplateVersion, string Author, long Rows24h, int Errors24h);

public sealed record DwAction(string Id, int Code, string Label, bool Mutating,
                              bool Danger, string Verb, IReadOnlySet<MapState> AppliesTo);
// start=1, stop=4, pause=5, resume=6, initial=8  (codes are fixed by the gateway API)

public sealed record GatewayInfo(string Identifier, string Region, string Host,
                                 string Cid, string CName, string ClientId,
                                 AuthSnapshot Auth);

public sealed record AuthSnapshot(string Mode, string Account, TimeSpan Expires);

public sealed record EnvProfile(string Id, string Name, string Url, string Legal,
                                string Tier, string Tenant, EnvStatus Status,
                                int? LatencyMs, GatewayInfo? Gateway);

public enum EnvStatus { Connected, TokenExpired, Disconnected }
```

## Service interfaces (`toolBax.Core/Services`)

```csharp
public interface IDualWriteGateway {
    Task<GatewayInfo> ResolveEnvironmentAsync(EnvProfile env, CancellationToken ct);
    Task<IReadOnlyList<DwMap>> GetMapsAsync(string cid, CancellationToken ct);
    // Returns a request id the caller polls. action.Code is sent to the gateway.
    Task<string> SubmitActionAsync(string cid, DwAction action,
                                   IReadOnlyList<string> tableIds, CancellationToken ct);
    Task<GatewayStatus> GetStatusAsync(string requestId, CancellationToken ct);
}
public sealed record GatewayStatus(string RequestId, RequestPhase Phase,
                                   IReadOnlyDictionary<string, MapState> MapStates);
public enum RequestPhase { Posting, InProgress, Succeeded, Failed }

public interface IPluginCatalog       { IReadOnlyList<PluginInfo> Plugins { get; } }
public interface IProfileStore        { /* CRUD env profiles; active profile; persisted */ }
public interface IMetadataService     { /* entity sets + cached $metadata fields */ }
public interface IODataClient         { /* GET query preview, POST/PATCH/DELETE */ }
public interface IDualWriteCompareService { Task<IReadOnlyList<DiffRow>> CompareAsync(string srcCid, string tgtCid, CancellationToken ct); }
public interface IDualWriteMapService { // read-only inspector (Map Browser, §4)
    Task<IReadOnlyList<DwMap>> GetMapsAsync(string cid, CancellationToken ct);
    Task<MapDetail> GetDetailAsync(string cid, string tableId, CancellationToken ct);   // bindings + value maps
    Task<IReadOnlyList<RunRecord>> GetRunsAsync(string cid, string tableId, CancellationToken ct);
    Task<IReadOnlyList<SyncError>> GetErrorsAsync(string cid, string tableId, CancellationToken ct);
}

// Platform-isolated (Windows impls; fakes for tests/non-Windows)
public interface ISecretProtector       { byte[] Protect(string s); string Unprotect(byte[] b); }
public interface IInteractiveAuthBroker { Task<AuthSnapshot> SignInAsync(AuthRequest r, CancellationToken ct); } // MSAL loopback / WebView2
public interface IDialogService         { Task<bool> ConfirmAsync(ConfirmRequest r); }
```

---

## §A DualWriteOpsViewModel (flagship — full contract)

```csharp
public partial class DualWriteOpsViewModel : ObservableObject {
    private readonly IDualWriteGateway _gateway;
    private readonly IDialogService _dialogs;

    public ObservableCollection<MapRowViewModel> Maps { get; } = new();
    public ObservableCollection<GatewayLogEntry> Log { get; } = new();
    public IReadOnlyList<DwAction> Actions { get; }      // the 5 actions, for the CommandBar
    public GatewayInfo Gateway { get; private set; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _activeRequestId;
    [ObservableProperty] private int _pollDone;
    [ObservableProperty] private int _pollTotal;

    public int SelectedCount => Maps.Count(m => m.IsChecked);
    public int EligibleCount(DwAction a) =>
        Maps.Count(m => m.IsChecked && a.AppliesTo.Contains(m.State));
    public bool CanRun(DwAction a) => !IsBusy && EligibleCount(a) > 0;

    // 1) opens confirm dialog (no mutation yet)
    [RelayCommand] private async Task RunAction(DwAction action) {
        var targets = Maps.Where(m => m.IsChecked && action.AppliesTo.Contains(m.State)).ToList();
        if (targets.Count == 0) return;
        var ok = await _dialogs.ConfirmAsync(ConfirmRequest.For(action, targets, Gateway.CName));
        if (!ok) return;
        await ExecuteActionAsync(action, targets);
    }

    // 2) actual gateway call + polling (separated so tests can call it directly)
    public async Task ExecuteActionAsync(DwAction action, IReadOnlyList<MapRowViewModel> targets) {
        IsBusy = true;
        foreach (var m in targets) m.State = TransitionVerb(action);     // running→pausing, etc.
        var ids = targets.Select(m => m.TableId).ToList();
        var reqId = await _gateway.SubmitActionAsync(Gateway.Cid, action, ids, CancellationToken.None);
        ActiveRequestId = reqId; PollTotal = ids.Count; PollDone = 0;
        AppendLog($"POST Start · action={action.Code} ({action.Id}) · {ids.Count} maps", $"requestId {reqId}", LogKind.Info);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(600));
        while (await timer.WaitForNextTickAsync()) {
            var s = await _gateway.GetStatusAsync(reqId, CancellationToken.None);
            ApplyStatus(s);                                   // settle each map to result state
            if (s.Phase is RequestPhase.Succeeded or RequestPhase.Failed) break;
        }
        IsBusy = false; ActiveRequestId = null;
    }

    private static MapState TransitionVerb(DwAction a) => a.Id switch {
        "start" => MapState.Starting, "stop" => MapState.Stopping, "pause" => MapState.Pausing,
        "resume" => MapState.Resuming, "initial" => MapState.InitialSyncing, _ => MapState.Running };
}

public partial class MapRowViewModel : ObservableObject {
    public string TableId { get; init; }
    public string FoEntity { get; init; }   public string DvEntity { get; init; }
    public DwDirection Direction { get; init; }
    public string TemplateVersion { get; init; }  public string Author { get; init; }
    public long Rows24h { get; init; }  public int Errors24h { get; init; }
    [ObservableProperty] private MapState _state;
    [ObservableProperty] private bool _isChecked;        // raise SelectedCount/CanRun on change
    public bool IsTransitional => State is MapState.Starting or MapState.Stopping
        or MapState.Pausing or MapState.Resuming or MapState.InitialSyncing;
}
```

**Eligibility table** (`DwAction.AppliesTo`): start={Stopped,Idle}, stop={Running,Paused},
pause={Running}, resume={Paused}, initial={Running,Stopped,Idle,Paused}. Result state after
success: start/resume/initial→Running, stop→Stopped, pause→Paused.

**Wiring note:** `IsChecked`/`State` changes must re-raise `CanExecute` for every action command
(`SelectAllCommand` too) — hook `PropertyChanged` on rows to call `NotifyCanExecuteChanged()` on the
action commands, or recompute via a shared `CanRun`.

---

## §B ProfilesViewModel + child tab VMs

```csharp
public partial class ProfilesViewModel : ObservableObject {
    public ObservableCollection<EnvProfile> Profiles { get; }
    [ObservableProperty] private EnvProfile _selected;     // master selection
    [ObservableProperty] private string? _activeId;        // the active profile
    [ObservableProperty] private string _search = "";
    public IEnumerable<EnvProfile> Filtered => /* Profiles filtered by Search */;
    [RelayCommand] void SetActive() => ActiveId = Selected.Id;
    [RelayCommand] Task TestFo()  => _odata.TestAsync(Selected, Scope.Fo);   // → status line
    [RelayCommand] Task TestCe()  => _odata.TestAsync(Selected, Scope.Ce);
    [RelayCommand] Task Save()    => _store.SaveAsync(Selected);
}
```
Detail is a `TabControl`/pivot with four tabs, each its own VM:
- **FoEnvironmentTabVm** — name, base url, tenant, scope, default company.
- **CeDataverseTabVm** — base url, tenant, web api.
- **AuthTabVm** — `Mode` (Client credentials | Bearer token); Client cred path = client id +
  secret (via `ISecretProtector`); Bearer path = interactive `SignInCommand` →
  `IInteractiveAuthBroker.SignInAsync` (MSAL loopback) + Azure-CLI token option + token cache info.
- **DataIntegratorTabVm** — `Mode` (Ropc | Interactive); permanent warn `InfoBar` (delegated-only);
  ROPC = tenant + username + password (protected) with the MFA caveat (AADSTS50076);
  Interactive = `SignInCommand` (WebView2, captures delegated + refresh token).

The sticky toolbar's status line ← a `[ObservableProperty] ConnectionStatus` (text + kind) set by
the Test/Save commands.

## §C Other screen VMs (brief)
- **PluginsHomeViewModel** — `Plugins` (from `IPluginCatalog`), `Filter`, `OpenPluginCommand(id)`.
- **QueryBuilderViewModel** — `Entities`, `SelectedEntity`, `Fields`, `SelectedFields` (set),
  computed `QueryUrl`, `ResultRows`, `RowCount`, `RunCommand`, `ExportCsvCommand`.
- **MetadataViewModel** — `Entities`, `Selected`, `Fields`, cached-or-empty state.
- **PostBuilderViewModel** — `Method`, `Path`, `RequestBody`, `ResponseBody`, `StatusText`, `SendCommand`.
- **DualWriteCompareViewModel** — `Environments`, `Source`, `Target`, `CompareCommand`
  (enabled when Source≠Target), `DiffRows`, bucket `Counts`.
- **ShellViewModel** — `Environments`, `ActiveEnvironment`, `CurrentTool` (drives the nav +
  content `ContentControl`), `IsCommandPaletteOpen`, `OpenCommandPaletteCommand`,
  `SetActiveEnvironmentCommand`, `Palette` (sub-VM with `Query`/`FilteredCommands`/`InvokeCommand`).
  No shell-level busy state or pane toggle — `IsBusy`/`IsPaneOpen`/`TogglePaneCommand` were removed
  as unreferenced (#168); each tool owns and shows its own busy state.

## Seed data
`prototype/data.js` holds realistic seed data — reuse the **shapes** (and values, for fakes):
`ENVS`, `PLUGINS`, `ENTITIES`, `FIELDS`, `SAMPLE_ROWS`, `DW_GATEWAY`, `DW_ACTIONS`, `DW_OPS_MAPS`.
A `FakeDualWriteGateway` for tests/design-mode should mirror the prototype's simulation: accept the
action, then over successive `GetStatusAsync` calls move maps from the verb state to the result
state one at a time, ending `Succeeded`.
