namespace Tech901.IdPhoto.Core.Models;

public record FaceDetectionResult(
    PointF NoseTip,
    RectangleF FaceRectangle,
    PointF LeftEye,
    PointF RightEye);

public record struct PointF(float X, float Y);

public record struct RectangleF(float X, float Y, float Width, float Height);
