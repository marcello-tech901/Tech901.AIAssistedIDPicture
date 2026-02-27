using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Services;

public partial class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;

    public ImageProcessingService(ILogger<ImageProcessingService> logger)
    {
        _logger = logger;
    }

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

    internal static SixLabors.ImageSharp.Rectangle ComputeCropRectangle(
        Models.PointF noseTip,
        Models.RectangleF faceRect,
        double paddingMultiplier,
        int targetWidth,
        int targetHeight,
        int imageWidth,
        int imageHeight)
    {
        var faceSize = Math.Max(faceRect.Width, faceRect.Height);
        var cropSize = faceSize * paddingMultiplier;

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

        // Center on nose tip
        var x = (int)(noseTip.X - cropWidth / 2);
        var y = (int)(noseTip.Y - cropHeight / 2);
        var w = (int)cropWidth;
        var h = (int)cropHeight;

        // Clamp to image bounds
        x = Math.Max(0, Math.Min(x, imageWidth - w));
        y = Math.Max(0, Math.Min(y, imageHeight - h));
        w = Math.Min(w, imageWidth - x);
        h = Math.Min(h, imageHeight - y);

        return new SixLabors.ImageSharp.Rectangle(x, y, w, h);
    }

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
            cropH = imageHeight;
            cropW = (int)(imageHeight * targetAspect);
        }
        else
        {
            cropW = imageWidth;
            cropH = (int)(imageWidth / targetAspect);
        }

        var x = (imageWidth - cropW) / 2;
        var y = (imageHeight - cropH) / 2;

        return new SixLabors.ImageSharp.Rectangle(x, y, cropW, cropH);
    }

    private static string SanitizeFilename(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex UnresolvedTokenRegex();
}
