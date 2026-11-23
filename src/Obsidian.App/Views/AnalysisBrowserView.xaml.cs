using System.Windows.Controls;
using System.Windows.Input;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class AnalysisBrowserView : UserControl
{
    public AnalysisBrowserView()
    {
        InitializeComponent();
    }

    private void List_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && DataContext is AnalysisBrowserViewModel viewModel)
        {
            viewModel.Activate(list.SelectedItem);
        }
    }
}
