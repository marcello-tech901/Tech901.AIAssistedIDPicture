# Tech901.IdPhoto.Core

Domain layer — models, service interfaces, enums, and platform-agnostic service implementations. This project has no dependency on WPF, Azure, or hardware SDKs.

## Models

| Model                | Purpose                                                |
|----------------------|--------------------------------------------------------|
| `Student`            | Roster entry (StudentId, FirstName, LastName, extras)  |
| `RosterMatch`        | Candidate match result with confidence score           |
| `FaceDetectionResult`| Face landmark coordinates from detection services      |
| `CropSettings`       | Output dimensions, padding multiplier, format          |
| `SessionState`       | Tracks completed students and session progress         |
| `RosterImportResult` | Result of CSV roster import (students + validation)    |

## Interfaces

| Interface                   | Implemented By                                      |
|-----------------------------|-----------------------------------------------------|
| `ICameraService`            | `WebcamService` (Infrastructure)                    |
| `ISpeechService`            | `AzureSpeechService`, `NullSpeechService`           |
| `IFaceDetectionService`     | `AzureFaceService`, `NullFaceDetectionService`      |
| `IImageProcessingService`   | `ImageProcessingService` (this project)             |
| `IRosterService`            | `RosterService` (this project)                      |
| `IDispatcher`               | `WpfDispatcher` (App), `TestDispatcher` (tests)     |

## Services

- **`RosterService`** — Loads roster from CSV (via CsvHelper), performs fuzzy name matching (via FuzzySharp), thread-safe with `lock(_lock)`.
- **`ImageProcessingService`** — Face-aware or center-crop image processing using SixLabors.ImageSharp. Configurable output dimensions and padding.

## Enums

- **`KioskState`** — State machine values (Idle, Detected, NameCapture, etc.)
- **`MatchConfidence`** — High, Medium, Low confidence for roster matches

## Dependencies

- CsvHelper
- FuzzySharp
- SixLabors.ImageSharp
- Microsoft.Extensions.Logging.Abstractions
