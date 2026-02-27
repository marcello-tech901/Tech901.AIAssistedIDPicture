using Azure;
using Azure.AI.Vision.Face;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Infrastructure.Configuration;
using CoreModels = Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Infrastructure.FaceDetection;

/// <summary>
/// Detects faces in images using the Azure AI Face API and returns landmark coordinates
/// used for intelligent photo cropping.
/// </summary>
/// <remarks>
/// <para>
/// AI-102: This service demonstrates the Azure AI Face API client pattern —
/// authenticating with <see cref="AzureKeyCredential"/>, selecting detection/recognition
/// models, and extracting face landmarks from binary image data.
/// </para>
/// <para>
/// The service includes production-hardening via Polly retry policies (exponential backoff
/// for transient failures) and <see cref="SemaphoreSlim"/>-based rate limiting to stay
/// within the Face API free-tier throttle (20 calls/minute).
/// </para>
/// </remarks>
public sealed class AzureFaceService : IFaceDetectionService
{
    private readonly FaceClient _client;
    private readonly ILogger<AzureFaceService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    // AI-102: SemaphoreSlim(1,1) acts as an async-compatible mutex to serialize API calls,
    // preventing burst traffic that would trigger HTTP 429 (Too Many Requests) responses
    // from the Face API rate limiter.
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastCallTime = DateTime.MinValue;

    /// <summary>
    /// Initializes the Face API client with key-based authentication and configures
    /// retry and rate-limiting policies.
    /// </summary>
    /// <param name="options">Azure Face API endpoint and key from configuration.</param>
    /// <param name="logger">Logger for diagnostics and retry telemetry.</param>
    public AzureFaceService(IOptions<AzureFaceOptions> options, ILogger<AzureFaceService> logger)
    {
        _logger = logger;
        var opts = options.Value;

        // AI-102: AzureKeyCredential authenticates each request via the Ocp-Apim-Subscription-Key
        // header. This is the simplest auth model for Azure AI services — suitable for
        // server-side or kiosk apps where the key can be kept secret.
        _client = new FaceClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.Key));

        // AI-102: Polly retry with exponential backoff handles transient failures gracefully.
        // We retry on HTTP 429 (rate limited) and 5xx (server errors) — these are the
        // standard retryable status codes for Azure AI services.
        _retryPolicy = Policy
            .Handle<RequestFailedException>(ex => ex.Status == 429 || ex.Status >= 500)
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Retry {Attempt} after {Delay}s", attempt, delay.TotalSeconds));
    }

    /// <summary>
    /// Detects the primary face in the provided image and returns landmark coordinates.
    /// </summary>
    /// <param name="imageData">Raw image bytes (JPEG or PNG) to analyze.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="CoreModels.FaceDetectionResult"/> containing nose, eye, and bounding-box
    /// coordinates for the first detected face, or <c>null</c> if no face is found.
    /// </returns>
    /// <remarks>
    /// AI-102: The method requests face landmarks but sets <c>returnFaceId: false</c>.
    /// This is a deliberate privacy decision — face IDs enable cross-image identification,
    /// which is unnecessary for photo cropping and raises data-protection concerns.
    /// </remarks>
    public async Task<CoreModels.FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default)
    {
        await ThrottleAsync(ct).ConfigureAwait(false);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var data = BinaryData.FromBytes(imageData);

            // AI-102: Detection03 is the latest detection model with improved accuracy for
            // small, side-view, and blurry faces. Recognition04 is the latest recognition model.
            // returnFaceId:false — we only need landmarks for cropping, not identity matching.
            // returnFaceLandmarks:true — provides nose tip, pupil positions, etc. for smart crop.
            var response = await _client.DetectAsync(
                data,
                FaceDetectionModel.Detection03,
                FaceRecognitionModel.Recognition04,
                returnFaceId: false,
                returnFaceLandmarks: true,
                cancellationToken: ct).ConfigureAwait(false);

            var faces = response.Value;
            if (faces.Count == 0)
            {
                _logger.LogInformation("No faces detected");
                return null;
            }

            // Take the first (largest/most prominent) detected face
            var face = faces[0];
            var rect = face.FaceRectangle;
            var landmarks = face.FaceLandmarks;

            return new CoreModels.FaceDetectionResult(
                NoseTip: new CoreModels.PointF((float)landmarks.NoseTip.X, (float)landmarks.NoseTip.Y),
                FaceRectangle: new CoreModels.RectangleF(rect.Left, rect.Top, rect.Width, rect.Height),
                LeftEye: new CoreModels.PointF((float)landmarks.PupilLeft.X, (float)landmarks.PupilLeft.Y),
                RightEye: new CoreModels.PointF((float)landmarks.PupilRight.X, (float)landmarks.PupilRight.Y));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Enforces a minimum 1-second interval between API calls to respect rate limits.
    /// </summary>
    /// <remarks>
    /// AI-102: Azure AI services enforce rate limits (requests per second/minute) based on
    /// pricing tier. The free tier for Face API allows 20 calls/minute. This throttle
    /// cooperates with the Polly retry policy — throttling prevents 429s proactively,
    /// while retry handles them reactively if they still occur.
    /// </remarks>
    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCallTime;
            if (elapsed < TimeSpan.FromSeconds(1))
            {
                await Task.Delay(TimeSpan.FromSeconds(1) - elapsed, ct).ConfigureAwait(false);
            }

            _lastCallTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
