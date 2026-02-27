using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Audio;
using Tech901.IdPhoto.Infrastructure.Camera;
using Tech901.IdPhoto.Infrastructure.Configuration;
using Tech901.IdPhoto.Infrastructure.FaceDetection;
using Tech901.IdPhoto.Infrastructure.Speech;

namespace Tech901.IdPhoto.Infrastructure.DependencyInjection;

/// <summary>
/// Registers all Infrastructure-layer services (Azure AI, camera, audio) into the
/// .NET dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// AI-102: This class demonstrates a critical DI pattern for Azure AI services —
/// <strong>resolution-time factory lambdas</strong>. Configuration values (API keys,
/// endpoints) are read when the service is first resolved, not when it is registered.
/// This guarantees that all configuration sources (appsettings.json, User Secrets,
/// environment variables) have been loaded before the decision to use a real or null
/// service is made.
/// </para>
/// <para>
/// The Null Object Pattern is applied at the DI level: if Azure credentials are missing,
/// the factory returns a <see cref="NullSpeechService"/> or <see cref="NullFaceDetectionService"/>
/// instead of throwing. This lets the kiosk run in a degraded but functional mode without
/// any conditional logic in consuming code.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services including camera, speech, face detection, audio
    /// enumeration, and presence detection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">
    /// The application configuration root, providing access to Azure credentials and
    /// device settings via the Options Pattern.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // AI-102: The Options Pattern (IOptions<T>) binds configuration sections to strongly-typed
        // POCOs. This decouples services from IConfiguration and makes settings testable/mockable.
        services.Configure<AzureSpeechOptions>(o =>
        {
            configuration.GetSection(AzureSpeechOptions.SectionName).Bind(o);

            // Allow Kiosk:SpeechVoice to override the Azure section voice
            var kioskVoice = configuration["Kiosk:SpeechVoice"];
            if (!string.IsNullOrWhiteSpace(kioskVoice))
                o.Voice = kioskVoice;
        });
        services.Configure<AzureFaceOptions>(o => configuration.GetSection(AzureFaceOptions.SectionName).Bind(o));

        // Camera
        services.Configure<CameraOptions>(o => configuration.GetSection(CameraOptions.SectionName).Bind(o));
        services.AddSingleton<ICameraService, WebcamService>();

        // Local presence detection (always local — no Azure needed)
        services.AddSingleton<IPresenceDetector, LocalPresenceDetector>();

        // Audio device enumeration
        services.AddSingleton<IAudioDeviceEnumerator, AudioDeviceEnumerator>();

        // AI-102: Resolution-time factory lambda for Speech service.
        // WHY not registration-time? At registration time, the Generic Host has not yet
        // finished loading all configuration providers (User Secrets, env vars). By deferring
        // the key check to resolution time, we guarantee the final merged configuration is used.
        services.AddSingleton<ISpeechService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureSpeechOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ISpeechService>>();

            if (!string.IsNullOrWhiteSpace(opts.Key) && !string.IsNullOrWhiteSpace(opts.Region))
            {
                try
                {
                    // AI-102: ActivatorUtilities.CreateInstance resolves constructor parameters
                    // from the DI container while allowing the factory to control instantiation.
                    // This is preferred over new AzureSpeechService(...) because it lets DI
                    // inject IOptions<T>, ILogger<T>, etc. automatically.
                    var svc = ActivatorUtilities.CreateInstance<AzureSpeechService>(sp);
                    logger.LogInformation("Azure Speech Service activated (Region={Region})", opts.Region);
                    return svc;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create AzureSpeechService — falling back to NullSpeechService");
                }
            }
            else
            {
                logger.LogWarning("Azure Speech not configured (Key={HasKey}, Region={HasRegion}) — using NullSpeechService",
                    !string.IsNullOrWhiteSpace(opts.Key), !string.IsNullOrWhiteSpace(opts.Region));
            }

            // Null Object fallback — kiosk runs without speech, no null-checks needed downstream
            return new NullSpeechService();
        });

        // AI-102: Same resolution-time factory pattern for Face Detection.
        // The factory checks for Endpoint + Key at resolution time, returning the null
        // fallback if credentials are absent. This mirrors the Speech service pattern above.
        services.AddSingleton<IFaceDetectionService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureFaceOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<IFaceDetectionService>>();

            if (!string.IsNullOrWhiteSpace(opts.Endpoint) && !string.IsNullOrWhiteSpace(opts.Key))
            {
                try
                {
                    var svc = ActivatorUtilities.CreateInstance<AzureFaceService>(sp);
                    logger.LogInformation("Azure Face Service activated (Endpoint={Endpoint})", opts.Endpoint);
                    return svc;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create AzureFaceService — falling back to NullFaceDetectionService");
                }
            }
            else
            {
                logger.LogWarning("Azure Face API not configured — using NullFaceDetectionService");
            }
            return ActivatorUtilities.CreateInstance<NullFaceDetectionService>(sp);
        });

        return services;
    }
}
