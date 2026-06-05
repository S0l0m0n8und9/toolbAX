using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class DualWriteMapView : UserControl
{
    private const double SparkWidth = 150;
    private const double SparkHeight = 32;

    private DualWriteMapViewModel? _vm;

    public DualWriteMapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // The 24h activity sparkline is drawn as a StreamGeometry on a Path — rebuilt whenever the
    // selected map's activity series changes (kept out of XAML since it's pure geometry).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as DualWriteMapViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildSparkline();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DualWriteMapViewModel.Activity))
        {
            RebuildSparkline();
        }
    }

    private void RebuildSparkline()
    {
        var path = this.FindControl<Path>("SparkPath");
        if (path is null)
        {
            return;
        }

        var data = _vm?.Activity;
        if (data is null || data.Count < 2)
        {
            path.Data = null;
            return;
        }

        path.Data = BuildSparkGeometry(data);
    }

    private static Geometry BuildSparkGeometry(IReadOnlyList<double> data)
    {
        var max = Math.Max(data.Max(), 1);
        var step = SparkWidth / (data.Count - 1);

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(0, SparkHeight - data[0] / max * SparkHeight), isFilled: false);
        for (var i = 1; i < data.Count; i++)
        {
            ctx.LineTo(new Point(i * step, SparkHeight - data[i] / max * SparkHeight));
        }

        ctx.EndFigure(isClosed: false);
        return geometry;
    }
}
