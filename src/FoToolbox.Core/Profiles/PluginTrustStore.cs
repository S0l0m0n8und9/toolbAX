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
