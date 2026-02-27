using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Services;

/// <summary>
/// Handles ID photo post-processing: face-aware cropping, resizing, format encoding, and
/// file output. Uses ImageSharp (cross-platform, no native dependencies) for all image
/// manipulation. When Azure Face API results are available the crop is centered on the
/// participant's nose tip; otherwise a simple center-crop fallback is used.
/// </summary>
public partial class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageProcessingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for image processing operations.</param>
    public ImageProcessingService(ILogger<ImageProcessingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Crops and resizes a raw camera frame into a finalized ID photo.
    /// If <paramref name="face"/> is provided, the crop rectangle is anchored on the nose-tip
    /// landmark so the participant's face is consistently centered regardless of where they
    /// stood in the frame. Without face data, a center-crop is used as a reasonable default.
    /// </summary>
    /// <param name="imageData">Raw image bytes (any format ImageSharp can decode).</param>
    /// <param name="face">Face detection result with landmarks, or <c>null</c> for center-crop fallback.</param>
    /// <param name="settings">Output dimensions, padding multiplier, and format (jpg/png).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Encoded image bytes in the requested format.</returns>
    public async Task<byte[]> CropAndResizeAsync(
        byte[] imageData,
        FaceDetectionResult? face,
        CropSettings settings,
        CancellationToken ct = default)
    {
        try
        {
            using var image = Image.Load(imageData);

            SixLabors.ImageSharp.Rectangle cropRect;
            if (face is not null)
            {
                cropRect = ComputeCropRectangle(
                    face.NoseTip,
                    face.FaceRectangle,
                    settings.PaddingMultiplier,
                    settings.OutputWidth,
                    settings.OutputHeight,
                    image.Width,
                    image.Height);
            }
            else
            {
                // Center-crop fallback when no face detection available
                // (e.g., Azure Face API key not configured or detection failed)
                cropRect = ComputeCenterCropRectangle(
                    settings.OutputWidth,
                    settings.OutputHeight,
                    image.Width,
                    image.Height);
                _logger.LogInformation("Using center crop fallback");
            }

            image.Mutate(ctx => ctx
                .Crop(cropRect)
                .Resize(settings.OutputWidth, settings.OutputHeight));

            using var ms = new MemoryStream();
            if (settings.OutputFormat.Equals("png", StringComparison.OrdinalIgnoreCase))
                await image.SaveAsync(ms, new PngEncoder(), ct).ConfigureAwait(false);
            else
                await image.SaveAsync(ms, new JpegEncoder { Quality = 95 }, ct).ConfigureAwait(false);

            _logger.LogInformation("Cropped and resized image to {Width}x{Height} ({Format})",
                settings.OutputWidth, settings.OutputHeight, settings.OutputFormat);

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to crop and resize image ({InputLength} bytes, target {Width}x{Height})",
                imageData.Length, settings.OutputWidth, settings.OutputHeight);
            throw;
        }
    }

    /// <summary>
    /// Writes image bytes to disk, creating parent directories as needed.
    /// </summary>
    /// <param name="imageData">Encoded image bytes to save.</param>
    /// <param name="outputPath">Destination file path (relative or absolute).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SaveImageAsync(byte[] imageData, string outputPath, CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(outputPath, imageData, ct).ConfigureAwait(false);
            _logger.LogInformation("Saved image to {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save image to {Path}", outputPath);
            throw;
        }
    }

    /// <summary>
    /// Expands a filename template by substituting <c>{StudentId}</c>, <c>{FirstName}</c>,
    /// <c>{LastName}</c>, <c>{PreferredName}</c>, and any extra-field tokens with sanitized
    /// values from the participant record. Unresolved tokens are replaced with "UNKNOWN"
    /// so filenames never contain raw braces.
    /// </summary>
    /// <param name="template">Filename template, e.g. <c>"{LastName}_{FirstName}_{StudentId}.jpg"</c>.</param>
    /// <param name="student">The participant whose data populates the template.</param>
    /// <returns>A filesystem-safe filename string.</returns>
    public string BuildFilename(string template, Student student)
    {
        var result = template;

        result = result.Replace("{StudentId}", SanitizeFilename(student.StudentId));
        result = result.Replace("{FirstName}", SanitizeFilename(student.FirstName));
        result = result.Replace("{LastName}", SanitizeFilename(student.LastName));

        if (student.PreferredName is not null)
            result = result.Replace("{PreferredName}", SanitizeFilename(student.PreferredName));

        foreach (var (key, value) in student.ExtraFields)
            result = result.Replace($"{{{key}}}", SanitizeFilename(value));

        // Remove any remaining unresolved tokens
        result = UnresolvedTokenRegex().Replace(result, "UNKNOWN");

        return result;
    }

    /// <summary>
    /// Computes a face-aware crop rectangle anchored on the nose-tip landmark.
    /// The nose tip produces better centering than the face-rectangle center because
    /// face rectangles often extend asymmetrically above the forehead. The padding
    /// multiplier controls how much background context surrounds the face (1.5x is a
    /// good default for ID photos, giving forehead-to-chin with some margin).
    /// </summary>
    /// <param name="noseTip">Nose-tip landmark coordinates from face detection.</param>
    /// <param name="faceRect">Bounding rectangle of the detected face.</param>
    /// <param name="paddingMultiplier">How many times the face size the crop region should be (e.g., 1.5).</param>
    /// <param name="targetWidth">Desired output width in pixels (used only for aspect ratio).</param>
    /// <param name="targetHeight">Desired output height in pixels (used only for aspect ratio).</param>
    /// <param name="imageWidth">Source image width for bounds clamping.</param>
    /// <param name="imageHeight">Source image height for bounds clamping.</param>
    /// <returns>A crop rectangle clamped to image bounds.</returns>
    internal static SixLabors.ImageSharp.Rectangle ComputeCropRectangle(
        Models.PointF noseTip,
        Models.RectangleF faceRect,
        double paddingMultiplier,
        int targetWidth,
        int targetHeight,
        int imageWidth,
        int imageHeight)
    {
        // Use the larger face dimension so the crop fully contains the face even if
        // the bounding box is wider than it is tall (or vice versa).
        var faceSize = Math.Max(faceRect.Width, faceRect.Height);
        var cropSize = faceSize * paddingMultiplier;

        // Scale the crop rectangle to match the target aspect ratio while keeping
        // the padded face size as the constraining dimension.
        var targetAspect = (double)targetWidth / targetHeight;
        double cropWidth, cropHeight;

        if (targetAspect >= 1.0)
        {
            cropWidth = cropSize * targetAspect;
            cropHeight = cropSize;
        }
        else
        {
            cropWidth = cropSize;
            cropHeight = cropSize / targetAspect;
        }

        // Center on nose tip — this gives a natural "looking at camera" composition
        var x = (int)(noseTip.X - cropWidth / 2);
        var y = (int)(noseTip.Y - cropHeight / 2);
        var w = (int)cropWidth;
        var h = (int)cropHeight;

        // Clamp to image bounds so ImageSharp doesn't throw on out-of-range rectangles
        x = Math.Max(0, Math.Min(x, imageWidth - w));
        y = Math.Max(0, Math.Min(y, imageHeight - h));
        w = Math.Min(w, imageWidth - x);
        h = Math.Min(h, imageHeight - y);

        return new SixLabors.ImageSharp.Rectangle(x, y, w, h);
    }

    /// <summary>
    /// Computes a center-crop rectangle that matches the target aspect ratio.
    /// Used as a fallback when face detection is unavailable (e.g., Azure Face API
    /// not configured via <c>NullFaceDetectionService</c>). The crop maximizes the
    /// area used from the source image while honoring the aspect ratio.
    /// </summary>
    /// <param name="targetWidth">Desired output width (used only for aspect ratio).</param>
    /// <param name="targetHeight">Desired output height (used only for aspect ratio).</param>
    /// <param name="imageWidth">Source image width.</param>
    /// <param name="imageHeight">Source image height.</param>
    /// <returns>A centered crop rectangle matching the target aspect ratio.</returns>
    internal static SixLabors.ImageSharp.Rectangle ComputeCenterCropRectangle(
        int targetWidth,
        int targetHeight,
        int imageWidth,
        int imageHeight)
    {
        var targetAspect = (double)targetWidth / targetHeight;
        var imageAspect = (double)imageWidth / imageHeight;

        int cropW, cropH;
        if (imageAspect > targetAspect)
        {
            // Image is wider than target ratio — constrain by height, trim sides
            cropH = imageHeight;
            cropW = (int)(imageHeight * targetAspect);
        }
        else
        {
            // Image is taller than target ratio — constrain by width, trim top/bottom
            cropW = imageWidth;
            cropH = (int)(imageWidth / targetAspect);
        }

        var x = (imageWidth - cropW) / 2;
        var y = (imageHeight - cropH) / 2;

        return new SixLabors.ImageSharp.Rectangle(x, y, cropW, cropH);
    }

    /// <summary>
    /// Replaces characters that are invalid in filenames with underscores.
    /// </summary>
    private static string SanitizeFilename(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>
    /// Source-generated regex that matches unresolved <c>{Token}</c> placeholders in filename templates.
    /// Using <see cref="GeneratedRegexAttribute"/> avoids runtime regex compilation overhead.
    /// </summary>
    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex UnresolvedTokenRegex();
}
