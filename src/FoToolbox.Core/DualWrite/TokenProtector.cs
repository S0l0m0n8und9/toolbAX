using System;
using System.Security.Cryptography;
using System.Text;

namespace FoToolbox.Core.DualWrite;

/// <summary>Encrypts/decrypts the pasted bearer token at rest. Abstracted for testability.</summary>
public interface ITokenProtector
{
    string Protect(string plaintext);
    string? Unprotect(string protectedValue);
}

/// <summary>
/// DPAPI (CurrentUser) protector. The token never leaves the signed-in Windows user's
/// profile in plaintext, so the on-disk connection file is useless to other users.
/// </summary>
public sealed class DpapiTokenProtector : ITokenProtector
{
    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
