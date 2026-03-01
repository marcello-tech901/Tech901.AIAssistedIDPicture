using FluentAssertions;
using Tech901.IdPhoto.Infrastructure.Speech;
using Xunit;

namespace Tech901.IdPhoto.Infrastructure.Tests;

public class NullSpeechServiceTests
{
    private readonly NullSpeechService _sut = new();

    [Fact]
    public void CurrentVoice_ReturnsEmpty()
    {
        _sut.CurrentVoice.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableVoicesAsync_ReturnsEmptyList()
    {
        var voices = await _sut.GetAvailableVoicesAsync();
        voices.Should().BeEmpty();
    }

    [Fact]
    public void SetVoice_DoesNotThrow()
    {
        var act = () => _sut.SetVoice("en-US-JennyNeural");
        act.Should().NotThrow();
    }
}
