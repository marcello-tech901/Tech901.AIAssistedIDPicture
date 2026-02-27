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

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Options
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

        // Speech - resolve at service-creation time so all config sources are loaded
        services.AddSingleton<ISpeechService>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureSpeechOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<ISpeechService>>();

            if (!string.IsNullOrWhiteSpace(opts.Key) && !string.IsNullOrWhiteSpace(opts.Region))
            {
                try
                {
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
            return new NullSpeechService();
        });

        // Face Detection - resolve at service-creation time so all config sources are loaded
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
