using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.Infrastructure.FaceDetection;

/// <summary>
/// Local face presence detector using OpenCvSharp Haar cascade.
/// Used for the Idle → NameCapture gate — no Azure dependency needed.
/// </summary>
public sealed class LocalPresenceDetector : IPresenceDetector, IDisposable
{
    private readonly CascadeClassifier _cascade;
    private readonly ILogger<LocalPresenceDetector> _logger;

    public LocalPresenceDetector(ILogger<LocalPresenceDetector> logger)
    {
        _logger = logger;

        var cascadePath = Path.Combine(
            AppContext.BaseDirectory,
            "FaceDetection",
            "haarcascade_frontalface_default.xml");

        if (!File.Exists(cascadePath))
        {
            // Fall back to OpenCvSharp4 data directory
            var openCvDir = Path.GetDirectoryName(typeof(Cv2).Assembly.Location);
            if (openCvDir is not null)
            {
                var altPath = Path.Combine(openCvDir, "haarcascade_frontalface_default.xml");
                if (File.Exists(altPath))
                    cascadePath = altPath;
            }
        }

        if (!File.Exists(cascadePath))
        {
            _logger.LogWarning(
                "Haar cascade file not found at {Path} — presence detection will always return true",
                cascadePath);
            _cascade = new CascadeClassifier();
            return;
        }

        _cascade = new CascadeClassifier(cascadePath);

        if (_cascade.Empty())
        {
            _logger.LogWarning(
                "Haar cascade loaded but empty at {Path} — presence detection will always return true",
                cascadePath);
        }
        else
        {
            _logger.LogInformation("Loaded Haar cascade from {Path}", cascadePath);
        }
    }

    public bool DetectPresence(byte[] imageData)
    {
        if (_cascade.Empty())
            return true;

        try
        {
            using var raw = Mat.FromImageData(imageData, ImreadModes.Color);
            using var gray = new Mat();
            Cv2.CvtColor(raw, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            var faces = _cascade.DetectMultiScale(
                gray,
                scaleFactor: 1.1,
                minNeighbors: 3,
                flags: HaarDetectionTypes.ScaleImage,
                minSize: new Size(80, 80));

            return faces.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Haar cascade detection failed — returning false");
            return false;
        }
    }

    public void Dispose()
    {
        _cascade.Dispose();
    }
}
