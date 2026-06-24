using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / headless <see cref="IUrlLauncher"/>: records the last URL instead of opening a browser.
/// </summary>
public sealed class FakeUrlLauncher : IUrlLauncher
{
    public string? LastUrl { get; private set; }

    public Task<bool> OpenAsync(string? url)
    {
        LastUrl = url;
        return Task.FromResult(!string.IsNullOrWhiteSpace(url));
    }
}
