using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class CallGraphView : UserControl
{
    public CallGraphView()
    {
        InitializeComponent();
    }

    private void Node_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CallGraphNodeViewModel node } &&
            DataContext is CallGraphViewModel viewModel)
        {
            viewModel.SelectNode(node);
            e.Handled = true;
        }
    }
}
