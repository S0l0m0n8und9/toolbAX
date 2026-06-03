# Dual-write Profile Auth (ROPC) — V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Dual-write Operations plugin obtain its gateway token from a profile-level,
browser-free **ROPC** credential (username/password, client `2e49aa60`) instead of owning auth — for
non-MFA service accounts. The existing WebView2 sign-in stays as the fallback for MFA users.

**Architecture:** A host-owned `DataIntegratorCredentialStore` persists `{clientId, username,
password}` (DPAPI vault) per environment. A `DataIntegratorTokenService` mints an IntegratorApp token
via MSAL `AcquireTokenByUsernamePassword` (cached until expiry). The host exposes it to plugins via a
new `IPluginContextDualWrite.AcquireDataIntegratorTokenAsync`. The plugin builds its gateway with that
token when a credential is configured; otherwise it uses the current WebView2-captured token path.

**Tech Stack:** .NET 10 / C#, MSAL.NET (`Microsoft.Identity.Client` 4.62), xUnit, WPF (Profiles UI),
SQLite-backed `SecretVaultService` (DPAPI).

**Scope note:** V1 deliberately does NOT relocate the interactive WebView2 sign-in into the profile,
and does NOT add an `AuthTarget.DataIntegrator` enum value (a dedicated store avoids rippling through
`ServicePrincipal`/`ProfileBundle`). Those, plus browser-free gateway discovery via `ClusterDiscovery`,
are follow-up plans. The default client id `2e49aa60` already exists as `DualWriteAuthConstants.ClientId`.

---

## File structure

- Create `src/FoToolbox.Core/DualWrite/Auth/DataIntegratorCredential.cs` — the in-memory credential record.
- Create `src/FoToolbox.Core/DualWrite/Auth/IDataIntegratorTokenAcquirer.cs` — ROPC acquirer abstraction (testable seam).
- Create `src/FoToolbox.Core/DualWrite/Auth/MsalRopcTokenAcquirer.cs` — real MSAL ROPC impl.
- Create `src/FoToolbox.Core/DualWrite/Auth/DataIntegratorTokenService.cs` — caches + acquires the token.
- Modify `src/FoToolbox.Core/DualWrite/DualWriteGatewayFactory.cs` — add `CreateWithTokenProvider` + `DelegatedTokenHandler`.
- Create `src/FoToolbox.SDK/Plugins/IPluginContextDualWrite.cs` — plugin-facing token call.
- Create `src/FoToolbox.Host/DataIntegratorCredentialStore.cs` — vault + settings persistence.
- Modify `src/FoToolbox.Host/Plugins/PluginContext.cs` (and `PluginContextWrite.cs`) — implement `IPluginContextDualWrite`.
- Modify `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs` + `Views/ProfilesView.xaml` — Data Integrator section.
- Modify `plugins/DualWriteOperations/DualWriteOperationsViewModel.cs` — prefer the context token.
- Tests in `tests/FoToolbox.Tests/`.

---

### Task 1: Live ROPC validation (human checkpoint — GATE)

**Files:** Modify `artifacts/authprobe/probe.cs` (gitignored throwaway).

This proves client `2e49aa60` + ROPC actually mints an IntegratorApp token before we build on it. If
it fails (e.g. `AADSTS50076` MFA, or `AADSTS7000218` client-not-public), STOP and reassess — ROPC may
need a different client id or a non-MFA service account.

- [ ] **Step 1: Replace the probe body with an ROPC acquisition**

```csharp
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Identity.Client@4.62.0
using Microsoft.Identity.Client;
using System.Net.Http.Headers;

const string clientId = "2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b";
const string scope    = "https://IntegratorApp.com/.default";
var tenant      = args.Length > 0 ? args[0] : "organizations";
var gatewayBase = args.Length > 1 ? args[1] : null;
var identifier  = args.Length > 2 ? args[2] : null;

Console.Write("Username (UPN): "); var user = Console.ReadLine();
Console.Write("Password: ");       var pwd  = ReadPassword();

var app = PublicClientApplicationBuilder.Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenant}").Build();
try
{
    var r = await app.AcquireTokenByUsernamePassword(new[] { scope }, user, pwd).ExecuteAsync();
    Console.WriteLine("\nSUCCESS: token acquired. expires " + r.ExpiresOn);
    if (gatewayBase is not null && identifier is not null)
    {
        using var http = new HttpClient { BaseAddress = new Uri(gatewayBase.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", r.AccessToken);
        var resp = await http.GetAsync($"api/DualWriteManagement/1.0/Environments?targetType=AX&identifier={Uri.EscapeDataString(identifier)}");
        Console.WriteLine($"gateway <= {(int)resp.StatusCode}; body: {await resp.Content.ReadAsStringAsync()}");
    }
}
catch (MsalException ex) { Console.WriteLine($"\nFAILED: {ex.ErrorCode} — {ex.Message.Split('\n')[0]}"); }

static string ReadPassword()
{
    var sb = new System.Text.StringBuilder();
    while (true) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; } else sb.Append(k.KeyChar); }
    return sb.ToString();
}
```

- [ ] **Step 2: Build + run (human)**

Run: `dotnet build .\artifacts\authprobe\probe.cs` then
`dotnet run .\artifacts\authprobe\probe.cs <tenant-guid> https://projectmanagementservice.au-il102.gateway.prod.island.powerapps.com https://ranzfodev.sandbox.operations.dynamics.com`
Expected (success case): `SUCCESS: token acquired` and a gateway `200`/`401`/`[]`.
**GATE:** Only proceed to Task 2 if `SUCCESS`. A `FAILED: AADSTS...` means ROPC isn't viable as specced — stop and reassess the client id / account.

---

### Task 2: Core — credential record + token service (ROPC, cached)

**Files:**
- Create: `src/FoToolbox.Core/DualWrite/Auth/DataIntegratorCredential.cs`
- Create: `src/FoToolbox.Core/DualWrite/Auth/IDataIntegratorTokenAcquirer.cs`
- Create: `src/FoToolbox.Core/DualWrite/Auth/DataIntegratorTokenService.cs`
- Test: `tests/FoToolbox.Tests/DataIntegratorTokenServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using Xunit;

namespace FoToolbox.Tests;

public class DataIntegratorTokenServiceTests
{
    private sealed class FakeAcquirer : IDataIntegratorTokenAcquirer
    {
        public int Calls;
        public DualWriteToken Next = new("t1", null, new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero));
        public Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct)
        { Calls++; return Task.FromResult(Next); }
    }

    private static DataIntegratorCredential Cred() => new("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", "svc@contoso.com", "pw");

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetToken_AcquiresThenCachesUntilExpiry()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var acquirer = new FakeAcquirer { Next = new DualWriteToken("acc", null, now.AddHours(1)) };
        var svc = new DataIntegratorTokenService(acquirer) { Clock = () => now };

        var a = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);
        var b = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        Assert.Equal("acc", a);
        Assert.Equal("acc", b);
        Assert.Equal(1, acquirer.Calls); // cached; not re-acquired
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task GetToken_ReacquiresWhenExpired()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var acquirer = new FakeAcquirer { Next = new DualWriteToken("acc", null, now.AddMinutes(1)) };
        var svc = new DataIntegratorTokenService(acquirer) { Clock = () => now };
        await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        svc.Clock = () => now.AddMinutes(5); // past expiry (incl. margin)
        acquirer.Next = new DualWriteToken("acc2", null, now.AddHours(1));
        var c = await svc.GetTokenAsync(Cred(), "tenant-1", CancellationToken.None);

        Assert.Equal("acc2", c);
        Assert.Equal(2, acquirer.Calls);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DataIntegratorTokenServiceTests"`
Expected: FAIL — `DataIntegratorTokenService` / `IDataIntegratorTokenAcquirer` / `DataIntegratorCredential` don't exist.

- [ ] **Step 3: Create the credential record**

`src/FoToolbox.Core/DualWrite/Auth/DataIntegratorCredential.cs`:
```csharp
namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Decrypted, in-memory ROPC credential for the Dual-write (IntegratorApp) gateway. Persisted via the
/// host's DPAPI vault — never logged.
/// </summary>
public sealed record DataIntegratorCredential(string ClientId, string Username, string Password)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}
```

- [ ] **Step 4: Create the acquirer abstraction**

`src/FoToolbox.Core/DualWrite/Auth/IDataIntegratorTokenAcquirer.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Acquires an IntegratorApp delegated token via ROPC. Abstracted for testing.</summary>
public interface IDataIntegratorTokenAcquirer
{
    Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct);
}
```

- [ ] **Step 5: Create the token service**

`src/FoToolbox.Core/DualWrite/Auth/DataIntegratorTokenService.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Produces an IntegratorApp access token from a ROPC <see cref="DataIntegratorCredential"/>, caching
/// it in memory until it nears expiry (ROPC re-sends the password on every acquisition, so we cache).
/// </summary>
public sealed class DataIntegratorTokenService
{
    private const string ScopeDefault = "https://IntegratorApp.com/.default";
    private const string AuthorityBase = "https://login.microsoftonline.com";

    private readonly IDataIntegratorTokenAcquirer _acquirer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DualWriteToken? _cached;

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public DataIntegratorTokenService(IDataIntegratorTokenAcquirer acquirer) =>
        _acquirer = acquirer ?? throw new ArgumentNullException(nameof(acquirer));

    public async Task<string> GetTokenAsync(DataIntegratorCredential credential, string tenantId, CancellationToken ct = default)
    {
        if (credential is null || !credential.IsComplete)
        {
            throw new DualWriteAuthException("No Data Integrator credential is configured. Set it in Profiles → Data Integrator.");
        }

        if (_cached is not null && !_cached.IsExpired(Clock()))
        {
            return _cached.AccessToken;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null && !_cached.IsExpired(Clock()))
            {
                return _cached.AccessToken;
            }

            var authority = $"{AuthorityBase}/{tenantId}";
            _cached = await _acquirer.AcquireAsync(authority, credential.ClientId, ScopeDefault, credential.Username, credential.Password, ct).ConfigureAwait(false);
            return _cached.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DataIntegratorTokenServiceTests"`
Expected: PASS (2 tests). (`DualWriteToken.IsExpired` already applies a 2-minute margin.)

- [ ] **Step 7: Commit**

```bash
git add src/FoToolbox.Core/DualWrite/Auth/DataIntegratorCredential.cs \
        src/FoToolbox.Core/DualWrite/Auth/IDataIntegratorTokenAcquirer.cs \
        src/FoToolbox.Core/DualWrite/Auth/DataIntegratorTokenService.cs \
        tests/FoToolbox.Tests/DataIntegratorTokenServiceTests.cs
git commit -m "feat(dualwrite): ROPC Data Integrator token service (cached)"
```

---

### Task 3: Core — real MSAL ROPC acquirer

**Files:**
- Create: `src/FoToolbox.Core/DualWrite/Auth/MsalRopcTokenAcquirer.cs`

(No unit test — it calls Entra; covered by the Task 1 live probe and the Task 10 manual run.)

- [ ] **Step 1: Implement**

`src/FoToolbox.Core/DualWrite/Auth/MsalRopcTokenAcquirer.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Real ROPC acquirer using MSAL <c>AcquireTokenByUsernamePassword</c>.</summary>
public sealed class MsalRopcTokenAcquirer : IDataIntegratorTokenAcquirer
{
    public async Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct)
    {
        var app = PublicClientApplicationBuilder.Create(clientId).WithAuthority(authority).Build();
        try
        {
            var result = await app.AcquireTokenByUsernamePassword(new[] { scope }, username, password).ExecuteAsync(ct).ConfigureAwait(false);
            return new DualWriteToken(result.AccessToken, null, result.ExpiresOn);
        }
        catch (MsalException ex)
        {
            throw new DualWriteAuthException($"Data Integrator ROPC sign-in failed: {ex.ErrorCode}. {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build ./FoToolbox.sln -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/FoToolbox.Core/DualWrite/Auth/MsalRopcTokenAcquirer.cs
git commit -m "feat(dualwrite): MSAL ROPC acquirer for Data Integrator"
```

---

### Task 4: Core — gateway factory token-provider path

**Files:**
- Modify: `src/FoToolbox.Core/DualWrite/DualWriteGatewayFactory.cs`
- Test: `tests/FoToolbox.Tests/DualWriteGatewayFactoryTokenProviderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteGatewayFactoryTokenProviderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastAuth;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuth = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task CreateWithTokenProvider_AttachesProviderToken()
    {
        var inner = new CapturingHandler();
        var factory = new DualWriteGatewayFactory();
        var token = "abc";
        var gateway = factory.CreateWithTokenProvider(
            "https://projectmanagementservice.au-il102.gateway.prod.island.powerapps.com",
            _ => Task.FromResult(token),
            innerHandler: inner);

        await gateway.GetEnvironmentAsync("https://x.operations.dynamics.com", CancellationToken.None);

        Assert.Equal("abc", inner.LastAuth);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DualWriteGatewayFactoryTokenProviderTests"`
Expected: FAIL — `CreateWithTokenProvider` not defined.

- [ ] **Step 3: Add the method + handler**

In `DualWriteGatewayFactory` (interface `IDualWriteGatewayFactory` and class), add:
```csharp
    // (interface)
    IDualWriteGateway CreateWithTokenProvider(string gatewayBaseUrl, Func<CancellationToken, Task<string>> getToken, HttpMessageHandler? innerHandler = null);
```
```csharp
    // (class) — uses the same GatewayUri helper already in the file
    public IDualWriteGateway CreateWithTokenProvider(string gatewayBaseUrl, Func<CancellationToken, Task<string>> getToken, HttpMessageHandler? innerHandler = null)
    {
        if (string.IsNullOrWhiteSpace(gatewayBaseUrl))
        {
            throw new InvalidOperationException("Gateway base URL is not configured.");
        }

        var http = new HttpClient(new DelegatedTokenHandler(getToken, innerHandler ?? new HttpClientHandler()))
        {
            BaseAddress = new Uri(gatewayBaseUrl.TrimEnd('/') + "/")
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http);
    }
```
Add the handler (same file, after `BearerTokenHandler`):
```csharp
internal sealed class DelegatedTokenHandler : DelegatingHandler
{
    private readonly Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>> _getToken;

    public DelegatedTokenHandler(Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<string>> getToken, HttpMessageHandler inner) : base(inner)
        => _getToken = getToken;

    protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        var token = await _getToken(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DualWriteGatewayFactoryTokenProviderTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/FoToolbox.Core/DualWrite/DualWriteGatewayFactory.cs tests/FoToolbox.Tests/DualWriteGatewayFactoryTokenProviderTests.cs
git commit -m "feat(dualwrite): gateway factory token-provider path"
```

---

### Task 5: SDK — plugin-facing context interface

**Files:**
- Create: `src/FoToolbox.SDK/Plugins/IPluginContextDualWrite.cs`

- [ ] **Step 1: Create the interface**

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional context extension for dual-write plugins: acquires a delegated token for the Data
/// Integrator (IntegratorApp) gateway from the active profile's credential. Cast
/// <see cref="IPluginContext"/> to this.
/// </summary>
public interface IPluginContextDualWrite
{
    Task<string> AcquireDataIntegratorTokenAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build ./FoToolbox.sln -c Release` → 0 errors.
```bash
git add src/FoToolbox.SDK/Plugins/IPluginContextDualWrite.cs
git commit -m "feat(sdk): IPluginContextDualWrite token contract"
```

---

### Task 6: Host — credential store (vault + settings)

**Files:**
- Create: `src/FoToolbox.Host/DataIntegratorCredentialStore.cs`
- Test: `tests/FoToolbox.Tests/DataIntegratorCredentialStoreTests.cs`

`SecretVaultService` API (existing): `Task<string> StoreSecretAsync(string label, T payload)`,
`Task<T?> ReadSecretAsync<T>(string secretRef, CancellationToken ct = default)`.
`ProfileStore` (existing): `Task<string?> GetSettingAsync(string key, ...)`, `Task SetSettingAsync(string key, string value, ...)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Profiles;
using FoToolbox.Host;
using Xunit;

namespace FoToolbox.Tests;

public class DataIntegratorCredentialStoreTests
{
    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveThenGet_RoundTrips()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"di-{System.Guid.NewGuid():N}.db");
        try
        {
            var profiles = new ProfileStore(dbPath);
            await profiles.EnsureCreatedAsync();
            var vault = new SecretVaultService(profiles.ConnectionString);
            var store = new DataIntegratorCredentialStore(profiles, vault);

            await store.SaveAsync("env-1", new DataIntegratorCredential("2e49aa60-1bd3-43b6-8ab6-03ada3d9f08b", "svc@contoso.com", "pw"), CancellationToken.None);
            var got = await store.GetAsync("env-1", CancellationToken.None);

            Assert.NotNull(got);
            Assert.Equal("svc@contoso.com", got!.Username);
            Assert.Equal("pw", got.Password);
            Assert.Null(await store.GetAsync("env-2", CancellationToken.None));
        }
        finally { File.Delete(dbPath); }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DataIntegratorCredentialStoreTests"`
Expected: FAIL — `DataIntegratorCredentialStore` not defined.

- [ ] **Step 3: Implement the store**

`src/FoToolbox.Host/DataIntegratorCredentialStore.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Profiles;

namespace FoToolbox.Host;

/// <summary>
/// Persists the per-environment Data Integrator ROPC credential: the secret payload (clientId,
/// username, password) goes in the DPAPI vault; a settings row maps the env to its secret ref.
/// </summary>
internal sealed class DataIntegratorCredentialStore
{
    private readonly ProfileStore _profiles;
    private readonly SecretVaultService _vault;

    public DataIntegratorCredentialStore(ProfileStore profiles, SecretVaultService vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    private static string Key(string envId) => $"DataIntegrator:{envId}";

    public async Task SaveAsync(string envId, DataIntegratorCredential credential, CancellationToken ct)
    {
        var secretRef = await _vault.StoreSecretAsync("DataIntegrator", new Payload
        {
            ClientId = credential.ClientId,
            Username = credential.Username,
            Password = credential.Password,
        });
        await _profiles.SetSettingAsync(Key(envId), secretRef, ct);
    }

    public async Task<DataIntegratorCredential?> GetAsync(string envId, CancellationToken ct)
    {
        var secretRef = await _profiles.GetSettingAsync(Key(envId), ct);
        if (string.IsNullOrWhiteSpace(secretRef)) return null;
        var payload = await _vault.ReadSecretAsync<Payload>(secretRef, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Username)) return null;
        return new DataIntegratorCredential(payload.ClientId ?? string.Empty, payload.Username ?? string.Empty, payload.Password ?? string.Empty);
    }

    private sealed class Payload
    {
        public string? ClientId { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~DataIntegratorCredentialStoreTests"`
Expected: PASS. (If `ProfileStore.ConnectionString`/`SetSettingAsync` signatures differ, adjust the calls to match — verify by reading `ProfileStore.cs` first.)

- [ ] **Step 5: Commit**

```bash
git add src/FoToolbox.Host/DataIntegratorCredentialStore.cs tests/FoToolbox.Tests/DataIntegratorCredentialStoreTests.cs
git commit -m "feat(host): Data Integrator credential store (DPAPI vault)"
```

---

### Task 7: Host — implement IPluginContextDualWrite

**Files:**
- Modify: `src/FoToolbox.Host/Plugins/PluginContext.cs`, `src/FoToolbox.Host/Plugins/PluginContextWrite.cs`
- (Wiring) `src/FoToolbox.Host/Plugins/PluginManager.cs` / `AppBootstrapper.cs` to supply the store + token service.

Both context classes already implement several `IPluginContext*` interfaces; add `IPluginContextDualWrite`.
The token service caches per instance, so create one per context. The active env's `TenantId` comes from `CurrentEnv`.

- [ ] **Step 1: Read the current constructors**

Run: open `src/FoToolbox.Host/Plugins/PluginContext.cs` and `PluginContextWrite.cs` and note their constructor params and how `PluginManager` builds them.

- [ ] **Step 2: Add the capability to PluginContext**

Add fields + interface impl (mirror in `PluginContextWrite`):
```csharp
// new ctor params: DataIntegratorCredentialStore diStore, DataIntegratorTokenService diTokens
private readonly DataIntegratorCredentialStore _diStore;
private readonly DataIntegratorTokenService _diTokens;

public async Task<string> AcquireDataIntegratorTokenAsync(CancellationToken cancellationToken = default)
{
    var credential = await _diStore.GetAsync(CurrentEnv.Id, cancellationToken).ConfigureAwait(false);
    if (credential is null)
    {
        throw new FoToolbox.Core.DualWrite.Auth.DualWriteAuthException(
            "No Data Integrator credential configured for this profile. Set it in Profiles → Data Integrator.");
    }
    return await _diTokens.GetTokenAsync(credential, CurrentEnv.TenantId, cancellationToken).ConfigureAwait(false);
}
```
Add `IPluginContextDualWrite` to the class declaration's interface list. Update `PluginManager`/`AppBootstrapper`
to construct `new DataIntegratorCredentialStore(profileStore, vault)` and `new DataIntegratorTokenService(new MsalRopcTokenAcquirer())`
and pass them into each context. (A fresh `DataIntegratorTokenService` per context gives correct per-profile caching.)

- [ ] **Step 3: Build**

Run: `dotnet build ./FoToolbox.sln -c Release`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/FoToolbox.Host/Plugins/PluginContext.cs src/FoToolbox.Host/Plugins/PluginContextWrite.cs src/FoToolbox.Host/Plugins/PluginManager.cs src/FoToolbox.Host/AppBootstrapper.cs
git commit -m "feat(host): plugin context exposes Data Integrator token"
```

---

### Task 8: Host — Profiles "Data Integrator" section

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs`
- Modify: `src/FoToolbox.Host/Views/ProfilesView.xaml`
- Test: `tests/FoToolbox.Tests/ProfilesDataIntegratorTests.cs`

- [ ] **Step 1: Write the failing VM test**

```csharp
using System.IO;
using System.Threading.Tasks;
using FoToolbox.Host;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.Core.Profiles;
using Xunit;

namespace FoToolbox.Tests;

public class ProfilesDataIntegratorTests
{
    [Trait("Category", "DualWrite")]
    [Fact]
    public async Task SaveDataIntegrator_PersistsViaStore()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dip-{System.Guid.NewGuid():N}.db");
        try
        {
            var profiles = new ProfileStore(dbPath);
            await profiles.EnsureCreatedAsync();
            var vault = new SecretVaultService(profiles.ConnectionString);
            var store = new DataIntegratorCredentialStore(profiles, vault);

            await store.SaveAsync("env-1", new DataIntegratorCredential(DualWriteAuthConstants.ClientId, "svc@contoso.com", "pw"), default);
            var got = await store.GetAsync("env-1", default);

            Assert.Equal(DualWriteAuthConstants.ClientId, got!.ClientId); // default client id is 2e49aa60
        }
        finally { File.Delete(dbPath); }
    }
}
```

(The store is the testable unit; the WPF VM wiring is verified manually in Task 10. This test pins the
default client id contract.)

- [ ] **Step 2: Run to verify it passes after referencing the right symbols**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~ProfilesDataIntegratorTests"`
Expected: PASS (it exercises the store from Task 6).

- [ ] **Step 3: Add VM members**

In `ProfilesViewModel`, add a `DataIntegratorCredentialStore` field (built from the existing `_store`/`_vault`),
bindable properties `DiClientId` (default `DualWriteAuthConstants.ClientId`), `DiUsername`, `DiPassword`
(PasswordBox-backed pending value), a stored-status string, and `SaveDataIntegratorCommand` /
`ClearDataIntegratorCommand` that call `store.SaveAsync(Selected.Environment.Id, new DataIntegratorCredential(DiClientId, DiUsername, DiPassword))`
and clear the pending password. On `Selected` change, load existing via `store.GetAsync` and populate
`DiClientId`/`DiUsername` (never surface the stored password).

- [ ] **Step 4: Add the XAML section**

In `ProfilesView.xaml`, add a "Data Integrator (dual-write)" group (near the FO/CE sections) with: a
read-only note ("ROPC service-account sign-in for the Dual-write gateway; non-MFA accounts only"), a
`TextBox` bound to `DiClientId`, a `TextBox` bound to `DiUsername`, a `PasswordBox` (wire to `DiPassword`
via the existing password-binding pattern used for client secret/bearer fields), a stored-status
`TextBlock`, and **Save** / **Clear** buttons bound to the new commands.

- [ ] **Step 5: Build + run tests**

Run: `dotnet build ./FoToolbox.sln -c Release` and `dotnet test ./FoToolbox.sln -c Release --filter "Category=DualWrite"`
Expected: build 0 errors; all DualWrite tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs src/FoToolbox.Host/Views/ProfilesView.xaml tests/FoToolbox.Tests/ProfilesDataIntegratorTests.cs
git commit -m "feat(host): Profiles Data Integrator (ROPC) credential section"
```

---

### Task 9: Plugin — prefer the context token

**Files:**
- Modify: `plugins/DualWriteOperations/DualWriteOperationsViewModel.cs`
- Test: `tests/FoToolbox.Tests/DualWriteOperationsViewModelTests.cs`

The plugin should, in `BuildGateway`, use the context's Data Integrator token when the context supports
it AND a token is obtainable; otherwise fall back to the existing connection-token path (unchanged).

- [ ] **Step 1: Write the failing test**

Add to `DualWriteOperationsViewModelTests` a fake context implementing `IPluginContext` + `IPluginContextDualWrite`
whose `AcquireDataIntegratorTokenAsync` returns `"ctx-token"`, plus a `FakeFactory` that records which
create path was used. Assert that after `LoadMapsCommand`, the gateway was built via
`CreateWithTokenProvider` and the provider yields `"ctx-token"`.

```csharp
[Trait("Category", "DualWrite")]
[Fact]
public async Task LoadMaps_UsesContextDataIntegratorToken_WhenAvailable()
{
    var path = Path.Combine(Path.GetTempPath(), $"dwc-{System.Guid.NewGuid():N}.json");
    try
    {
        var store = new DualWriteConnectionStore(path, new PassthroughProtector());
        await store.SaveAsync(new DualWriteConnectionSettings("env-1",
            "https://projectmanagementservice.au-il102.gateway.prod.island.powerapps.com", "https://x.operations.dynamics.com", null), default);
        var gateway = new FakeGateway();
        var factory = new FakeFactory(gateway);
        var vm = new DualWriteOperationsViewModel(new DualWriteFakeContext("ctx-token"), store, factory);

        await vm.LoadMapsCommand.ExecuteAsync();

        Assert.True(factory.UsedTokenProvider);
        Assert.Equal("ctx-token", await factory.LastTokenProvider!(default));
    }
    finally { File.Delete(path); }
}
```
(Extend `FakeFactory` with `bool UsedTokenProvider` + `Func<CancellationToken,Task<string>>? LastTokenProvider`,
implementing the new `CreateWithTokenProvider`. Add a `DualWriteFakeContext : IPluginContext, IPluginContextDualWrite`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "FullyQualifiedName~LoadMaps_UsesContextDataIntegratorToken"`
Expected: FAIL — plugin still builds via the old path.

- [ ] **Step 3: Implement in `BuildGateway`**

```csharp
private IDualWriteGateway BuildGateway(DualWriteConnectionSettings settings)
{
    if (_ctx is IPluginContextDualWrite dw)
    {
        return _factory.CreateWithTokenProvider(settings.GatewayBaseUrl, ct => dw.AcquireDataIntegratorTokenAsync(ct));
    }

    // Fallback: existing connection-token / refresh path (unchanged).
    if (!settings.HasDelegatedSession) return _factory.Create(settings);
    return _factory.CreateRefreshing(settings, async refreshed => { /* existing persist callback */ });
}
```
Guard: only take the context path when a Data Integrator credential is configured — wrap the first
`AcquireDataIntegratorTokenAsync` call so a `DualWriteAuthException` (none configured) falls back to the
existing path with a clear status message. Simplest: attempt the context token lazily inside the provider;
if it throws "not configured", surface "Configure Data Integrator in Profiles, or paste a token."

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test ./FoToolbox.sln -c Release --filter "Category=DualWrite"`
Expected: PASS (new test + all existing DualWrite tests).

- [ ] **Step 5: Commit**

```bash
git add plugins/DualWriteOperations/DualWriteOperationsViewModel.cs tests/FoToolbox.Tests/DualWriteOperationsViewModelTests.cs
git commit -m "feat(dualwrite): plugin uses profile Data Integrator token when configured"
```

---

### Task 10: Manual end-to-end validation

**Files:** none (manual).

- [ ] **Step 1: Stage + run the app**

Run `install\build.ps1 -Configuration Debug` (or publish host + stage plugin), launch
`artifacts\FoToolbox\FoToolbox.Host.exe`.

- [ ] **Step 2: Configure ROPC credential**

Profiles → Data Integrator: client id `2e49aa60-…` (default), username/password of a **non-MFA service
account**, Save.

- [ ] **Step 3: Run an operation**

In Dual-write Operations: set the gateway URL (paste, or use Discover gateway once) + F&O identifier →
Load Maps. Expected: a token is acquired silently (no browser) and maps load (or `200 []` if the env
isn't dual-write-linked — server-side, per the spec).

- [ ] **Step 4: Confirm no regression for the existing path**

With no Data Integrator credential configured, the existing WebView2 sign-in path still works.

---

## Self-review

- **Spec coverage:** ROPC profile credential ✓ (Tasks 2,3,6,8); default client `2e49aa60` ✓ (existing
  `DualWriteAuthConstants.ClientId`, pinned in Task 8); `IPluginContextDualWrite` ✓ (Task 5,7); plugin
  consumes it ✓ (Task 9). Deferred (documented): interactive-WebView2 relocation, `ClusterDiscovery`,
  `AuthTarget.DataIntegrator` enum, Compare reuse — out of V1 scope by design.
- **Placeholders:** Task 7 and Task 8 steps 3–4 describe wiring against existing code whose exact
  signatures must be read first (`PluginContext`/`PluginManager` ctors; the Profiles password-binding
  pattern) — the executor must open those files before editing. Flagged inline; not free-floating TODOs.
- **Type consistency:** `DataIntegratorCredential(ClientId, Username, Password)`, `IDataIntegratorTokenAcquirer.AcquireAsync(authority, clientId, scope, username, password, ct)`,
  `DataIntegratorTokenService.GetTokenAsync(credential, tenantId, ct)`, `CreateWithTokenProvider(gatewayBaseUrl, getToken, innerHandler)`,
  `IPluginContextDualWrite.AcquireDataIntegratorTokenAsync(ct)` — consistent across tasks.
- **Risk gate:** Task 1 live-validates ROPC before any build investment.
