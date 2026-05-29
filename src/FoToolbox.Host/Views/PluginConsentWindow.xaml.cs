using System.Windows;
using FoToolbox.Host.Plugins;

namespace FoToolbox.Host.Views;

public partial class PluginConsentWindow : Window
{
    public PluginConsentDecision Decision { get; private set; } = PluginConsentDecision.Deny;

    public PluginConsentWindow(PluginConsentRequest request)
    {
        InitializeComponent();
        PluginNameRun.Text = request.AssemblyName;
        ShaRun.Text = request.Sha256;
    }

    private void Deny_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.Deny);
    private void Once_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.LoadOnce);
    private void Always_Click(object sender, RoutedEventArgs e) => Close(PluginConsentDecision.AlwaysTrust);

    private void Close(PluginConsentDecision decision)
    {
        Decision = decision;
        DialogResult = decision != PluginConsentDecision.Deny;
        Close();
    }
}
