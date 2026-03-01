using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private readonly IRosterService _roster;
    private readonly IAudioDeviceEnumerator _audioDevices;
    private readonly ICameraService _camera;
    private readonly IDispatcher _dispatcher;
    private readonly KioskFlowViewModel _kioskFlow;
    private readonly ISpeechService _speech;
    private readonly IFaceDetectionService _faceDetection;
    private readonly ILogger<AdminViewModel> _logger;

    [ObservableProperty]
    private string? _rosterFilePath;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _filenameTemplate = "{LastName}_{FirstName}_{StudentId}";

    [ObservableProperty]
    private string _outputFormat = "jpg";

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private RosterImportResult? _lastImportResult;

    [ObservableProperty]
    private bool _isImportResultVisible;

    [ObservableProperty]
    private string? _importErrorMessage;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMicrophoneDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedSpeakerDevice;

    [ObservableProperty]
    private CameraDeviceInfo? _selectedCameraDevice;

    [ObservableProperty]
    private byte[]? _cameraPreviewFrame;

    [ObservableProperty]
    private bool _isCameraPreviewActive;

    [ObservableProperty]
    private bool _isCameraSwitching;

    [ObservableProperty]
    private bool _isServiceStatusVisible;

    /// <summary>Whether the Azure Speech Service is active (vs. NullSpeechService fallback).</summary>
    public bool IsSpeechAvailable => _speech.IsAvailable;

    /// <summary>Whether the Azure Face API is active (vs. NullFaceDetectionService fallback).</summary>
    public bool IsFaceDetectionAvailable => _faceDetection.IsAvailable;

    /// <summary>
    /// Describes the speech service fallback behavior when Azure is not configured.
    /// </summary>
    public string SpeechFallbackDescription => IsSpeechAvailable
        ? "Azure Cognitive Services Speech SDK"
        : "Disabled — participants type names instead of speaking";

    /// <summary>
    /// Describes the face detection fallback behavior when Azure is not configured.
    /// </summary>
    public string FaceDetectionFallbackDescription => IsFaceDetectionAvailable
        ? "Azure AI Face API with landmark detection"
        : "Disabled — photos use center-crop instead of face-crop";

    /// <summary>Presence detection is always local (OpenCvSharp HOG detector).</summary>
    public string PresenceDetectionDescription => "Local OpenCvSharp HOG detector (no Azure required)";

    public ObservableCollection<StudentStatusItem> Students { get; } = [];
    public ObservableCollection<AudioDeviceInfo> MicrophoneDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> SpeakerDevices { get; } = [];
    public ObservableCollection<CameraDeviceInfo> CameraDevices { get; } = [];

    /// <summary>Raised when the admin wants to exit back to kiosk.</summary>
    public event Action? ExitRequested;

    /// <summary>Raised when the admin wants to open batch processing.</summary>
    public event Action? BatchProcessRequested;

    /// <summary>Raised when a file dialog should be shown to pick a roster CSV.</summary>
    public event Func<Task<string?>>? RosterFileRequested;

    /// <summary>Raised when a file dialog should be shown to pick an export path.</summary>
    public event Func<Task<string?>>? ExportPathRequested;

    /// <summary>Raised when a folder dialog should be shown to pick the output directory.</summary>
    public event Func<Task<string?>>? OutputFolderRequested;

    public AdminViewModel(
        IRosterService roster,
        IAudioDeviceEnumerator audioDevices,
        ICameraService camera,
        IDispatcher dispatcher,
        KioskFlowViewModel kioskFlow,
        ISpeechService speech,
        IFaceDetectionService faceDetection,
        ILogger<AdminViewModel> logger)
    {
        _roster = roster;
        _audioDevices = audioDevices;
        _camera = camera;
        _dispatcher = dispatcher;
        _kioskFlow = kioskFlow;
        _speech = speech;
        _faceDetection = faceDetection;
        _logger = logger;

        RefreshDevices();
        RefreshCameraDevices();
    }

    [RelayCommand]
    private void ToggleServiceStatus()
    {
        IsServiceStatusVisible = !IsServiceStatusVisible;
    }

    [RelayCommand]
    private void ExitAdmin()
    {
        StopCameraPreview();
        ExitRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenBatch()
    {
        BatchProcessRequested?.Invoke();
    }

    [RelayCommand]
    private async Task ImportRosterAsync(CancellationToken ct)
    {
        var path = RosterFileRequested is not null
            ? await RosterFileRequested.Invoke()
            : null;

        if (string.IsNullOrWhiteSpace(path))
            return;

        // Reset previous results
        LastImportResult = null;
        ImportErrorMessage = null;
        IsImportResultVisible = false;

        try
        {
            _logger.LogInformation("Importing roster from {Path}", path);
            var result = await _roster.LoadRosterAsync(path, ct);
            RosterFilePath = path;
            LastImportResult = result;
            IsImportResultVisible = true;
            RefreshStudents();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "CSV structure error during import");
            ImportErrorMessage = ex.Message;
            IsImportResultVisible = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error importing roster from {Path}", path);
            ImportErrorMessage = $"Unexpected error: {ex.Message}";
            IsImportResultVisible = true;
        }
    }

    [RelayCommand]
    private void DismissImportResult()
    {
        IsImportResultVisible = false;
        LastImportResult = null;
        ImportErrorMessage = null;
    }

    [RelayCommand]
    private void StartSession()
    {
        _logger.LogInformation("Session started");
        RefreshStudents();
    }

    [RelayCommand]
    private async Task EndSessionAsync(CancellationToken ct)
    {
        _logger.LogInformation("Session ended");
        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            var sessionPath = Path.Combine(OutputDirectory, "session.json");
            await _roster.SaveSessionStateAsync(sessionPath, ct);
        }
    }

    [RelayCommand]
    private async Task ExportReportAsync(CancellationToken ct)
    {
        var path = ExportPathRequested is not null
            ? await ExportPathRequested.Invoke()
            : null;

        if (string.IsNullOrWhiteSpace(path))
            return;

        _logger.LogInformation("Exporting report to {Path}", path);
        await _roster.SaveSessionStateAsync(path, ct);
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        var path = OutputFolderRequested is not null
            ? await OutputFolderRequested.Invoke()
            : null;

        if (!string.IsNullOrWhiteSpace(path))
            OutputDirectory = path;
    }

    [RelayCommand]
    private void InsertToken(string? token)
    {
        if (!string.IsNullOrEmpty(token))
            FilenameTemplate += token;
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        MicrophoneDevices.Clear();
        foreach (var device in _audioDevices.GetInputDevices())
            MicrophoneDevices.Add(device);

        SpeakerDevices.Clear();
        foreach (var device in _audioDevices.GetOutputDevices())
            SpeakerDevices.Add(device);

        SelectedMicrophoneDevice ??= MicrophoneDevices.FirstOrDefault();
        SelectedSpeakerDevice ??= SpeakerDevices.FirstOrDefault();

        _logger.LogInformation("Enumerated {InputCount} input and {OutputCount} output audio devices",
            MicrophoneDevices.Count, SpeakerDevices.Count);
    }

    [RelayCommand]
    private void RefreshCameraDevices()
    {
        CameraDevices.Clear();
        var deviceNames = _camera.EnumerateDevices();
        for (var i = 0; i < deviceNames.Count; i++)
            CameraDevices.Add(new CameraDeviceInfo(i, deviceNames[i]));

        // Pre-select the currently active device
        var currentIndex = _kioskFlow.CameraDeviceIndex;
        SelectedCameraDevice = CameraDevices.FirstOrDefault(d => d.DeviceIndex == currentIndex)
            ?? CameraDevices.FirstOrDefault();

        _logger.LogInformation("Enumerated {Count} camera devices, current index {Index}",
            CameraDevices.Count, currentIndex);
    }

    [RelayCommand]
    private void ToggleCameraPreview()
    {
        if (IsCameraPreviewActive)
        {
            StopCameraPreview();
        }
        else
        {
            _camera.FrameCaptured += OnCameraPreviewFrame;
            IsCameraPreviewActive = true;
        }
    }

    [RelayCommand]
    private async Task SwitchCameraAsync()
    {
        if (SelectedCameraDevice is null)
            return;

        var newIndex = SelectedCameraDevice.DeviceIndex;
        if (newIndex == _kioskFlow.CameraDeviceIndex)
            return;

        IsCameraSwitching = true;
        try
        {
            _logger.LogInformation("Switching camera from index {Old} to {New}",
                _kioskFlow.CameraDeviceIndex, newIndex);

            await _camera.StopAsync();
            await _camera.StartAsync(newIndex);
            _kioskFlow.CameraDeviceIndex = newIndex;

            _logger.LogInformation("Camera switched to device index {Index}", newIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch camera to index {Index}, reverting", newIndex);
            // Attempt to restart with the previous device
            try
            {
                await _camera.StartAsync(_kioskFlow.CameraDeviceIndex);
            }
            catch (Exception revertEx)
            {
                _logger.LogError(revertEx, "Failed to revert camera to index {Index}",
                    _kioskFlow.CameraDeviceIndex);
            }

            // Re-select the current device in the UI
            SelectedCameraDevice = CameraDevices
                .FirstOrDefault(d => d.DeviceIndex == _kioskFlow.CameraDeviceIndex);
        }
        finally
        {
            IsCameraSwitching = false;
        }
    }

    partial void OnSelectedCameraDeviceChanged(CameraDeviceInfo? value)
    {
        if (value is not null && value.DeviceIndex != _kioskFlow.CameraDeviceIndex)
            FireAndForget(SwitchCameraAsync());
    }

    [RelayCommand]
    private void TestSpeaker()
    {
        FireAndForget(_speech.SpeakAsync("This is a test of the speaker output."));
    }

    private void OnCameraPreviewFrame(object? sender, byte[] frame)
    {
        _dispatcher.Invoke(() => CameraPreviewFrame = frame);
    }

    private void StopCameraPreview()
    {
        _camera.FrameCaptured -= OnCameraPreviewFrame;
        IsCameraPreviewActive = false;
        CameraPreviewFrame = null;
    }

    private void RefreshStudents()
    {
        Students.Clear();
        var all = _roster.GetAllStudents();
        foreach (var s in all)
        {
            Students.Add(new StudentStatusItem(
                s,
                _roster.IsCompleted(s.StudentId)));
        }

        TotalCount = all.Count;
        CompletedCount = _roster.GetCompletedStudents().Count;
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}

public record StudentStatusItem(Student Student, bool IsCompleted);
