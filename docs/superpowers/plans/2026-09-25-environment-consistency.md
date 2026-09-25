# Environment Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make active-environment switches and same-profile connection edits transactional, and prevent cached, in-flight, or mutating work from crossing the resulting environment boundary.

**Architecture:** Add one immutable `EnvironmentIdentity` in App Services and use it at every existing environment-sensitive seam. Shell owns one awaited activation/save decision and disposes cached tool VMs only after confirmation and persistence succeed. Existing services and VMs gain local generation/disposal guards; there is no event bus, base-VM framework, service locator, or auth redesign.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3, Avalonia.Headless.XUnit, FoToolbox.Core URL normalization.

**Spec:** `docs/superpowers/specs/2026-09-25-environment-consistency-design.md`

## Global Constraints

- Work only in `C:\Users\ben.jones\.codex\worktrees\toolbax-production-hardening\toolbAX` on `codex/fix-environment-consistency`; do not touch the main checkout or other worktrees.
- Capture one immutable `EnvProfile` per operation, derive identity and request/catalog inputs from that same snapshot, and compare against current identity after awaits.
- Keep `ResourceUrlNormalizer` in FoToolbox.Core as the endpoint normalization source; do not add a Core-to-UI dependency.
- Preserve H02 Compare service sign-in sequencing, existing same-origin guards, auth-mode support, retry/parser policies, and gateway failure disposal.
- Use controlled `TaskCompletionSource`/fake HTTP handlers; no sleeps, live services, sign-ins, dependencies, or machine configuration.
- Production Shell wiring must bind Query and POST plus the already-bound Map Browser, Virtual Tables, and Operations accessors; App wiring keeps Core metadata/HTTP services bound. Explicitly unbound constructor seams may remain for isolated legacy tests. Compare keeps explicit source/target profiles and receives lifecycle cancellation only.
- A tracker item remains In progress until its PR is merged and every observed Greptile finding is adjudicated.

## Review Focus

- A profile is changed only by URL casing/trailing slash: identity stays equal; Task 1 adds normalization-equivalence tests.
- Active profile deletion while a confirmation is open: deletion is refused and the mutation continues; Tasks 3 and 5 add gated-confirmation tests.
- A stale operation throws after its VM is disposed: the discarded VM does not publish an error/status that appears current; Task 4 adds disposal-plus-late-failure tests.
- Two overlapping reloads share one busy indicator: the newest generation owns completion and older completion cannot lower/overwrite it; Task 6 adds out-of-order tests.
- Active environment becomes null during token acquisition: the client refuses dispatch exactly like an identity mismatch; Task 2 adds null-after-token tests.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `avalonia/toolBax.App/Services/EnvironmentIdentity.cs` | One immutable, normalized connection identity | Create |
| `avalonia/toolBax.App/Services/CoreProfileStore.cs` | Persist `ActiveId` before changing its cache | Modify |
| `avalonia/toolBax.App/Services/CoreMetadataService.cs` | Complete-identity cache and generation invalidation | Modify |
| `avalonia/toolBax.App/Services/CoreODataClient.cs` | Captured F&O request and post-token identity guard | Modify |
| `avalonia/toolBax.App/Services/CoreDataverseClient.cs` | Captured Dataverse request and post-token identity guard | Modify |
| `avalonia/toolBax.App/Services/CoreDualWriteMapReader.cs` | Shared identity, pinned paging scope | Modify |
| `avalonia/toolBax.App/Services/IDualWriteConnector.cs` | Session carries profile snapshot/identity | Modify |
| `avalonia/toolBax.App/Services/CoreDualWriteConnector.cs` | Populate snapshot session | Modify |
| `avalonia/toolBax.App/Services/FakeDualWriteConnector.cs` | Match real session contract | Modify |
| `avalonia/toolBax.App/ViewModels/ShellViewModel.cs` | Transactional activation/save funnel, mutation gate, invalidation | Modify |
| `avalonia/toolBax.App/ViewModels/ProfilesViewModel.cs` | Await Shell activation/save decisions; capture save target | Modify |
| `avalonia/toolBax.App/Views/MainWindow.axaml.cs` | Selection rollback support only if notification alone is insufficient | Modify only if test proves needed |
| `avalonia/toolBax.App/ViewModels/EntityCatalogLoader.cs` | Cancel/discard loads after disposal | Modify |
| `avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs` | Bound identity, request/result generation, export disposal | Modify |
| `avalonia/toolBax.App/ViewModels/PostBuilderViewModel.cs` | Approved request snapshot, mutation flag, identity/disposal guard | Modify |
| `avalonia/toolBax.App/ViewModels/MetadataViewModel.cs` | Disposed/generation-aware catalogue UI | Modify |
| `avalonia/toolBax.App/ViewModels/DualWriteCompareViewModel.cs` | Cancel/discard compare after Shell invalidation | Modify |
| `avalonia/toolBax.App/ViewModels/DualWriteOpsViewModel.cs` | Full session identity, mutation flag, late-session disposal | Modify |
| `avalonia/toolBax.App/ViewModels/DualWriteMapViewModel.cs` | Loaded identity/profile, link attribution, lifecycle | Modify |
| `avalonia/toolBax.App/ViewModels/VirtualTablesViewModel.cs` | Loaded identity/profile, link attribution, lifecycle | Modify |
| `avalonia/toolBax.App.Tests/` focused files named in Tasks 1-6 | Deterministic identity, transaction, request, and lifecycle regressions | Modify/Create only the named files |
| `docs/production-readiness/2026-09-25-hardening.md` | H01/H02/H09 evidence and D01 follow-up | Modify |

---

### Task 1: Define complete identity and fix ActiveId cache ordering

**Files:**
- Create: `avalonia/toolBax.App/Services/EnvironmentIdentity.cs`
- Modify: `avalonia/toolBax.App/Services/CoreProfileStore.cs`
- Create: `avalonia/toolBax.App.Tests/EnvironmentIdentityTests.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreProfileStoreTests.cs`

**Interfaces:**
- Produces: `EnvironmentIdentity.Create(EnvProfile profile) : EnvironmentIdentity`
- Produces: `EnvironmentIdentity.TryCreate(EnvProfile? profile) : EnvironmentIdentity?`
- Produces: `EnvironmentIdentity.IsCurrent(EnvProfile? current) : bool`

- [ ] **Step 1: Write failing identity tests**

Create a table-driven test that starts from one literal profile and mutates one field at a time:

```csharp
public static TheoryData<Func<EnvProfile, EnvProfile>> ConnectionChanges => new()
{
    p => p with { Url = "https://other.operations.dynamics.com" },
    p => p with { DataverseUrl = "https://other.crm.dynamics.com" },
    p => p with { Tenant = "other.onmicrosoft.com" },
    p => p with { ClientId = "fo-client-2" },
    p => p with { AuthMode = FoAuthMode.ClientSecret },
    p => p with { DataverseClientId = "dv-client-2" },
    p => p with { DataverseAuthMode = FoAuthMode.ClientSecret },
    p => p with { Legal = "DEMF" },
};

[Theory]
[MemberData(nameof(ConnectionChanges))]
public void Same_profile_id_with_changed_connection_data_is_a_different_identity(
    Func<EnvProfile, EnvProfile> change)
{
    var before = Profile();
    Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(change(before)));
}
```

Add `Cosmetic_profile_changes_keep_the_same_identity` for Name/Status/LatencyMs/Tier, `Equivalent_endpoint_spelling_keeps_the_same_identity` for scheme/trailing slash/case, and `Data_integrator_legacy_fields_do_not_change_current_live_identity` to pin the source-backed exclusion.

- [ ] **Step 2: Write `Active_id_cache_stays_old_when_persistence_fails`**

Create a real store with active `env1`. Using `Microsoft.Data.Sqlite`, add a temporary trigger on `Settings` that executes `RAISE(ABORT, 'blocked default env update')` when `OLD.Key = 'DefaultEnvId'`. Attempt the update and assert:

```csharp
var before = store.ActiveId;
Assert.ThrowsAny<Exception>(() => store.ActiveId = "env2");
Assert.Equal(before, store.ActiveId);
Assert.Equal(before, await NewService().GetDefaultEnvironmentIdAsync(ct));
```

Drop the trigger in `finally`; the fixture continues using its normal database cleanup.

- [ ] **Step 3: Run focused tests and capture RED**

Run:

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EnvironmentIdentityTests|FullyQualifiedName~CoreProfileStoreTests.Active_id_cache"
```

Expected behavioral RED: the existing metadata/session same-id endpoint regression accepts stale state, and the ActiveId ordering test reports cached `env2` after persistence throws. Add the helper matrix before GREEN, but do not count a missing-type compiler error as RED evidence.

- [ ] **Step 4: Implement the immutable helper and ordering fix**

Use this shape:

```csharp
public sealed record EnvironmentIdentity(
    string ProfileId,
    string FoEndpoint,
    string DataverseEndpoint,
    string Tenant,
    string FoClientId,
    FoAuthMode FoAuthMode,
    string DataverseClientId,
    FoAuthMode DataverseAuthMode,
    string DefaultCompany)
{
    public static EnvironmentIdentity Create(EnvProfile profile) => new(
        profile.Id,
        NormalizeUrl(ResourceUrlNormalizer.NormalizeFoBaseUrl(profile.Url)),
        NormalizeUrl(ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(profile.DataverseUrl ?? string.Empty)),
        NormalizeIdentifier(profile.Tenant),
        NormalizeIdentifier(profile.ClientId),
        profile.AuthMode,
        NormalizeIdentifier(profile.DataverseClientId),
        profile.DataverseAuthMode,
        profile.Legal);
    public static EnvironmentIdentity? TryCreate(EnvProfile? profile) =>
        profile is null ? null : Create(profile);
    public bool IsCurrent(EnvProfile? current) => Equals(TryCreate(current));
    private static string NormalizeIdentifier(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        var authority = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        var suffix = uri.PathAndQuery + uri.Fragment;
        return suffix == "/" ? authority : authority + suffix;
    }
}
```

Normalize F&O/Dataverse endpoints with `ResourceUrlNormalizer`, then canonicalize only scheme/authority. Preserve URL path/query/fragment case and invalid normalized text exactly. Preserve `profile.Id` and `Legal` exactly and ordinally. Trim/case-normalize tenant and client ids only. Add profile-id-case, company-case, and URL-path-case distinction tests. Do not include Name/Status/Latency/Tier or unused DI fields.

Change `CoreProfileStore.ActiveId` ordering to:

```csharp
RunBlocking(() => _profiles.SetDefaultEnvironmentAsync(value ?? string.Empty));
_activeId = value;
```

- [ ] **Step 5: Run focused tests and capture GREEN**

Run the Step 3 command. Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add avalonia/toolBax.App/Services/EnvironmentIdentity.cs avalonia/toolBax.App/Services/CoreProfileStore.cs avalonia/toolBax.App.Tests/EnvironmentIdentityTests.cs avalonia/toolBax.App.Tests/CoreProfileStoreTests.cs
git commit -m "fix: define complete environment identity"
```

---

### Task 2: Guard metadata, HTTP dispatch, and reader scope

**Files:**
- Modify: `avalonia/toolBax.Core/Services/IMetadataService.cs`
- Modify: `avalonia/toolBax.App/Services/CoreMetadataService.cs`
- Modify: `avalonia/toolBax.App/Services/CoreODataClient.cs`
- Modify: `avalonia/toolBax.App/Services/CoreDataverseClient.cs`
- Modify: `avalonia/toolBax.App/Services/CoreDualWriteMapReader.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreMetadataServiceTests.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreODataClientTests.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreDataverseClientTests.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreDualWriteMapReaderTests.cs`

**Interfaces:**
- Produces: `IMetadataService.Invalidate() : void`
- Consumes: `EnvironmentIdentity.Create/TryCreate`

- [ ] **Step 1: Write failing service regressions**

Add controlled gates and these exact cases:

```csharp
[Fact]
public async Task Metadata_completion_after_invalidate_cannot_commit_without_a_getter_observing_the_switch()
{
    var load = service.LoadEntitiesAsync();
    await catalog.Entered;
    active = EnvB();
    service.Invalidate();
    catalog.Release(EnvAIndex);
    await load;
    Assert.Empty(service.GetEntities());
}
```

Add `Metadata_A_to_B_to_A_still_discards_the_old_generation`, `Same_id_endpoint_edit_clears_metadata`, `Fo_token_completion_after_identity_change_sends_no_HTTP_request`, `Dataverse_token_completion_after_identity_change_sends_no_HTTP_request`, and null-current variants. For both clients, the fake auth returns from a `TaskCompletionSource`; assert the handler call count remains zero and the response names the environment change.

For `CoreDualWriteMapReader`, add `Map_paging_stops_when_complete_identity_changes`, `Same_id_auth_edit_does_not_reuse_logical_name_cache`, and preserve `A_profile_repointed_at_another_organisation...` using the shared helper.

- [ ] **Step 2: Run focused tests and capture RED**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreMetadataServiceTests|FullyQualifiedName~CoreODataClientTests|FullyQualifiedName~CoreDataverseClientTests|FullyQualifiedName~CoreDualWriteMapReaderTests"
```

Expected: new tests show stale metadata commit, one HTTP dispatch after gated auth, and reader identity/cache reuse.

- [ ] **Step 3: Implement metadata identity plus generation**

Add mandatory `void Invalidate();` to `IMetadataService`. Add explicit no-op implementations to `FakeMetadataService` and genuinely cacheless test fakes; cache-owning wrappers delegate to their inner service. Replace `_cacheEnvId` with identity and generation under `_envSync`. Capture one profile once:

```csharp
var profile = _activeEnv();
if (profile is null) return;
var identity = EnvironmentIdentity.Create(profile);
var environment = ToFoEnvironment(profile);
var generation = PrepareGeneration(identity);
var index = await _catalog.GetODataEntityIndexAsync(environment, mode, ct).ConfigureAwait(false);
lock (_envSync)
{
    if (generation != _generation || !identity.IsCurrent(_activeEnv())) return;
    // commit
}
```

`Invalidate()` increments `_generation` and clears entities, enums, fields, and navigations under the same lock.

- [ ] **Step 4: Implement captured client dispatch**

In each client, derive URI and identity from one captured profile. After token acquisition:

```csharp
if (!capturedIdentity.IsCurrent(_activeEnv()))
{
    return new ODataResponse(0, "Environment changed",
        "The active environment changed before the request was sent.", elapsed);
}
```

Send to the URI derived from the captured profile; retain existing origin checks and token handling.

- [ ] **Step 5: Pin CoreDualWriteMapReader operations**

Replace the private `EnvIdentity` string with `EnvironmentIdentity`. At each public entry point capture one profile/identity/API base. Pass that captured scope through component/map/solution paging and count helpers; use absolute pinned URLs where an endpoint exists. Before each request and before returning/caching, require the captured identity to remain current.

- [ ] **Step 6: Run focused tests and capture GREEN**

Run the Step 2 command. Expected: all selected tests pass, including existing origin and paging tests.

- [ ] **Step 7: Commit**

```powershell
git add avalonia/toolBax.Core/Services/IMetadataService.cs avalonia/toolBax.App/Services/CoreMetadataService.cs avalonia/toolBax.App/Services/CoreODataClient.cs avalonia/toolBax.App/Services/CoreDataverseClient.cs avalonia/toolBax.App/Services/CoreDualWriteMapReader.cs avalonia/toolBax.App/Services/FakeMetadataService.cs avalonia/toolBax.App.Tests/CoreMetadataServiceTests.cs avalonia/toolBax.App.Tests/CoreMetadataServiceEnvScopeTests.cs avalonia/toolBax.App.Tests/CoreODataClientTests.cs avalonia/toolBax.App.Tests/CoreDataverseClientTests.cs avalonia/toolBax.App.Tests/CoreDualWriteMapReaderTests.cs
git commit -m "fix: reject stale environment service results"
```

---

### Task 3: Make Shell and Profiles one transaction

**Files:**
- Modify: `avalonia/toolBax.App/ViewModels/ShellViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/ProfilesViewModel.cs`
- Modify: `avalonia/toolBax.App/Views/MainWindow.axaml.cs` only if the headless rollback test proves notification insufficient
- Modify: `avalonia/toolBax.App.Tests/ShellViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/ShellRenderTests.cs`
- Modify: `avalonia/toolBax.App.Tests/ProfilesViewModelTests.cs`

**Interfaces:**
- Produces for Profiles: `Func<EnvProfile, Task<string?>> requestActivation` (`null` success, message refusal)
- Produces for Profiles: `Func<EnvProfile, EnvProfile, Task<string?>> commitActiveIdentitySave`
- Produces for Profiles delete: `Func<string?> mutationBlockReason`
- Consumes: `CoreMetadataService.Invalidate()` through an App-layer concrete type check (`_metadataService is CoreMetadataService core`); do not add or change a `toolBax.Core` interface.

- [ ] **Step 1: Replace optional-refresh expectations with failing transactional tests**

Update only tests that encode the removed "switch now, optionally refresh later" behavior. Add:

```csharp
[Fact]
public async Task Declining_switch_keeps_header_store_drafts_and_open_tool()
{
    var shell = ShellWithDecliningDialog();
    var old = shell.ActiveEnvironment!;
    var tool = OpenQuery(shell);
    await shell.SetActiveEnvironmentCommand.ExecuteAsync(Other(shell));
    Assert.Same(old, shell.ActiveEnvironment);
    Assert.Equal(old.Id, store.ActiveId);
    Assert.Same(tool, shell.CurrentContent);
}
```

Add `Accepted_switch_persists_then_invalidates_open_tools`, `First_selection_without_open_tools_does_not_prompt`, `Same_complete_identity_does_not_prompt`, `Cosmetic_active_profile_save_preserves_tool_instance`, `Active_company_save_requires_discard_and_invalidates_tools`, `Identity_save_decline_preserves_profile_and_drafts`, `Profile_save_captures_the_profile_selected_before_approval`, `Profiles_Set_active_awaits_the_same_shell_funnel`, `Overlapping_shell_transitions_are_serialized`, `Mutation_starting_while_switch_confirmation_is_open_blocks_the_commit`, and persistence/dialog failure cases.

Add `[AvaloniaFact] Declined_switch_forces_the_ComboBox_back_to_the_old_selection_even_when_ActiveEnvironment_reference_never_changed`.

- [ ] **Step 2: Run focused tests and capture RED**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ShellViewModelTests|FullyQualifiedName~ShellRenderTests|FullyQualifiedName~ProfilesViewModelTests"
```

Expected: current Shell commits before decline; Profiles writes the store before Shell; ComboBox remains on the declined item.

- [ ] **Step 3: Implement activation ordering and rollback**

Guard activation and active-identity-save flows with one private `SemaphoreSlim(1, 1)`. Reshape the Shell activation funnel in this order:

```csharp
if (sameIdentity) return null;
if (MutationBlockReason() is { } blocked) return Reject(blocked);
if (HasOpenDataTools() && !await ConfirmDiscardAsync(previous, target)) return Reject("Environment switch cancelled.");
if (!previousIdentity.Equals(EnvironmentIdentity.TryCreate(ActiveEnvironment)))
    return Reject("The active environment changed while confirmation was open.");
if (MutationBlockReason() is { } afterDialog) return Reject(afterDialog);
try { _profileStore.ActiveId = target.Id; }
catch (Exception ex) { return Reject($"Couldn't switch environment: {ex.Message}"); }
ActiveEnvironment = target;
InvalidateEnvironmentTools();
return null;
```

`Reject` raises `OnPropertyChanged(nameof(ActiveEnvironment))` and leaves state untouched. Confirm copy states that accepting changes environment and discards open tool state; it must not claim the switch already occurred.

- [ ] **Step 4: Implement awaited Profiles activation/save**

Make `SetActive` and `Save` async. `SetActive` calls `requestActivation(SelectedSnapshot)` and updates local status/id only on null result.

`Save` captures `selected` and builds `updated`. A non-active or cosmetic save persists directly. An active identity change calls `commitActiveIdentitySave(selected, updated)`; Shell holds the transition gate across approval, post-await mutation/current-identity recheck, `_profileStore.Save(updated)`, active-record replacement, and invalidation. On refusal/failure Profiles keeps drafts and list record. After success it updates its list entry by captured id and replaces `Selected` only when it still names the captured profile.

Before deleting the active profile, call `mutationBlockReason`; a non-null reason leaves store/list untouched and becomes `Status`.

- [ ] **Step 5: Wire successful profile saves and invalidation**

Shell's `ProfileSaved` handler remains the list/header synchronization path for ordinary/cosmetic saves and is idempotent after the active-identity callback already updated Shell. Cosmetic save: replace environment/header record only. Identity save invalidation occurs inside `commitActiveIdentitySave` while the transition gate is held. Header and Profiles both call the same activation delegate.

- [ ] **Step 6: Run focused tests and capture GREEN**

Run the Step 2 command. Expected: all selected tests pass, including rendered ComboBox rollback.

- [ ] **Step 7: Commit**

```powershell
git add avalonia/toolBax.App/ViewModels/ShellViewModel.cs avalonia/toolBax.App/ViewModels/ProfilesViewModel.cs avalonia/toolBax.App/Views/MainWindow.axaml.cs avalonia/toolBax.App.Tests/ShellViewModelTests.cs avalonia/toolBax.App.Tests/ShellRenderTests.cs avalonia/toolBax.App.Tests/ProfilesViewModelTests.cs
git commit -m "fix: make environment changes transactional"
```

---

### Task 4: Bind Query/POST and cancel discarded catalogue work

**Files:**
- Modify: `avalonia/toolBax.App/ViewModels/EntityCatalogLoader.cs`
- Modify: `avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/PostBuilderViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/MetadataViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/DualWriteCompareViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/ShellViewModel.cs` (constructor wiring and mutation inspection only)
- Modify: `avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/PostBuilderViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/MetadataViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteCompareViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Query/Post constructors consume optional `Func<EnvProfile?>? activeEnv`; Shell always passes `() => ActiveEnvironment`.
- POST produces `public bool MutationInProgress { get; private set; }`.
- EntityCatalogLoader, Query, POST, Metadata, Compare implement `IDisposable` as required by their owned command/CTS lifecycle.

- [ ] **Step 1: Write failing Query and lifecycle tests**

Add `Query_started_under_A_is_discarded_after_same_id_identity_edit`, `Query_A_to_B_to_A_is_discarded_after_dispose`, and `Disposed_export_all_never_opens_the_late_save_picker`. Use a client gate that deliberately ignores cancellation for the last case; dispose after request entry, release the response, assert `FakeFileSaveService` saw no call.

Add Metadata/Compare tests that dispose with a controlled load/compare in flight, release it, and assert no collection/result resurrection.

- [ ] **Step 2: Write failing POST snapshot/mutation tests**

Use a gated dialog and gated auth/client seam:

```csharp
var send = vm.SendCommand.ExecuteAsync(null);
await dialogs.Entered;
Assert.True(vm.MutationInProgress);
vm.Method = "DELETE";
vm.Path = "/data/Other";
dialogs.Release(true);
await send;
Assert.Equal(("POST", "/data/Original", originalBody, originalHeaders), client.LastRequest);
```

Add `Identity_change_during_confirmation_prevents_dispatch`, `Identity_change_during_token_acquisition_prevents_dispatch` at the real client boundary, `Shell_refuses_switch_while_POST_confirmation_is_open`, and `Disposed_catalogue_load_does_not_publish_a_late_error`.

- [ ] **Step 3: Run focused tests and capture RED**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QueryBuilderViewModelTests|FullyQualifiedName~PostBuilderViewModelTests|FullyQualifiedName~MetadataViewModelTests|FullyQualifiedName~DualWriteCompareViewModelTests|FullyQualifiedName~ShellViewModelTests"
```

Expected: stale completions/picker occur, POST sends mutable current properties, and Shell switches during pending confirmation.

- [ ] **Step 4: Implement local lifetime guards**

Each affected VM owns `_disposed` plus a monotonic `_generation`. `Dispose()` sets `_disposed`, increments generation, cancels its generated Initialize/Load/Run/Export/Compare commands, and disposes `EntityCatalogLoader`. Every post-await commit checks both.

`EntityCatalogLoader.Dispose()` cancels and disposes its active CTS; its catch/finally does not publish `LastError` after disposal or a newer generation.

- [ ] **Step 5: Implement Query operation scope**

Capture profile/identity/generation/path/columns before awaiting each Run, LoadMore, ExportCurrent, and ExportAll. In bound mode, no profile means a clear no-environment status. Before rows/status/picker commit, require current identity, generation, and not disposed. Keep explicitly unbound tests behavior-compatible.

- [ ] **Step 6: Implement POST approved snapshot and mutation flag**

At Send entry capture method, effective path, body/null, copied headers, identity, and generation. Set `MutationInProgress = true` before confirmation. Build `ConfirmRequest` from captured values. After confirmation and after client completion, check identity/generation/disposal. Send only captured values. Clear mutation in `finally` and raise command state notifications.

- [ ] **Step 7: Bind production VMs and Shell mutation inspection**

Change `ShellViewModel.ResolveContent` to pass `() => ActiveEnvironment` into Query and POST. `MutationBlockReason()` inspects cached POST and Operations instances; at this task POST is covered, and Task 5 completes Operations.

- [ ] **Step 8: Run focused tests and capture GREEN**

Run the Step 3 command. Expected: all selected tests pass and H02 service tests remain unchanged.

- [ ] **Step 9: Commit**

```powershell
git add avalonia/toolBax.App/ViewModels/EntityCatalogLoader.cs avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs avalonia/toolBax.App/ViewModels/PostBuilderViewModel.cs avalonia/toolBax.App/ViewModels/MetadataViewModel.cs avalonia/toolBax.App/ViewModels/DualWriteCompareViewModel.cs avalonia/toolBax.App/ViewModels/ShellViewModel.cs avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs avalonia/toolBax.App.Tests/PostBuilderViewModelTests.cs avalonia/toolBax.App.Tests/MetadataViewModelTests.cs avalonia/toolBax.App.Tests/DualWriteCompareViewModelTests.cs avalonia/toolBax.App.Tests/ShellViewModelTests.cs
git commit -m "fix: discard invalidated query and post work"
```

---

### Task 5: Carry complete identity through dual-write sessions

**Files:**
- Modify: `avalonia/toolBax.App/Services/IDualWriteConnector.cs`
- Modify: `avalonia/toolBax.App/Services/CoreDualWriteConnector.cs`
- Modify: `avalonia/toolBax.App/Services/FakeDualWriteConnector.cs`
- Modify: `avalonia/toolBax.App/ViewModels/DualWriteOpsViewModel.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreDualWriteCompareServiceTests.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteCompareViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteOpsTests.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteConnectionGuardTests.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteCompareViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/CoreDualWriteCompareServiceTests.cs`
- Modify: `avalonia/toolBax.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- `DualWriteSession` positional input changes from `string EnvId` to `EnvProfile Profile`.
- `DualWriteSession.Identity` derives once from `Profile`; `EnvId => Profile.Id` remains available.
- Operations produces `public bool MutationInProgress { get; private set; }`.

- [ ] **Step 1: Write failing session and late-connect tests**

Add `Same_id_endpoint_edit_refuses_gateway_action`, `Same_id_auth_edit_refuses_debug_requests`, and `Late_session_returned_after_dispose_is_disposed_and_never_assigned`:

```csharp
var load = vm.LoadCommand.ExecuteAsync(null);
await connector.Entered;
vm.Dispose();
var gateway = connector.ReleaseSession(profileA);
await load;
Assert.True(gateway.Disposed);
Assert.False(vm.IsConnected);
```

Add gated-confirmation tests for lifecycle action and debug toggle asserting `MutationInProgress` is true and Shell refuses header switch, active identity save, and active-profile deletion without disposing the gateway.

- [ ] **Step 2: Run focused tests and capture RED**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DualWriteOpsTests|FullyQualifiedName~DualWriteConnectionGuardTests|FullyQualifiedName~DualWriteCompareViewModelTests|FullyQualifiedName~CoreDualWriteCompareServiceTests|FullyQualifiedName~ShellViewModelTests"
```

Expected: id-only session accepts same-id edits; late connector result survives disposal; Shell switches during dual-write confirmation.

- [ ] **Step 3: Change the session contract and connectors**

Use:

```csharp
public sealed record DualWriteSession(
    IDualWriteGateway Gateway, string Cid, string Cname, EnvProfile Profile,
    string GatewayBaseUrl = "")
{
    public EnvironmentIdentity Identity { get; } = EnvironmentIdentity.Create(Profile);
    public string EnvId => Profile.Id;
}
```

Real/fake connectors pass the immutable `env` argument they already captured. Update test session constructors mechanically without changing H02 connect ordering.

- [ ] **Step 4: Implement Operations generation, disposal, and mutation guards**

Load captures profile/identity/generation. After `ConnectAsync`, dispose the returned gateway and return when disposed, generation changed, or current identity differs. Recheck after map load before assigning observable state.

Replace id-only mismatch with session identity equality. Set `MutationInProgress` before lifecycle confirmation and before debug metadata/auth work; clear it in `finally` after every branch. Keep the existing per-await action/debug guards.

- [ ] **Step 5: Run focused tests and capture GREEN**

Run the Step 2 command. Expected: all selected tests pass, including the H02 UI-thread regression.

- [ ] **Step 6: Commit**

```powershell
git add avalonia/toolBax.App/Services/IDualWriteConnector.cs avalonia/toolBax.App/Services/CoreDualWriteConnector.cs avalonia/toolBax.App/Services/FakeDualWriteConnector.cs avalonia/toolBax.App/ViewModels/DualWriteOpsViewModel.cs avalonia/toolBax.App.Tests/DualWriteOpsTests.cs avalonia/toolBax.App.Tests/DualWriteConnectionGuardTests.cs avalonia/toolBax.App.Tests/DualWriteCompareViewModelTests.cs avalonia/toolBax.App.Tests/CoreDualWriteCompareServiceTests.cs avalonia/toolBax.App.Tests/ShellViewModelTests.cs
git commit -m "fix: bind dual-write sessions to complete identity"
```

---

### Task 6: Attribute Map Browser and Virtual Tables data and links

**Files:**
- Modify: `avalonia/toolBax.App/ViewModels/DualWriteMapViewModel.cs`
- Modify: `avalonia/toolBax.App/ViewModels/VirtualTablesViewModel.cs`
- Modify: `avalonia/toolBax.App.Tests/DualWriteMapViewModelTests.cs`
- Modify: `avalonia/toolBax.App.Tests/VirtualTablesViewModelTests.cs`

**Interfaces:**
- Consumes: `EnvironmentIdentity`
- Both VMs implement `IDisposable` and keep captured loaded `EnvProfile`/identity plus local generation.

- [ ] **Step 1: Write failing loaded-scope tests**

Add Map Browser tests: `Same_id_Dataverse_edit_blocks_stale_counts`, `Map_link_uses_loaded_profiles_Dataverse_url_after_active_profile_changes`, `Disposed_map_link_commands_do_not_launch_or_copy`, `Slow_A_completion_after_B_does_not_overwrite_B`, `A_to_B_to_A_old_generation_is_discarded`, and `Dispose_cancels_reload_count_and_export_without_late_state`.

Add Virtual Tables tests: `Same_id_endpoint_edit_reloads`, `Selected_link_uses_loaded_profile_not_current_profile`, `Disposed_link_command_does_not_launch`, `A_to_B_to_A_old_generation_is_discarded`, and `Disposed_load_cannot_publish_tables_or_error`.

Use per-call `TaskCompletionSource` gates and literal A/B table/map payloads; no sleeps.

- [ ] **Step 2: Run focused tests and capture RED**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DualWriteMapViewModelTests|FullyQualifiedName~VirtualTablesViewModelTests"
```

Expected: id-only guards accept same-id edits, links use the current environment, and disposed/old-generation completions can publish.

- [ ] **Step 3: Implement Map Browser loaded scope**

Capture one profile/identity/generation before each reader call. Commit maps/solutions/count statuses only if current identity and generation still match and the VM is not disposed. Stamp successful map data with the captured profile and identity. `MapRecordUrl` uses `_loadedProfile?.DataverseUrl`; open/copy commands return immediately once disposed. `Dispose()` increments generation and cancels Initialize, ReloadMaps, CountAllRows, and ExportMarkdown commands.

- [ ] **Step 4: Implement Virtual Tables loaded scope**

Replace `_loadedEnvId` with captured loaded profile/identity. `SelectedTableUrl` uses the loaded profile; the open command returns immediately once disposed. Commit result/error/loaded label only for current generation and identity. `Dispose()` cancels Initialize/Refresh and blocks late state.

- [ ] **Step 5: Run focused tests and capture GREEN**

Run the Step 2 command. Expected: all selected tests pass, including existing overlapping-load busy-state cases.

- [ ] **Step 6: Commit**

```powershell
git add avalonia/toolBax.App/ViewModels/DualWriteMapViewModel.cs avalonia/toolBax.App/ViewModels/VirtualTablesViewModel.cs avalonia/toolBax.App.Tests/DualWriteMapViewModelTests.cs avalonia/toolBax.App.Tests/VirtualTablesViewModelTests.cs
git commit -m "fix: attribute loaded environment data and links"
```

---

### Task 7: Integrate, document evidence, and run complete gates

**Files:**
- Modify: `docs/production-readiness/2026-09-25-hardening.md`
- Review all files changed by Tasks 1-6

**Interfaces:**
- No new runtime interface; this task validates the completed H01 slice.

- [ ] **Step 1: Run the focused environment-consistency set**

```powershell
$env:CI='true'
dotnet test .\avalonia\toolBax.App.Tests\toolBax.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EnvironmentIdentityTests|FullyQualifiedName~ShellViewModelTests|FullyQualifiedName~ShellRenderTests|FullyQualifiedName~ProfilesViewModelTests|FullyQualifiedName~CoreMetadataServiceTests|FullyQualifiedName~CoreODataClientTests|FullyQualifiedName~CoreDataverseClientTests|FullyQualifiedName~CoreDualWriteMapReaderTests|FullyQualifiedName~QueryBuilderViewModelTests|FullyQualifiedName~PostBuilderViewModelTests|FullyQualifiedName~DualWriteOpsTests|FullyQualifiedName~DualWriteMapViewModelTests|FullyQualifiedName~VirtualTablesViewModelTests|FullyQualifiedName~MetadataViewModelTests|FullyQualifiedName~DualWriteCompareViewModelTests"
```

Expected: all pass with no warnings/errors.

- [ ] **Step 2: Run both CI-strict Release build/test gates**

```powershell
$env:CI='true'
dotnet build .\avalonia\toolBax.slnx -c Release --no-restore
dotnet test .\avalonia\toolBax.slnx -c Release --no-build
dotnet build .\FoToolbox.sln -c Release --no-restore
dotnet test .\FoToolbox.sln -c Release --no-build
```

Expected: both builds report 0 warnings and 0 errors; both complete suites pass.

- [ ] **Step 3: Audit invariants and diff**

Run:

```powershell
git diff --check
rg -n "_activeEnv\(\).*_activeEnv\(" avalonia/toolBax.App/Services avalonia/toolBax.App/ViewModels
rg -n "EnvId.*_activeEnv|_activeEnv.*\.Id|_loadedEnvId|_cacheEnvId" avalonia/toolBax.App/Services avalonia/toolBax.App/ViewModels
git status --short
git diff --stat origin/main...HEAD
```

Inspect each hit; no environment-sensitive live path may remain id-only, and no operation may separately fetch its stamp and request profile.

- [ ] **Step 4: Update tracker evidence without claiming merge**

Keep H01 `In progress`. Record focused red/green evidence, final suite counts, and the H09 `ActiveId` overlap. Keep D01 pending and explicitly outside H01. Do not mark H01 complete or add PR/Greptile/merge evidence before those events occur.

- [ ] **Step 5: Commit final evidence**

```powershell
git add docs/production-readiness/2026-09-25-hardening.md
git commit -m "docs: record environment consistency validation"
```

- [ ] **Step 6: Parent and independent review gate**

Return the complete diff, focused red/green outputs, suite totals, commit list, and remaining limitations to the parent. The parent runs final diff review and a separate reviewer before any push/PR. Parent owns remote push, PR, Greptile handling, merge, main fast-forward, and the later tracker completion evidence.

---

## Self-review record

- Spec coverage: every locked decision maps to Tasks 1-7; D01 is recorded but excluded from implementation.
- Placeholder scan: the plan names concrete files, methods, tests, commands, and expected failures/passes.
- Type consistency: all tasks use `EnvironmentIdentity.Create/TryCreate/IsCurrent`; session and constructor changes are defined before consumers rely on them.
- Review Focus: all five conditions have explicit tests in Tasks 1, 2, 4, 5, or 6.
- Scope: no auth-mode removal, retries, parser changes, H02 Compare service changes, payload-default change, or new architecture framework is included.
