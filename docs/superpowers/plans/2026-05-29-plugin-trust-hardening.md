# Plugin Trust Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace silent loading of unsigned plugins with strong-name integrity pinning for the 5 bundled plugins and an interactive trust-on-first-use (TOFU) consent flow for unsigned third-party plugins.

**Architecture:** Strong-name `FoToolbox.Core`, `FoToolbox.SDK`, and the 5 bundled plugin projects with one repo key. The host derives the expected public-key token from its own statically-referenced SDK assembly and rejects any bundled-named plugin whose token differs. Unsigned third-party plugins go through an injectable `IPluginConsentPrompt`; "always trust" decisions persist to a JSON `PluginTrustStore`. When no prompt is available (tests/headless), unsigned third-party plugins are denied.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, xUnit, `System.Text.Json`, `System.Reflection.AssemblyName`, strong naming via `.snk`.

---

## File Structure

| File | Responsibility | Created/Modified |
| --- | --- | --- |
| `build/fotoolbox.snk` | Repo strong-name key (committed; tamper-detection, not a secret) | Create |
| `src/FoToolbox.Core/FoToolbox.Core.csproj` | Enable signing | Modify |
| `src/FoToolbox.SDK/FoToolbox.SDK.csproj` | Enable signing (token source) | Modify |
| `plugins/*/*.csproj` (×5) | Enable signing | Modify |
| `src/FoToolbox.Host/Plugins/PluginTrustOptions.cs` | Default `AllowUnsigned=false`; env semantics | Modify |
| `src/FoToolbox.Core/Profiles/PluginTrustStore.cs` | Persist/query always-trust decisions (JSON) | Create |
| `src/FoToolbox.Host/Plugins/IPluginConsentPrompt.cs` | Consent abstraction + request/decision types | Create |
| `src/FoToolbox.Host/Plugins/PluginManager.cs` | New trust-decision flow (pin/sign/consent/store) | Modify |
| `tests/Fixtures/UnsignedTestPlugin/*` | Unsigned, non-bundled plugin used by loader tests | Create |
| `tests/FoToolbox.Tests/PluginTrustStoreTests.cs` | Trust store unit tests | Create |
| `tests/FoToolbox.Tests/PluginTrustOptionsTests.cs` | Env/default unit tests | Create |
| `tests/FoToolbox.Tests/PluginManagerTests.cs` | Consent/deny/pin loader tests; update unsigned test | Modify |
| `src/FoToolbox.Host/Views/PluginConsentWindow.xaml(.cs)` | WPF consent dialog | Create |
| `src/FoToolbox.Host/Plugins/PluginConsentPrompt.cs` | `IPluginConsentPrompt` WPF impl | Create |
| `src/FoToolbox.Host/AppBootstrapper.cs` | Wire trust store + consent prompt into `PluginManager` | Modify |

---

## Task 1: Strong-name the bundled assemblies

**Files:**
- Create: `build/fotoolbox.snk`
- Modify: `src/FoToolbox.Core/FoToolbox.Core.csproj`, `src/FoToolbox.SDK/FoToolbox.SDK.csproj`, `plugins/HelloPlugin/HelloPlugin.csproj`, `plugins/QueryBuilder/QueryBuilder.csproj`, `plugins/TableEntityBrowser/TableEntityBrowser.csproj`, `plugins/ODataPostBuilder/ODataPostBuilder.csproj`, `plugins/DualWriteMapBrowser/DualWriteMapBrowser.csproj`

- [ ] **Step 1: Generate the strong-name key**

`sn.exe` is not available in this environment, so generate the `.snk` directly. The SNK
format is a CryptoAPI RSA private-key blob, which `RSACryptoServiceProvider.ExportCspBlob`
produces exactly (Windows-only API; this is a Windows repo). Run:

```powershell
New-Item -ItemType Directory -Force build | Out-Null
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 2048
[System.IO.File]::WriteAllBytes("$PWD\build\fotoolbox.snk", $rsa.ExportCspBlob($true))
Write-Output ("Wrote build\fotoolbox.snk (" + (Get-Item build\fotoolbox.snk).Length + " bytes)")
```

Expected: `Wrote build\fotoolbox.snk (1172 bytes)` (a 2048-bit key blob is ~1172 bytes). The build in Step 3 is the real validation that the key is usable for signing.

- [ ] **Step 2: Enable signing on the 7 projects**

Add this property block inside the existing top `<PropertyGroup>` of each of the 7 csproj files listed above. The relative path differs by depth: use `$(MSBuildProjectDirectory)` anchoring so it is unambiguous.

For `src/FoToolbox.Core/FoToolbox.Core.csproj` and `src/FoToolbox.SDK/FoToolbox.SDK.csproj` (two levels under repo root):

```xml
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$(MSBuildProjectDirectory)\..\..\build\fotoolbox.snk</AssemblyOriginatorKeyFile>
```

For the 5 plugin csproj files under `plugins/<Name>/` (two levels under repo root, same relative depth):

```xml
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$(MSBuildProjectDirectory)\..\..\build\fotoolbox.snk</AssemblyOriginatorKeyFile>
```

- [ ] **Step 3: Build the solution**

Run: `dotnet build .\FoToolbox.sln -c Release`
Expected: PASS. If a plugin emits warning `CS8002` for a *third-party NuGet* reference that is not strong-named, add `<NoWarn>$(NoWarn);CS8002</NoWarn>` to that plugin's `<PropertyGroup>` (this warning is about an external dependency, not our integrity boundary). Do NOT suppress it for `Core`/`SDK` references — those are signed in this task, so no `CS8002` should appear for them.

- [ ] **Step 4: Verify the assemblies are strong-named**

Run:
```powershell
[Reflection.AssemblyName]::GetAssemblyName("src\FoToolbox.SDK\bin\Release\net10.0-windows\FoToolbox.SDK.dll").GetPublicKeyToken() -join ''
```
Expected: a non-empty 16-character hex string (the 8-byte token). Repeat for `plugins\HelloPlugin\bin\Release\net10.0-windows\HelloPlugin.dll` — it MUST print the **same** token.

- [ ] **Step 5: Run the existing test suite (still green pre-logic-change)**

Run: `dotnet test .\FoToolbox.sln -c Release`
Expected: PASS. Bundled plugins now strong-named but `PluginManager` logic is unchanged, so all existing tests still pass.

- [ ] **Step 6: Commit**

```powershell
git add build/fotoolbox.snk src/FoToolbox.Core/FoToolbox.Core.csproj src/FoToolbox.SDK/FoToolbox.SDK.csproj plugins/HelloPlugin/HelloPlugin.csproj plugins/QueryBuilder/QueryBuilder.csproj plugins/TableEntityBrowser/TableEntityBrowser.csproj plugins/ODataPostBuilder/ODataPostBuilder.csproj plugins/DualWriteMapBrowser/DualWriteMapBrowser.csproj
git commit -m "build: strong-name Core, SDK, and bundled plugins with repo key"
```

---

## Task 2: PluginTrustOptions — default deny + env semantics

**Files:**
- Modify: `src/FoToolbox.Host/Plugins/PluginTrustOptions.cs`
- Test: `tests/FoToolbox.Tests/PluginTrustOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FoToolbox.Tests/PluginTrustOptionsTests.cs`:

```csharp
using System;
using FoToolbox.Host.Plugins;
using Xunit;

namespace FoToolbox.Tests;

[Collection("EnvVars")]
public sealed class PluginTrustOptionsTests
{
    [Fact]
    public void Default_DoesNotAllowUnsigned()
    {
        Assert.False(PluginTrustOptions.Default.AllowUnsigned);
    }

    [Fact]
    public void FromEnvironment_AllowUnsigned_False_When_Unset()
    {
        Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", null);
        Assert.False(PluginTrustOptions.FromEnvironment().AllowUnsigned);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("1", false)]
    public void FromEnvironment_AllowUnsigned_Only_True_When_Literal_True(string value, bool expected)
    {
        try
        {
            Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", value);
            Assert.Equal(expected, PluginTrustOptions.FromEnvironment().AllowUnsigned);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS", null);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginTrustOptionsTests"`
Expected: FAIL on `Default_DoesNotAllowUnsigned` and the `unset`/non-`true` cases (current default is `true`).

- [ ] **Step 3: Update PluginTrustOptions**

Replace the body of `src/FoToolbox.Host/Plugins/PluginTrustOptions.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Host.Plugins;

public sealed record PluginTrustOptions(bool AllowUnsigned, IReadOnlyCollection<string> AllowedThumbprints)
{
    public static PluginTrustOptions Default => new(false, Array.Empty<string>());

    public static PluginTrustOptions FromEnvironment()
    {
        var allowUnsignedEnv = Environment.GetEnvironmentVariable("FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS");
        var allowUnsigned = string.Equals(allowUnsignedEnv, "true", StringComparison.OrdinalIgnoreCase);

        var thumbsEnv = Environment.GetEnvironmentVariable("FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS");
        var thumbs = (thumbsEnv ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        return new PluginTrustOptions(allowUnsigned, thumbs);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginTrustOptionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/FoToolbox.Host/Plugins/PluginTrustOptions.cs tests/FoToolbox.Tests/PluginTrustOptionsTests.cs
git commit -m "feat: default-deny unsigned plugins in PluginTrustOptions"
```

---

## Task 3: PluginTrustStore (JSON persistence)

**Files:**
- Create: `src/FoToolbox.Core/Profiles/PluginTrustStore.cs`
- Test: `tests/FoToolbox.Tests/PluginTrustStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FoToolbox.Tests/PluginTrustStoreTests.cs`:

```csharp
using System.IO;
using FoToolbox.Core.Profiles;
using Xunit;

namespace FoToolbox.Tests;

public sealed class PluginTrustStoreTests
{
    private static string TempStorePath() =>
        Path.Combine(Directory.CreateTempSubdirectory("trust-store").FullName, "trusted-plugins.json");

    [Fact]
    public void IsTrusted_False_For_Unknown_Plugin()
    {
        var store = new PluginTrustStore(TempStorePath());
        Assert.False(store.IsTrusted("Some.Plugin", "abc123"));
    }

    [Fact]
    public void Add_Then_IsTrusted_RoundTrips_Across_Instances()
    {
        var path = TempStorePath();
        new PluginTrustStore(path).Add("Some.Plugin", "ABC123");

        var reopened = new PluginTrustStore(path);
        Assert.True(reopened.IsTrusted("Some.Plugin", "abc123")); // hash compare is case-insensitive
    }

    [Fact]
    public void IsTrusted_False_When_Hash_Differs()
    {
        var path = TempStorePath();
        var store = new PluginTrustStore(path);
        store.Add("Some.Plugin", "hash-one");

        Assert.False(store.IsTrusted("Some.Plugin", "hash-two"));
    }

    [Fact]
    public void Add_Is_Idempotent()
    {
        var path = TempStorePath();
        var store = new PluginTrustStore(path);
        store.Add("Some.Plugin", "h");
        store.Add("Some.Plugin", "h");

        var json = File.ReadAllText(path);
        // Only one entry serialized.
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(json, "Some.Plugin").Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginTrustStoreTests"`
Expected: FAIL with compile error "type or namespace PluginTrustStore could not be found".

- [ ] **Step 3: Implement PluginTrustStore**

Create `src/FoToolbox.Core/Profiles/PluginTrustStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FoToolbox.Core.Profiles;

/// <summary>
/// Records user "always trust" decisions for unsigned third-party plugins as a
/// non-secret JSON list keyed by assembly name + SHA-256. Human-inspectable; deleting
/// the file clears all decisions.
/// </summary>
public sealed class PluginTrustStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private List<PluginTrustEntry>? _cache;

    public PluginTrustStore(string? path = null)
    {
        _path = path ?? ProfilePaths.ResolveAppDataPath("trusted-plugins.json");
    }

    public bool IsTrusted(string assemblyName, string sha256)
    {
        return Load().Any(e => Matches(e, assemblyName, sha256));
    }

    public void Add(string assemblyName, string sha256)
    {
        var entries = Load();
        if (entries.Any(e => Matches(e, assemblyName, sha256)))
        {
            return;
        }

        entries.Add(new PluginTrustEntry(assemblyName, sha256, DateTime.UtcNow.ToString("o")));
        Save(entries);
    }

    private static bool Matches(PluginTrustEntry entry, string assemblyName, string sha256) =>
        string.Equals(entry.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Sha256, sha256, StringComparison.OrdinalIgnoreCase);

    private List<PluginTrustEntry> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            _cache = new List<PluginTrustEntry>();
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(_path);
            _cache = JsonSerializer.Deserialize<List<PluginTrustEntry>>(json, Options) ?? new List<PluginTrustEntry>();
        }
        catch
        {
            _cache = new List<PluginTrustEntry>();
        }

        return _cache;
    }

    private void Save(List<PluginTrustEntry> entries)
    {
        _cache = entries;
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(entries, Options));
    }
}

public sealed record PluginTrustEntry(string AssemblyName, string Sha256, string ApprovedUtc);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginTrustStoreTests"`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```powershell
git add src/FoToolbox.Core/Profiles/PluginTrustStore.cs tests/FoToolbox.Tests/PluginTrustStoreTests.cs
git commit -m "feat: add PluginTrustStore for TOFU plugin trust decisions"
```

---

## Task 4: Consent abstraction types

**Files:**
- Create: `src/FoToolbox.Host/Plugins/IPluginConsentPrompt.cs`

- [ ] **Step 1: Create the abstraction**

Create `src/FoToolbox.Host/Plugins/IPluginConsentPrompt.cs`:

```csharp
namespace FoToolbox.Host.Plugins;

/// <summary>The user's trust decision for an unsigned third-party plugin.</summary>
public enum PluginConsentDecision
{
    /// <summary>Do not load the plugin.</summary>
    Deny = 0,

    /// <summary>Load for this session only; do not persist.</summary>
    LoadOnce = 1,

    /// <summary>Load and remember (persist to the trust store).</summary>
    AlwaysTrust = 2
}

/// <summary>Details shown to the user when asking whether to load an unsigned plugin.</summary>
public sealed record PluginConsentRequest(string AssemblyName, string AssemblyPath, string Sha256);

/// <summary>
/// Abstraction over the user consent prompt for unsigned third-party plugins.
/// Implemented by the host UI; left null in headless/test contexts (which then deny).
/// </summary>
public interface IPluginConsentPrompt
{
    PluginConsentDecision RequestConsent(PluginConsentRequest request);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build .\FoToolbox.sln -c Release`
Expected: PASS.

- [ ] **Step 3: Commit**

```powershell
git add src/FoToolbox.Host/Plugins/IPluginConsentPrompt.cs
git commit -m "feat: add IPluginConsentPrompt abstraction"
```

---

## Task 5: Unsigned non-bundled test plugin fixture

**Files:**
- Create: `tests/Fixtures/UnsignedTestPlugin/UnsignedTestPlugin.csproj`
- Create: `tests/Fixtures/UnsignedTestPlugin/PluginManifest.json`
- Create: `tests/Fixtures/UnsignedTestPlugin/UnsignedTestPlugin.cs`
- Modify: `tests/FoToolbox.Tests/FoToolbox.Tests.csproj`

This plugin is deliberately **not** strong-named and **not** in `BundledPluginAssemblyNames`, so it exercises the unsigned-third-party path.

- [ ] **Step 1: Create the manifest**

Create `tests/Fixtures/UnsignedTestPlugin/PluginManifest.json`:

```json
{
  "id": "test.unsigned",
  "name": "Unsigned Test Plugin",
  "version": "0.1.0",
  "minSdk": "0.2.0",
  "capabilities": [ "OData.Read" ]
}
```

- [ ] **Step 2: Create the plugin implementation**

Create `tests/Fixtures/UnsignedTestPlugin/UnsignedTestPlugin.cs`:

```csharp
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using FoToolbox.SDK.Plugins;

namespace UnsignedTestPlugin;

public sealed class UnsignedTestPlugin : IFoToolPlugin
{
    public string Id => "test.unsigned";

    public Version Version => new(0, 1, 0, 0);

    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Unsigned Test Plugin",
        Version = Version.ToString(),
        MinSdk = "0.2.0",
        Capabilities = new[] { "OData.Read" }
    };

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public UserControl CreateTool() => new UserControl();
}
```

- [ ] **Step 3: Create the csproj (no SignAssembly)**

Create `tests/Fixtures/UnsignedTestPlugin/UnsignedTestPlugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <!-- Intentionally NOT strong-named: this fixture represents an untrusted third-party plugin. -->
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\FoToolbox.SDK\FoToolbox.SDK.csproj" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="PluginManifest.json" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Reference it from the test project**

In `tests/FoToolbox.Tests/FoToolbox.Tests.csproj`, add this line inside the `<ItemGroup>` that holds the other `ProjectReference` entries (after the DualWriteMapBrowser reference):

```xml
    <ProjectReference Include="..\\..\\tests\\Fixtures\\UnsignedTestPlugin\\UnsignedTestPlugin.csproj" />
```

- [ ] **Step 5: Build to verify the fixture compiles and is referenced**

Run: `dotnet build .\FoToolbox.sln -c Release`
Expected: PASS. (A `CS8002` strong-name warning will NOT occur here because `UnsignedTestPlugin` is not strong-named.)

- [ ] **Step 6: Verify the fixture is NOT strong-named**

Run:
```powershell
[Reflection.AssemblyName]::GetAssemblyName("tests\Fixtures\UnsignedTestPlugin\bin\Release\net10.0-windows\UnsignedTestPlugin.dll").GetPublicKeyToken().Length
```
Expected: `0` (no public-key token).

- [ ] **Step 7: Commit**

```powershell
git add tests/Fixtures/UnsignedTestPlugin/ tests/FoToolbox.Tests/FoToolbox.Tests.csproj
git commit -m "test: add unsigned non-bundled plugin fixture"
```

---

## Task 6: PluginManager trust-decision flow

**Files:**
- Modify: `src/FoToolbox.Host/Plugins/PluginManager.cs`
- Modify: `tests/FoToolbox.Tests/PluginManagerTests.cs`

- [ ] **Step 1: Write the failing loader tests**

Add to `tests/FoToolbox.Tests/PluginManagerTests.cs`. First add `using UnsignedTestPlugin;` is not needed (we stage by file path), but add these members. Insert a fake prompt class and helper inside the `PluginManagerTests` class (e.g. after the `CapturingLogger` class), then the new `[Fact]`s.

Fake prompt + staging helper (add inside the class):

```csharp
    private sealed class FakeConsentPrompt : IPluginConsentPrompt
    {
        private readonly PluginConsentDecision _decision;
        public int Calls { get; private set; }
        public FakeConsentPrompt(PluginConsentDecision decision) => _decision = decision;
        public PluginConsentDecision RequestConsent(PluginConsentRequest request)
        {
            Calls++;
            return _decision;
        }
    }

    private static string UnsignedPluginAssemblyPath() =>
        typeof(UnsignedTestPlugin.UnsignedTestPlugin).Assembly.Location;

    private static string StageUnsigned(string pluginRoot, string fileName)
    {
        Directory.CreateDirectory(pluginRoot);
        var dest = Path.Combine(pluginRoot, fileName);
        File.Copy(UnsignedPluginAssemblyPath(), dest, overwrite: true);
        return dest;
    }
```

Add `using UnsignedTestPlugin;` to the file's using block (alongside `using HelloPlugin;`).

New tests (add at the end of the class, before `RunSta`):

```csharp
    [Fact]
    public void Unsigned_ThirdParty_AlwaysTrust_Loads_And_Persists()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-always").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");
            var storePath = Path.Combine(Directory.CreateTempSubdirectory("ts-always").FullName, "trusted.json");
            var store = new FoToolbox.Core.Profiles.PluginTrustStore(storePath);
            var prompt = new FakeConsentPrompt(PluginConsentDecision.AlwaysTrust);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                trustStore: store, consentPrompt: prompt);

            var plugins = await manager.DiscoverAsync();

            Assert.Single(plugins, p => p.Manifest.Id == "test.unsigned");
            Assert.Equal(1, prompt.Calls);
            Assert.True(File.Exists(storePath));
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_LoadOnce_Loads_Without_Persisting()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-once").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");
            var storePath = Path.Combine(Directory.CreateTempSubdirectory("ts-once").FullName, "trusted.json");
            var store = new FoToolbox.Core.Profiles.PluginTrustStore(storePath);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                trustStore: store, consentPrompt: new FakeConsentPrompt(PluginConsentDecision.LoadOnce));

            var plugins = await manager.DiscoverAsync();

            Assert.Single(plugins, p => p.Manifest.Id == "test.unsigned");
            Assert.False(File.Exists(storePath));
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_Deny_Skips_Plugin()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-deny").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                consentPrompt: new FakeConsentPrompt(PluginConsentDecision.Deny));

            var plugins = await manager.DiscoverAsync();

            Assert.DoesNotContain(plugins, p => p.Manifest.Id == "test.unsigned");
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_Denied_When_No_ConsentPrompt()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-headless-deny").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()));

            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        });
    }

    [Fact]
    public void Bundled_Plugin_With_Wrong_Token_Is_Rejected()
    {
        RunSta(async () =>
        {
            // Stage the unsigned (no-token) assembly under a bundled assembly file name so the
            // strong-name pin check runs and fails (token absent != pinned token).
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-pin-mismatch").FullName;
            var bundledDir = Path.Combine(pluginRoot, "HelloPlugin");
            Directory.CreateDirectory(bundledDir);
            File.Copy(UnsignedPluginAssemblyPath(), Path.Combine(bundledDir, "HelloPlugin.dll"), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));

            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
                e.Message.Contains("strong-name", StringComparison.OrdinalIgnoreCase));
        });
    }
```

Also **update the existing** `Unsigned_Plugin_Blocked_When_Not_Allowed` test — it currently copies the now-strong-named `HelloPlugin` and expects an exception. Replace its body so it uses the unsigned fixture and asserts skip-with-warning (no exception):

```csharp
    [Fact]
    public void Unsigned_Plugin_Blocked_When_Not_Allowed()
    {
        RunSta(async () =>
        {
            var pluginDir = Directory.CreateTempSubdirectory("unsigned-blocked").FullName;
            StageUnsigned(pluginDir, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginDir, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()));
            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        });
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginManagerTests"`
Expected: FAIL — `PluginManager` has no `trustStore`/`consentPrompt` constructor parameters yet (compile error).

- [ ] **Step 3: Update the PluginManager constructor and fields**

In `src/FoToolbox.Host/Plugins/PluginManager.cs`, add `using FoToolbox.Core.Profiles;` to the using block. Add two fields after `private readonly PluginTrustOptions _trustOptions;`:

```csharp
    private readonly PluginTrustStore? _trustStore;
    private readonly IPluginConsentPrompt? _consentPrompt;
    private readonly HashSet<string> _sessionTrusted = new(StringComparer.OrdinalIgnoreCase);

    private static readonly byte[] PinnedPublicKeyToken =
        typeof(FoToolbox.SDK.Plugins.IFoToolPlugin).Assembly.GetName().GetPublicKeyToken() ?? Array.Empty<byte>();
```

Extend the constructor signature (add two trailing optional params) and assign them. Change the signature's end from:

```csharp
        PluginTrustOptions? trustOptions = null)
    {
```
to:
```csharp
        PluginTrustOptions? trustOptions = null,
        PluginTrustStore? trustStore = null,
        IPluginConsentPrompt? consentPrompt = null)
    {
```
and add inside the constructor body (after `_trustOptions = trustOptions ?? PluginTrustOptions.Default;`):
```csharp
        _trustStore = trustStore;
        _consentPrompt = consentPrompt;
```

- [ ] **Step 4: Replace the signature gate in LoadPluginAsync with the trust flow**

In `LoadPluginAsync`, replace the first line `ValidateSignatureOrThrow(assemblyPath);` with:

```csharp
        if (!ResolvePluginTrust(assemblyPath))
        {
            return null;
        }
```

(`DiscoverAsync` already treats a `null` result as "skip and continue".)

- [ ] **Step 5: Replace ValidateSignatureOrThrow with the trust-decision methods**

Delete the entire `ValidateSignatureOrThrow` method and replace it (keep `GetRevocationModeFromEnvironment` as-is) with:

```csharp
    private bool ResolvePluginTrust(string assemblyPath)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

        // 1. Bundled plugins: must carry the pinned strong-name token. No prompt; mismatch = refuse.
        if (BundledPluginAssemblyNames.Contains(assemblyName))
        {
            if (IsBundledTokenValid(assemblyPath))
            {
                return true;
            }

            _logger.LogError("Bundled plugin {Path} failed strong-name validation; refusing to load.", assemblyPath);
            return false;
        }

        // 2. Authenticode-signed third-party plugins keep the existing thumbprint + chain checks.
        var signer = TryGetAuthenticodeSigner(assemblyPath);
        if (signer is not null)
        {
            return ValidateAuthenticodeSigner(assemblyPath, signer);
        }

        // 3. Unsigned third-party plugins.
        if (_trustOptions.AllowUnsigned)
        {
            _logger.LogWarning("Plugin {Path} is unsigned. Allowed by FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS.", assemblyPath);
            return true;
        }

        var sha = ComputeSha256(assemblyPath);
        if (_trustStore is not null && _trustStore.IsTrusted(assemblyName, sha))
        {
            return true;
        }

        if (_sessionTrusted.Contains(sha))
        {
            return true;
        }

        if (_consentPrompt is not null)
        {
            var decision = _consentPrompt.RequestConsent(new PluginConsentRequest(assemblyName, assemblyPath, sha));
            switch (decision)
            {
                case PluginConsentDecision.AlwaysTrust:
                    _trustStore?.Add(assemblyName, sha);
                    _logger.LogInformation("User granted persistent trust to unsigned plugin {Path}.", assemblyPath);
                    return true;
                case PluginConsentDecision.LoadOnce:
                    _sessionTrusted.Add(sha);
                    _logger.LogInformation("User granted session trust to unsigned plugin {Path}.", assemblyPath);
                    return true;
                default:
                    _logger.LogWarning("User denied loading unsigned plugin {Path}.", assemblyPath);
                    return false;
            }
        }

        _logger.LogWarning("Unsigned plugin {Path} denied: no consent prompt available and AllowUnsigned=false.", assemblyPath);
        return false;
    }

    private bool IsBundledTokenValid(string assemblyPath)
    {
        if (PinnedPublicKeyToken.Length == 0)
        {
            _logger.LogError("Host SDK assembly is not strong-named; cannot validate bundled plugin {Path}.", assemblyPath);
            return false;
        }

        try
        {
            var token = AssemblyName.GetAssemblyName(assemblyPath).GetPublicKeyToken();
            return token is { Length: > 0 } && token.SequenceEqual(PinnedPublicKeyToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed reading strong-name token from {Path}.", assemblyPath);
            return false;
        }
    }

    private static X509Certificate2? TryGetAuthenticodeSigner(string assemblyPath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            return new X509Certificate2(X509Certificate.CreateFromSignedFile(assemblyPath));
#pragma warning restore SYSLIB0057
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private bool ValidateAuthenticodeSigner(string assemblyPath, X509Certificate2 signer)
    {
        var thumbprint = signer.Thumbprint?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)?.ToUpperInvariant() ?? string.Empty;
        if (_trustOptions.AllowedThumbprints.Count > 0 && !_trustOptions.AllowedThumbprints.Contains(thumbprint))
        {
            _logger.LogError("Plugin {Path} signed with thumbprint {Thumbprint}, not in allowlist; refusing to load.", assemblyPath, thumbprint);
            return false;
        }

        var chain = new X509Chain
        {
            ChainPolicy =
            {
                RevocationMode = GetRevocationModeFromEnvironment(),
                VerificationFlags = X509VerificationFlags.NoFlag
            }
        };

        if (!chain.Build(signer))
        {
            var statuses = string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
            _logger.LogError("Plugin {Path} failed signature trust validation: {Statuses}", assemblyPath, statuses);
            return false;
        }

        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
```

- [ ] **Step 6: Run the full PluginManager test class**

Run: `dotnet test .\FoToolbox.sln -c Release --filter "FullyQualifiedName~PluginManagerTests"`
Expected: PASS — including the bundled-plugin discovery tests (now via the strong-name pin path), the consent tests, the deny-default test, and the pin-mismatch test.

- [ ] **Step 7: Run the entire suite (regression)**

Run: `dotnet test .\FoToolbox.sln -c Release`
Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src/FoToolbox.Host/Plugins/PluginManager.cs tests/FoToolbox.Tests/PluginManagerTests.cs
git commit -m "feat: strong-name pinning + TOFU consent in PluginManager"
```

---

## Task 7: WPF consent dialog + host wiring

**Files:**
- Create: `src/FoToolbox.Host/Views/PluginConsentWindow.xaml`
- Create: `src/FoToolbox.Host/Views/PluginConsentWindow.xaml.cs`
- Create: `src/FoToolbox.Host/Plugins/PluginConsentPrompt.cs`
- Modify: `src/FoToolbox.Host/AppBootstrapper.cs`

- [ ] **Step 1: Create the consent window XAML**

Create `src/FoToolbox.Host/Views/PluginConsentWindow.xaml`:

```xml
<Window x:Class="FoToolbox.Host.Views.PluginConsentWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Unsigned plugin" Width="460" SizeToContent="Height"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize" ShowInTaskbar="False">
    <StackPanel Margin="16">
        <TextBlock Text="An unsigned plugin wants to load" FontWeight="Bold" FontSize="14" Margin="0,0,0,8"/>
        <TextBlock TextWrapping="Wrap" Margin="0,0,0,4">
            <Run Text="Plugin: "/><Run x:Name="PluginNameRun" FontWeight="SemiBold"/>
        </TextBlock>
        <TextBlock TextWrapping="Wrap" Margin="0,0,0,4">
            <Run Text="SHA-256: "/><Run x:Name="ShaRun" FontFamily="Consolas"/>
        </TextBlock>
        <TextBlock TextWrapping="Wrap" Foreground="#A03030" Margin="0,8,0,12"
                   Text="Only load plugins from sources you trust. Unsigned plugins run with full access to your environment and credentials."/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="DenyButton" Content="Don't load" Width="100" Margin="0,0,8,0" IsCancel="True" Click="Deny_Click"/>
            <Button x:Name="OnceButton" Content="Load once" Width="100" Margin="0,0,8,0" Click="Once_Click"/>
            <Button x:Name="AlwaysButton" Content="Always trust" Width="110" IsDefault="True" Click="Always_Click"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `src/FoToolbox.Host/Views/PluginConsentWindow.xaml.cs`:

```csharp
using System.Windows;
using FoToolbox.Host.Plugins;

namespace FoToolbox.Host.Views;

public partial class PluginConsentWindow : Window
{
    public PluginConsentDecision Decision { get; private set; } = PluginConsentDecision.Deny;

    public PluginConsentWindow(PluginConsentRequest request)
    {
        InitializeComponent();
        PluginNameRun.Text = request.AssemblyName;
        ShaRun.Text = request.Sha256;
    }

    private void Deny_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.Deny);
    private void Once_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.LoadOnce);
    private void Always_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.AlwaysTrust);

    private void Close(PluginConsentDecision decision)
    {
        Decision = decision;
        DialogResult = decision != PluginConsentDecision.Deny;
        Close();
    }
}
```

- [ ] **Step 3: Create the IPluginConsentPrompt implementation**

Create `src/FoToolbox.Host/Plugins/PluginConsentPrompt.cs`:

```csharp
using System.Windows;
using FoToolbox.Host.Views;

namespace FoToolbox.Host.Plugins;

/// <summary>WPF implementation of <see cref="IPluginConsentPrompt"/>; marshals to the UI thread.</summary>
public sealed class PluginConsentPrompt : IPluginConsentPrompt
{
    public PluginConsentDecision RequestConsent(PluginConsentRequest request)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            return PluginConsentDecision.Deny;
        }

        return app.Dispatcher.Invoke(() =>
        {
            var window = new PluginConsentWindow(request)
            {
                Owner = app.MainWindow
            };
            window.ShowDialog();
            return window.Decision;
        });
    }
}
```

- [ ] **Step 4: Wire the trust store + prompt into AppBootstrapper**

In `src/FoToolbox.Host/AppBootstrapper.cs`, add `using FoToolbox.Core.Profiles;` if not present (it is already imported). In `ApplyProfileAsync`, change the `PluginManager` construction. Replace:

```csharp
        var trust = PluginTrustOptions.FromEnvironment();
        var manager = new PluginManager(
            pluginRoot,
            bundle.FoEnvironment,
            odata,
            odataWrite,
            catalog,
            _logger,
            IsDataverseConfigured(bundle.DataverseEnvironment) ? bundle.DataverseEnvironment : null,
            _dataverseHttpClient,
            trust);
```
with:
```csharp
        var trust = PluginTrustOptions.FromEnvironment();
        var manager = new PluginManager(
            pluginRoot,
            bundle.FoEnvironment,
            odata,
            odataWrite,
            catalog,
            _logger,
            IsDataverseConfigured(bundle.DataverseEnvironment) ? bundle.DataverseEnvironment : null,
            _dataverseHttpClient,
            trust,
            new PluginTrustStore(),
            new PluginConsentPrompt());
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build .\FoToolbox.sln -c Release`
Expected: PASS.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test .\FoToolbox.sln -c Release`
Expected: PASS. (The consent dialog is UI and not unit-tested; the prompt logic is covered via the `FakeConsentPrompt` tests in Task 6.)

- [ ] **Step 7: Manual smoke (optional, documented)**

Build and run `src/FoToolbox.Host`. Drop an unsigned third-party DLL into the plugin folder and confirm the consent dialog appears with the SHA-256 shown; "Always trust" writes an entry to `%LOCALAPPDATA%\FoToolbox\trusted-plugins.json`; the 5 bundled plugins load without any prompt.

- [ ] **Step 8: Commit**

```powershell
git add src/FoToolbox.Host/Views/PluginConsentWindow.xaml src/FoToolbox.Host/Views/PluginConsentWindow.xaml.cs src/FoToolbox.Host/Plugins/PluginConsentPrompt.cs src/FoToolbox.Host/AppBootstrapper.cs
git commit -m "feat: WPF plugin consent dialog wired into host plugin loading"
```

---

## Task 8: Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document the trust model**

Add a short "Plugin trust" subsection under the existing security/plugin content in `README.md`:

```markdown
## Plugin trust

- The 5 bundled plugins are strong-name pinned; the host refuses to load a bundled
  plugin whose assembly has been tampered with.
- Unsigned third-party plugins prompt for consent on first load (Load once / Always
  trust / Don't load). "Always trust" decisions are stored in
  `%LOCALAPPDATA%\FoToolbox\trusted-plugins.json` — delete that file to reset them.
- Set `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS=true` to load all unsigned plugins silently
  (intended for development/CI only).
- Authenticode-signed plugins can be restricted to an allowlist via
  `FOTOOLBOX_ALLOWED_PLUGIN_THUMBPRINTS`.
```

- [ ] **Step 2: Commit**

```powershell
git add README.md
git commit -m "docs: document plugin trust model"
```

---

## Self-Review Notes

- **Spec coverage:** Trust flow (Task 6), strong-name pinning (Task 1 + Task 6 `IsBundledTokenValid`), consent abstraction (Task 4), consent UI + wiring (Task 7), trust store (Task 3), default-deny + env semantics (Task 2), failure-handling-as-skip (Task 6 returns `null`/`false`, `DiscoverAsync` continues), all spec test cases (Tasks 2/3/6) — covered.
- **Pinned-token source:** Derived at runtime from the host's static reference to `FoToolbox.SDK` (`typeof(IFoToolPlugin).Assembly`), so there is no hardcoded magic token to drift; SDK and bundled plugins share one `.snk`, guaranteeing matching tokens.
- **Type consistency:** `PluginConsentDecision { Deny, LoadOnce, AlwaysTrust }`, `PluginConsentRequest(AssemblyName, AssemblyPath, Sha256)`, `PluginTrustStore.IsTrusted/Add`, `PluginTrustEntry(AssemblyName, Sha256, ApprovedUtc)`, and `PluginManager.ResolvePluginTrust` are used consistently across tasks.
- **Behavior change risk:** the existing `Unsigned_Plugin_Blocked_When_Not_Allowed` test is explicitly rewritten (Task 6) because `HelloPlugin` is now strong-named and would no longer be "unsigned".
```
