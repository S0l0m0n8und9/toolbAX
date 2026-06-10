# Unified Auth Broker (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One token-acquisition pipeline in `FoToolbox.Core` (`AuthBroker`) that every consumer — the WPF live request path, the WPF Profiles "Test connection" path, and the Avalonia `CoreAuthService` — calls, with MSAL interactive (delegated) as a first-class `AuthMode` that silently renews in the WPF live pipeline.

**Architecture:** A new `AuthBroker` class routes by `ServicePrincipal.AuthMode`: `Interactive` → existing `MsalInteractiveTokenProvider` (silent-first, browser fallback, prompts serialized by a semaphore); `ClientSecret`/`Certificate` → existing `AuthService` + `MsalTokenProvider` (now caching its MSAL confidential-client apps so repeat calls are silent); `BearerToken` → vault/env-var resolution with expiry check. Shared JWT/bearer helpers replace the three private copies. Credential resolution (vault → env var, tolerant of both stored payload shapes) moves into Core. All public SDK plugin contracts, profile DB rows, env vars (`FOTB_*`), and existing constructors keep working — changes are additive.

**Tech Stack:** .NET 10, MSAL (`Microsoft.Identity.Client`), SQLite + DPAPI vault, xUnit. Build/test per CLAUDE.md: `dotnet build .\FoToolbox.sln -c Release --no-restore`, `dotnet test .\FoToolbox.sln`. Remember `TreatWarningsAsErrors` is CI-only — build clean locally anyway.

**Out of scope (later phases):** per-audience status/traffic-light UI, schema for connection descriptors, migrating Avalonia's Settings-table auth keys, dual-write token storage migration. Dual-write portal sign-in (`WebView2DualWriteSignIn`, `DualWriteSignInCapture`) is untouched.

---

## File map

| File | Action | Responsibility |
|---|---|---|
| `src/FoToolbox.Core/Auth/JwtInspector.cs` | Create | Read-only JWT payload claims (`tid`, `exp`) |
| `src/FoToolbox.Core/Auth/BearerTokenText.cs` | Create | Normalize pasted bearer tokens |
| `src/FoToolbox.Core/Auth/SecretPayloads.cs` | Create | Public `ClientSecretPayload` / `BearerTokenPayload` (same JSON shape as today's private copies) |
| `src/FoToolbox.Core/Auth/VaultSecretReader.cs` | Create | Tolerant vault reads (typed payload OR raw string) |
| `src/FoToolbox.Core/Models/AuthMode.cs` | Modify | Add `Interactive = 3` |
| `src/FoToolbox.Core/Auth/MsalTokenProvider.cs` | Modify | Cache `IConfidentialClientApplication` per (clientId, authority, credential) |
| `src/FoToolbox.Core/Auth/AuthBroker.cs` | Create | The single routing entry point |
| `src/FoToolbox.Core/Auth/AuthService.cs` | Modify | Delegate `tid` extraction to `JwtInspector` |
| `src/FoToolbox.Host/AuthenticatedHandler.cs` | Modify | Thin handler over `AuthBroker`; delete private resolution/JWT code |
| `src/FoToolbox.Host/AppBootstrapper.cs` | Modify | One shared `AuthBroker` for both HttpClients |
| `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs` | Modify | Test path via broker; Core payload types; Interactive save branch; default mode Interactive |
| `src/FoToolbox.Host/Views/ProfilesView.xaml` | Modify | Hint text for Interactive mode |
| `avalonia/toolBax.App/Services/CoreAuthService.cs` | Modify | Delegate F&O/Dataverse acquisition to broker |
| `tests/FoToolbox.Tests/JwtInspectorTests.cs` | Create | |
| `tests/FoToolbox.Tests/VaultSecretReaderTests.cs` | Create | |
| `tests/FoToolbox.Tests/AuthBrokerTests.cs` | Create | |
| `tests/FoToolbox.Tests/AuthenticatedHandlerTests.cs` | Modify | Add interactive live-path test |

Existing types reused unchanged: `IInteractiveTokenProvider` / `InteractiveTokenRequest` / `InteractiveTokenResult` ([InteractiveTokenProvider.cs](../../../src/FoToolbox.Core/Auth/InteractiveTokenProvider.cs)), `MsalInteractiveTokenProvider`, `AuthService`, `AuthRecoveryException`, `SecretVaultService`, `ResourceUrlNormalizer`, `AuthReauthCoordinator`.

**Test JWT helper used throughout:** several tasks need a fake JWT. `AuthenticatedHandlerTests` already has a private `CreateJwtToken(DateTimeOffset)`; new test files define their own local copy (below) rather than sharing private members:

```csharp
private static string CreateJwt(DateTimeOffset expiresUtc, string? tenantId = null)
{
    static string B64Url(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var header = B64Url("{\"alg\":\"none\"}");
    var tid = tenantId is null ? "" : $",\"tid\":\"{tenantId}\"";
    var payload = B64Url($"{{\"exp\":{expiresUtc.ToUnixTimeSeconds()}{tid}}}");
    return $"{header}.{payload}.sig";
}
```

---

### Task 1: `JwtInspector` + `BearerTokenText` helpers; `AuthService` delegates to them

**Files:**
- Create: `src/FoToolbox.Core/Auth/JwtInspector.cs`
- Create: `src/FoToolbox.Core/Auth/BearerTokenText.cs`
- Modify: `src/FoToolbox.Core/Auth/AuthService.cs:92-133` (replace private helpers)
- Test: `tests/FoToolbox.Tests/JwtInspectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using FoToolbox.Core.Auth;
using System;
using Xunit;

namespace FoToolbox.Tests;

public class JwtInspectorTests
{
    // <paste the CreateJwt helper from the plan header here>

    [Fact]
    public void TryGetTenantId_Reads_Tid_Claim()
    {
        var jwt = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "11111111-2222-3333-4444-555555555555");
        Assert.True(JwtInspector.TryGetTenantId(jwt, out var tid));
        Assert.Equal("11111111-2222-3333-4444-555555555555", tid);
    }

    [Fact]
    public void TryGetTenantId_False_When_No_Tid()
    {
        var jwt = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(JwtInspector.TryGetTenantId(jwt, out _));
    }

    [Fact]
    public void TryGetExpiryUtc_Reads_Exp_Claim()
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
        var jwt = CreateJwt(expires);
        Assert.True(JwtInspector.TryGetExpiryUtc(jwt, out var exp));
        Assert.Equal(expires, exp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one")]
    public void TryGet_Handles_Garbage(string input)
    {
        Assert.False(JwtInspector.TryGetTenantId(input, out _));
        Assert.False(JwtInspector.TryGetExpiryUtc(input, out _));
    }

    [Theory]
    [InlineData("Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("  bearer abc ", "abc")]
    [InlineData("abc\r\ndef", "abcdef")]
    [InlineData("plain", "plain")]
    public void Normalize_Strips_Prefix_And_Whitespace(string input, string expected)
    {
        Assert.Equal(expected, BearerTokenText.Normalize(input));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~JwtInspectorTests"`
Expected: build FAILS — `JwtInspector` and `BearerTokenText` don't exist.

- [ ] **Step 3: Implement the helpers**

`src/FoToolbox.Core/Auth/JwtInspector.cs`:

```csharp
using System;
using System.Text;
using System.Text.Json;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Read-only JWT payload inspection (no signature validation) for the claims the toolbox needs
/// during auth setup: tenant (<c>tid</c>) and expiry (<c>exp</c>).
/// </summary>
public static class JwtInspector
{
    public static bool TryGetTenantId(string token, out string tenantId)
    {
        tenantId = string.Empty;
        if (!TryParsePayload(token, out var doc)) return false;
        using (doc)
        {
            if (doc!.RootElement.TryGetProperty("tid", out var tid) && tid.ValueKind == JsonValueKind.String)
            {
                tenantId = tid.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(tenantId);
            }
        }
        return false;
    }

    public static bool TryGetExpiryUtc(string token, out DateTimeOffset expiryUtc)
    {
        expiryUtc = default;
        if (!TryParsePayload(token, out var doc)) return false;
        using (doc)
        {
            if (doc!.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
            {
                expiryUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return true;
            }
        }
        return false;
    }

    private static bool TryParsePayload(string token, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.');
        if (parts.Length < 2) return false;

        try
        {
            var normalized = parts[1].Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2: normalized += "=="; break;
                case 3: normalized += "="; break;
                case 1: return false;
            }
            var bytes = Convert.FromBase64String(normalized);
            document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            return true;
        }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }
}
```

`src/FoToolbox.Core/Auth/BearerTokenText.cs`:

```csharp
using System;
using System.Text;

namespace FoToolbox.Core.Auth;

/// <summary>Normalizes a pasted bearer token: strips a "Bearer " prefix and all whitespace.</summary>
public static class BearerTokenText
{
    public static string Normalize(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Bearer ".Length..];
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~JwtInspectorTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Refactor `AuthService` to use `JwtInspector`**

In [AuthService.cs](../../../src/FoToolbox.Core/Auth/AuthService.cs), delete the private `TryExtractTokenTenant` (lines 92–120) and `DecodeBase64UrlToUtf8String` (lines 122–133), and change `ValidateTokenTenant` to:

```csharp
    public static void ValidateTokenTenant(string token, string expectedTenantId)
    {
        if (string.IsNullOrWhiteSpace(expectedTenantId))
        {
            return;
        }

        if (!JwtInspector.TryGetTenantId(token, out var tokenTenantId))
        {
            return;
        }

        if (!string.Equals(tokenTenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new TenantMismatchException(expectedTenantId, tokenTenantId);
        }
    }
```

Remove the now-unused `using System.Text;` and `using System.Text.Json;` from AuthService.cs if nothing else needs them.

- [ ] **Step 6: Run the full auth test category**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~JwtInspectorTests"`
Expected: PASS, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/FoToolbox.Core/Auth/JwtInspector.cs src/FoToolbox.Core/Auth/BearerTokenText.cs src/FoToolbox.Core/Auth/AuthService.cs tests/FoToolbox.Tests/JwtInspectorTests.cs
git commit -m "refactor(auth): shared JwtInspector + BearerTokenText helpers in Core"
```

---

### Task 2: Public secret payload types + tolerant `VaultSecretReader`

The WPF host stores client secrets as `{"Value":"…"}` (`ClientSecretPayload`) while the Avalonia `CoreSecretStore` stores them as a raw JSON string. Core must read both.

**Files:**
- Create: `src/FoToolbox.Core/Auth/SecretPayloads.cs`
- Create: `src/FoToolbox.Core/Auth/VaultSecretReader.cs`
- Test: `tests/FoToolbox.Tests/VaultSecretReaderTests.cs`

- [ ] **Step 1: Check `SecretVaultService.ReadSecretAsync<T>` failure behavior**

Read `src/FoToolbox.Core/Profiles/SecretVaultService.cs`. Confirm whether deserializing a mismatched payload shape throws `JsonException` or returns null. If it already catches and returns null, `VaultSecretReader` below can drop its own try/catch — adjust accordingly (the *order* of attempts is what matters).

- [ ] **Step 2: Write the failing tests**

```csharp
using FoToolbox.Core.Auth;
using FoToolbox.Core.Profiles;
using System;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class VaultSecretReaderTests
{
    private static SecretVaultService NewVault() =>
        new($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    [Fact]
    public async Task Reads_Typed_ClientSecretPayload()
    {
        var vault = NewVault();
        var secretRef = await vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = "s3cret" });
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    public async Task Reads_Raw_String_Secret_Avalonia_Shape()
    {
        var vault = NewVault();
        var secretRef = await vault.StoreSecretAsync("fo-client-secret", "s3cret");
        Assert.Equal("s3cret", await VaultSecretReader.ReadClientSecretAsync(vault, secretRef, default));
    }

    [Fact]
    public async Task Reads_BearerTokenPayload()
    {
        var vault = NewVault();
        var secretRef = await vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = "abc.def.ghi" });
        var payload = await VaultSecretReader.ReadBearerTokenAsync(vault, secretRef, default);
        Assert.Equal("abc.def.ghi", payload?.AccessToken);
    }

    [Fact]
    public async Task Returns_Null_For_Missing_Ref()
    {
        var vault = NewVault();
        Assert.Null(await VaultSecretReader.ReadClientSecretAsync(vault, Guid.NewGuid().ToString(), default));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~VaultSecretReaderTests"`
Expected: build FAILS — types don't exist.

- [ ] **Step 4: Implement**

`src/FoToolbox.Core/Auth/SecretPayloads.cs` — **property names must stay exactly `Value` / `AccessToken` / `ExpiresUtc`** so existing vault blobs written by the WPF host deserialize unchanged:

```csharp
namespace FoToolbox.Core.Auth;

/// <summary>Vault payload for a stored client secret (WPF host shape: <c>{"Value":"…"}</c>).</summary>
public sealed class ClientSecretPayload
{
    public string? Value { get; set; }
}

/// <summary>Vault payload for a stored bearer token.</summary>
public sealed class BearerTokenPayload
{
    public string? AccessToken { get; set; }
    public string? ExpiresUtc { get; set; }
}
```

`src/FoToolbox.Core/Auth/VaultSecretReader.cs`:

```csharp
using FoToolbox.Core.Profiles;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Reads credentials from the DPAPI vault tolerantly: the WPF host stores typed payloads
/// (<see cref="ClientSecretPayload"/>), the Avalonia host stores raw strings. Both must resolve.
/// </summary>
public static class VaultSecretReader
{
    public static async Task<string?> ReadClientSecretAsync(SecretVaultService vault, string secretRef, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await vault.ReadSecretAsync<ClientSecretPayload>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.Value)) return payload.Value;
        }
        catch (JsonException) { }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
        }
        catch (JsonException) { }

        return null;
    }

    public static async Task<BearerTokenPayload?> ReadBearerTokenAsync(SecretVaultService vault, string secretRef, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await vault.ReadSecretAsync<BearerTokenPayload>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken)) return payload;
        }
        catch (JsonException) { }

        try
        {
            var raw = await vault.ReadSecretAsync<string>(secretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw)) return new BearerTokenPayload { AccessToken = raw };
        }
        catch (JsonException) { }

        return null;
    }
}
```

(Adjust the try/catch per Step 1's finding.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~VaultSecretReaderTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/FoToolbox.Core/Auth/SecretPayloads.cs src/FoToolbox.Core/Auth/VaultSecretReader.cs tests/FoToolbox.Tests/VaultSecretReaderTests.cs
git commit -m "feat(auth): public secret payload types + tolerant vault reader in Core"
```

---

### Task 3: Add `AuthMode.Interactive`

`AuthMode` is persisted as TEXT via `ToString()`/`Enum.Parse` ([ProfileStore.cs:316,371](../../../src/FoToolbox.Core/Profiles/ProfileStore.cs)), so a new member round-trips with no migration.

**Files:**
- Modify: `src/FoToolbox.Core/Models/AuthMode.cs`
- Test: `tests/FoToolbox.Tests/AuthBrokerTests.cs` (started here, grown in Task 5)

- [ ] **Step 1: Write the failing round-trip test**

Create `tests/FoToolbox.Tests/AuthBrokerTests.cs`:

```csharp
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthBrokerTests
{
    [Fact]
    public async Task Interactive_AuthMode_RoundTrips_Through_ProfileStore()
    {
        var store = new ProfileStore($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();
        await svc.UpsertEnvironmentAsync(new FoEnvironment("env1", "Env", "https://contoso.operations.dynamics.com", "tenant", null));
        await svc.UpsertServicePrincipalAsync(new ServicePrincipal("sp1", "env1", "client-id", AuthMode.Interactive, null, null, AuthTarget.Fo));

        var loaded = await svc.GetServicePrincipalAsync("env1", AuthTarget.Fo);

        Assert.Equal(AuthMode.Interactive, loaded!.AuthMode);
    }
}
```

> If `ProfileStore`'s constructor or `ProfileService`'s upsert method names differ (check `src/FoToolbox.Core/Profiles/ProfileService.cs` for the exact upsert signatures), adapt the arrange block — the assertion is the point.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthBrokerTests"`
Expected: build FAILS — `AuthMode.Interactive` doesn't exist.

- [ ] **Step 3: Add the enum member**

```csharp
namespace FoToolbox.Core.Models;

public enum AuthMode
{
    ClientSecret = 0,
    Certificate = 1,
    BearerToken = 2,
    Interactive = 3
}
```

- [ ] **Step 4: Find every switch/branch on `AuthMode` and make Interactive explicit**

Run: `dotnet build .\FoToolbox.sln` and also `Grep "AuthMode\." src plugins avalonia` for branch sites. Known sites that need an explicit decision (handled in later tasks — for now just confirm they fail safe):
- `ProfilesViewModel.BuildStoredCredentialStatus` (switch, ~line 1018) — Task 7 adds the arm.
- `CoreAuthService.ResolveCredentialAsync` (avalonia) — already throws `NotSupportedException` for non-ClientSecret; fine until Task 9.
- `AuthenticatedHandler.SendAsync` — currently treats non-BearerToken as client-credentials, which would mis-route Interactive; Task 6 replaces this routing. Until Task 6 lands nothing creates Interactive principals in WPF, so this is safe in the interim.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthBrokerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FoToolbox.Core/Models/AuthMode.cs tests/FoToolbox.Tests/AuthBrokerTests.cs
git commit -m "feat(auth): first-class AuthMode.Interactive"
```

---

### Task 4: Cache MSAL confidential-client apps in `MsalTokenProvider`

Today every `GetTokenAsync` builds a fresh `ConfidentialClientApplication`, so **every HTTP request hits Entra** — no token caching at all on the client-credentials path. Reusing the app per (clientId, authority, credential) lets MSAL's built-in app token cache serve repeat calls silently. Keyed on a credential fingerprint so secret rotation creates a new entry.

**Files:**
- Modify: `src/FoToolbox.Core/Auth/MsalTokenProvider.cs`
- Modify: `src/FoToolbox.Core/FoToolbox.Core.csproj` (InternalsVisibleTo, if absent)
- Test: append to `tests/FoToolbox.Tests/AuthBrokerTests.cs`

- [ ] **Step 1: Ensure `FoToolbox.Tests` can see Core internals**

Run: `Grep "InternalsVisibleTo" src/FoToolbox.Core`. If absent, add to `FoToolbox.Core.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="FoToolbox.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Append to `AuthBrokerTests.cs`:

```csharp
    [Fact]
    public void MsalTokenProvider_Reuses_App_For_Same_Credential_And_Rebuilds_On_Rotation()
    {
        var provider = new FoToolbox.Core.Auth.MsalTokenProvider(
            "https://login.microsoftonline.com",
            (_, _) => Task.FromResult<FoToolbox.Core.Auth.ClientCredential>(new FoToolbox.Core.Auth.ClientSecretCredential("secret-1")));

        var sp = new ServicePrincipal("sp", "env", "client-id", AuthMode.ClientSecret, null, null);
        var app1 = provider.GetOrCreateApp(sp, "https://login.microsoftonline.com/tenant", new FoToolbox.Core.Auth.ClientSecretCredential("secret-1"));
        var app2 = provider.GetOrCreateApp(sp, "https://login.microsoftonline.com/tenant", new FoToolbox.Core.Auth.ClientSecretCredential("secret-1"));
        var app3 = provider.GetOrCreateApp(sp, "https://login.microsoftonline.com/tenant", new FoToolbox.Core.Auth.ClientSecretCredential("secret-2"));

        Assert.Same(app1, app2);
        Assert.NotSame(app1, app3);
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~MsalTokenProvider_Reuses_App"`
Expected: build FAILS — `GetOrCreateApp` doesn't exist.

- [ ] **Step 4: Implement caching**

In [MsalTokenProvider.cs](../../../src/FoToolbox.Core/Auth/MsalTokenProvider.cs), add fields/usings and extract app construction from `GetTokenAsync`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
```

```csharp
    private readonly ConcurrentDictionary<string, IConfidentialClientApplication> _apps = new();

    internal IConfidentialClientApplication GetOrCreateApp(ServicePrincipal principal, string authority, ClientCredential credential)
    {
        var fingerprint = credential switch
        {
            ClientSecretCredential s => Sha256Hex(s.Secret),
            ClientCertificateCredential c => c.Certificate.Thumbprint,
            _ => throw new InvalidOperationException("Unsupported credential type.")
        };
        var cacheKey = $"{principal.ClientId}|{authority}|{fingerprint}";

        return _apps.GetOrAdd(cacheKey, _ =>
        {
            var appBuilder = ConfidentialClientApplicationBuilder
                .Create(principal.ClientId)
                .WithAuthority(authority);

            appBuilder = credential switch
            {
                ClientSecretCredential secret => appBuilder.WithClientSecret(secret.Secret),
                ClientCertificateCredential cert => appBuilder.WithCertificate(cert.Certificate),
                _ => throw new InvalidOperationException("Unsupported credential type.")
            };

            return appBuilder.Build();
        });
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
```

Then inside `GetTokenAsync`'s try block, replace the app construction (lines 42–60) with:

```csharp
                var credential = await _credentialProvider(request.Principal, cancellationToken);
                var authority = $"{_authorityBase}/{request.TenantId}";
                var app = GetOrCreateApp(request.Principal, authority, credential);
```

(The `AcquireTokenForClient(...).WithSendX5C(true)` call and retry loop stay as they are.)

- [ ] **Step 5: Run tests**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthBrokerTests|FullyQualifiedName~AuthServiceTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/FoToolbox.Core/Auth/MsalTokenProvider.cs src/FoToolbox.Core/FoToolbox.Core.csproj tests/FoToolbox.Tests/AuthBrokerTests.cs
git commit -m "perf(auth): cache MSAL confidential-client apps per credential"
```

---### Task 5: `AuthBroker` — the single routing entry point

**Files:**
- Create: `src/FoToolbox.Core/Auth/AuthBroker.cs`
- Test: append to `tests/FoToolbox.Tests/AuthBrokerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `AuthBrokerTests.cs` (add the `CreateJwt` helper from the plan header, plus the fake):

```csharp
    private sealed class FakeInteractiveProvider : FoToolbox.Core.Auth.IInteractiveTokenProvider
    {
        public FoToolbox.Core.Auth.InteractiveTokenRequest? LastRequest;
        public string Token = "";
        public Task<FoToolbox.Core.Auth.InteractiveTokenResult> AcquireTokenAsync(
            FoToolbox.Core.Auth.InteractiveTokenRequest request, System.Threading.CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new FoToolbox.Core.Auth.InteractiveTokenResult(Token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private static FoToolbox.Core.Profiles.SecretVaultService NewVault() =>
        new($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    [Fact]
    public async Task Interactive_Mode_Routes_To_Interactive_Provider_With_Sp_ClientId()
    {
        var fake = new FakeInteractiveProvider { Token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "tenant-1") };
        var broker = new FoToolbox.Core.Auth.AuthBroker(NewVault(), fake);
        var sp = new ServicePrincipal("sp", "env", "public-client-id", AuthMode.Interactive, null, null);

        var token = await broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
            "https://contoso.operations.dynamics.com", "tenant-1", sp));

        Assert.Equal(fake.Token, token);
        Assert.Equal("public-client-id", fake.LastRequest!.ClientId);
        Assert.Equal("tenant-1", fake.LastRequest.TenantId);
        Assert.Equal("https://contoso.operations.dynamics.com", fake.LastRequest.ResourceBaseUrl);
    }

    [Fact]
    public async Task Interactive_Mode_Rejects_Cross_Tenant_Token()
    {
        var fake = new FakeInteractiveProvider { Token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "other-tenant") };
        var broker = new FoToolbox.Core.Auth.AuthBroker(NewVault(), fake);
        var sp = new ServicePrincipal("sp", "env", "public-client-id", AuthMode.Interactive, null, null);

        await Assert.ThrowsAsync<FoToolbox.Core.Auth.TenantMismatchException>(() =>
            broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
                "https://contoso.operations.dynamics.com", "tenant-1", sp)));
    }

    [Fact]
    public async Task BearerToken_Mode_Resolves_Pending_Token_First()
    {
        var broker = new FoToolbox.Core.Auth.AuthBroker(NewVault(), new FakeInteractiveProvider());
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.BearerToken, null, null);
        var pending = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(30));

        var token = await broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
            "https://contoso.operations.dynamics.com", "tenant-1", sp, PendingBearerToken: $"Bearer {pending}"));

        Assert.Equal(pending, token); // normalized: prefix stripped
    }

    [Fact]
    public async Task BearerToken_Mode_Reads_Vault_Then_EnvVar_And_Rejects_Expired()
    {
        var vault = NewVault();
        var broker = new FoToolbox.Core.Auth.AuthBroker(vault, new FakeInteractiveProvider());
        var expired = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(-5));
        var secretRef = await vault.StoreSecretAsync("BearerToken", new FoToolbox.Core.Auth.BearerTokenPayload { AccessToken = expired });
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.BearerToken, secretRef, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
                "https://contoso.operations.dynamics.com", "tenant-1", sp)));

        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BearerToken_Mode_Falls_Back_To_Target_Specific_EnvVar()
    {
        var broker = new FoToolbox.Core.Auth.AuthBroker(NewVault(), new FakeInteractiveProvider());
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.BearerToken, null, null, AuthTarget.Dataverse);
        var fresh = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(30));
        Environment.SetEnvironmentVariable("FOTB_CE_BEARER_TOKEN", fresh);
        try
        {
            var token = await broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
                "https://contoso.crm.dynamics.com", "tenant-1", sp));
            Assert.Equal(fresh, token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_CE_BEARER_TOKEN", null);
        }
    }

    [Fact]
    public async Task ClientSecret_Mode_Without_Any_Credential_Throws_Actionable_Message()
    {
        var broker = new FoToolbox.Core.Auth.AuthBroker(NewVault(), new FakeInteractiveProvider());
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, null, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broker.AcquireTokenAsync(new FoToolbox.Core.Auth.AuthTokenRequest(
                "https://contoso.operations.dynamics.com", "tenant-1", sp)));

        Assert.Contains("FOTB_CLIENT_SECRET", ex.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthBrokerTests"`
Expected: build FAILS — `AuthBroker`/`AuthTokenRequest` don't exist.

- [ ] **Step 3: Implement `AuthBroker`**

`src/FoToolbox.Core/Auth/AuthBroker.cs`:

```csharp
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Inputs for one token acquisition. Pending* values let the Profiles "Test connection" path test
/// credentials the user has typed but not yet saved — the live path leaves them null.
/// </summary>
public sealed record AuthTokenRequest(
    string ResourceBaseUrl,
    string TenantId,
    ServicePrincipal Principal,
    string ServiceName = "service",
    string? PendingClientSecret = null,
    string? PendingBearerToken = null);

/// <summary>
/// The single token-acquisition pipeline. Routes by <see cref="ServicePrincipal.AuthMode"/>:
/// Interactive → delegated MSAL (silent-first, browser fallback); ClientSecret/Certificate →
/// client-credentials via <see cref="AuthService"/>; BearerToken → vault/env-var resolution.
/// Both the live request path and "Test connection" must call this so they can never diverge.
/// </summary>
public sealed class AuthBroker
{
    private readonly SecretVaultService _vault;
    private readonly IInteractiveTokenProvider _interactive;
    private readonly string _authorityBase;
    private readonly Action<AuthRecoveryException>? _interactiveFallback;
    private readonly MsalTokenProvider _clientCredentialProvider;
    private readonly SemaphoreSlim _interactiveGate = new(1, 1);

    public AuthBroker(
        SecretVaultService vault,
        IInteractiveTokenProvider? interactiveProvider = null,
        string authorityBase = "https://login.microsoftonline.com",
        Action<AuthRecoveryException>? interactiveFallback = null)
    {
        _vault = vault;
        _interactive = interactiveProvider ?? new MsalInteractiveTokenProvider();
        _authorityBase = authorityBase.TrimEnd('/');
        _interactiveFallback = interactiveFallback;
        _clientCredentialProvider = new MsalTokenProvider(_authorityBase, ResolveStoredCredentialAsync);
    }

    public Task<string> AcquireTokenAsync(AuthTokenRequest request, CancellationToken cancellationToken = default) =>
        request.Principal.AuthMode switch
        {
            AuthMode.Interactive => AcquireInteractiveAsync(request, cancellationToken),
            AuthMode.BearerToken => ResolveBearerAsync(request, cancellationToken),
            _ => AcquireClientCredentialAsync(request, cancellationToken),
        };

    private async Task<string> AcquireInteractiveAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Principal.ClientId))
        {
            throw new InvalidOperationException(
                $"No client ID is configured for interactive sign-in to {request.ServiceName}. Set a public-client Application (client) ID in Profiles.");
        }

        // Serialize interactive acquisitions: concurrent requests (e.g. several plugins loading at
        // once) must not each open a browser. The first acquisition populates the MSAL cache; the
        // rest then complete silently.
        await _interactiveGate.WaitAsync(cancellationToken);
        try
        {
            var result = await _interactive.AcquireTokenAsync(
                new InteractiveTokenRequest(request.Principal.ClientId, request.TenantId, request.ResourceBaseUrl, _authorityBase),
                cancellationToken);
            AuthService.ValidateTokenTenant(result.AccessToken, request.TenantId);
            return result.AccessToken;
        }
        finally
        {
            _interactiveGate.Release();
        }
    }

    private async Task<string> AcquireClientCredentialAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        // A pending (typed-but-unsaved) secret short-circuits stored resolution: that is what the
        // Test button must exercise. The transient provider is fine here — test calls are rare.
        var provider = string.IsNullOrWhiteSpace(request.PendingClientSecret)
            ? _clientCredentialProvider
            : new MsalTokenProvider(_authorityBase, (_, _) =>
                Task.FromResult<ClientCredential>(new ClientSecretCredential(request.PendingClientSecret!)));

        var auth = new AuthService(provider, request.ServiceName, _interactiveFallback);
        return await auth.AcquireTokenAsync(request.ResourceBaseUrl, request.TenantId, request.Principal, cancellationToken);
    }

    private async Task<ClientCredential> ResolveStoredCredentialAsync(ServicePrincipal sp, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var secret = await VaultSecretReader.ReadClientSecretAsync(_vault, sp.SecretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secret))
            {
                return new ClientSecretCredential(secret);
            }
        }

        var envVar = ClientSecretEnvVar(sp.Target);
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return new ClientSecretCredential(fromEnv);
        }

        throw new InvalidOperationException(
            $"No client secret configured for this profile. Set it in Profiles and Save, or set {envVar}.");
    }

    private async Task<string> ResolveBearerAsync(AuthTokenRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PendingBearerToken))
        {
            return ValidateBearer(BearerTokenText.Normalize(request.PendingBearerToken), "Pending bearer token");
        }

        if (!string.IsNullOrWhiteSpace(request.Principal.SecretRef))
        {
            var payload = await VaultSecretReader.ReadBearerTokenAsync(_vault, request.Principal.SecretRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                return ValidateBearer(BearerTokenText.Normalize(payload.AccessToken), "Bearer token");
            }
        }

        var envVar = BearerTokenEnvVar(request.Principal.Target);
        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return ValidateBearer(BearerTokenText.Normalize(fromEnv), envVar);
        }

        throw new InvalidOperationException(
            $"No bearer token configured for this profile. Paste a token in Profiles and Save, or set {envVar}.");
    }

    private static string ValidateBearer(string token, string sourceLabel)
    {
        if (JwtInspector.TryGetExpiryUtc(token, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException($"{sourceLabel} expired at {expiryUtc:u}. Update it in Profiles.");
        }
        return token;
    }

    internal static string ClientSecretEnvVar(AuthTarget target) =>
        target == AuthTarget.Dataverse ? "FOTB_CE_CLIENT_SECRET" : "FOTB_CLIENT_SECRET";

    internal static string BearerTokenEnvVar(AuthTarget target) =>
        target == AuthTarget.Dataverse ? "FOTB_CE_BEARER_TOKEN" : "FOTB_BEARER_TOKEN";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthBrokerTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/FoToolbox.Core/Auth/AuthBroker.cs tests/FoToolbox.Tests/AuthBrokerTests.cs
git commit -m "feat(auth): AuthBroker - single token-acquisition pipeline in Core"
```

---

### Task 6: WPF `AuthenticatedHandler` + `AppBootstrapper` route through the broker

Keep the existing public behavior (401 → `AuthRecoveryException`, expired-bearer recovery message, coordinator notification) and the existing constructors so `AuthenticatedHandlerTests` and any other caller keep compiling. Add a constructor that accepts a shared broker; `AppBootstrapper` uses it so both HttpClients share one MSAL cache.

**Files:**
- Modify: `src/FoToolbox.Host/AuthenticatedHandler.cs`
- Modify: `src/FoToolbox.Host/AppBootstrapper.cs:26-41,103-111`
- Test: `tests/FoToolbox.Tests/AuthenticatedHandlerTests.cs`

- [ ] **Step 1: Write the failing interactive live-path test**

Append to `AuthenticatedHandlerTests.cs` (it already has `CreateJwtToken`; if its signature lacks a tenant parameter, add the local `CreateJwt` helper from the plan header instead):

```csharp
    [Trait("Category", "Auth")]
    [Fact]
    public async Task SendAsync_Interactive_Mode_Attaches_Delegated_Token()
    {
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "contoso-tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "public-client-id", AuthMode.Interactive, null, null);
        var fakeToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "contoso-tenant");
        var broker = new AuthBroker(
            new SecretVaultService($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared"),
            new FakeInteractiveProvider(fakeToken));

        string? observedAuthHeader = null;
        var handler = new AuthenticatedHandler(env, sp, broker, new AuthReauthCoordinator())
        {
            InnerHandler = new CapturingHandler(req => observedAuthHeader = req.Headers.Authorization?.ToString())
        };

        using var http = new HttpClient(handler);
        var response = await http.GetAsync("https://contoso.operations.dynamics.com/data");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal($"Bearer {fakeToken}", observedAuthHeader);
    }

    private sealed class FakeInteractiveProvider : IInteractiveTokenProvider
    {
        private readonly string _token;
        public FakeInteractiveProvider(string token) => _token = token;
        public Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InteractiveTokenResult(_token, DateTimeOffset.UtcNow.AddHours(1)));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _onRequest;
        public CapturingHandler(Action<HttpRequestMessage> onRequest) => _onRequest = onRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _onRequest(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~SendAsync_Interactive_Mode"`
Expected: build FAILS — no `AuthenticatedHandler` constructor takes a broker.

- [ ] **Step 3: Rewrite `AuthenticatedHandler` over the broker**

Replace the body of [AuthenticatedHandler.cs](../../../src/FoToolbox.Host/AuthenticatedHandler.cs) with:

```csharp
using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Host;

/// <summary>
/// Delegating handler that injects a bearer token acquired through the shared <see cref="AuthBroker"/>.
/// All token-acquisition policy (mode routing, credential resolution, caching) lives in the broker —
/// this class only maps failures to <see cref="AuthRecoveryException"/> and notifies the coordinator.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
    private readonly AuthReauthCoordinator? _reauthCoordinator;
    private readonly AuthBroker _broker;
    private readonly AuthTokenRequest _request;
    private readonly string _serviceName;

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp, SecretVaultService vault, AuthReauthCoordinator? reauthCoordinator = null)
        : this(env, sp, new AuthBroker(vault), reauthCoordinator)
    {
    }

    public AuthenticatedHandler(string resourceBaseUrl, string tenantId, ServicePrincipal sp, SecretVaultService vault, AuthReauthCoordinator? reauthCoordinator = null)
        : this(resourceBaseUrl, tenantId, sp, new AuthBroker(vault), reauthCoordinator)
    {
    }

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp, AuthBroker broker, AuthReauthCoordinator? reauthCoordinator = null)
        : this(ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl), env.TenantId, sp, broker, reauthCoordinator)
    {
    }

    public AuthenticatedHandler(string resourceBaseUrl, string tenantId, ServicePrincipal sp, AuthBroker broker, AuthReauthCoordinator? reauthCoordinator = null)
        : base(new HttpClientHandler())
    {
        _reauthCoordinator = reauthCoordinator;
        _broker = broker;
        _serviceName = sp.Target == AuthTarget.Dataverse ? "Dataverse" : "Finance and Operations";
        _request = new AuthTokenRequest(resourceBaseUrl, tenantId, sp, _serviceName);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = await _broker.AcquireTokenAsync(_request, cancellationToken);
        }
        catch (Exception ex) when (TryCreateRecoveryException(ex, out var recovery))
        {
            _reauthCoordinator?.Notify(recovery!);
            throw recovery!;
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            var recovery = new AuthRecoveryException(
                _serviceName,
                $"{_serviceName} needs you to sign in again. The host will switch to Profiles so you can complete interactive re-authentication for this environment, then save and re-apply the profile.",
                requiresInteractiveReauth: true);
            _reauthCoordinator?.Notify(recovery);
            throw recovery;
        }

        return response;
    }

    private bool TryCreateRecoveryException(Exception exception, out AuthRecoveryException? recovery)
    {
        if (exception is AuthRecoveryException authRecovery)
        {
            recovery = authRecovery;
            return true;
        }

        if (_request.Principal.AuthMode == AuthMode.BearerToken &&
            exception is InvalidOperationException invalidOperation &&
            invalidOperation.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            recovery = new AuthRecoveryException(
                _serviceName,
                $"{_serviceName} bearer token has expired. The host will switch to Profiles so you can acquire a fresh token for this environment, then save and re-apply the profile.",
                requiresInteractiveReauth: true,
                exception);
            return true;
        }

        recovery = null;
        return false;
    }
}
```

Note what was deleted: `ResolveCredentialAsync`, `ResolveBearerTokenAsync`, `NormalizeBearerToken`, `TryGetJwtExpiryUtc`, `Base64UrlDecode`, the private payload classes, and `BuildTokenProvider`/`NotifyInteractiveFallback` (the broker's `interactiveFallback` ctor arg is unused here because the handler already maps `AuthRecoveryException` in `TryCreateRecoveryException` — the `AuthService` inside the broker still raises it).

> One intentional behavior change to verify in Step 4's run: the old vault-secret path read `ClientSecretPayload` only; the broker's `VaultSecretReader` also accepts raw-string payloads. Strictly more permissive — no existing secret stops working.

- [ ] **Step 4: Wire `AppBootstrapper` to one shared broker**

In [AppBootstrapper.cs](../../../src/FoToolbox.Host/AppBootstrapper.cs):

```csharp
    private readonly AuthBroker _authBroker;
```

In the constructor, after `_vault = new SecretVaultService(store.ConnectionString);`:

```csharp
        _authBroker = new AuthBroker(_vault, interactiveFallback: ex => _reauthCoordinator.Notify(ex));
```

(`_reauthCoordinator` is assigned on the previous line — keep the order: coordinator first, then broker.)

And change the two factory methods (lines 103–111) to:

```csharp
    private HttpClient CreateAuthenticatedHttpClient(FoEnvironment env, ServicePrincipal sp)
    {
        return new HttpClient(new AuthenticatedHandler(env, sp, _authBroker, _reauthCoordinator));
    }

    private HttpClient CreateAuthenticatedHttpClient(string resourceBaseUrl, string tenantId, ServicePrincipal sp)
    {
        return new HttpClient(new AuthenticatedHandler(resourceBaseUrl, tenantId, sp, _authBroker, _reauthCoordinator));
    }
```

Add `using FoToolbox.Core.Auth;` if not present (it is — line 1).

- [ ] **Step 5: Run the existing + new handler tests**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~AuthenticatedHandlerTests"`
Expected: PASS — the two pre-existing tests (vault-ctor overload, env-var bearer paths) and the new interactive test.

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build .\FoToolbox.sln`
Expected: SUCCESS, zero warnings introduced.

- [ ] **Step 7: Commit**

```bash
git add src/FoToolbox.Host/AuthenticatedHandler.cs src/FoToolbox.Host/AppBootstrapper.cs tests/FoToolbox.Tests/AuthenticatedHandlerTests.cs
git commit -m "feat(host): live request path acquires tokens via shared AuthBroker (interactive silent renewal)"
```

---

### Task 7: WPF Profiles "Test connection" path through the same broker

This kills the test-vs-live divergence: `AcquireTokenForTestAsync` stops building its own transient providers.

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs` (lines 49, 464, 522, 735–763, 970–1010, plus deletions)
- Test: `tests/FoToolbox.Tests/ProfilesViewModelAuthValidationTests.cs` (run, adapt if it stubs removed members)

- [ ] **Step 1: Add an injectable broker to the ViewModel**

Next to the existing `InteractiveTokenProvider` test seam (line 49):

```csharp
    internal IInteractiveTokenProvider InteractiveTokenProvider { get; set; } = new MsalInteractiveTokenProvider();

    private AuthBroker? _broker;
    /// <summary>Lazily built so tests that swap <see cref="InteractiveTokenProvider"/> get a broker using their fake.</summary>
    internal AuthBroker Broker
    {
        get => _broker ??= new AuthBroker(_vault, InteractiveTokenProvider);
        set => _broker = value;
    }
```

- [ ] **Step 2: Replace `AcquireTokenForTestAsync` (lines 735–763)**

```csharp
    private async Task<string> AcquireTokenForTestAsync(
        string baseUrl,
        string tenantId,
        ServicePrincipal sp,
        string? pendingBearerToken,
        string? pendingClientSecret,
        AuthTarget target)
    {
        if (sp.AuthMode != AuthMode.BearerToken &&
            sp.AuthMode != AuthMode.Interactive &&
            (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sp.ClientId)))
        {
            throw new InvalidOperationException("Tenant ID and Client ID are required to test this auth mode.");
        }

        var resourceBase = NormalizeResourceBaseUrl(target, baseUrl);
        var request = new AuthTokenRequest(
            resourceBase,
            tenantId,
            sp,
            ServiceName: target == AuthTarget.Fo ? "Finance and Operations" : "Dataverse",
            PendingClientSecret: pendingClientSecret,
            PendingBearerToken: pendingBearerToken);

        return await Broker.AcquireTokenAsync(request, CancellationToken.None);
    }
```

Update the two call sites to drop the env-var-name arguments (the broker derives them from `sp.Target`):

Line 464: `var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingFoBearerToken, PendingFoClientSecret, AuthTarget.Fo);`
Line 522: `var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingCeBearerToken, PendingCeClientSecret, AuthTarget.Dataverse);`

- [ ] **Step 3: Delete the superseded private members and use Core types**

In ProfilesViewModel.cs:
- Delete `ResolveCredentialForTestAsync` and `ResolveBearerTokenForTestAsync` (the only caller was the old `AcquireTokenForTestAsync` — confirm with `Grep "ResolveCredentialForTestAsync|ResolveBearerTokenForTestAsync" src`).
- Delete the private `NormalizeBearerToken` and `TryGetJwtExpiryUtc` methods; replace every remaining call with `BearerTokenText.Normalize(...)` and `JwtInspector.TryGetExpiryUtc(...)` (call sites include `StoreAcquiredBearerTokenAsync` lines 700/711 and `PersistPrincipalCredentials` lines 992–993; find all with `Grep "NormalizeBearerToken|TryGetJwtExpiryUtc" src/FoToolbox.Host`).
- Delete the private nested `ClientSecretPayload` and `BearerTokenPayload` classes (~line 1169); the file already has `using FoToolbox.Core.Auth;` for the Core versions — verify with the compiler. **The JSON property names are identical (`Value`, `AccessToken`, `ExpiresUtc`), so previously stored vault secrets keep deserializing.**

- [ ] **Step 4: Run the Profiles test suites**

Run: `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj --filter "FullyQualifiedName~ProfilesViewModel"`
Expected: PASS. If `ProfilesViewModelAuthValidationTests` or `ProfilesViewModelInteractiveAuthTests` reference deleted members, update them to assert through `Broker` (set `vm.Broker = new AuthBroker(vault, fakeInteractiveProvider)`).

- [ ] **Step 5: Commit**

```bash
git add src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs tests/FoToolbox.Tests/ProfilesViewModelAuthValidationTests.cs tests/FoToolbox.Tests/ProfilesViewModelInteractiveAuthTests.cs
git commit -m "refactor(host): Test connection uses the same AuthBroker as the live path"
```

---

### Task 8: Interactive mode in the WPF Profiles UI

`AuthModeValues` is `Enum.GetValues(typeof(AuthMode))` (ProfilesViewModel.cs:43), so `Interactive` automatically appears in both Auth-mode ComboBoxes, and the Client ID row is always visible (ProfilesView.xaml:223–225, 300–302) — the mode is usable with zero XAML changes. This task adds the save-branch, the stored-credential status line, a hint text, and flips the default for new principals.

**Files:**
- Modify: `src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs` (`PersistPrincipalCredentials` ~line 965, `BuildStoredCredentialStatus` ~line 1018, `ServicePrincipalEditor` ~line 1278)
- Modify: `src/FoToolbox.Host/Views/ProfilesView.xaml`

- [ ] **Step 1: Add the Interactive branch to `PersistPrincipalCredentials`**

After the `BearerToken` branch (line 1010), add:

```csharp
        else if (principal.AuthMode == AuthMode.Interactive)
        {
            // Interactive needs only the public-client ID; tokens live in the MSAL cache, not the vault.
            principal = principal with { CertThumbprint = null, SecretRef = null };
        }
```

- [ ] **Step 2: Add the Interactive arm to `BuildStoredCredentialStatus`**

Read the switch at ~line 1018 and add (before the default/discard arm):

```csharp
            AuthMode.Interactive => "Interactive sign-in — tokens are acquired in your browser when needed and renewed silently.",
```

- [ ] **Step 3: Default new principals to Interactive**

In `ServicePrincipalEditor` (ProfilesViewModel.cs:1278), find the `AuthMode` property's backing field and initialize it: `private AuthMode _authMode = AuthMode.Interactive;` (adapt to the actual field name). Then run `Grep "new ServicePrincipalEditor" src/FoToolbox.Host` — if any creation site explicitly sets `AuthMode = AuthMode.ClientSecret` for a *new* (not loaded-from-DB) principal, change it to `AuthMode.Interactive`. Loaded principals keep their stored mode — only blank editors change.

- [ ] **Step 4: Add Interactive hint styles + text to the XAML**

In `ProfilesView.xaml` resources (after the `VisibleWhenCeCertificate` style, line 61), add:

```xml
        <Style x:Key="VisibleWhenFoInteractive" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.FoPrincipal.AuthMode}" Value="{x:Static models:AuthMode.Interactive}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
        <Style x:Key="VisibleWhenCeInteractive" TargetType="{x:Type FrameworkElement}">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding Selected.DataversePrincipal.AuthMode}" Value="{x:Static models:AuthMode.Interactive}">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
```

Below each Auth-mode ComboBox (`FoAuthModeComboBox` line 228, `CeAuthModeComboBox` line 305) add a hint row inside the same Grid (pick the next free row index in that Grid and mirror the row-definition pattern used by its siblings):

```xml
        <TextBlock Style="{StaticResource VisibleWhenFoInteractive}"
                   Grid.Column="1" TextWrapping="Wrap" Opacity="0.75"
                   Text="Signs you in via your browser when a tool first needs access, then renews silently. Requires a public-client app registration with an http://localhost redirect. No secret is stored." />
```

(and the `VisibleWhenCeInteractive` twin under the CE ComboBox; set `Grid.Row` to the row you added).

- [ ] **Step 5: Build + run the UI binding-error harness**

Run: `dotnet build .\FoToolbox.sln` then `dotnet test .\tests\FoToolbox.UiTests\FoToolbox.UiTests.csproj`
Expected: build SUCCESS; UiTests PASS (the offscreen harness fails on XAML binding errors — this catches a bad trigger/row).

- [ ] **Step 6: Manual smoke check**

Run the app from `src/FoToolbox.Host`. In Profiles: select a profile → Auth mode ComboBox now lists `Interactive`; choosing it shows the hint and hides nothing it shouldn't; Save succeeds; "Test connection" with a valid public client ID opens the system browser (or completes silently if a cached session exists).

- [ ] **Step 7: Commit**

```bash
git add src/FoToolbox.Host/ViewModels/ProfilesViewModel.cs src/FoToolbox.Host/Views/ProfilesView.xaml
git commit -m "feat(host): Interactive auth mode in Profiles UI, default for new principals"
```

---

### Task 9: Avalonia `CoreAuthService` delegates to the broker

`CoreAuthService` keeps its public `IAuthService` surface and its friendly pre-validation messages; only the acquisition internals change. The dual-write path keeps using the interactive provider directly (unchanged), sharing the same provider instance with the broker.

**Files:**
- Modify: `avalonia/toolBax.App/Services/CoreAuthService.cs`

- [ ] **Step 1: Replace the private acquisition machinery**

Replace the `_provider`/`_auth` fields and both client-credentials blocks with a broker. The full new shape of the class internals:

```csharp
    private AuthBroker? _broker;
    private IInteractiveTokenProvider? _interactive;

    private AuthBroker Broker => _broker ??= new AuthBroker(_vault, Interactive, _authorityBase);
    private IInteractiveTokenProvider Interactive => _interactive ??= new MsalInteractiveTokenProvider();
```

`AcquireFoTokenAsync` becomes (pre-validation unchanged):

```csharp
    public async Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Tenant))
        {
            throw new InvalidOperationException("No tenant ID is configured for this environment.");
        }

        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("No F&O environment URL is configured.");
        }

        var resourceBase = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.Url);

        if (env.AuthMode == FoAuthMode.Interactive)
        {
            if (string.IsNullOrWhiteSpace(env.ClientId))
            {
                throw new InvalidOperationException("No F&O client ID is configured for interactive sign-in.");
            }

            var interactiveSp = new ServicePrincipal($"interactive-fo-{env.Id}", env.Id, env.ClientId, AuthMode.Interactive, null, null, AuthTarget.Fo);
            return await Broker.AcquireTokenAsync(new AuthTokenRequest(resourceBase, env.Tenant, interactiveSp, "F&O"), ct).ConfigureAwait(false);
        }

        var sp = await _profiles.GetServicePrincipalAsync(env.Id, AuthTarget.Fo, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No F&O service principal is configured (set a client ID on the FO Environment tab).");
        if (string.IsNullOrEmpty(sp.SecretRef))
        {
            throw new InvalidOperationException("No client secret is stored for this environment.");
        }

        return await Broker.AcquireTokenAsync(new AuthTokenRequest(resourceBase, env.Tenant, sp, "F&O"), ct).ConfigureAwait(false);
    }
```

`AcquireDataverseTokenAsync` follows the same pattern with `ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(env.DataverseUrl)`, `env.DataverseAuthMode`, `env.DataverseClientId`, `AuthTarget.Dataverse`, service name `"Dataverse"`, sp-missing message `"No Dataverse service principal is configured (set a Dataverse client ID on the CE/Dataverse tab)."`, secret-missing message `"No client secret is stored for the Dataverse app registration."`, and interactive-clientId-missing message `"No Dataverse client ID is configured for interactive sign-in."`.

`AcquireDualWriteTokenAsync` keeps its current body but acquires through the shared `Interactive` property instead of the old `_interactive ??= new MsalInteractiveTokenProvider();` line (delete `AcquireInteractiveTokenAsync` once both F&O/Dataverse routes go through the broker — the dual-write path inlines it):

```csharp
        var result = await Interactive
            .AcquireTokenAsync(new InteractiveTokenRequest(clientId, env.Tenant, DualWriteAuthConstants.ResourceBaseUrl, _authorityBase), ct)
            .ConfigureAwait(false);
        return result.AccessToken;
```

Delete `ResolveCredentialAsync` (the broker's resolver replaces it). **Behavior note:** the old resolver threw `NotSupportedException` for `Certificate` mode; the broker supports certificates only via a vault-stored secret path it doesn't have for certs — Avalonia profiles can't create Certificate principals today (only Interactive/ClientSecret per `FoAuthMode`), so nothing regresses. The raw-string secret shape `CoreSecretStore` writes is covered by `VaultSecretReader` (Task 2 test).

- [ ] **Step 2: Build and run the Avalonia test suite**

Run: `dotnet build .\avalonia\toolBax.slnx` then `dotnet test .\avalonia\toolBax.slnx`
Expected: build SUCCESS; all headless tests PASS (CoreAuthService itself is Windows-only and faked in tests, so failures here mean a compile-level break, not behavior).

- [ ] **Step 3: Commit**

```bash
git add avalonia/toolBax.App/Services/CoreAuthService.cs
git commit -m "refactor(avalonia): CoreAuthService acquires F&O/Dataverse tokens via shared AuthBroker"
```

---

### Task 10: Full verification

- [ ] **Step 1: Full build + test, main solution**

```powershell
dotnet restore .\FoToolbox.sln
dotnet build  .\FoToolbox.sln -c Release --no-restore
dotnet test   .\FoToolbox.sln -c Release --no-build
```
Expected: SUCCESS / all PASS. CI has `TreatWarningsAsErrors` — fix any new warnings now, locally green is not enough.

- [ ] **Step 2: Full build + test, Avalonia solution**

```powershell
dotnet build .\avalonia\toolBax.slnx -c Release
dotnet test  .\avalonia\toolBax.slnx -c Release --no-build
```
Expected: SUCCESS / all PASS.

- [ ] **Step 3: Manual end-to-end smoke (WPF)**

Run the host. With a profile set to Interactive mode + a valid public client ID: open Query Builder, run a query → browser sign-in appears once → query succeeds → run a second query → **no** second prompt (silent renewal through the broker). Then Profiles → Test connection → succeeds **without** a prompt (same MSAL cache, same path).

- [ ] **Step 4: Commit any verification fixes, then hand off**

Use the superpowers:finishing-a-development-branch skill (branch → PR per repo convention; Greptile reviews once on PR creation).

---

## Self-review notes (done at planning time)

- **Spec coverage:** broker (T5), shared helpers (T1–T2), Interactive first-class (T3, T8), WPF live silent renewal (T6), test=live path (T7), Avalonia convergence (T9), MSAL caching (T4). Deferred per scope note: status UI, schema descriptors, Settings-key migration, dual-write storage.
- **Known adaptation points (not placeholders, but verify-on-site):** `ProfileService` upsert signatures (T3 S1), `SecretVaultService` deserialization failure mode (T2 S1), `ServicePrincipalEditor` backing-field name (T8 S3), exact Grid row indices for the XAML hint (T8 S4), and whether `ProfilesViewModel` tests stub deleted members (T7 S4). Each has an explicit check step.
- **Type consistency check:** `AuthTokenRequest(ResourceBaseUrl, TenantId, Principal, ServiceName, PendingClientSecret, PendingBearerToken)` used identically in T5/T6/T7/T9; `AuthBroker(vault, interactiveProvider, authorityBase, interactiveFallback)` ctor order consistent across T5/T6/T7/T9; `GetOrCreateApp(principal, authority, credential)` matches T4 test and implementation.
