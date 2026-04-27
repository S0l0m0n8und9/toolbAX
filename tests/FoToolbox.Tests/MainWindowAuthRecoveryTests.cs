using FoToolbox.Core.Auth;
using FoToolbox.Host;
using FoToolbox.Host.ViewModels;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace FoToolbox.Tests;

public class MainWindowAuthRecoveryTests
{
    [Trait("Category", "Auth")]
    [Fact]
    public void ReauthPrompt_Selects_Profiles_Tab_And_Shows_Message()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                try
                {
                    var viewModel = GetPrivateField<MainWindowViewModel>(window, "_vm");
                    var profilesView = GetPrivateField<object>(window, "_profilesView");

                    if (profilesView is null)
                    {
                        var profilesViewType = typeof(MainWindow).Assembly.GetType("FoToolbox.Host.Views.ProfilesView")
                            ?? throw new Xunit.Sdk.XunitException("ProfilesView type was not found.");
                        var profilesViewModelType = typeof(MainWindow).Assembly.GetType("FoToolbox.Host.ViewModels.ProfilesViewModel")
                            ?? throw new Xunit.Sdk.XunitException("ProfilesViewModel type was not found.");

                        var ctor = profilesViewType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                            .Single();
                        var profilesVm = Activator.CreateInstance(
                            profilesViewModelType,
                            "Data Source=:memory:",
                            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                            (Action<FoToolbox.Core.Profiles.ProfileBundle>)(_ => { }))
                            ?? throw new Xunit.Sdk.XunitException("ProfilesViewModel could not be created.");
                        profilesView = ctor.Invoke(new[] { profilesVm });
                        SetPrivateField(window, "_profilesView", profilesView);
                    }

                    viewModel.LoadPlugins(Array.Empty<FoToolbox.Host.Plugins.LoadedPlugin>(), (System.Windows.Controls.UserControl)profilesView);
                    viewModel.Selected = null;

                    string? shownMessage = null;
                    string? shownTitle = null;
                    window.ShowMessageBox = (message, title, _, _) =>
                    {
                        shownMessage = message;
                        shownTitle = title;
                    };

                    var showPrompt = typeof(MainWindow).GetMethod("ShowReauthPrompt", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new Xunit.Sdk.XunitException("ShowReauthPrompt method was not found.");
                    var exception = new AuthRecoveryException(
                        "Finance and Operations",
                        "Finance and Operations needs you to sign in again. Open Profiles and complete interactive sign-in.");

                    showPrompt.Invoke(window, new object[] { exception });

                    Assert.Equal("Profiles", viewModel.Selected?.Name);
                    Assert.Equal(exception.ReauthMessage, shownMessage);
                    Assert.Equal(exception.PromptTitle, shownTitle);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void ReauthPrompt_Starts_Profile_Reauth_Workflow()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                try
                {
                    var viewModel = GetPrivateField<MainWindowViewModel>(window, "_vm");
                    var profilesView = EnsureProfilesView(window);
                    var profilesViewModel = Assert.IsType<ProfilesViewModel>(((System.Windows.FrameworkElement)profilesView).DataContext);

                    viewModel.LoadPlugins(Array.Empty<FoToolbox.Host.Plugins.LoadedPlugin>(), (System.Windows.Controls.UserControl)profilesView);
                    profilesViewModel.AddProfileCommand.Execute(null);

                    var selected = Assert.IsType<ProfileItem>(profilesViewModel.Selected);
                    selected.Environment.Name = "Env";
                    selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
                    selected.Environment.TenantId = "contoso.onmicrosoft.com";
                    selected.FoPrincipal.AuthMode = FoToolbox.Core.Models.AuthMode.BearerToken;

                    window.ShowMessageBox = (_, _, _, _) => { };

                    var showPrompt = typeof(MainWindow).GetMethod("ShowReauthPrompt", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new Xunit.Sdk.XunitException("ShowReauthPrompt method was not found.");
                    var exception = new AuthRecoveryException(
                        "Finance and Operations",
                        "Finance and Operations needs you to sign in again. Open Profiles and complete interactive sign-in.");

                    showPrompt.Invoke(window, new object[] { exception });

                    Assert.Equal("Profiles", viewModel.Selected?.Name);
                    Assert.Contains("Acquiring FO bearer token via Azure CLI", profilesViewModel.Status, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    private static object EnsureProfilesView(MainWindow window)
    {
        var profilesView = GetPrivateField<object>(window, "_profilesView");

        if (profilesView is null)
        {
            var profilesViewType = typeof(MainWindow).Assembly.GetType("FoToolbox.Host.Views.ProfilesView")
                ?? throw new Xunit.Sdk.XunitException("ProfilesView type was not found.");
            var profilesViewModelType = typeof(MainWindow).Assembly.GetType("FoToolbox.Host.ViewModels.ProfilesViewModel")
                ?? throw new Xunit.Sdk.XunitException("ProfilesViewModel type was not found.");

            var ctor = profilesViewType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();
            var profilesVm = Activator.CreateInstance(
                profilesViewModelType,
                "Data Source=:memory:",
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                (Action<FoToolbox.Core.Profiles.ProfileBundle>)(_ => { }))
                ?? throw new Xunit.Sdk.XunitException("ProfilesViewModel could not be created.");
            profilesView = ctor.Invoke(new[] { profilesVm });
            SetPrivateField(window, "_profilesView", profilesView);
        }

        return profilesView;
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Field '{fieldName}' was not found.");
        return (T)field.GetValue(instance)!;
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Field '{fieldName}' was not found.");
        field.SetValue(instance, value);
    }
}
