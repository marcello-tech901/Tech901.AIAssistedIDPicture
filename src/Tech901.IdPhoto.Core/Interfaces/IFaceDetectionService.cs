using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Abstracts face detection so the application can swap between a real Azure Face API
/// implementation and a null/offline fallback without changing consuming code.
/// </summary>
/// <remarks>
/// The Azure Face API (AI-102 cognitive services) performs face detection and returns
/// landmark coordinates used for face-aware cropping. When the service is unavailable
/// or no face is found, callers should fall back to center-crop logic.
/// </remarks>
public interface IFaceDetectionService
{
    /// <summary>
    /// Detects the primary face in the supplied image data.
    /// </summary>
    /// <param name="imageData">Raw image bytes (JPEG or PNG) to analyze.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>
    /// A <see cref="FaceDetectionResult"/> containing the face rectangle and landmarks,
    /// or <see langword="null"/> if no face was found. A null result signals the caller
    /// to use center-crop fallback positioning.
    /// </returns>
    Task<FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default);
}
