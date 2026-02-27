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

## Learning Objectives

This project demonstrates several foundational software design patterns:

- **Interface Segregation Principle (ISP)** -- Each service interface (`ICameraService`, `ISpeechService`, `IFaceDetectionService`, `IRosterService`, `IImageProcessingService`) defines a focused contract with only the methods its consumers need. Compare the narrow `IDispatcher` (single `InvokeAsync` method in `Interfaces/IDispatcher.cs`) with the broader `IRosterService` to see ISP in practice.

- **Null Object Pattern** -- The interfaces in this project define contracts that are implemented by both real services and no-op "null" fallbacks (e.g., `NullSpeechService`, `NullFaceDetectionService` in Infrastructure). Callers never check for null; they always get a valid implementation. The DI container selects the appropriate implementation at resolution time.

- **C# Record Types for Immutable Domain Models** -- Models like `Student`, `RosterMatch`, `FaceDetectionResult`, and `CropSettings` are declared as `record` types (see `Models/Student.cs`). Records provide value equality, immutability, and concise syntax for data-carrying types that represent domain concepts rather than mutable entities.

- **Fuzzy String Matching with FuzzySharp** -- `RosterService` (in `Services/RosterService.cs`) uses the FuzzySharp library to match spoken or typed participant names against the imported roster. This handles misspellings and partial matches, returning ranked `RosterMatch` results with a `MatchConfidence` level.

## Dependencies

- CsvHelper
- FuzzySharp
- SixLabors.ImageSharp
- Microsoft.Extensions.Logging.Abstractions
