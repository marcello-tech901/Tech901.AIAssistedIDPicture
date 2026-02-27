namespace Tech901.IdPhoto.Core.Models;

public record CropSettings(
    int OutputWidth,
    int OutputHeight,
    double PaddingMultiplier,
    string OutputFormat);
