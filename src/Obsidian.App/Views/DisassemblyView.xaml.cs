using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Obsidian.App.ViewModels;

namespace Obsidian.App.Views;

public partial class DisassemblyView : UserControl
{
    private DisassemblyViewModel? _viewModel;

    public DisassemblyView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            if (_viewModel is { SelectedIndex: >= 0 } viewModel)
            {
                SelectIndex(viewModel.SelectedIndex);
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToIndexRequested -= OnScrollToIndexRequested;
        }

        _viewModel = e.NewValue as DisassemblyViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToIndexRequested += OnScrollToIndexRequested;
        }
    }

    private void OnScrollToIndexRequested(int index)
    {
        Dispatcher.BeginInvoke(
            new Action(() => SelectIndex(index)),
            DispatcherPriority.Loaded);
    }

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= InstructionList.Items.Count)
        {
            return;
        }

        InstructionList.SelectedItems.Clear();
        InstructionList.SelectedIndex = index;
        InstructionList.ScrollIntoView(InstructionList.Items[index]);
    }

    private void InstructionList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is not null && InstructionList.SelectedItem is InstructionRowViewModel row)
        {
            _viewModel.SelectRow(row);
        }
    }

    private void GoToBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel?.GoToAddressCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void InstructionList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            GoToBox.Focus();
            GoToBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter
                 && _viewModel?.SelectedInstruction is { HasStaticTarget: true })
        {
            _viewModel.FollowTargetCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void InstructionList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.SelectedInstruction is { HasStaticTarget: true })
        {
            _viewModel.FollowTargetCommand.Execute(null);
        }
    }

    private void CopySelection_OnClick(object sender, RoutedEventArgs e) => CopySelection();

    private void CopySelection()
    {
        if (_viewModel is null)
        {
            return;
        }

        var rows = InstructionList.SelectedItems
            .OfType<InstructionRowViewModel>()
            .OrderBy(row => row.Index)
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
                .Append(row.BytesText.PadRight(44))
                .Append("  ")
                .Append(row.Mnemonic)
                .Append(' ')
                .AppendLine(row.Operands);
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
