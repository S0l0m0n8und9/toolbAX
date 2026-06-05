using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using FoToolbox.SDK;
using FoToolbox.Core.OData;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace FoToolbox.Host.Plugins;

/// <summary>
/// Discovers and loads plugins from a directory.
/// </summary>
public sealed class PluginManager
{
    private static readonly HashSet<string> BundledPluginAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "HelloPlugin",
        "QueryBuilder",
        "TableEntityBrowser",
        "ODataPostBuilder",
        "DualWriteMapBrowser",
        "DualWriteOperations",
        "DualWriteCompare"
    };

    private readonly string _pluginRoot;
    private readonly FoEnvironment _env;
    private readonly IODataClient _odata;
    private readonly IODataWriteClient _odataWrite;
    private readonly ICatalogService _catalog;
    private readonly ILogger _logger;
    private readonly PluginTrustOptions _trustOptions;
    private readonly PluginTrustStore? _trustStore;
    private readonly IPluginConsentPrompt? _consentPrompt;
    // Session-only "load once" trust, keyed by SHA-256 alone (identical bytes = same plugin file).
    private readonly HashSet<string> _sessionTrusted = new(StringComparer.OrdinalIgnoreCase);

    private static readonly byte[] PinnedPublicKeyToken =
        typeof(FoToolbox.SDK.Plugins.IFoToolPlugin).Assembly.GetName().GetPublicKeyToken() ?? Array.Empty<byte>();
    private readonly DataverseEnvironment? _dataverseEnv;
    private readonly HttpClient? _dataverseHttp;
    private readonly PluginNavigationBus _navBus = new();

    /// <summary>
    /// The shared navigation bus. The host UI should subscribe to
    /// <see cref="PluginNavigationBus.PluginActivationRequested"/> after <see cref="DiscoverAsync"/>
    /// to bring the target plugin tab into focus when navigation is requested.
    /// </summary>
    public PluginNavigationBus NavigationBus => _navBus;

    public PluginManager(
        string pluginRoot,
        FoEnvironment env,
        IODataClient odata,
        IODataWriteClient odataWrite,
        ICatalogService catalog,
        ILogger logger,
        DataverseEnvironment? dataverseEnv = null,
        HttpClient? dataverseHttp = null,
        PluginTrustOptions? trustOptions = null,
        PluginTrustStore? trustStore = null,
        IPluginConsentPrompt? consentPrompt = null)
    {
        _pluginRoot = pluginRoot;
        _env = env;
        _odata = odata;
        _odataWrite = odataWrite;
        _catalog = catalog;
        _logger = logger;
        _dataverseEnv = dataverseEnv;
        _dataverseHttp = dataverseHttp;
        _trustOptions = trustOptions ?? PluginTrustOptions.Default;
        _trustStore = trustStore;
        _consentPrompt = consentPrompt;
    }

    public async Task<IReadOnlyList<LoadedPlugin>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<LoadedPlugin>();
        _logger.LogInformation("Discovering plugins from root {PluginRoot}.", _pluginRoot);

        if (!Directory.Exists(_pluginRoot))
        {
            _logger.LogWarning("Plugin directory {Root} not found.", _pluginRoot);
            return results;
        }

        MigrateLegacyFlatBundledPluginsToCanonicalLayout();

        // Prefer "one plugin per directory" layout:
        // plugins/
        //   QueryBuilder/QueryBuilder.dll (+ deps)
        //   HelloPlugin/HelloPlugin.dll (+ deps)
        // This avoids attempting to load every dependency DLL as if it were a plugin.
        var allDlls = Directory.GetFiles(_pluginRoot, "*.dll", SearchOption.AllDirectories);
        var candidates = DiscoverPluginCandidates();

        foreach (var candidate in candidates)
        {
            _logger.LogInformation("Discovered plugin candidate {Dll} ({Layout}).", candidate.Path, candidate.Layout);
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var loaded = await LoadPluginAsync(candidate.Path, cancellationToken);
                if (loaded is not null)
                {
                    var duplicate = results.FirstOrDefault(p => string.Equals(p.Manifest.Id, loaded.Manifest.Id, StringComparison.OrdinalIgnoreCase));
                    if (duplicate is not null)
                    {
                        loaded.LoadContext.Unload();
                        _logger.LogWarning(
                            "Skipping duplicate plugin {PluginId} from {Dll}; already loaded from {LoadedDll}.",
                            loaded.Manifest.Id,
                            candidate.Path,
                            duplicate.Instance.GetType().Assembly.Location);
                        continue;
                    }

                    results.Add(loaded);
                    _logger.LogInformation(
                        "Loaded plugin {PluginName} ({PluginId}) from {Dll}.",
                        loaded.Manifest.Name,
                        loaded.Manifest.Id,
                        candidate.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin {Dll}", candidate.Path);
            }
        }

        if (allDlls.Length > 0 && results.Count == 0)
        {
            _logger.LogWarning(
                "Plugin DLLs were found under {PluginRoot}, but zero plugins loaded. Check plugin layout, signatures, manifests, SDK compatibility, and preceding load errors.",
                _pluginRoot);
        }

        _navBus.SetPlugins(results);
        return results;
    }

    private void MigrateLegacyFlatBundledPluginsToCanonicalLayout()
    {
        foreach (var flatDll in Directory.GetFiles(_pluginRoot, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(flatDll);
            if (!BundledPluginAssemblyNames.Contains(assemblyName))
            {
                continue;
            }

            var canonicalDirectory = Path.Combine(_pluginRoot, assemblyName);
            var canonicalPath = Path.Combine(canonicalDirectory, assemblyName + ".dll");
            if (File.Exists(canonicalPath))
            {
                _logger.LogInformation(
                    "Skipping legacy flat plugin migration for {PluginName}; canonical plugin already exists at {CanonicalPath}. Legacy path: {LegacyPath}.",
                    assemblyName,
                    canonicalPath,
                    flatDll);
                continue;
            }

            try
            {
                Directory.CreateDirectory(canonicalDirectory);
                File.Move(flatDll, canonicalPath, overwrite: true);
                _logger.LogInformation(
                    "Migrated legacy flat plugin {PluginName} from {LegacyPath} to canonical path {CanonicalPath}.",
                    assemblyName,
                    flatDll,
                    canonicalPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed migrating legacy flat plugin {PluginName} from {LegacyPath} to {CanonicalPath}; falling back to flat-layout loading.",
                    assemblyName,
                    flatDll,
                    canonicalPath);
            }
        }
    }

    private IReadOnlyList<PluginCandidate> DiscoverPluginCandidates()
    {
        var candidates = new List<PluginCandidate>();
        var canonicalAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(_pluginRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            var primary = Path.Combine(dir, name + ".dll");
            if (File.Exists(primary))
            {
                candidates.Add(new PluginCandidate(primary, PluginCandidateLayout.CanonicalSubfolder));
                canonicalAssemblyNames.Add(Path.GetFileNameWithoutExtension(primary));
            }
        }

        foreach (var flatDll in Directory.GetFiles(_pluginRoot, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(flatDll);
            if (canonicalAssemblyNames.Contains(assemblyName))
            {
                _logger.LogInformation(
                    "Skipping duplicate plugin candidate {Dll}; canonical subfolder copy takes precedence.",
                    flatDll);
                continue;
            }

            candidates.Add(new PluginCandidate(flatDll, PluginCandidateLayout.LegacyFlat));
        }

        return candidates;
    }

    private async Task<LoadedPlugin?> LoadPluginAsync(string assemblyPath, CancellationToken cancellationToken)
    {
        // Only assemblies that actually carry an embedded plugin manifest are subject to the trust
        // decision. Probe the manifest from PE metadata first (no code runs, nothing is loaded into an
        // AssemblyLoadContext), so stray dependency/framework DLLs in the plugin root are skipped
        // silently instead of popping a blocking "unsigned plugin" consent prompt that wedges startup.
        if (!PluginManifestReader.HasManifestResource(assemblyPath))
        {
            _logger.LogDebug("Skipping {Dll}: no embedded plugin manifest; not a plugin.", assemblyPath);
            return null;
        }

        if (!ResolvePluginTrust(assemblyPath))
        {
            return null;
        }

        var loadContext = new PluginLoadContext(Path.GetDirectoryName(assemblyPath)!);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

        var manifest = PluginManifestReader.ReadOrThrow(assembly);
        ValidateManifest(manifest);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IFoToolPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

        if (pluginType is null)
        {
            throw new InvalidOperationException($"No IFoToolPlugin implementation found in {assemblyPath}.");
        }

        var plugin = Activator.CreateInstance(pluginType) as IFoToolPlugin
                     ?? throw new InvalidOperationException($"Could not create instance of {pluginType.FullName}.");

        IPluginContext ctx = RequiresWrite(manifest)
            ? new PluginContextWrite(_env, _odata, _odataWrite, _catalog, _logger, _dataverseEnv, _dataverseHttp, _navBus)
            : new PluginContext(_env, _odata, _catalog, _logger, _dataverseEnv, _dataverseHttp, _navBus);
        await plugin.InitializeAsync(ctx);
        var control = plugin.CreateTool();

        return new LoadedPlugin
        {
            Instance = plugin,
            Manifest = manifest,
            ToolControl = control,
            LoadContext = loadContext
        };
    }

    internal static void ValidateManifest(FoPluginManifest manifest)
    {
        if (!Version.TryParse(manifest.MinSdk, out var minSdk))
        {
            throw new InvalidOperationException($"Invalid minSdk '{manifest.MinSdk}' for plugin {manifest.Id}.");
        }

        if (minSdk > SdkInfo.Version)
        {
            throw new InvalidOperationException($"Plugin {manifest.Id} requires SDK {manifest.MinSdk} but host has {SdkInfo.Version}.");
        }
    }

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
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static X509RevocationMode GetRevocationModeFromEnvironment()
    {
        // Default preserves existing behavior. Online revocation checks can be slow/blocked in some environments.
        var mode = Environment.GetEnvironmentVariable("FOTOOLBOX_PLUGIN_REVOCATION");
        if (string.IsNullOrWhiteSpace(mode)) return X509RevocationMode.NoCheck;

        return mode.Trim().ToLowerInvariant() switch
        {
            "online" => X509RevocationMode.Online,
            "offline" => X509RevocationMode.Offline,
            "nocheck" => X509RevocationMode.NoCheck,
            _ => X509RevocationMode.NoCheck
        };
    }

    private static bool RequiresWrite(FoPluginManifest manifest)
    {
        return manifest.CapabilitiesOrEmpty().Contains("OData.Write", StringComparer.OrdinalIgnoreCase);
    }

    private enum PluginCandidateLayout
    {
        CanonicalSubfolder,
        LegacyFlat
    }

    private sealed record PluginCandidate(string Path, PluginCandidateLayout Layout);
}

internal static class FoPluginManifestExtensions
{
    public static IReadOnlyCollection<string> CapabilitiesOrEmpty(this FoPluginManifest manifest) =>
        manifest.Capabilities ?? Array.Empty<string>();
}
