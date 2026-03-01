using FluentAssertions;
using Tech901.IdPhoto.Infrastructure.Speech;
using Xunit;

namespace Tech901.IdPhoto.Infrastructure.Tests;

public class FallbackVoiceListTests
{
    [Fact]
    public void FallbackVoices_HasAtLeastTenEntries()
    {
        AzureSpeechService.FallbackVoices.Should().HaveCountGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void FallbackVoices_AllHaveEnUsPrefix()
    {
        AzureSpeechService.FallbackVoices.Should()
            .AllSatisfy(v => v.ShortName.Should().StartWith("en-US-"));
    }

    [Fact]
    public void FallbackVoices_AllHaveNonEmptyFields()
    {
        AzureSpeechService.FallbackVoices.Should()
            .AllSatisfy(v =>
            {
                v.ShortName.Should().NotBeNullOrWhiteSpace();
                v.DisplayName.Should().NotBeNullOrWhiteSpace();
                v.Gender.Should().NotBeNullOrWhiteSpace();
            });
    }

    [Fact]
    public void FallbackVoices_GendersAreValid()
    {
        var validGenders = new[] { "Male", "Female" };
        AzureSpeechService.FallbackVoices.Should()
            .AllSatisfy(v => validGenders.Should().Contain(v.Gender));
    }

    [Fact]
    public void FallbackVoices_AreOrderedByDisplayName()
    {
        var names = AzureSpeechService.FallbackVoices.Select(v => v.DisplayName).ToList();
        names.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }
}
