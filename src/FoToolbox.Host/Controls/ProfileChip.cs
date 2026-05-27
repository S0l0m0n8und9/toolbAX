using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FoToolbox.Host.ViewModels;

namespace FoToolbox.Host.Controls;

internal sealed class ProfileChip : Control
{
    static ProfileChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ProfileChip),
            new FrameworkPropertyMetadata(typeof(ProfileChip)));
    }

    public static readonly DependencyProperty ProfilesProperty = DependencyProperty.Register(
        nameof(Profiles), typeof(IEnumerable), typeof(ProfileChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActiveProfileNameProperty = DependencyProperty.Register(
        nameof(ActiveProfileName), typeof(string), typeof(ProfileChip),
        new PropertyMetadata("No profile"));

    public static readonly DependencyProperty ConnectionStatusProperty = DependencyProperty.Register(
        nameof(ConnectionStatus), typeof(ConnectionStatus), typeof(ProfileChip),
        new PropertyMetadata(ConnectionStatus.Unknown));

    public static readonly DependencyProperty SetActiveProfileCommandProperty = DependencyProperty.Register(
        nameof(SetActiveProfileCommand), typeof(ICommand), typeof(ProfileChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty OpenProfilesCommandProperty = DependencyProperty.Register(
        nameof(OpenProfilesCommand), typeof(ICommand), typeof(ProfileChip),
        new PropertyMetadata(null));

    public IEnumerable? Profiles
    {
        get => (IEnumerable?)GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public string ActiveProfileName
    {
        get => (string)GetValue(ActiveProfileNameProperty);
        set => SetValue(ActiveProfileNameProperty, value);
    }

    public ConnectionStatus ConnectionStatus
    {
        get => (ConnectionStatus)GetValue(ConnectionStatusProperty);
        set => SetValue(ConnectionStatusProperty, value);
    }

    public ICommand? SetActiveProfileCommand
    {
        get => (ICommand?)GetValue(SetActiveProfileCommandProperty);
        set => SetValue(SetActiveProfileCommandProperty, value);
    }

    public ICommand? OpenProfilesCommand
    {
        get => (ICommand?)GetValue(OpenProfilesCommandProperty);
        set => SetValue(OpenProfilesCommandProperty, value);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (OpenProfilesCommand?.CanExecute(null) == true)
        {
            OpenProfilesCommand.Execute(null);
            e.Handled = true;
        }
    }
}
