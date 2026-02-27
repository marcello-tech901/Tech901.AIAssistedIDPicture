using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tech901.IdPhoto.Core.Models;
using Tech901.IdPhoto.Core.Services;
using Xunit;

namespace Tech901.IdPhoto.Core.Tests;

public class ImageProcessingServiceTests
{
    private readonly ImageProcessingService _sut;

    public ImageProcessingServiceTests()
    {
        _sut = new ImageProcessingService(NullLogger<ImageProcessingService>.Instance);
    }

    [Fact]
    public void BuildFilename_ReplacesAllTokens()
    {
        var student = new Student("S001", "John", "Doe", "Johnny", new Dictionary<string, string>
        {
            ["Grade"] = "10"
        });

        var result = _sut.BuildFilename("{LastName}_{FirstName}_{StudentId}_{PreferredName}_{Grade}.jpg", student);

        result.Should().Be("Doe_John_S001_Johnny_10.jpg");
    }

    [Fact]
    public void BuildFilename_SanitizesInvalidFilenameCharacters()
    {
        var student = new Student("S/001", "Jo:hn", "D<oe>", null, new Dictionary<string, string>());

        var result = _sut.BuildFilename("{LastName}_{FirstName}_{StudentId}.jpg", student);

        result.Should().NotContainAny("/", ":", "<", ">");
        result.Should().Be("D_oe__Jo_hn_S_001.jpg");
    }

    [Fact]
    public void BuildFilename_HandlesUnresolvedTokens()
    {
        var student = new Student("S001", "John", "Doe", null, new Dictionary<string, string>());

        var result = _sut.BuildFilename("{LastName}_{UnknownField}.jpg", student);

        result.Should().Be("Doe_UNKNOWN.jpg");
    }

    [Fact]
    public void ComputeCropRectangle_CentersOnNoseTip()
    {
        var noseTip = new PointF(500, 500);
        var faceRect = new RectangleF(400, 400, 200, 200);

        var rect = ImageProcessingService.ComputeCropRectangle(
            noseTip, faceRect,
            paddingMultiplier: 2.0,
            targetWidth: 600, targetHeight: 600,
            imageWidth: 1000, imageHeight: 1000);

        // Crop should be centered on nose tip (500, 500)
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        centerX.Should().Be(500);
        centerY.Should().Be(500);
    }

    [Fact]
    public void ComputeCropRectangle_ClampsToImageBounds()
    {
        // Nose tip near top-left corner
        var noseTip = new PointF(50, 50);
        var faceRect = new RectangleF(0, 0, 200, 200);

        var rect = ImageProcessingService.ComputeCropRectangle(
            noseTip, faceRect,
            paddingMultiplier: 2.0,
            targetWidth: 600, targetHeight: 600,
            imageWidth: 800, imageHeight: 800);

        rect.X.Should().BeGreaterThanOrEqualTo(0);
        rect.Y.Should().BeGreaterThanOrEqualTo(0);
        (rect.X + rect.Width).Should().BeLessThanOrEqualTo(800);
        (rect.Y + rect.Height).Should().BeLessThanOrEqualTo(800);
    }

    [Fact]
    public void ComputeCropRectangle_RespectsAspectRatio()
    {
        var noseTip = new PointF(500, 500);
        var faceRect = new RectangleF(400, 400, 200, 200);

        // Target is 2:3 portrait (width < height)
        var rect = ImageProcessingService.ComputeCropRectangle(
            noseTip, faceRect,
            paddingMultiplier: 2.0,
            targetWidth: 400, targetHeight: 600,
            imageWidth: 1200, imageHeight: 1200);

        var actualAspect = (double)rect.Width / rect.Height;
        var expectedAspect = 400.0 / 600.0;

        actualAspect.Should().BeApproximately(expectedAspect, 0.05);
    }
}
