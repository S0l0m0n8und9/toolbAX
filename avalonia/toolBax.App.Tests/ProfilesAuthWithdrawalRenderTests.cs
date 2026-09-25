using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class ProfilesAuthWithdrawalRenderTests
{
    private static ProfilesView RenderLegacyProfile(FoAuthMode foMode, FoAuthMode dvMode, out Window window)
    {
        var profile = new EnvProfile("legacy", "Legacy", "https://legacy.operations.dynamics.com", "tenant",
            "USMF", "Tier 1", EnvStatus.Disconnected, DataverseUrl: "https://legacy.crm.dynamics.com",
            ClientId: "fo-client", AuthMode: foMode, DataverseClientId: "dv-client", DataverseAuthMode: dvMode);
        var store = new FakeProfileStore(new[] { profile }) { ActiveId = profile.Id };
        var view = new ProfilesView { DataContext = new ProfilesViewModel(store) };
        window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    [AvaloniaFact]
    public void Legacy_FO_selector_has_no_supported_selection_and_does_not_rewrite_the_draft()
    {
        var view = RenderLegacyProfile(FoAuthMode.Certificate, FoAuthMode.Interactive, out var window);
        try
        {
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            var selector = view.FindControl<ComboBox>("FoAuthModeSelector");
            var warning = view.FindControl<Control>("FoUnsupportedAuthWarning");
            var client = view.FindControl<TextBox>("FoClientIdInput");

            Assert.NotNull(selector);
            Assert.Equal(2, selector.ItemCount);
            Assert.Null(selector.SelectedItem);
            Assert.True(warning!.IsEffectivelyVisible);
            Assert.False(client!.IsEnabled);
            Assert.Equal(FoAuthMode.Certificate, vm.DraftAuthMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Legacy_Dataverse_selector_has_no_supported_selection_and_does_not_rewrite_the_draft()
    {
        var view = RenderLegacyProfile(FoAuthMode.Interactive, (FoAuthMode)99, out var window);
        try
        {
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            var selector = view.FindControl<ComboBox>("DataverseAuthModeSelector");
            var warning = view.FindControl<Control>("DataverseUnsupportedAuthWarning");
            var client = view.FindControl<TextBox>("DataverseClientIdInput");

            Assert.NotNull(selector);
            Assert.Equal(2, selector.ItemCount);
            Assert.Null(selector.SelectedItem);
            Assert.True(warning!.IsEffectivelyVisible);
            Assert.False(client!.IsEnabled);
            Assert.Equal((FoAuthMode)99, vm.DraftDataverseAuthMode);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Data_Integrator_tab_offers_portal_test_and_explicit_legacy_password_clear_only()
    {
        var profile = new EnvProfile("legacy", "Legacy", "https://legacy.operations.dynamics.com", "tenant",
            "USMF", "Tier 1", EnvStatus.Disconnected, DataIntegratorClientId: "legacy-client",
            DataIntegratorMode: DiAuthMode.Ropc, DualWriteGatewayUrl: "https://legacy-gateway");
        var store = new FakeProfileStore(new[] { profile }) { ActiveId = profile.Id };
        var secrets = new FakeSecretStore();
        secrets.SetSecret(profile.Id, "legacy-password", SecretTarget.DataIntegrator);
        var view = new ProfilesView { DataContext = new ProfilesViewModel(store, secrets) };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 3;
            Dispatcher.UIThread.RunJobs();
            var warning = view.FindControl<Border>("LegacyDiWarning");
            var gateway = view.FindControl<Button>("PortalGatewayTestButton");
            var clear = view.FindControl<Button>("ClearLegacyDiPasswordButton");

            Assert.True(warning!.IsEffectivelyVisible);
            Assert.Equal("Sign in & test gateway", gateway!.Content);
            Assert.True(gateway.IsEffectivelyVisible);
            Assert.Equal("Clear legacy password", clear!.Content);
            Assert.True(clear.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
