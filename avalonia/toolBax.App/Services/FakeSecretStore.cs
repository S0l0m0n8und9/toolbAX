using System;
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

    private static string Compose(string key, SecretTarget target) => $"{target}:{key}";

    public bool HasSecret(string key, SecretTarget target = SecretTarget.Fo) => _keys.Contains(Compose(key, target));

    public void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo)
    {
        // Matches CoreSecretStore's contract: an empty secret is rejected, not silently dropped, so a
        // test passing against this fake means the same call passes against the real store.
        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("A secret must be non-empty; use ClearSecret to remove one.", nameof(plaintext));
        }

        _keys.Add(Compose(key, target));
    }

    public void ClearSecret(string key, SecretTarget target = SecretTarget.Fo) => _keys.Remove(Compose(key, target));
}
