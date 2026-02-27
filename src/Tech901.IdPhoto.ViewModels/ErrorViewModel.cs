using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Tech901.IdPhoto.ViewModels;

public partial class ErrorViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<ErrorViewModel> _logger;
    private CancellationTokenSource? _autoRecoverCts;

    [ObservableProperty]
    private string _errorMessage = "An unexpected error occurred.";

    public event Action? ReturnToIdle;

    public int AutoRecoverSeconds { get; init; } = 10;

    public ErrorViewModel(ILogger<ErrorViewModel> logger)
    {
        _logger = logger;
    }

    public async Task StartAutoRecoverAsync()
    {
        _autoRecoverCts?.Cancel();
        _autoRecoverCts?.Dispose();
        _autoRecoverCts = new CancellationTokenSource();
        var ct = _autoRecoverCts.Token;

        try
        {
            _logger.LogInformation("Auto-recover to idle in {Seconds}s", AutoRecoverSeconds);
            await Task.Delay(TimeSpan.FromSeconds(AutoRecoverSeconds), ct);
            ReturnToIdle?.Invoke();
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private void Dismiss()
    {
        ReturnToIdle?.Invoke();
    }

    public void Dispose()
    {
        _autoRecoverCts?.Cancel();
        _autoRecoverCts?.Dispose();
        _autoRecoverCts = null;
    }
}
