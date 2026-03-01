namespace Tech901.IdPhoto.Infrastructure.Configuration;

public class CameraOptions
{
    public const string SectionName = "Kiosk:Camera";

    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int JpegQuality { get; set; } = 95;
    public int DeviceIndex { get; set; } = 0;
}
