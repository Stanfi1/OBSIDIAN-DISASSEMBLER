using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Obsidian.Core.Model;
using Obsidian.Core.Services;
using Serilog;

namespace Obsidian.App.ViewModels;

/// <summary>
/// Owns the background analysis pass. It waits for the disassembly,
/// reports progress, and hands the immutable result to the shell.
/// </summary>
public sealed partial class AnalysisViewModel : ObservableObject
{
    private readonly BinaryImage _image;
    private readonly DisassemblyViewModel _disassembly;
    private readonly IBinaryAnalysisEngine _engine;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _loadTask;

    public AnalysisViewModel(
        BinaryImage image,
        DisassemblyViewModel disassembly,
        IBinaryAnalysisEngine engine,
        ILogger logger)
    {
        _image = image;
        _disassembly = disassembly;
        _engine = engine;
        _logger = logger;
    }

    [ObservableProperty]
    private AnalysisResult? _result;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusText = "Waiting for disassembly…";

    [ObservableProperty]
    private string? _message;

    public event Action<AnalysisResult>? Completed;

    public event Action<string>? Failed;

    public bool HasResult => Result is { FunctionCount: > 0 };

    public Task LoadAsync() => _loadTask ??= LoadCoreAsync();

    public Task WaitForCompletionAsync() => _loadTask ?? LoadAsync();

    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void CancelAnalysis() => Cancel();

    private async Task LoadCoreAsync()
    {
        try
        {
            await _disassembly.WaitForCompletionAsync();

            if (_disassembly.Result is not { HasInstructions: true } disassembly)
            {
                Message = "Analysis requires a successful disassembly.";
                StatusText = Message;
                ProgressPercent = 100;
                Failed?.Invoke(Message);
                return;
            }

            IsBusy = true;
            ProgressPercent = 0;
            StatusText = "Analyzing functions, references and strings…";
            _cts = new CancellationTokenSource();

            var progress = new Progress<LoadProgress>(update =>
            {
                if (update.Percent is { } percent)
                {
                    ProgressPercent = percent;
                }

                StatusText = update.Message;
            });

            var result = await _engine.AnalyzeAsync(_image, disassembly, progress, _cts.Token);
            Result = result;
            OnPropertyChanged(nameof(HasResult));
            Message = result.Message;
            StatusText = result.HasFunctions
                ? $"Analysis complete: {result.FunctionCount:N0} functions, {result.CrossReferenceCount:N0} references, {result.StringCount:N0} strings."
                : result.Message ?? "Analysis produced no functions.";

            _logger.Information(
                "Analysis complete: {FunctionCount} functions, {XrefCount} cross references, {StringCount} strings",
                result.FunctionCount,
                result.CrossReferenceCount,
                result.StringCount);

            Completed?.Invoke(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Binary analysis failed");
            Message = "Analysis failed: " + ex.Message;
            StatusText = Message;
            Failed?.Invoke(Message);
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
