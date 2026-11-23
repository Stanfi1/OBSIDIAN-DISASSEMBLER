using System.Windows;
using Microsoft.Win32;
using Obsidian.App.Services;
using Obsidian.App.ViewModels;
using Obsidian.Core.Model;
using Serilog;

namespace Obsidian.App.Views;

public sealed class DialogService : IDialogService
{
    private readonly ILogger _logger;

    public DialogService(ILogger logger)
    {
        _logger = logger.ForContext<DialogService>();
    }

    public string? PickBinaryFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open binary",
            Filter = "Portable Executable (*.exe;*.dll;*.sys;*.scr;*.cpl;*.ocx)|*.exe;*.dll;*.sys;*.scr;*.cpl;*.ocx|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };

        var owner = OwnerWindow;
        var result = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public string? PickProjectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Obsidian project",
            Filter = "Obsidian project (*.obsproj)|*.obsproj|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
        };

        var owner = OwnerWindow;
        var result = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public string? PickProjectSaveFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Obsidian project",
            Filter = "Obsidian project (*.obsproj)|*.obsproj",
            AddExtension = true,
            DefaultExt = ".obsproj",
            FileName = suggestedName,
            OverwritePrompt = true,
            RestoreDirectory = true,
        };

        var owner = OwnerWindow;
        var result = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public string? PickPatchedBinaryFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save patched binary copy",
            Filter = "Portable Executable (*.exe;*.dll;*.sys)|*.exe;*.dll;*.sys|All files (*.*)|*.*",
            AddExtension = true,
            FileName = suggestedName,
            OverwritePrompt = true,
            RestoreDirectory = true,
        };

        var owner = OwnerWindow;
        var result = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public byte[]? ShowPatchInstruction(DisassembledInstruction instruction, BinaryImage image)
    {
        if (App.IsDiagnosticsMode)
        {
            _logger.Information("Diagnostics mode suppressed the patch dialog.");
            return null;
        }

        var window = new PatchInstructionWindow(instruction, image);
        ShowDialog(window);
        return window.DialogResult == true ? window.PatchedBytes : null;
    }

    public string? PromptText(string title, string prompt, string initialValue, bool multiline = false)
    {
        if (App.IsDiagnosticsMode)
        {
            _logger.Information("Diagnostics mode suppressed text prompt: {Title}", title);
            return null;
        }

        var window = new TextPromptWindow(title, prompt, initialValue, multiline);
        ShowDialog(window);
        return window.DialogResult == true ? window.Value : null;
    }

    public SaveChangesDecision ShowSaveChanges(string title, string message)
    {
        if (App.IsDiagnosticsMode)
        {
            _logger.Information("Diagnostics mode suppressed save-changes prompt: {Title}", title);
            return SaveChangesDecision.Discard;
        }

        var window = new SaveChangesWindow(title, message);
        ShowDialog(window);
        return window.Decision;
    }

    public void ShowError(string title, string message, Exception? exception = null)
    {
        _logger.Debug("Showing error dialog: {Title}", title);

        if (App.IsDiagnosticsMode)
        {
            _logger.Error(exception, "Diagnostics mode suppressed a modal error: {Title} - {Message}", title, message);
            return;
        }

        var window = new MessageWindow(title, message, exception?.ToString());
        ShowDialog(window);
    }

    public void ShowInformation(string title, string message)
    {
        if (App.IsDiagnosticsMode)
        {
            _logger.Information("Diagnostics mode suppressed an information dialog: {Title} - {Message}", title, message);
            return;
        }

        var window = new MessageWindow(title, message);
        ShowDialog(window);
    }

    public SettingsDialogResult? ShowSettings(AppTheme currentTheme, bool rememberWindowSize)
    {
        var viewModel = new SettingsViewModel(
            currentTheme,
            rememberWindowSize,
            typeof(DialogService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            ShellPaths.DataDirectory);

        var window = new SettingsWindow { DataContext = viewModel };
        ShowDialog(window);

        return window.DialogResult == true
            ? new SettingsDialogResult
            {
                Theme = viewModel.SelectedTheme,
                RememberWindowSize = viewModel.RememberWindowSize,
            }
            : null;
    }

    public AddressResolution? ShowGoToAddress(AddressMap addressMap)
    {
        var window = new GoToAddressWindow(addressMap);
        ShowDialog(window);
        return window.DialogResult == true ? window.Result : null;
    }

    public void ShowAbout()
    {
        var window = new AboutWindow();
        ShowDialog(window);
    }

    private static Window? OwnerWindow =>
        Application.Current?.MainWindow is { IsLoaded: true } mainWindow
            ? mainWindow
            : null;

    private void ShowDialog(Window window)
    {
        var owner = OwnerWindow;
        if (owner is not null && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }
}
