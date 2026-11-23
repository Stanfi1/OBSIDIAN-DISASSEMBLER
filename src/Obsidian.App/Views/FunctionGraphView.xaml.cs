using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class FunctionGraphView : UserControl
{
    private FunctionGraphViewModel? _viewModel;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;

    public FunctionGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => FitGraph(readable: true);
        Viewport.SizeChanged += (_, _) =>
        {
            if (GraphScale.ScaleX <= 0.001)
            {
                FitGraph(readable: true);
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as FunctionGraphViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Dispatcher.BeginInvoke(new Action(() => FitGraph(readable: true)), DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FunctionGraphViewModel.SelectedFunction)
            or nameof(FunctionGraphViewModel.GraphWidth)
            or nameof(FunctionGraphViewModel.GraphHeight))
        {
            Dispatcher.BeginInvoke(new Action(() => FitGraph(readable: true)), DispatcherPriority.Loaded);
        }
    }

    private void FitButton_OnClick(object sender, RoutedEventArgs e) => FitGraph();

    private void ResetZoomButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetScale(1.0);
        CenterGraph();
    }

    private void OpenDisassemblyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.NavigateToFunctionEntry();
    }

    private void Node_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CfgNodeViewModel node })
        {
            _viewModel?.NavigateToBlock(node);
            e.Handled = true;
        }
    }

    private void Viewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(Viewport);
        _panStartX = GraphTranslate.X;
        _panStartY = GraphTranslate.Y;
        Viewport.CaptureMouse();
        e.Handled = true;
    }

    private void Viewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(Viewport);
        GraphTranslate.X = _panStartX + (current.X - _panStart.X);
        GraphTranslate.Y = _panStartY + (current.Y - _panStart.Y);
    }

    private void Viewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var oldScale = GraphScale.ScaleX;
        var newScale = Math.Clamp(oldScale * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.1, 3.0);
        var position = e.GetPosition(Viewport);

        var graphX = (position.X - GraphTranslate.X) / oldScale;
        var graphY = (position.Y - GraphTranslate.Y) / oldScale;

        GraphScale.ScaleX = newScale;
        GraphScale.ScaleY = newScale;
        GraphTranslate.X = position.X - graphX * newScale;
        GraphTranslate.Y = position.Y - graphY * newScale;

        e.Handled = true;
    }

    private void FitGraph(bool readable = false)
    {
        if (_viewModel is null || Viewport.ActualWidth <= 1 || Viewport.ActualHeight <= 1)
        {
            return;
        }

        var width = Math.Max(1, _viewModel.GraphWidth);
        var height = Math.Max(1, _viewModel.GraphHeight);
        var fittedScale = Math.Clamp(
            Math.Min(
                (Viewport.ActualWidth - 40) / width,
                (Viewport.ActualHeight - 40) / height),
            0.1,
            1.5);
        var scale = readable ? Math.Max(fittedScale, 0.75) : fittedScale;

        SetScale(scale);
        if (readable && fittedScale < 0.75)
        {
            GraphTranslate.X = 16;
            GraphTranslate.Y = 16;
            return;
        }

        GraphTranslate.X = (Viewport.ActualWidth - width * scale) / 2;
        GraphTranslate.Y = (Viewport.ActualHeight - height * scale) / 2;
    }

    private void SetScale(double scale)
    {
        GraphScale.ScaleX = scale;
        GraphScale.ScaleY = scale;
    }

    private void CenterGraph()
    {
        if (_viewModel is null)
        {
            return;
        }

        GraphTranslate.X = (Viewport.ActualWidth - _viewModel.GraphWidth * GraphScale.ScaleX) / 2;
        GraphTranslate.Y = (Viewport.ActualHeight - _viewModel.GraphHeight * GraphScale.ScaleY) / 2;
    }
}
