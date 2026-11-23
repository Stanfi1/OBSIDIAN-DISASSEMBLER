using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class HexEditorView : UserControl
{
    private HexEditorViewModel? _viewModel;
    private bool _programmaticSelection;

    public HexEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            if (_viewModel is not null && HexList.Items.Count > 0 && HexList.SelectedItems.Count == 0)
            {
                SelectRow((int)Math.Clamp(_viewModel.SelectedOffset / 16, 0, HexList.Items.Count - 1));
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToRowRequested -= OnScrollToRowRequested;
        }

        _viewModel = e.NewValue as HexEditorViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToRowRequested += OnScrollToRowRequested;
        }
    }

    private void OnScrollToRowRequested(int rowIndex)
    {
        Dispatcher.BeginInvoke(
            new Action(() => SelectRow(rowIndex)),
            DispatcherPriority.Loaded);
    }

    private void SelectRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= HexList.Items.Count)
        {
            return;
        }

        _programmaticSelection = HexList.SelectedIndex != rowIndex;
        HexList.SelectedItems.Clear();
        HexList.SelectedIndex = rowIndex;
        HexList.ScrollIntoView(HexList.Items[rowIndex]);

        Dispatcher.BeginInvoke(
            new Action(() => _programmaticSelection = false),
            DispatcherPriority.Background);
    }

    private void HexList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var rows = HexList.SelectedItems
            .OfType<HexRowViewModel>()
            .ToArray();
        _viewModel.NotifySelection(rows, preserveOffset: _programmaticSelection);
        _programmaticSelection = false;
    }

    private void HexList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void SearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _viewModel?.FindPreviousCommand.Execute(null);
        }
        else
        {
            _viewModel?.FindNextCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void GoToBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel?.GoToOffsetCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CopySelection_OnClick(object sender, RoutedEventArgs e) => CopySelection();

    private void CopySelection()
    {
        if (_viewModel is null)
        {
            return;
        }

        var rows = HexList.SelectedItems
            .OfType<HexRowViewModel>()
            .OrderBy(row => row.Offset)
            .ToArray();

        if (rows.Length == 0)
        {
            _viewModel.ReportNoSelection();
            return;
        }

        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(row.AddressText)
                .Append("  ")
                .Append(row.HexText)
                .Append("  |")
                .Append(row.AsciiText)
                .AppendLine("|");
        }

        try
        {
            Clipboard.SetText(text.ToString());
            _viewModel.ReportCopied(rows.Length);
        }
        catch (Exception ex)
        {
            _viewModel.ReportClipboardError("Clipboard is unavailable: " + ex.Message);
        }
    }
}
