using System;

namespace FoToolbox.Core.Catalog;

/// <summary>
/// Raised when a legacy imported table catalog cannot be attributed safely to a case-sensitive F&amp;O URL
/// suffix. Callers may explicitly import <see cref="OriginalJson"/> for the selected environment.
/// </summary>
public sealed class LegacyTableCatalogRecoveryRequiredException : InvalidOperationException
{
    private const string SafeMessage =
        "This imported table catalog cannot be automatically recovered because its original path casing cannot be established. Re-import it for the selected environment.";

    public LegacyTableCatalogRecoveryRequiredException(string originalJson) : base(SafeMessage)
    {
        OriginalJson = originalJson ?? throw new ArgumentNullException(nameof(originalJson));
    }

    /// <summary>The preserved legacy catalog payload for explicit re-import; never included in the exception message.</summary>
    public string OriginalJson { get; }
}
