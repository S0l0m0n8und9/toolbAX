using System.Windows;
using System.Windows.Controls;

namespace FoToolbox.Host.Controls;

public enum PipState
{
    Idle,
    Busy,
    Ok,
    Warning,
    Error,
}

public sealed class StatusPip : Control
{
    static StatusPip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StatusPip),
            new FrameworkPropertyMetadata(typeof(StatusPip)));
    }

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PipState), typeof(StatusPip),
        new PropertyMetadata(PipState.Idle));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(StatusPip),
        new PropertyMetadata(string.Empty));

    public PipState State
    {
        get => (PipState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
