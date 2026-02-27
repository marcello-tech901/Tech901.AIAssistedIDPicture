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

public sealed class AzureFaceService : IFaceDetectionService
{
    private readonly FaceClient _client;
    private readonly ILogger<AzureFaceService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastCallTime = DateTime.MinValue;

    public AzureFaceService(IOptions<AzureFaceOptions> options, ILogger<AzureFaceService> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _client = new FaceClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.Key));

        _retryPolicy = Policy
            .Handle<RequestFailedException>(ex => ex.Status == 429 || ex.Status >= 500)
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Retry {Attempt} after {Delay}s", attempt, delay.TotalSeconds));
    }

    public async Task<CoreModels.FaceDetectionResult?> DetectFaceAsync(byte[] imageData, CancellationToken ct = default)
    {
        await ThrottleAsync(ct).ConfigureAwait(false);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var data = BinaryData.FromBytes(imageData);
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
