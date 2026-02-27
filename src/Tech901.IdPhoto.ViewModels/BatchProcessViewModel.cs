using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class BatchProcessViewModel : ObservableObject, IDisposable
{
    private readonly IImageProcessingService _imageProcessing;
    private readonly IFaceDetectionService _faceDetection;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<BatchProcessViewModel> _logger;
    private CancellationTokenSource? _processCts;

    [ObservableProperty]
    private string _inputFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _processedFiles;

    [ObservableProperty]
    private bool _isProcessing;

    public ObservableCollection<BatchResult> Results { get; } = [];

    /// <summary>Raised when the user wants to go back to admin.</summary>
    public event Action? BackRequested;

    /// <summary>Raised when a folder dialog should be shown.</summary>
    public event Func<Task<string?>>? FolderRequested;

    public BatchProcessViewModel(
        IImageProcessingService imageProcessing,
        IFaceDetectionService faceDetection,
        IDispatcher dispatcher,
        ILogger<BatchProcessViewModel> logger)
    {
        _imageProcessing = imageProcessing;
        _faceDetection = faceDetection;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    [RelayCommand]
    private void Back()
    {
        BackRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SelectInputFolderAsync()
    {
        var path = FolderRequested is not null ? await FolderRequested.Invoke() : null;
        if (!string.IsNullOrWhiteSpace(path))
            InputFolder = path;
    }

    [RelayCommand]
    private async Task SelectOutputFolderAsync()
    {
        var path = FolderRequested is not null ? await FolderRequested.Invoke() : null;
        if (!string.IsNullOrWhiteSpace(path))
            OutputFolder = path;
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task ProcessAsync(CancellationToken ct)
    {
        _processCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _processCts.Token;

        _dispatcher.Invoke(() => IsProcessing = true);
        Results.Clear();

        try
        {
            var files = Directory.GetFiles(InputFolder, "*.*")
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            _dispatcher.Invoke(() =>
            {
                TotalFiles = files.Length;
                ProcessedFiles = 0;
                Progress = 0;
            });

            _logger.LogInformation("Batch processing {Count} files", files.Length);

            var cropSettings = new CropSettings(600, 600, 1.5, "jpg");

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                var filename = Path.GetFileName(file);
                try
                {
                    var imageData = await File.ReadAllBytesAsync(file, token);
                    var face = await _faceDetection.DetectFaceAsync(imageData, token);

                    if (face is null)
                    {
                        Results.Add(new BatchResult(filename, false, "No face detected"));
                        continue;
                    }

                    var cropped = await _imageProcessing.CropAndResizeAsync(imageData, face, cropSettings, token);
                    var outputPath = Path.Combine(OutputFolder, filename);
                    await _imageProcessing.SaveImageAsync(cropped, outputPath, token);

                    Results.Add(new BatchResult(filename, true, "OK"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process {File}", filename);
                    Results.Add(new BatchResult(filename, false, ex.Message));
                }

                _dispatcher.Invoke(() =>
                {
                    ProcessedFiles++;
                    Progress = TotalFiles > 0 ? (int)(ProcessedFiles * 100.0 / TotalFiles) : 0;
                });
            }

            _logger.LogInformation("Batch processing complete: {Processed}/{Total}", ProcessedFiles, TotalFiles);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Batch processing cancelled");
        }
        finally
        {
            _dispatcher.Invoke(() => IsProcessing = false);
        }
    }

    private bool CanProcess() =>
        !IsProcessing
        && !string.IsNullOrWhiteSpace(InputFolder)
        && !string.IsNullOrWhiteSpace(OutputFolder);

    partial void OnIsProcessingChanged(bool value)
    {
        ProcessCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnInputFolderChanged(string value) => ProcessCommand.NotifyCanExecuteChanged();
    partial void OnOutputFolderChanged(string value) => ProcessCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(IsProcessing))]
    private void Cancel()
    {
        _logger.LogInformation("Batch processing cancel requested");
        _processCts?.Cancel();
    }

    public void Dispose()
    {
        _processCts?.Cancel();
        _processCts?.Dispose();
        _processCts = null;
    }
}

public record BatchResult(string FileName, bool Success, string Message);
