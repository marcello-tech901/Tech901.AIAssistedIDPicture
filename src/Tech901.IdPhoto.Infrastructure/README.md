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

## Dependencies

- Azure.AI.Vision.Face (1.0.0-beta.*)
- Microsoft.CognitiveServices.Speech
- OpenCvSharp4 + OpenCvSharp4.runtime.win
- Polly (retry policies)
- Microsoft.Extensions.Options
- Tech901.IdPhoto.Core
