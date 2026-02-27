using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Infrastructure.FaceDetection;

/// <summary>
/// A no-op implementation of <see cref="IFaceDetectionService"/> used when Azure Face API
/// credentials are not configured.
/// </summary>
/// <remarks>
/// <para>
/// This class implements the <strong>Null Object Pattern</strong>: the DI container always
/// provides a valid <see cref="IFaceDetectionService"/> instance, even without Azure credentials.
/// Consumers never need to null-check the service itself — only the detection result.
/// </para>
/// <para>
/// AI-102: When this null service returns <c>null</c> from <see cref="DetectFaceAsync"/>,
/// the downstream <c>ImageProcessingService</c> falls back to center-cropping the photo.
/// This graceful degradation means the kiosk still produces usable ID photos — they just
/// may not be as precisely framed as face-detected crops.
/// </para>
/// </remarks>
public sealed class NullFaceDetectionService : IFaceDetectionService
{
    private readonly ILogger<NullFaceDetectionService> _logger;

    /// <summary>
    /// Initializes the null face detection service and logs a warning to alert operators
    /// that face detection is disabled.
    /// </summary>
    /// <param name="logger">Logger used to emit the startup warning.</param>
    public NullFaceDetectionService(ILogger<NullFaceDetectionService> logger)
    {
        _logger = logger;

        // Log at Warning level so operators reviewing kiosk logs can immediately see
        // that face detection is disabled and photos will be center-cropped instead.
        _logger.LogWarning("Azure Face API not configured — face detection is disabled. Photos will be center-cropped.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always returns <c>null</c>, signaling to callers that no face was detected.
    /// The <c>ImageProcessingService</c> interprets a <c>null</c> result as "use center-crop."
    /// </remarks>
    public Task<FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default)
    {
        return Task.FromResult<FaceDetectionResult?>(null);
    }
}
