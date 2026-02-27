using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Tech901.IdPhoto.ViewModels;

public partial class ReviewViewModel : ObservableObject
{
    private readonly ILogger<ReviewViewModel> _logger;

    [ObservableProperty]
    private byte[] _capturedImage = [];

    public event Action<byte[]>? Accepted;
    public event Action? RetakeRequested;

    public ReviewViewModel(ILogger<ReviewViewModel> logger)
    {
        _logger = logger;
    }

    public void LoadImage(byte[] imageData)
    {
        CapturedImage = imageData;
    }

    [RelayCommand]
    private void Accept()
    {
        _logger.LogInformation("Photo accepted");
        Accepted?.Invoke(CapturedImage);
    }

    [RelayCommand]
    private void Retake()
    {
        _logger.LogInformation("Retake requested");
        RetakeRequested?.Invoke();
    }
}
