using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Infrastructure.FaceDetection;

public sealed class NullFaceDetectionService : IFaceDetectionService
{
    private readonly ILogger<NullFaceDetectionService> _logger;

    public NullFaceDetectionService(ILogger<NullFaceDetectionService> logger)
    {
        _logger = logger;
        _logger.LogWarning("Azure Face API not configured — face detection is disabled. Photos will be center-cropped.");
    }

    public Task<FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default)
    {
        return Task.FromResult<FaceDetectionResult?>(null);
    }
}
