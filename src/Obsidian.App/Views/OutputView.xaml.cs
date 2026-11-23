using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class OutputView : UserControl
{
    private OutputViewModel? _viewModel;

    public OutputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged -= OnEntriesChanged;
        }

        _viewModel = e.NewValue as OutputViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add)
        {
            return;
        }

        // Never scroll the virtualizing panel from inside CollectionChanged:
        // log entries can be appended during a measure/layout pass (for example
        // while a modal error dialog is opening), and a synchronous
        // ScrollIntoView would re-enter the item container generator.
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!IsLoaded || OutputList.Items.Count == 0)
                {
                    return;
                }

                OutputList.ScrollIntoView(OutputList.Items[^1]);
            }),
            DispatcherPriority.Background);
    }
}
