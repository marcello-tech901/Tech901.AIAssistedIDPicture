using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

public interface IFaceDetectionService
{
    Task<FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default);
}
