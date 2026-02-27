using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

public interface IImageProcessingService
{
    Task<byte[]> CropAndResizeAsync(byte[] imageData, FaceDetectionResult? face, CropSettings settings, CancellationToken ct = default);
    Task SaveImageAsync(byte[] imageData, string outputPath, CancellationToken ct = default);
    string BuildFilename(string template, Student student);
}
