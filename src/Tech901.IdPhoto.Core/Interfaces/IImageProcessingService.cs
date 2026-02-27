using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Handles image cropping, resizing, and file output for ID photos.
/// Supports both face-aware cropping (using Face API results) and
/// center-crop fallback when no face data is available.
/// </summary>
/// <remarks>
/// The dual-mode cropping pipeline ensures every participant gets a
/// properly framed photo regardless of whether the Azure Face API
/// is configured or returns a detection result.
/// </remarks>
public interface IImageProcessingService
{
    /// <summary>
    /// Crops and resizes the source image to produce a final ID photo.
    /// </summary>
    /// <param name="imageData">Raw image bytes (JPEG or PNG) of the captured photo.</param>
    /// <param name="face">
    /// Face detection result used to position the crop around the detected face.
    /// When <see langword="null"/>, a center-crop is applied instead.
    /// </param>
    /// <param name="settings">Crop dimensions and aspect-ratio configuration.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The cropped and resized image as a byte array.</returns>
    Task<byte[]> CropAndResizeAsync(byte[] imageData, FaceDetectionResult? face, CropSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Writes the supplied image bytes to disk at the specified path.
    /// </summary>
    /// <param name="imageData">The image bytes to persist.</param>
    /// <param name="outputPath">Absolute file path for the saved image.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task SaveImageAsync(byte[] imageData, string outputPath, CancellationToken ct = default);

    /// <summary>
    /// Builds a filename for the participant's ID photo using a template with
    /// placeholder tokens (e.g., <c>{FirstName}</c>, <c>{LastName}</c>).
    /// </summary>
    /// <param name="template">Filename template containing placeholder tokens.</param>
    /// <param name="student">The participant whose data fills the template tokens.</param>
    /// <returns>The resolved filename string.</returns>
    string BuildFilename(string template, Student student);
}
