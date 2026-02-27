using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class KioskFlowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ICameraService _camera;
    private readonly IRosterService _roster;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IFaceDetectionService _faceDetection;
    private readonly ISpeechService _speech;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<KioskFlowViewModel> _logger;

    private Student? _currentStudent;
    private byte[]? _capturedPhoto;

    private const string AdminPin = "9019";

    [ObservableProperty]
    private KioskState _currentState = KioskState.Idle;

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    [ObservableProperty]
    private byte[]? _currentFrame;

    [ObservableProperty]
    private bool _isAdminMode;

    public KioskFlowViewModel(
        IServiceProvider services,
        ICameraService camera,
        IRosterService roster,
        IImageProcessingService imageProcessing,
        IFaceDetectionService faceDetection,
        ISpeechService speech,
        IDispatcher dispatcher,
        ILogger<KioskFlowViewModel> logger)
    {
        _services = services;
        _camera = camera;
        _roster = roster;
        _imageProcessing = imageProcessing;
        _faceDetection = faceDetection;
        _speech = speech;
        _dispatcher = dispatcher;
        _logger = logger;

        _camera.FrameCaptured += OnFrameCaptured;
    }

    private void OnFrameCaptured(object? sender, byte[] frame)
    {
        _dispatcher.Invoke(() => CurrentFrame = frame);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing kiosk flow");
        await _camera.StartAsync(0, ct);
        TransitionTo(KioskState.Idle);
    }

    [ObservableProperty]
    private bool _isPinDialogVisible;

    [ObservableProperty]
    private string? _pinEntry;

    [RelayCommand]
    private void ToggleAdmin()
    {
        if (IsAdminMode)
        {
            IsAdminMode = false;
            _logger.LogInformation("Admin mode deactivated");
            TransitionTo(KioskState.Idle);
            return;
        }

        // Show PIN dialog
        PinEntry = null;
        IsPinDialogVisible = true;
    }

    [RelayCommand]
    private void SubmitPin()
    {
        if (PinEntry == AdminPin)
        {
            IsPinDialogVisible = false;
            IsAdminMode = true;
            _logger.LogInformation("Admin mode activated");
            var admin = Resolve<AdminViewModel>();
            admin.ExitRequested += () => ToggleAdmin();
            admin.BatchProcessRequested += () => ShowBatchProcess();
            SetCurrentViewModel(admin);
        }
        else
        {
            _logger.LogWarning("Invalid admin PIN attempt");
            PinEntry = null;
        }
    }

    [RelayCommand]
    private void CancelPin()
    {
        IsPinDialogVisible = false;
        PinEntry = null;
    }

    public void TransitionToIdle()
    {
        TransitionTo(KioskState.Idle);
    }

    private void ShowBatchProcess()
    {
        var batch = Resolve<BatchProcessViewModel>();
        batch.BackRequested += () =>
        {
            // Return to admin
            var admin = Resolve<AdminViewModel>();
            admin.ExitRequested += () => ToggleAdmin();
            admin.BatchProcessRequested += () => ShowBatchProcess();
            SetCurrentViewModel(admin);
        };
        SetCurrentViewModel(batch);
    }

    private void TransitionTo(KioskState newState)
    {
        var previous = CurrentState;
        CurrentState = newState;
        _logger.LogInformation("State transition: {From} -> {To}", previous, newState);

        DisposeCurrentChild();

        switch (newState)
        {
            case KioskState.Idle:
                _currentStudent = null;
                _capturedPhoto = null;
                var idle = Resolve<IdleViewModel>();
                idle.FaceDetected += () => TransitionTo(KioskState.NameCapture);
                FireAndForget(idle.StartPollingAsync());
                SetCurrentViewModel(idle);
                break;

            case KioskState.NameCapture:
                var nameCapture = Resolve<NameCaptureViewModel>();
                nameCapture.NameSubmitted += OnNameSubmitted;
                SetCurrentViewModel(nameCapture);
                FireAndForget(StartNameCaptureAsync(nameCapture));
                break;

            case KioskState.RosterMatch:
                FireAndForget(_speech.SpeakAsync("We found a few matches. Please select your name."));
                break;

            case KioskState.Positioning:
                FireAndForget(_speech.SpeakAsync("Please look at the camera and center your face."));
                var positioning = Resolve<PositioningViewModel>();
                positioning.CaptureRequested += () => TransitionTo(KioskState.Capture);
                FireAndForget(positioning.StartGuidanceAsync());
                SetCurrentViewModel(positioning);
                break;

            case KioskState.Capture:
                var capture = Resolve<CaptureViewModel>();
                capture.PhotoCaptured += OnPhotoCaptured;
                FireAndForget(capture.RunCountdownAsync());
                SetCurrentViewModel(capture);
                break;

            case KioskState.Review:
                FireAndForget(_speech.SpeakAsync("Here's your photo. Accept it or retake."));
                var review = Resolve<ReviewViewModel>();
                review.LoadImage(_capturedPhoto!);
                review.Accepted += OnPhotoAccepted;
                review.RetakeRequested += () => TransitionTo(KioskState.Positioning);
                SetCurrentViewModel(review);
                break;

            case KioskState.Processing:
                FireAndForget(ProcessPhotoAsync());
                break;

            case KioskState.Complete:
                var studentName = _currentStudent?.PreferredName
                    ?? _currentStudent?.FirstName
                    ?? "Student";
                FireAndForget(_speech.SpeakAsync($"All done! Thank you, {studentName}."));
                var complete = Resolve<CompleteViewModel>();
                complete.StudentName = studentName;
                complete.ReturnToIdle += () => TransitionTo(KioskState.Idle);
                FireAndForget(complete.StartAutoReturnAsync());
                SetCurrentViewModel(complete);
                break;

            case KioskState.Error:
                FireAndForget(_speech.SpeakAsync("Something went wrong. We'll try again shortly."));
                var error = Resolve<ErrorViewModel>();
                error.ReturnToIdle += () => TransitionTo(KioskState.Idle);
                FireAndForget(error.StartAutoRecoverAsync());
                SetCurrentViewModel(error);
                break;
        }
    }

    private void OnNameSubmitted(string name)
    {
        var matches = _roster.FindMatches(name);

        if (matches.Count == 0)
        {
            _logger.LogWarning("No roster matches for name: {Name}", name);
            TransitionTo(KioskState.NameCapture);
            return;
        }

        if (matches.Count == 1 && matches[0].Confidence == MatchConfidence.High)
        {
            _currentStudent = matches[0].Student;
            TransitionTo(KioskState.Positioning);
            return;
        }

        var rosterMatch = Resolve<RosterMatchViewModel>();
        rosterMatch.LoadMatches(matches);
        rosterMatch.MatchConfirmed += student =>
        {
            _currentStudent = student;
            TransitionTo(KioskState.Positioning);
        };
        rosterMatch.MatchDenied += () => TransitionTo(KioskState.NameCapture);
        CurrentState = KioskState.RosterMatch;
        FireAndForget(_speech.SpeakAsync("We found a few matches. Please select your name."));
        SetCurrentViewModel(rosterMatch);
    }

    private void OnPhotoCaptured(byte[] photo)
    {
        _capturedPhoto = photo;
        TransitionTo(KioskState.Review);
    }

    private void OnPhotoAccepted(byte[] photo)
    {
        _capturedPhoto = photo;
        TransitionTo(KioskState.Processing);
    }

    private async Task ProcessPhotoAsync()
    {
        try
        {
            _logger.LogInformation("Processing photo for {StudentId}", _currentStudent!.StudentId);

            var face = await _faceDetection.DetectFaceAsync(_capturedPhoto!);
            var settings = new CropSettings(600, 600, 1.5, "jpg");

            byte[] cropped;
            if (face is not null)
            {
                cropped = await _imageProcessing.CropAndResizeAsync(_capturedPhoto!, face, settings);
            }
            else
            {
                _logger.LogWarning("No face detected — using center crop fallback");
                cropped = await _imageProcessing.CropAndResizeAsync(_capturedPhoto!, null, settings);
            }

            var filename = _imageProcessing.BuildFilename(
                "{LastName}_{FirstName}_{StudentId}.jpg", _currentStudent);
            var outputPath = Path.Combine("output", filename);
            await _imageProcessing.SaveImageAsync(cropped, outputPath);

            _roster.MarkCompleted(_currentStudent.StudentId);

            _logger.LogInformation("Photo saved for {StudentId} at {Path}", _currentStudent.StudentId, outputPath);
            TransitionTo(KioskState.Complete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process photo for {StudentId}", _currentStudent?.StudentId);
            TransitionTo(KioskState.Review);
        }
    }

    private async Task StartNameCaptureAsync(NameCaptureViewModel vm)
    {
        var prepareTask = _speech.PrepareListenAsync();
        await _speech.SpeakAsync("Welcome! Please say or type your name.");
        await prepareTask;
        await vm.StartAsync();
    }

    private void SetCurrentViewModel(ObservableObject vm)
    {
        CurrentViewModel = vm;
    }

    private void DisposeCurrentChild()
    {
        if (CurrentViewModel is IDisposable disposable)
            disposable.Dispose();
        CurrentViewModel = null;
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private T Resolve<T>() where T : notnull
    {
        return (T)_services.GetService(typeof(T))!;
    }

    public void Dispose()
    {
        _camera.FrameCaptured -= OnFrameCaptured;
        DisposeCurrentChild();
    }
}
