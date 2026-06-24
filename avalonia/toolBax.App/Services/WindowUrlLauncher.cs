using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// <see cref="IUrlLauncher"/> backed by a window's <see cref="TopLevel.Launcher"/> (the production
/// implementation). Refuses anything that isn't an absolute http(s) URL.
/// </summary>
public sealed class WindowUrlLauncher : IUrlLauncher
{
    private readonly TopLevel _topLevel;

    public WindowUrlLauncher(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<bool> OpenAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return await _topLevel.Launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }
}
