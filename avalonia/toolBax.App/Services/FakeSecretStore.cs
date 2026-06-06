using System.Collections.Generic;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="ISecretStore"/> for design-mode + tests. Tracks only which keys have a secret
/// — it deliberately does not retain the plaintext (the real Windows store protects via DPAPI over
/// the secret vault). Nothing here is logged.
/// </summary>
public sealed class FakeSecretStore : ISecretStore
{
    private readonly HashSet<string> _keys = new();

    public bool HasSecret(string key) => _keys.Contains(key);

    public void SetSecret(string key, string plaintext)
    {
        if (!string.IsNullOrEmpty(plaintext))
        {
            _keys.Add(key);
        }
    }

    public void ClearSecret(string key) => _keys.Remove(key);
}
