using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// Opens an absolute URL in the user's default browser. Behind an interface because the real
/// implementation needs an Avalonia TopLevel, keeping view models platform-neutral and headless-testable.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>Opens <paramref name="url"/>; returns false if it is missing/not an absolute URL or the launch fails.</summary>
    Task<bool> OpenAsync(string? url);
}
