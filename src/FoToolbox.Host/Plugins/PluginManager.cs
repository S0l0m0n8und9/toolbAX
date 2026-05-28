using FoToolbox.Core.Models;
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
        "DualWriteMapBrowser"
    };

    private readonly string _pluginRoot;
    private readonly FoEnvironment _env;
    private readonly IODataClient _odata;
    private readonly IODataWriteClient _odataWrite;
    private readonly ICatalogService _catalog;
    private readonly ILogger _logger;
    private readonly PluginTrustOptions _trustOptions;
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
        PluginTrustOptions? trustOptions = null)
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
        ValidateSignatureOrThrow(assemblyPath);

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

    private void ValidateSignatureOrThrow(string assemblyPath)
    {
        X509Certificate2? signer = null;
        try
        {
            // CreateFromSignedFile extracts the Authenticode signer from a PE. X509CertificateLoader
            // only loads raw cert files; there is no direct net9+ replacement for this use case.
#pragma warning disable SYSLIB0057
            signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(assemblyPath));
#pragma warning restore SYSLIB0057
        }
        catch (CryptographicException)
        {
            signer = null;
        }

        if (signer is null)
        {
            if (_trustOptions.AllowUnsigned)
            {
                _logger.LogWarning("Plugin {Path} is unsigned. Allowed by configuration.", assemblyPath);
                return;
            }

            throw new InvalidOperationException($"Plugin {assemblyPath} is unsigned and AllowUnsigned=false.");
        }

        var thumbprint = signer.Thumbprint?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)?.ToUpperInvariant() ?? string.Empty;
        if (_trustOptions.AllowedThumbprints.Count > 0 && !_trustOptions.AllowedThumbprints.Contains(thumbprint))
        {
            throw new InvalidOperationException($"Plugin {assemblyPath} signed with thumbprint {thumbprint}, not in allowlist.");
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
            throw new InvalidOperationException($"Plugin {assemblyPath} failed signature trust validation: {statuses}");
        }
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
