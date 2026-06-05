using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class QueryBuilderView : UserControl
{
    private QueryBuilderViewModel? _vm;

    public QueryBuilderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // The result grid's columns are dynamic (= the selected $select fields), so they're built here
    // rather than declared in XAML — rebuilt whenever a run replaces ResultColumns.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as QueryBuilderViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildColumns();
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
}
