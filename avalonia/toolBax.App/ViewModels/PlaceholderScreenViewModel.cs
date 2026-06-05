namespace ToolBax.App.ViewModels;

/// <summary>Stand-in content for tools whose screen isn't built yet (shows the tool title).</summary>
public sealed class PlaceholderScreenViewModel
{
    public PlaceholderScreenViewModel(string title) => Title = title;

    public string Title { get; }
}
