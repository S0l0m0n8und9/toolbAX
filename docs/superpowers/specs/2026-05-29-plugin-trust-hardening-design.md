# Plugin Trust Hardening — Design

Date: 2026-05-29
Status: Approved (pending implementation plan)

## Problem

`PluginManager` loads plugins from `%LOCALAPPDATA%\FoToolbox\plugins` into the host
process via `AssemblyLoadContext`. Loaded plugins get full in-process access to D365
access tokens and, when they declare the `OData.Write` capability, to write APIs.

Today, `PluginTrustOptions.Default.AllowUnsigned` is effectively `true`: an unsigned DLL
dropped into the plugin folder loads silently with only a log warning
(`PluginManager.ValidateSignatureOrThrow`). There is no per-plugin user decision and no
integrity check on the bundled built-in plugins.

## Threat model (scope)

Two threats are in scope:

1. **Accidental / social-engineering loads** — a user is told "drop this DLL in your
   plugins folder" or unknowingly installs a sketchy third-party plugin. Goal: make
   loading a deliberate, informed decision rather than silent.
2. **Tamper detection of the bundled plugins** — detect if one of the 5 built-in DLLs is
   swapped or modified after install.

Explicitly **out of scope**: hard isolation/sandboxing of loaded plugins, and defending a
machine where another process already runs as the user. The install is per-user
(`%LOCALAPPDATA%`), which is user-writable, so this is **defense-in-depth, not a hard
security boundary**. Strong-name pinning and the committed signing key are tamper-detection
aids, not secrets.

## Design

### 1. Trust decision flow

`PluginManager.ValidateSignatureOrThrow(path)` is replaced by a trust-decision method
(`EnsurePluginTrustedOrThrow(path)`). For each candidate, in order:

1. Compute the SHA-256 of the DLL.
2. **Bundled** (assembly name ∈ `BundledPluginAssemblyNames`): require the pinned
   strong-name public-key token. Match → auto-trust (no prompt). Missing/mismatch → log
   error and skip the plugin (a built-in should never differ).
3. **Authenticode-signed**: existing thumbprint-allowlist + `X509Chain` validation,
   unchanged (`AllowedThumbprints`, `FOTOOLBOX_PLUGIN_REVOCATION`).
4. **Unsigned third-party**:
   - `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS=true` set → trust silently (power-user / CI escape
     hatch, preserved).
   - else the trust store already contains `(assemblyName, sha256)` → trust.
   - else a consent prompt is available → prompt:
     - `AlwaysTrust` → persist to trust store + load,
     - `LoadOnce` → load for this session only,
     - `Deny` → skip.
   - else (no prompt available = headless / tests) → deny.

**Behavior change:** `PluginTrustOptions.AllowUnsigned` now means "silently load *all*
unsigned plugins without prompting" and defaults to `false` (was effectively `true`).
Silent-allow is replaced by the consent flow. `PluginTrustOptions.FromEnvironment` sets
`AllowUnsigned = true` only when `FOTOOLBOX_ALLOW_UNSIGNED_PLUGINS` equals `true`
(case-insensitive); unset/any-other-value → `false`.

### 2. Strong-name pinning for bundled plugins

- Add a committed repo key `build/fotoolbox.snk`. Documented in-repo as tamper-detection
  only, not a secret boundary.
- Enable `SignAssembly` + `AssemblyOriginatorKeyFile` on `FoToolbox.Core`,
  `FoToolbox.SDK`, and the bundled plugin projects that exist in `plugins/`
  (`HelloPlugin`, `QueryBuilder`, `ODataPostBuilder`, `DualWriteMapBrowser`).
  `BundledPluginAssemblyNames` in `PluginManager` is the source of truth for which
  assembly names are pinned; it also lists `TableEntityBrowser` for forward-compatibility,
  which should be strong-named if/when that project is added. Strong-named assemblies
  require their references to be strong-named, which is why `Core` and `SDK` are included.
- A non-strong-named third-party plugin can still reference a strong-named SDK — the
  "must reference strong-named" rule is one-directional — so this does not break the
  third-party plugin ecosystem.
- The host embeds the expected public-key token as a constant (computed once from the key)
  and reads each bundled candidate's token via `AssemblyName.GetAssemblyName(path)`
  (lightweight, pre-load).

### 3. Consent prompt abstraction

- New `IPluginConsentPrompt` in `FoToolbox.Host.Plugins`:
  `PluginConsentDecision RequestConsent(PluginConsentRequest request)`.
  - `PluginConsentRequest` carries assembly name, file path, and SHA-256.
  - `PluginConsentDecision` enum: `LoadOnce`, `AlwaysTrust`, `Deny`.
- Injected into `PluginManager` (nullable constructor parameter). `null` → the deny path in
  the flow above, which keeps `PluginManager` UI-free and keeps tests headless by default.
- The host implements `IPluginConsentPrompt` as a small WPF modal ("Plugin *X* is
  unsigned — SHA-256 `abc…` — Load once / Always trust / Don't load"), wired in
  `AppBootstrapper.ApplyProfileAsync` where `PluginManager` is constructed.

### 4. Trust store

- `PluginTrustStore` in `FoToolbox.Core.Profiles`, backed by JSON at
  `%LOCALAPPDATA%\FoToolbox\trusted-plugins.json` (via `ProfilePaths.ResolveAppDataPath`).
- Record shape: `{ assemblyName, sha256, approvedUtc }`.
- API: `bool IsTrusted(string assemblyName, string sha256)`,
  `void Add(string assemblyName, string sha256)` (or async equivalents matching existing
  store conventions in `Core/Profiles`).
- `AlwaysTrust` persists; `LoadOnce` is held in memory for the session only.
- The file is non-secret and human-inspectable; deleting it clears all "always trust"
  decisions.

### 5. Failure handling

Untrusted / denied / pin-mismatch plugins are logged and skipped. The existing per-plugin
`try/catch` in `PluginManager.DiscoverAsync` already isolates a single plugin's failure, so
the app continues to start with whatever loaded successfully. No new hard-failure paths.

## Components & boundaries

| Unit | Responsibility | Depends on |
| --- | --- | --- |
| `PluginTrustStore` (Core) | Persist/query always-trust decisions in JSON | `ProfilePaths`, `System.Text.Json` |
| `IPluginConsentPrompt` (Host) | Abstract user consent decision | — |
| WPF consent dialog (Host) | Implement `IPluginConsentPrompt` | WPF |
| `PluginManager` trust logic | Decide trust per candidate; orchestrate pin/sign/consent | `PluginTrustStore`, `IPluginConsentPrompt`, `PluginTrustOptions` |
| `PluginTrustOptions` | Carry config flags (default `AllowUnsigned=false`) | env vars |
| Strong-name key + csproj signing | Pin bundled-plugin identity | `build/fotoolbox.snk` |

## Testing

- `PluginTrustStore`: round-trip persistence; `IsTrusted` false on hash mismatch; false on
  unknown assembly.
- Loader with a fake `IPluginConsentPrompt`:
  - `AlwaysTrust` → plugin loads and a trust-store entry is persisted.
  - `LoadOnce` → plugin loads, no persisted entry.
  - `Deny` → plugin skipped.
- Bundled-pin: a bundled-named fixture with a wrong/absent public-key token is rejected; a
  fixture with the correct token loads.
- Headless default: no consent prompt injected + unsigned non-bundled candidate → not
  loaded.
- `PluginTrustOptions.FromEnvironment`: `AllowUnsigned` is `false` when the env var is
  unset and `true` only when it equals `true`.

## Out of scope / future

- Process-level or AppDomain-style sandboxing of plugins.
- Code-signing the released bundle itself (separate roadmap item; needs a real
  certificate).
- A UI to review/revoke entries in `trusted-plugins.json` (manual file edit/delete for now).
