# Tech901.IdPhoto.Infrastructure

Hardware and cloud service implementations. Depends on Core interfaces and provides concrete Azure and device integrations.

## Services

| Service                    | Implements               | Description                                        |
|----------------------------|--------------------------|----------------------------------------------------|
| `AzureSpeechService`       | `ISpeechService`         | Azure Cognitive Services Speech (TTS + STT)        |
| `NullSpeechService`        | `ISpeechService`         | No-op fallback when Azure Speech key is absent     |
| `AzureFaceService`         | `IFaceDetectionService`  | Azure Face API for face landmark detection (beta)  |
| `NullFaceDetectionService` | `IFaceDetectionService`  | No-op fallback; triggers center-crop in Core       |
| `WebcamService`            | `ICameraService`         | USB webcam capture via OpenCvSharp4                |

## Configuration

- **`AzureSpeechOptions`** — Binds `Azure:Speech:Key` and `Azure:Speech:Region`
- **`AzureFaceOptions`** — Binds `Azure:Face:Key` and `Azure:Face:Endpoint`

Options are populated from `appsettings.json` or .NET User Secrets.

## Dependency Injection

`ServiceCollectionExtensions.AddInfrastructure()` registers services conditionally:

- Azure Speech key present → `AzureSpeechService`; absent → `NullSpeechService`
- Azure Face key + endpoint present → `AzureFaceService`; absent → `NullFaceDetectionService`
- `WebcamService` is always registered

## Threading

- `AzureFaceService` uses `ConfigureAwait(false)` throughout for non-UI async calls.
- `WebcamService` uses `_stateLock` + `_frameLock` for thread-safe frame capture.

## AI-102 Concepts Demonstrated

This project covers several topics from the Azure AI Engineer (AI-102) exam:

- **Azure Cognitive Services Authentication Patterns** -- `AzureFaceService` (in `FaceDetection/AzureFaceService.cs`) authenticates with `AzureKeyCredential`, the standard pattern for key-based access to Cognitive Services. `AzureSpeechService` (in `Speech/AzureSpeechService.cs`) uses `SpeechConfig.FromSubscription()`, the Speech SDK equivalent. Both patterns retrieve credentials from the Options classes at construction time.

- **SDK Lifecycle Management** -- `AzureSpeechService` caches its `SpeechConfig` in the constructor rather than recreating it per call, avoiding repeated config parsing. `AzureFaceService` creates a `FaceClient` once and reuses it across detection requests. Understanding when to cache vs. recreate SDK objects is critical for production AI services.

- **Polly Resilience (Retry with Exponential Backoff)** -- Azure services can return transient HTTP errors (429 rate-limit, 503 unavailable). Polly retry policies wrap SDK calls with automatic retry and exponential backoff, preventing a single transient failure from disrupting the kiosk flow.

- **Rate Limiting with SemaphoreSlim** -- The Face API has per-second call limits, especially on the F0 (free) tier. `SemaphoreSlim` throttles concurrent requests so the application stays within quota rather than receiving 429 responses.

- **Options Pattern for Configuration** -- `AzureSpeechOptions` and `AzureFaceOptions` (in `Configuration/`) are POCO classes bound to configuration sections via `IOptions<T>`. This decouples service implementations from configuration sources (appsettings.json, User Secrets, or environment variables). See `DependencyInjection/ServiceCollectionExtensions.cs` for the binding setup.

## Dependencies

- Azure.AI.Vision.Face (1.0.0-beta.*)
- Microsoft.CognitiveServices.Speech
- OpenCvSharp4 + OpenCvSharp4.runtime.win
- Polly (retry policies)
- Microsoft.Extensions.Options
- Tech901.IdPhoto.Core
