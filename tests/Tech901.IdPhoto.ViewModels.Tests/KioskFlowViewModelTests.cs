using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;
using Xunit;

namespace Tech901.IdPhoto.ViewModels.Tests;

public class KioskFlowViewModelTests : IDisposable
{
    private readonly Mock<IServiceProvider> _serviceProvider;
    private readonly Mock<ICameraService> _camera;
    private readonly Mock<IRosterService> _roster;
    private readonly Mock<IImageProcessingService> _imageProcessing;
    private readonly Mock<IFaceDetectionService> _faceDetection;
    private readonly Mock<ISpeechService> _speech;
    private readonly Mock<IPresenceDetector> _presenceDetector;
    private readonly Mock<IAudioDeviceEnumerator> _audioDevices;
    private readonly IDispatcher _dispatcher;
    private readonly KioskFlowViewModel _sut;

    public KioskFlowViewModelTests()
    {
        _serviceProvider = new Mock<IServiceProvider>();
        _camera = new Mock<ICameraService>();
        _roster = new Mock<IRosterService>();
        _imageProcessing = new Mock<IImageProcessingService>();
        _faceDetection = new Mock<IFaceDetectionService>();
        _speech = new Mock<ISpeechService>();
        _presenceDetector = new Mock<IPresenceDetector>();
        _audioDevices = new Mock<IAudioDeviceEnumerator>();
        _dispatcher = new TestDispatcher();

        _audioDevices.Setup(a => a.GetInputDevices()).Returns([]);
        _audioDevices.Setup(a => a.GetOutputDevices()).Returns([]);
        _camera.Setup(c => c.EnumerateDevices()).Returns(new List<string>());
        _roster.Setup(r => r.GetAllStudents()).Returns(new List<Student>());
        _roster.Setup(r => r.GetCompletedStudents()).Returns(new List<Student>());

        var configuration = new Mock<IConfiguration>();

        _sut = new KioskFlowViewModel(
            _serviceProvider.Object,
            _camera.Object,
            _roster.Object,
            _imageProcessing.Object,
            _faceDetection.Object,
            _speech.Object,
            _dispatcher,
            configuration.Object,
            NullLogger<KioskFlowViewModel>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        _sut.CurrentState.Should().Be(KioskState.Idle);
    }

    [Fact]
    public async Task InitializeAsync_StartsCamera_AndRemainsIdle()
    {
        var idleVm = new IdleViewModel(
            _presenceDetector.Object,
            _camera.Object,
            _dispatcher,
            NullLogger<IdleViewModel>.Instance);

        _serviceProvider.Setup(sp => sp.GetService(typeof(IdleViewModel)))
            .Returns(idleVm);

        _camera.Setup(c => c.IsRunning).Returns(false);

        await _sut.InitializeAsync();

        _camera.Verify(c => c.StartAsync(0, It.IsAny<CancellationToken>()), Times.Once);
        _sut.CurrentState.Should().Be(KioskState.Idle);

        idleVm.Dispose();
    }

    [Fact]
    public void ToggleAdmin_WithCorrectPin_ActivatesAdminMode()
    {
        var adminVm = new AdminViewModel(
            _roster.Object,
            _audioDevices.Object,
            _camera.Object,
            _dispatcher,
            _sut,
            _speech.Object,
            _faceDetection.Object,
            new Mock<IConfiguration>().Object,
            NullLogger<AdminViewModel>.Instance);

        _serviceProvider.Setup(sp => sp.GetService(typeof(AdminViewModel)))
            .Returns(adminVm);

        // Open PIN dialog
        _sut.ToggleAdminCommand.Execute(null);
        _sut.IsPinDialogVisible.Should().BeTrue();

        // Submit correct PIN
        _sut.PinEntry = "9019";
        _sut.SubmitPinCommand.Execute(null);

        _sut.IsAdminMode.Should().BeTrue();
        _sut.IsPinDialogVisible.Should().BeFalse();
    }

    [Fact]
    public void ToggleAdmin_WithWrongPin_DoesNotActivateAdminMode()
    {
        // Open PIN dialog
        _sut.ToggleAdminCommand.Execute(null);
        _sut.IsPinDialogVisible.Should().BeTrue();

        // Submit wrong PIN
        _sut.PinEntry = "0000";
        _sut.SubmitPinCommand.Execute(null);

        _sut.IsAdminMode.Should().BeFalse();
    }

    [Fact]
    public void ToggleAdmin_WhenAlreadyAdmin_DeactivatesAndGoesToIdle()
    {
        // First activate admin
        var adminVm = new AdminViewModel(
            _roster.Object,
            _audioDevices.Object,
            _camera.Object,
            _dispatcher,
            _sut,
            _speech.Object,
            _faceDetection.Object,
            new Mock<IConfiguration>().Object,
            NullLogger<AdminViewModel>.Instance);

        _serviceProvider.Setup(sp => sp.GetService(typeof(AdminViewModel)))
            .Returns(adminVm);

        _sut.ToggleAdminCommand.Execute(null);
        _sut.PinEntry = "9019";
        _sut.SubmitPinCommand.Execute(null);
        _sut.IsAdminMode.Should().BeTrue();

        // Set up IdleViewModel for the return to Idle
        var idleVm = new IdleViewModel(
            _presenceDetector.Object,
            _camera.Object,
            _dispatcher,
            NullLogger<IdleViewModel>.Instance);

        _serviceProvider.Setup(sp => sp.GetService(typeof(IdleViewModel)))
            .Returns(idleVm);

        _camera.Setup(c => c.IsRunning).Returns(false);

        // Now toggle off (while in admin mode, it exits directly)
        _sut.ToggleAdminCommand.Execute(null);

        _sut.IsAdminMode.Should().BeFalse();
        _sut.CurrentState.Should().Be(KioskState.Idle);

        idleVm.Dispose();
    }
}
