using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;
using Xunit;

namespace Tech901.IdPhoto.ViewModels.Tests;

public class AdminViewModelVoiceTests
{
    private readonly Mock<IRosterService> _roster = new();
    private readonly Mock<IAudioDeviceEnumerator> _audioDevices = new();
    private readonly Mock<ICameraService> _camera = new();
    private readonly IDispatcher _dispatcher = new TestDispatcher();
    private readonly Mock<ISpeechService> _speech = new();
    private readonly Mock<IFaceDetectionService> _faceDetection = new();
    private readonly Mock<IConfiguration> _config = new();

    private static readonly List<VoiceInfo> TestVoices =
    [
        new("en-US-AriaNeural", "Aria", "Female"),
        new("en-US-GuyNeural", "Guy", "Male"),
        new("en-US-JennyNeural", "Jenny", "Female"),
    ];

    public AdminViewModelVoiceTests()
    {
        _roster.Setup(r => r.GetAllStudents()).Returns([]);
        _roster.Setup(r => r.GetCompletedStudents()).Returns([]);
        _audioDevices.Setup(a => a.GetInputDevices()).Returns([]);
        _audioDevices.Setup(a => a.GetOutputDevices()).Returns([]);
        _camera.Setup(c => c.EnumerateDevices()).Returns([]);

        _config.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        _config.Setup(c => c["Kiosk:OutputDirectory"]).Returns("./test-output");
    }

    private AdminViewModel CreateSut(bool speechAvailable = true)
    {
        _speech.Setup(s => s.IsAvailable).Returns(speechAvailable);
        if (!speechAvailable)
        {
            _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<VoiceInfo>());
        }

        var kioskFlow = new KioskFlowViewModel(
            new Mock<IServiceProvider>().Object,
            _camera.Object,
            _roster.Object,
            new Mock<IImageProcessingService>().Object,
            _faceDetection.Object,
            _speech.Object,
            _dispatcher,
            new Mock<IConfiguration>().Object,
            NullLogger<KioskFlowViewModel>.Instance);

        return new AdminViewModel(
            _roster.Object,
            _audioDevices.Object,
            _camera.Object,
            _dispatcher,
            kioskFlow,
            _speech.Object,
            _faceDetection.Object,
            _config.Object,
            NullLogger<AdminViewModel>.Instance);
    }

    [Fact]
    public async Task LoadVoices_PopulatesAvailableVoicesCollection()
    {
        // Arrange
        _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestVoices);
        _speech.Setup(s => s.CurrentVoice).Returns("en-US-GuyNeural");

        var sut = CreateSut();

        // Act — constructor fires LoadVoicesAsync via FireAndForget; give it a moment
        await Task.Delay(200);

        // Assert
        sut.AvailableVoices.Should().HaveCount(3);
    }

    [Fact]
    public async Task LoadVoices_PreselectsCurrentVoice()
    {
        // Arrange
        _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestVoices);
        _speech.Setup(s => s.CurrentVoice).Returns("en-US-JennyNeural");

        var sut = CreateSut();

        // Act
        await Task.Delay(200);

        // Assert
        sut.SelectedVoice.Should().NotBeNull();
        sut.SelectedVoice!.ShortName.Should().Be("en-US-JennyNeural");
    }

    [Fact]
    public async Task LoadVoices_FallsBackToFirstWhenSavedVoiceMissing()
    {
        // Arrange
        _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestVoices);
        _speech.Setup(s => s.CurrentVoice).Returns("en-US-NonExistentNeural");

        var sut = CreateSut();

        // Act
        await Task.Delay(200);

        // Assert
        sut.SelectedVoice.Should().NotBeNull();
        sut.SelectedVoice!.ShortName.Should().Be("en-US-AriaNeural");
    }

    [Fact]
    public async Task SelectedVoiceChanged_CallsSetVoice()
    {
        // Arrange
        _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestVoices);
        _speech.Setup(s => s.CurrentVoice).Returns("en-US-AriaNeural");

        var sut = CreateSut();
        await Task.Delay(200);

        _speech.Invocations.Clear();

        // Act
        sut.SelectedVoice = TestVoices[1]; // Guy

        // Assert
        _speech.Verify(s => s.SetVoice("en-US-GuyNeural"), Times.Once);
    }

    [Fact]
    public async Task PreviewVoice_CallsSpeakAsync()
    {
        // Arrange
        _speech.Setup(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestVoices);
        _speech.Setup(s => s.CurrentVoice).Returns("en-US-AriaNeural");

        var sut = CreateSut();
        await Task.Delay(200);

        _speech.Invocations.Clear();

        // Act
        sut.PreviewVoiceCommand.Execute(null);

        // Assert
        _speech.Verify(s => s.SpeakAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void SpeechUnavailable_DoesNotLoadVoices()
    {
        // Act
        var sut = CreateSut(speechAvailable: false);

        // Assert
        sut.AvailableVoices.Should().BeEmpty();
        _speech.Verify(s => s.GetAvailableVoicesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
