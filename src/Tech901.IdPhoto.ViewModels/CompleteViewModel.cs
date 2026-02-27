using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Tech901.IdPhoto.ViewModels;

public partial class CompleteViewModel : ObservableObject, IDisposable
{
    private readonly ILogger<CompleteViewModel> _logger;
    private CancellationTokenSource? _autoReturnCts;

    [ObservableProperty]
    private string _studentName = string.Empty;

    public event Action? ReturnToIdle;

    public int AutoReturnSeconds { get; init; } = 5;

    public CompleteViewModel(ILogger<CompleteViewModel> logger)
    {
        _logger = logger;
    }

    public async Task StartAutoReturnAsync()
    {
        _autoReturnCts?.Cancel();
        _autoReturnCts?.Dispose();
        _autoReturnCts = new CancellationTokenSource();
        var ct = _autoReturnCts.Token;

        try
        {
            _logger.LogInformation("Auto-return to idle in {Seconds}s for {Student}", AutoReturnSeconds, StudentName);
            await Task.Delay(TimeSpan.FromSeconds(AutoReturnSeconds), ct);
            ReturnToIdle?.Invoke();
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _autoReturnCts?.Cancel();
        _autoReturnCts?.Dispose();
        _autoReturnCts = null;
    }
}
