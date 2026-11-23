using System.Windows.Controls;
using Obsidian.App.ViewModels;
using Obsidian.Core.Model;

namespace Obsidian.App.Views;

public partial class AnalysisView : UserControl
{
    public AnalysisView()
    {
        InitializeComponent();
    }

    private void FindingsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && FindingsList.SelectedItem is PeFinding finding)
        {
            viewModel.SelectFinding(finding);
        }
    }
}
