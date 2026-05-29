using System.Windows;
using FoToolbox.Host.Views;

namespace FoToolbox.Host.Plugins;

/// <summary>WPF implementation of <see cref="IPluginConsentPrompt"/>; marshals to the UI thread.</summary>
public sealed class PluginConsentPrompt : IPluginConsentPrompt
{
    public PluginConsentDecision RequestConsent(PluginConsentRequest request)
    {
        var app = Application.Current;
        if (app?.Dispatcher is null)
        {
            return PluginConsentDecision.Deny;
        }

        return app.Dispatcher.Invoke(() =>
        {
            var window = new PluginConsentWindow(request)
            {
                Owner = app.MainWindow
            };
            window.ShowDialog();
            return window.Decision;
        });
    }
}
