using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Persists the MSAL token cache blob so a signed-in session (and its refresh token) survives
/// across operations and app restarts, enabling silent token renewal without re-prompting.
/// </summary>
public interface IMsalTokenCacheStore
{
    byte[]? Load(string key);
    void Save(string key, byte[] data);
}

/// <summary>In-memory store for tests and transient sessions.</summary>
public sealed class InMemoryMsalTokenCacheStore : IMsalTokenCacheStore
{
    private readonly ConcurrentDictionary<string, byte[]> _items = new(StringComparer.Ordinal);

    public byte[]? Load(string key) => _items.TryGetValue(key, out var data) ? data : null;

    public void Save(string key, byte[] data) => _items[key] = data;
}

/// <summary>
/// DPAPI-encrypted, file-backed cache store (CurrentUser scope). The cache blob never leaves the
/// signed-in Windows user's profile in plaintext. One file per cache key (hashed to a safe name).
/// Windows-only (DPAPI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiFileMsalTokenCacheStore : IMsalTokenCacheStore
{
    private readonly string _directory;

    public DpapiFileMsalTokenCacheStore(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public byte[]? Load(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var encrypted = File.ReadAllBytes(path);
            return ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            // Corrupted/unreadable cache (e.g. different user, tampered file): treat as no cache.
            return null;
        }
    }

    public void Save(string key, byte[] data)
    {
        Directory.CreateDirectory(_directory);
        var encrypted = ProtectedData.Protect(data, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), encrypted);
    }

    private string PathFor(string key)
    {
        // Hash the key to a filesystem-safe, fixed-length name.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key ?? string.Empty));
        return Path.Combine(_directory, Convert.ToHexString(hash) + ".bin");
    }
}
