using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Interfaces;
using Xunit;

namespace Tech901.IdPhoto.ViewModels.Tests;

public class NameCaptureViewModelTests : IDisposable
{
    private readonly Mock<ISpeechService> _speech;
    private readonly IDispatcher _dispatcher;
    private readonly NameCaptureViewModel _sut;

    public NameCaptureViewModelTests()
    {
        _speech = new Mock<ISpeechService>();
        _dispatcher = new TestDispatcher();

        _speech.Setup(s => s.IsAvailable).Returns(true);

        _sut = new NameCaptureViewModel(
            _speech.Object,
            _dispatcher,
            NullLogger<NameCaptureViewModel>.Instance);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public async Task StartAsync_WhenSpeechUnavailable_SetsSpeechStateToUnavailable()
    {
        // Arrange
        var speech = new Mock<ISpeechService>();
        speech.Setup(s => s.IsAvailable).Returns(false);
        using var sut = new NameCaptureViewModel(
            speech.Object,
            _dispatcher,
            NullLogger<NameCaptureViewModel>.Instance);

        // Act
        await sut.StartAsync();

        // Assert
        sut.SpeechState.Should().Be(SpeechState.Unavailable);
    }

    [Fact]
    public async Task StartAsync_WhenSpeechAvailable_TransitionsToListeningThenRecognized()
    {
        // Arrange — return name once, then block until cancelled (simulates real listen timeout)
        var callCount = 0;
        _speech.Setup(s => s.ListenAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                    return Task.FromResult<string?>("Jane Doe");
                var tcs = new TaskCompletionSource<string?>();
                ct.Register(() => tcs.TrySetResult(null));
                return tcs.Task;
            });

        // Act
        await _sut.StartAsync();

        // Assert
        _sut.SpeechState.Should().Be(SpeechState.Recognized);
        _sut.RecognizedText.Should().Be("Jane Doe");
        _sut.HasRecognitionResult.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenListenReturnsNull_SetsSpeechStateToFailed()
    {
        // Arrange
        _speech.Setup(s => s.ListenAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        await _sut.StartAsync();

        // Assert
        _sut.SpeechState.Should().Be(SpeechState.Failed);
        _sut.RecognitionFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ListenCommand_AfterRecognition_SpeaksConfirmationPrompt()
    {
        // Arrange — return name once, then block until cancelled
        var callCount = 0;
        _speech.Setup(s => s.ListenAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                    return Task.FromResult<string?>("John");
                var tcs = new TaskCompletionSource<string?>();
                ct.Register(() => tcs.TrySetResult(null));
                return tcs.Task;
            });

        // Act
        await _sut.ListenCommand.ExecuteAsync(null);

        // Allow fire-and-forget confirmation to start
        await Task.Delay(50);

        // Assert — confirmation prompt is spoken after recognition
        _speech.Verify(s => s.SpeakAsync(It.Is<string>(t => t.Contains("John")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Retry_ResetsSpeechStateToReady()
    {
        // Arrange
        _sut.RecognizedText = "Some Name";

        // Act
        _sut.RetryCommand.Execute(null);

        // Assert
        _sut.SpeechState.Should().Be(SpeechState.Ready);
        _sut.RecognizedText.Should().BeNull();
        _sut.TypedName.Should().BeNull();
    }

    [Fact]
    public void Submit_WithRecognizedText_FiresNameSubmitted()
    {
        // Arrange
        string? submitted = null;
        _sut.NameSubmitted += name => submitted = name;
        _sut.RecognizedText = "Alice Smith";

        // Act
        _sut.SubmitCommand.Execute(null);

        // Assert
        submitted.Should().Be("Alice Smith");
    }

    [Fact]
    public async Task Dispose_CancelsActiveListen()
    {
        // Arrange
        var tcs = new TaskCompletionSource<string?>();
        _speech.Setup(s => s.ListenAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns((TimeSpan? _, CancellationToken ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        var listenTask = _sut.ListenCommand.ExecuteAsync(null);

        // Act
        _sut.Dispose();

        // Assert — should complete without throwing
        await listenTask;
        _sut.IsListening.Should().BeFalse();
    }
}
