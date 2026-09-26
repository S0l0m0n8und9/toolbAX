using System;
using System.Linq;
using System.Threading.Tasks;
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
    private static TabItem SelectTab(ProfilesView view, Window window, string header)
    {
        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        var tab = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, header));
        tabs.SelectedItem = tab;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Same(tab, tabs.SelectedItem);
        return tab;
    }

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
            SelectTab(view, window, "FO Environment");
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            var selector = view.FindControl<ComboBox>("FoAuthModeSelector");
            var warning = view.FindControl<Control>("FoUnsupportedAuthWarning");
            var client = view.FindControl<TextBox>("FoClientIdInput");

            Assert.NotNull(selector);
            Assert.Contains(selector!, window.GetVisualDescendants());
            Assert.Contains(warning!, window.GetVisualDescendants());
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
            SelectTab(view, window, "CE · Dataverse");
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            var selector = view.FindControl<ComboBox>("DataverseAuthModeSelector");
            var warning = view.FindControl<Control>("DataverseUnsupportedAuthWarning");
            var client = view.FindControl<TextBox>("DataverseClientIdInput");

            Assert.NotNull(selector);
            Assert.Contains(selector!, window.GetVisualDescendants());
            Assert.Contains(warning!, window.GetVisualDescendants());
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
    public void Pending_legacy_FO_replacement_disables_secret_entry_and_store_with_save_first_hint()
    {
        var view = RenderLegacyProfile(FoAuthMode.Certificate, FoAuthMode.Interactive, out var window);
        try
        {
            SelectTab(view, window, "FO Environment");
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            vm.SelectedFoAuthMode = FoAuthMode.ClientSecret;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var input = view.FindControl<TextBox>("FoSecretInput");
            var store = view.FindControl<Button>("FoSecretStoreButton");
            var clear = view.FindControl<Button>("FoSecretClearButton");
            var hint = view.FindControl<TextBlock>("FoSecretSaveFirstHint");
            Assert.Contains(input!, window.GetVisualDescendants());
            Assert.False(input!.IsEnabled);
            Assert.False(store!.IsEnabled);
            Assert.False(clear!.IsEnabled);
            Assert.True(hint!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pending_legacy_Dataverse_replacement_disables_secret_entry_and_store_with_save_first_hint()
    {
        var view = RenderLegacyProfile(FoAuthMode.Interactive, FoAuthMode.Certificate, out var window);
        try
        {
            SelectTab(view, window, "CE · Dataverse");
            var vm = Assert.IsType<ProfilesViewModel>(view.DataContext);
            vm.SelectedDataverseAuthMode = FoAuthMode.ClientSecret;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var input = view.FindControl<TextBox>("DataverseSecretInput");
            var store = view.FindControl<Button>("DataverseSecretStoreButton");
            var clear = view.FindControl<Button>("DataverseSecretClearButton");
            var hint = view.FindControl<TextBlock>("DataverseSecretSaveFirstHint");
            Assert.Contains(input!, window.GetVisualDescendants());
            Assert.False(input!.IsEnabled);
            Assert.False(store!.IsEnabled);
            Assert.False(clear!.IsEnabled);
            Assert.True(hint!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Data_Integrator_tab_retains_legacy_clear_confirmation_after_same_profile_save()
    {
        var profile = new EnvProfile("legacy", "Legacy", "https://legacy.operations.dynamics.com", "tenant",
            "USMF", "Tier 1", EnvStatus.Disconnected, DataIntegratorClientId: "legacy-client",
            DataIntegratorMode: DiAuthMode.Ropc, DualWriteGatewayUrl: "https://legacy-gateway");
        var store = new FakeProfileStore(new[] { profile }) { ActiveId = profile.Id };
        var secrets = new FakeSecretStore();
        secrets.SetSecret(profile.Id, "legacy-password", SecretTarget.DataIntegrator);
        var vm = new ProfilesViewModel(store, secrets);
        var view = new ProfilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tab = SelectTab(view, window, "Data Integrator");
            var warning = view.FindControl<Border>("LegacyDiWarning");
            var gateway = view.FindControl<Button>("PortalGatewayTestButton");
            var clear = view.FindControl<Button>("ClearLegacyDiPasswordButton");
            var content = Assert.IsAssignableFrom<Control>(tab.Content);

            Assert.True(warning!.IsEffectivelyVisible);
            Assert.Contains(content, window.GetVisualDescendants());
            Assert.Contains(warning, window.GetVisualDescendants());
            Assert.Contains(gateway!, window.GetVisualDescendants());
            Assert.Contains(clear!, window.GetVisualDescendants());
            Assert.Equal("Sign in & test gateway", gateway!.Content);
            Assert.True(gateway.IsEffectivelyVisible);
            Assert.Equal("Clear legacy password", clear!.Content);
            Assert.True(clear.IsEffectivelyVisible);

            vm.ClearLegacyDiPasswordCommand.Execute(null);
            vm.DraftName = "Renamed after clear";
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Equal("Legacy Data Integrator password cleared.", vm.LegacyDiStatus);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var legacyStatus = view.FindControl<TextBlock>("LegacyDiClearStatus");
            Assert.NotNull(legacyStatus);
            Assert.Equal("Legacy Data Integrator password cleared.", legacyStatus!.Text);
            Assert.True(legacyStatus.IsEffectivelyVisible);
            Assert.False(clear.IsEffectivelyVisible);
            Assert.Empty(content.GetVisualDescendants().OfType<ComboBox>());
            Assert.Empty(content.GetVisualDescendants().OfType<TextBox>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Password_only_legacy_clear_hides_warning_but_keeps_confirmation_visible_after_draft_edit()
    {
        var profile = new EnvProfile("legacy", "Legacy", "https://legacy.operations.dynamics.com", "tenant",
            "USMF", "Tier 1", EnvStatus.Disconnected);
        var store = new FakeProfileStore(new[] { profile }) { ActiveId = profile.Id };
        var secrets = new FakeSecretStore();
        secrets.SetSecret(profile.Id, "legacy-password", SecretTarget.DataIntegrator);
        var vm = new ProfilesViewModel(store, secrets);
        var view = new ProfilesView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 850 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            SelectTab(view, window, "Data Integrator");
            var warning = view.FindControl<Border>("LegacyDiWarning");
            var clear = view.FindControl<Button>("ClearLegacyDiPasswordButton");
            Assert.True(warning!.IsEffectivelyVisible);
            Assert.True(clear!.IsEffectivelyVisible);

            vm.ClearLegacyDiPasswordCommand.Execute(null);
            vm.DraftName = "Renamed after clear";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var legacyStatus = view.FindControl<TextBlock>("LegacyDiClearStatus");
            Assert.False(warning.IsEffectivelyVisible);
            Assert.False(clear.IsEffectivelyVisible);
            Assert.NotNull(legacyStatus);
            Assert.Equal("Legacy Data Integrator password cleared.", legacyStatus!.Text);
            Assert.True(legacyStatus.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
