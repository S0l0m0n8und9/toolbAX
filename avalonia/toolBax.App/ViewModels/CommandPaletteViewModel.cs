using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Models;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Ctrl+K command palette: filter the tool list by name and invoke a selection to navigate
/// (control-map §0.5). Pure VM logic — headless-testable.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<NavTool> _all;
    private readonly Action<NavTool> _invoke;

    [ObservableProperty]
    private string _query = string.Empty;

    public ObservableCollection<NavTool> FilteredCommands { get; } = new();

    public CommandPaletteViewModel(IReadOnlyList<NavTool> tools, Action<NavTool> invoke)
    {
        _all = tools;
        _invoke = invoke;
        Refilter();
    }

    partial void OnQueryChanged(string value) => Refilter();

    /// <summary>Clears the query (and re-shows all commands) when the palette is opened.</summary>
    public void Reset() => Query = string.Empty;

    private void Refilter()
    {
        FilteredCommands.Clear();
        foreach (var tool in _all)
        {
            if (string.IsNullOrWhiteSpace(Query) ||
                tool.Title.Contains(Query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCommands.Add(tool);
            }
        }
    }

    [RelayCommand]
    private void Invoke(NavTool? tool)
    {
        if (tool is not null)
        {
            _invoke(tool);
        }
    }
}
