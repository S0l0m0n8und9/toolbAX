using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class QueryBuilderView : UserControl
{
    private QueryBuilderViewModel? _vm;

    public QueryBuilderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Track the VM only while attached so the cached VM doesn't retain a detached view.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // The result grid's columns are dynamic (= the selected $select fields), so they're built here
    // rather than declared in XAML — rebuilt whenever a run replaces ResultColumns.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        _vm = DataContext as QueryBuilderViewModel;
        Subscribe();
        RebuildColumns();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Subscribe();
        RebuildColumns();
        // Kick off the live $metadata fetch; the cached VM only refetches if the catalogue changes.
        _vm?.InitializeCommand.Execute(null);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => Unsubscribe();

    // Subscribe is idempotent (detach-then-attach), so Loaded + DataContextChanged can't double up.
    private void Subscribe()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void Unsubscribe()
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueryBuilderViewModel.ResultColumns))
        {
            RebuildColumns();
        }
    }

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultsGrid");
        if (grid is null)
        {
            return;
        }

        grid.Columns.Clear();
        if (_vm is null)
        {
            return;
        }

        foreach (var column in _vm.ResultColumns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column,
                Binding = new Binding($"[{column}]"),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }
    }

    // The Results DataGrid lives inside a TabItem, so the TabControl only realizes it when the Results
    // tab is first shown. Rebuild its dynamic columns when it attaches — the ResultColumns-change
    // trigger alone can fire while another tab is active, when FindControl("ResultsGrid") returns null.
    private void OnResultsGridAttached(object? sender, VisualTreeAttachmentEventArgs e) => RebuildColumns();
}
