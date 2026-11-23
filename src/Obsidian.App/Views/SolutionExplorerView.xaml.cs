using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class SolutionExplorerView : UserControl
{
    private SolutionExplorerViewModel? _viewModel;
    private bool _syncingSelection;

    public SolutionExplorerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => SyncSelection();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as SolutionExplorerViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncSelection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SolutionExplorerViewModel.SelectedNode))
        {
            Dispatcher.BeginInvoke(SyncSelection, DispatcherPriority.Loaded);
        }
    }

    private void ExplorerTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingSelection || _viewModel is null)
        {
            return;
        }

        _viewModel.SelectedNode = e.NewValue as ExplorerNode;
    }

    private void ExplorerTree_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel?.SelectedNode is { } node)
        {
            _viewModel.ActivateNode(node);
        }
    }

    private void ExplorerTree_OnPreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindAncestor<TreeViewItem>(source);
        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void RenameFunction_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestAnnotation(ExplorerAnnotationAction.RenameFunction);

    private void EditComment_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestAnnotation(ExplorerAnnotationAction.EditComment);

    private void AddBookmark_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestAnnotation(ExplorerAnnotationAction.AddBookmark);

    private void RemoveBookmark_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestAnnotation(ExplorerAnnotationAction.RemoveBookmark);

    private void OpenCallGraph_OnClick(object sender, RoutedEventArgs e) =>
        _viewModel?.RequestAnnotation(ExplorerAnnotationAction.OpenCallGraph);

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void SyncSelection()
    {
        if (_viewModel?.SelectedNode is not { } target)
        {
            return;
        }

        foreach (var ancestor in EnumerateAncestors(target))
        {
            ancestor.IsExpanded = true;
        }

        ExplorerTree.UpdateLayout();
        _syncingSelection = true;
        try
        {
            TrySelect(ExplorerTree, target);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static IEnumerable<ExplorerNode> EnumerateAncestors(ExplorerNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            yield return parent;
        }
    }

    private bool TrySelect(ItemsControl parent, ExplorerNode target)
    {
        foreach (var item in parent.Items)
        {
            if (item is not ExplorerNode node)
            {
                continue;
            }

            if (parent.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem container)
            {
                continue;
            }

            if (ReferenceEquals(node, target))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return true;
            }

            if (!IsAncestorOf(node, target))
            {
                continue;
            }

            container.IsExpanded = true;
            container.UpdateLayout();
            if (TrySelect(container, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAncestorOf(ExplorerNode candidate, ExplorerNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
