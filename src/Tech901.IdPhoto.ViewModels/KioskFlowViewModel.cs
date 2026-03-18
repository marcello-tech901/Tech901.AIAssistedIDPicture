using System.Runtime.CompilerServices;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Helpers;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

/// <summary>
/// Central state machine that orchestrates the kiosk photo-capture workflow.
/// Each <see cref="KioskState"/> maps to a child ViewModel; this coordinator creates the
/// child, wires its events, and swaps <see cref="CurrentViewModel"/> so the View layer
/// (via implicit DataTemplates) automatically shows the correct screen.
///
/// <para>
/// <b>State machine flow:</b><br/>
/// Idle -> NameCapture -> RosterMatch -> Positioning -> Capture -> Review -> Processing -> Complete<br/>
/// Any state may transition to Error, which auto-recovers back to Idle.
/// </para>
///
/// <para>
/// <b>Threading model:</b> WPF requires UI updates on the dispatcher thread. Camera frames
/// arrive on a background thread, so <see cref="IDispatcher"/> marshals them to the UI.
/// <see cref="FireAndForget"/> launches async work without blocking the state machine, while
/// still observing exceptions to avoid silent failures.
/// </para>
/// </summary>
public partial class KioskFlowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ICameraService _camera;
    private readonly IRosterService _roster;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IFaceDetectionService _faceDetection;
    private readonly ISpeechService _speech;
    private readonly IDispatcher _dispatcher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KioskFlowViewModel> _logger;

    /// <summary>The participant currently moving through the photo flow.</summary>
    private Student? _currentStudent;

    /// <summary>Raw camera frame bytes captured during the Capture state.</summary>
    private byte[]? _capturedPhoto;

    /// <summary>
    /// Admin PIN for accessing the admin panel. Triggered via Ctrl+Shift+A or the gear icon.
    /// </summary>
    private const string AdminPin = "9019";

    /// <summary>
    /// Zero-based index of the camera device to use. Updated from admin panel;
    /// read from config at startup, defaults to 0 (first camera).
    /// </summary>
    public int CameraDeviceIndex { get; set; }

    /// <summary>
    /// The current state in the kiosk workflow. Bound by the View layer to drive visual transitions.
    /// </summary>
    [ObservableProperty]
    private KioskState _currentState = KioskState.Idle;

    /// <summary>
    /// The active child ViewModel displayed in the main ContentControl.
    /// The View uses implicit DataTemplates to resolve the correct UserControl for each ViewModel type.
    /// </summary>
    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    /// <summary>
    /// Most recent camera frame, updated continuously from the camera's background thread.
    /// Bound by views that show a live preview (Idle, Positioning, Capture).
    /// </summary>
    [ObservableProperty]
    private byte[]? _currentFrame;

    /// <summary>
    /// Whether the admin panel is currently active. Controls visibility of admin-only UI.
    /// </summary>
    [ObservableProperty]
    private bool _isAdminMode;

    /// <summary>
    /// Initializes the kiosk flow coordinator with all required services.
    /// This ViewModel is registered as a Singleton in DI because there is exactly one kiosk
    /// workflow per application instance, and it must survive navigation between states.
    /// </summary>
    public KioskFlowViewModel(
        IServiceProvider services,
        ICameraService camera,
        IRosterService roster,
        IImageProcessingService imageProcessing,
        IFaceDetectionService faceDetection,
        ISpeechService speech,
        IDispatcher dispatcher,
        IConfiguration configuration,
        ILogger<KioskFlowViewModel> logger)
    {
        _services = services;
        _camera = camera;
        _roster = roster;
        _imageProcessing = imageProcessing;
        _faceDetection = faceDetection;
        _speech = speech;
        _dispatcher = dispatcher;
        _configuration = configuration;
        _logger = logger;

        if (int.TryParse(configuration["Kiosk:Camera:DeviceIndex"], out var configIndex))
            CameraDeviceIndex = configIndex;

        _camera.FrameCaptured += OnFrameCaptured;
    }

    /// <summary>
    /// Marshals camera frames from the capture thread to the UI thread via <see cref="IDispatcher"/>.
    /// Without this, setting <see cref="CurrentFrame"/> from a background thread would throw
    /// an InvalidOperationException in WPF.
    /// </summary>
    private void OnFrameCaptured(object? sender, byte[] frame)
    {
        _dispatcher.Invoke(() => CurrentFrame = frame);
    }

    /// <summary>
    /// Starts the camera and enters the Idle state. Called once during application startup.
    /// </summary>
    /// <param name="ct">Cancellation token for camera initialization.</param>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing kiosk flow with camera device index {DeviceIndex}", CameraDeviceIndex);
        await _camera.StartAsync(CameraDeviceIndex, ct);
        TransitionTo(KioskState.Idle);
    }

    /// <summary>Whether the PIN entry dialog overlay is visible.</summary>
    [ObservableProperty]
    private bool _isPinDialogVisible;

    /// <summary>Current value in the PIN entry field, bound two-way from the dialog.</summary>
    [ObservableProperty]
    private string? _pinEntry;

    /// <summary>
    /// Toggles admin mode on/off. If currently in admin mode, deactivates and returns to Idle.
    /// Otherwise, shows the PIN dialog for authentication.
    /// </summary>
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

    /// <summary>
    /// Validates the entered PIN and activates admin mode if correct.
    /// On invalid PIN, clears the entry field for retry (no lockout — this is a kiosk, not a vault).
    /// </summary>
    [RelayCommand]
    private void SubmitPin()
    {
        if (PinEntry == AdminPin)
        {
            IsPinDialogVisible = false;
            IsAdminMode = true;
            _logger.LogInformation("Admin mode activated");

            // Dispose the current child (e.g. IdleViewModel) to stop presence detection
            // polling. Without this, face detection would fire TransitionTo(NameCapture)
            // while the admin panel is open.
            DisposeCurrentChild();

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

    /// <summary>
    /// Dismisses the PIN dialog without activating admin mode.
    /// </summary>
    [RelayCommand]
    private void CancelPin()
    {
        IsPinDialogVisible = false;
        PinEntry = null;
    }

    /// <summary>
    /// Public entry point to return the kiosk to the Idle state from external callers
    /// (e.g., a global "reset" button).
    /// </summary>
    public void TransitionToIdle()
    {
        TransitionTo(KioskState.Idle);
    }

    /// <summary>
    /// Navigates to the batch-process view, which lets the admin process multiple photos at once.
    /// Wires the BackRequested event to return to the admin panel.
    /// </summary>
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

    /// <summary>
    /// The core transition function of the state machine. Given a target state, it:
    /// 1. Updates <see cref="CurrentState"/> (triggers PropertyChanged for View bindings)
    /// 2. Disposes the previous child ViewModel (cleanup timers, subscriptions)
    /// 3. Creates and configures the new child ViewModel for the target state
    /// 4. Wires event handlers that will trigger the next transition
    ///
    /// This is the single place where state transitions happen, making the flow
    /// easy to trace and debug via the structured log line emitted on every transition.
    /// </summary>
    /// <param name="newState">The target kiosk state to transition to.</param>
    private void TransitionTo(KioskState newState)
    {
        var previous = CurrentState;
        CurrentState = newState;
        _logger.LogInformation("State transition: {From} -> {To}", previous, newState);

        DisposeCurrentChild();

        switch (newState)
        {
            case KioskState.Idle:
                // Reset per-participant state so the next walkup starts clean
                _currentStudent = null;
                _capturedPhoto = null;
                var idle = Resolve<IdleViewModel>();
                idle.FaceDetected += () =>
                {
                    // Guard: ignore face detection when admin panel is open.
                    // The idle VM is disposed on admin entry, but this belt-and-suspenders
                    // check prevents transitions if a race condition fires the event late.
                    if (IsAdminMode)
                    {
                        _logger.LogDebug("Face detected while admin mode active — ignoring");
                        return;
                    }
                    TransitionTo(KioskState.NameCapture);
                };
                FireAndForget(idle.StartPollingAsync());
                SetCurrentViewModel(idle);
                break;

            case KioskState.NameCapture:
                var nameCapture = Resolve<NameCaptureViewModel>();
                nameCapture.NameSubmitted += OnNameSubmitted;
                SetCurrentViewModel(nameCapture);
                // Concurrent TTS greeting + microphone pre-warm so the participant doesn't
                // wait for both to happen sequentially.
                FireAndForget(StartNameCaptureAsync(nameCapture));
                break;

            case KioskState.RosterMatch:
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
                var review = Resolve<ReviewViewModel>();
                review.LoadImage(_capturedPhoto!);
                review.Accepted += OnPhotoAccepted;
                review.RetakeRequested += () => TransitionTo(KioskState.Positioning);
                SetCurrentViewModel(review);
                FireAndForget(StartReviewAsync(review));
                break;

            case KioskState.Processing:
                FireAndForget(ProcessPhotoAsync());
                break;

            case KioskState.Complete:
                var studentName = _currentStudent?.PreferredName
                    ?? _currentStudent?.FirstName
                    ?? "Student";
                // TODO DEMO-24: AI-102 — SSML <emphasis>. Stresses the participant's name for a more natural, personal feel.
                FireAndForget(_speech.SpeakSsmlAsync(SsmlBuilder.Build(_speech.CurrentVoice,
                    $"""All done! Thank you, <emphasis level="strong">{SecurityElement.Escape(studentName)}</emphasis>.""")));
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

    /// <summary>
    /// Handles the name submission from the NameCapture screen. Applies matching logic:
    /// - No matches: return to NameCapture so the participant can try again.
    /// - Single high-confidence match: skip the disambiguation screen and go straight to Positioning.
    /// - Multiple or ambiguous matches: show the RosterMatch screen for manual selection.
    /// </summary>
    private void OnNameSubmitted(string name)
    {
        var matches = _roster.FindMatches(name);

        if (matches.Count == 0)
        {
            _logger.LogWarning("No roster matches for name: {Name}", name);
            TransitionTo(KioskState.NameCapture);
            return;
        }

        // Fast path: if there's exactly one high-confidence match, skip disambiguation
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
        SetCurrentViewModel(rosterMatch);
        FireAndForget(StartRosterMatchAsync(rosterMatch));
    }

    /// <summary>
    /// Stores the captured photo bytes and advances to the Review state.
    /// </summary>
    private void OnPhotoCaptured(byte[] photo)
    {
        _capturedPhoto = photo;
        TransitionTo(KioskState.Review);
    }

    /// <summary>
    /// Stores the (possibly edited) accepted photo and advances to Processing.
    /// </summary>
    private void OnPhotoAccepted(byte[] photo)
    {
        _capturedPhoto = photo;
        TransitionTo(KioskState.Processing);
    }

    /// <summary>
    /// Runs the photo finalization pipeline: face detection, crop/resize, file save, and
    /// roster completion. On failure, returns to Review so the participant can retry or
    /// retake rather than losing their session.
    /// </summary>
    private async Task ProcessPhotoAsync()
    {
        try
        {
            _logger.LogInformation("Processing photo for {StudentId}", _currentStudent!.StudentId);

            // TODO DEMO-18: AI-102 — Integration point. Face detection result flows to ImageProcessingService for intelligent cropping. Set breakpoint here to inspect the FaceDetectionResult (nose tip, eye positions, bounding rect).
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

            // Auto-save session state so completed IDs survive crashes
            var outputDir = _configuration["Kiosk:OutputDirectory"] ?? "./output";
            var sessionPath = Path.Combine(outputDir, "session.json");
            FireAndForget(_roster.SaveSessionStateAsync(sessionPath));

            _logger.LogInformation("Photo saved for {StudentId} at {Path}", _currentStudent.StudentId, outputPath);
            TransitionTo(KioskState.Complete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process photo for {StudentId}", _currentStudent?.StudentId);
            TransitionTo(KioskState.Review);
        }
    }

    /// <summary>
    /// Runs TTS greeting and microphone pre-warming concurrently. The speech SDK needs
    /// time to initialize the audio input stream; by calling <c>PrepareListenAsync</c>
    /// while the greeting plays, the microphone is ready the moment the greeting finishes.
    /// This eliminates the awkward silence participants would otherwise experience.
    /// </summary>
    private async Task StartReviewAsync(ReviewViewModel vm)
    {
        var prepareTask = _speech.PrepareListenAsync();
        // TODO DEMO-25: AI-102 — SSML <prosody pitch>. Upbeat pitch on "Here's your photo" makes the prompt feel encouraging. <break/> separates the instruction naturally.
        if (_speech.IsAvailable)
            await _speech.SpeakSsmlAsync(SsmlBuilder.Build(_speech.CurrentVoice,
                """<prosody pitch="+5%">Here's your photo!</prosody> <break time="300ms"/> Say <emphasis level="moderate">use it</emphasis>, or say <emphasis level="moderate">retake</emphasis>."""));
        else
            await _speech.SpeakAsync("Here's your photo. Accept it or retake.");
        await prepareTask;
        vm.StartVoiceListening();
    }

    private async Task StartRosterMatchAsync(RosterMatchViewModel vm)
    {
        var prepareTask = _speech.PrepareListenAsync();
        string prompt = vm.IsSingleMatch
            ? (_speech.IsAvailable
                ? $"Is this you? {vm.TopMatchName}. Say yes or no."
                : $"Is this you? {vm.TopMatchName}.")
            : vm.IsMultipleMatch
                ? (_speech.IsAvailable
                    ? "We found a few matches. Say your name or a number to select."
                    : "We found a few matches. Please select your name.")
                : (_speech.IsAvailable
                    ? "No match found. Say try again."
                    : "No match found.");
        await _speech.SpeakAsync(prompt);
        await prepareTask;
        vm.StartVoiceListening();
    }

    private async Task StartNameCaptureAsync(NameCaptureViewModel vm)
    {
        // Start microphone pre-warm in parallel with the TTS greeting
        var prepareTask = _speech.PrepareListenAsync();
        // TODO DEMO-23: AI-102 — SSML <prosody> and <break>. Rate="slow" slows delivery for clarity. <break time="500ms"/> inserts a half-second pause between sentences.
        await _speech.SpeakSsmlAsync(SsmlBuilder.Build(_speech.CurrentVoice,
            """<prosody rate="slow">Welcome!</prosody> <break time="500ms"/> Please say or type your name."""));
        await prepareTask;
        await vm.StartAsync();
    }

    /// <summary>
    /// Sets <see cref="CurrentViewModel"/>, which triggers the View layer to swap
    /// the displayed UserControl via implicit DataTemplate resolution.
    /// </summary>
    private void SetCurrentViewModel(ObservableObject vm)
    {
        CurrentViewModel = vm;
    }

    /// <summary>
    /// Disposes the current child ViewModel if it implements <see cref="IDisposable"/>,
    /// then clears <see cref="CurrentViewModel"/>. This ensures timers, event subscriptions,
    /// and other resources from the previous state are cleaned up before the next state begins.
    /// </summary>
    private void DisposeCurrentChild()
    {
        if (CurrentViewModel is IDisposable disposable)
            disposable.Dispose();
        CurrentViewModel = null;
    }

    /// <summary>
    /// Launches an async task without awaiting it, while still observing faults.
    /// This avoids the "fire-and-forget" anti-pattern where exceptions vanish silently.
    /// <see cref="ContinueWith"/> with <see cref="TaskContinuationOptions.OnlyOnFaulted"/>
    /// ensures any exception is logged with the calling method's name for easy diagnosis.
    /// </summary>
    /// <param name="task">The background task to observe.</param>
    /// <param name="caller">Auto-populated caller name for structured logging.</param>
    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Resolves a service from the DI container. Child ViewModels are Transient, so each
    /// state transition gets a fresh instance with clean state.
    /// </summary>
    private T Resolve<T>() where T : notnull
    {
        return (T)_services.GetService(typeof(T))!;
    }

    /// <summary>
    /// Unsubscribes from camera events and disposes the current child ViewModel.
    /// Called when the application shuts down.
    /// </summary>
    public void Dispose()
    {
        _camera.FrameCaptured -= OnFrameCaptured;
        DisposeCurrentChild();
    }
}
