namespace Tech901.IdPhoto.Infrastructure.Configuration;

public class AzureFaceOptions
{
    public const string SectionName = "Azure:Face";

    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}
