namespace Tech901.IdPhoto.Infrastructure.Configuration;

/// <summary>
/// Strongly-typed configuration for the Azure AI Face API,
/// bound from the <c>Azure:Face</c> section of application configuration.
/// </summary>
/// <remarks>
/// <para>
/// AI-102: This class follows the <strong>Options Pattern</strong> (<c>IOptions&lt;T&gt;</c>),
/// the same approach used by <see cref="AzureSpeechOptions"/>. The pattern provides compile-time
/// safety (no typos in config key strings), IntelliSense support, and easy unit testing via
/// <c>Options.Create(new AzureFaceOptions { ... })</c>.
/// </para>
/// <para>
/// On the kiosk, credentials are provided via environment variables (<c>Azure__Face__Key</c>,
/// <c>Azure__Face__Endpoint</c>) using the double-underscore separator convention that
/// .NET configuration maps to nested section paths.
/// </para>
/// </remarks>
public class AzureFaceOptions
{
    /// <summary>
    /// The configuration section path that maps to this options class.
    /// </summary>
    /// <remarks>
    /// AI-102: Keeping the section name as a constant on the options class itself is a
    /// .NET convention that prevents magic strings from spreading across DI registration code.
    /// </remarks>
    public const string SectionName = "Azure:Face";

    /// <summary>
    /// The Azure Face API subscription key used for <see cref="Azure.AzureKeyCredential"/>
    /// authentication.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The Azure Face API endpoint URL (e.g., "https://myface.cognitiveservices.azure.com/").
    /// </summary>
    /// <remarks>
    /// AI-102: Unlike Speech (which uses region-based routing), the Face API uses a
    /// full endpoint URL. This is because Face API resources have unique DNS names
    /// per deployment, enabling network isolation and private endpoint scenarios.
    /// </remarks>
    public string Endpoint { get; set; } = string.Empty;
}
