using Obsidian.Core.Model;

namespace Obsidian.App.Services;

public sealed class SettingsDialogResult
{
    public required AppTheme Theme { get; init; }

    public required bool RememberWindowSize { get; init; }
}

public enum SaveChangesDecision
{
    Cancel = 0,
    Discard,
    Save,
}

/// <summary>All modal user interaction is routed through this interface.</summary>
public interface IDialogService
{
    string? PickBinaryFile();

    string? PickProjectFile();

    string? PickProjectSaveFile(string suggestedName);

    string? PickPatchedBinaryFile(string suggestedName);

    string? PromptText(string title, string prompt, string initialValue, bool multiline = false);

    SaveChangesDecision ShowSaveChanges(string title, string message);

    byte[]? ShowPatchInstruction(DisassembledInstruction instruction, BinaryImage image);

    void ShowError(string title, string message, Exception? exception = null);

    void ShowInformation(string title, string message);

    SettingsDialogResult? ShowSettings(AppTheme currentTheme, bool rememberWindowSize);

    AddressResolution? ShowGoToAddress(AddressMap addressMap);

    void ShowAbout();
}
