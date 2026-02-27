# Tech901 AI-Assisted ID Photo

A .NET 10 WPF kiosk application that guides participants through capturing a high-quality ID photo. The app detects when a person steps up, captures their name via speech or keyboard, matches against a pre-loaded roster, guides positioning, captures and auto-crops the photo using face landmarks, and saves it with a configurable filename template.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- Windows 10/11 with a USB webcam
- (Optional) Azure Speech and Face API keys for voice interaction and face-aware cropping

### Run the App

```bash
dotnet run --project src/Tech901.IdPhoto.App
```

The app works without Azure keys — speech falls back to keyboard input and face detection falls back to center-crop.

### Configure Azure Services (Optional)

```powershell
# Deploy Azure resources and store keys automatically
./infra/deploy.ps1 -Env dev -Location eastus

# Or set keys manually
dotnet user-secrets --id Tech901.IdPhoto.App set "Azure:Speech:Key" "<key>"
dotnet user-secrets --id Tech901.IdPhoto.App set "Azure:Speech:Region" "eastus"
dotnet user-secrets --id Tech901.IdPhoto.App set "Azure:Face:Key" "<key>"
dotnet user-secrets --id Tech901.IdPhoto.App set "Azure:Face:Endpoint" "<endpoint>"
```

## Architecture

```
src/
  Tech901.IdPhoto.App/            # WPF host, views, DI, theming
  Tech901.IdPhoto.ViewModels/     # MVVM ViewModels (CommunityToolkit.Mvvm)
  Tech901.IdPhoto.Core/           # Domain models, interfaces, core services
  Tech901.IdPhoto.Infrastructure/ # Azure services, webcam (OpenCvSharp4)

tests/
  Tech901.IdPhoto.Core.Tests/
  Tech901.IdPhoto.ViewModels.Tests/
  Tech901.IdPhoto.Infrastructure.Tests/

infra/
  main.bicep              # Azure resource definitions
  deploy.ps1 / teardown.ps1
  publish.ps1             # Build, test, sign, package pipeline
  Install-Kiosk.ps1       # Target machine installer
  New-CodeSigningCert.ps1 # Dev certificate creation
```

**Dependency graph:**

```
App (WPF) --> ViewModels --> Core
          --> Infrastructure --> Core
```

### Kiosk State Machine

```
Idle --> Detected --> NameCapture --> RosterMatch --> Positioning --> Capture --> Review --> Processing --> Complete
  \        \            \               \              \              \          \           \              |
   \--------\------------\---------------\--------------\--------------\----------\-----------\-----> Error
                                                                                                      |
                                                                                                      v
                                                                                                    Idle
```

Each state has a dedicated ViewModel and View. `KioskFlowViewModel` orchestrates transitions.

> **Note:** The `Detected` state is defined in the `KioskState` enum but the current flow transitions directly from `Idle` to `NameCapture`. Any state can transition to `Error` on unhandled exceptions; `Error` resets back to `Idle`.

## Build & Test

```bash
dotnet build                                          # Build entire solution
dotnet test                                           # Run all tests
dotnet test --filter "FullyQualifiedName~RosterServiceTests"  # Run specific test class
```

Build enforces `TreatWarningsAsErrors` and `Nullable=enable` via `Directory.Build.props`.

## Publishing & Deployment

The app is published as a self-contained, signed folder deployment (not MSIX/ClickOnce).

```powershell
# One-time: create a dev signing certificate (elevated)
./infra/New-CodeSigningCert.ps1 -TrustCert

# Build, test, sign, and package as ZIP
./infra/publish.ps1 -Version 1.0.0 -CertPath .\certs\Tech901-IdPhoto-Dev.pfx

# Install on a kiosk machine (elevated)
./infra/Install-Kiosk.ps1 -SourcePath .\Tech901-IdPhoto-1.0.0-win-x64.zip -AutoStart
```

See each project's README for more detail on its responsibilities.

## Admin Access

PIN `9019`, triggered via **Ctrl+Shift+A** or the gear icon.

## Brand Colors

| Color      | Hex       |
|------------|-----------|
| Navy       | `#003763` |
| Lime Green | `#A4CC27` |
| Near-Black | `#141414` |
| White      | `#FFFFFF` |
| Gold       | `#FFBF3B` |

## Learning Guide: AI-102 Exam Objectives

This project covers several domains from the [AI-102: Designing and Implementing a Microsoft Azure AI Solution](https://learn.microsoft.com/en-us/credentials/certifications/azure-ai-engineer/) exam. The table below maps exam domains to concrete code in this repository.

| AI-102 Domain | Concept | Code File(s) |
|---|---|---|
| Plan and manage an Azure AI solution | Resource provisioning via Bicep | `infra/main.bicep`, `infra/deploy.ps1` |
| Plan and manage an Azure AI solution | Key/endpoint configuration & fallback | `ServiceCollectionExtensions.cs`, `appsettings.json` |
| Implement computer vision solutions | Face API detection & landmarks | `AzureFaceService.cs`, `IFaceDetectionService.cs` |
| Implement computer vision solutions | Face-aware image cropping | `ImageProcessingService.cs`, `CropSettings.cs` |
| Implement natural language processing | Speech-to-text (voice name capture) | `AzureSpeechService.cs`, `ISpeechService.cs` |
| Implement natural language processing | Text-to-speech (kiosk prompts) | `AzureSpeechService.cs`, `ISpeechService.cs` |
| Implement decision support solutions | N/A (not covered in this project) | — |
| Implement knowledge mining and document intelligence | N/A (not covered in this project) | — |
| Implement generative AI solutions | N/A (not covered in this project) | — |

## Software Engineering Concepts

Patterns and practices demonstrated in this codebase, with file references:

- **Dependency Injection (Generic Host)** — `App.xaml.cs`, `ServiceCollectionExtensions.cs`
- **Null Object Pattern** — `NullSpeechService.cs`, `NullFaceDetectionService.cs` (graceful degradation when Azure keys are absent)
- **State Machine** — `KioskFlowViewModel.cs` (event-driven transitions between kiosk states)
- **Options Pattern** — `AzureSpeechOptions.cs`, `AzureFaceOptions.cs` (strongly-typed configuration binding)
- **Polly Resilience** — `AzureFaceService.cs` (retry and circuit-breaker for Azure API calls)
- **MVVM with Source Generators** — ViewModels project using CommunityToolkit.Mvvm `[ObservableProperty]` and `[RelayCommand]`
- **Thread Safety** — `RosterService.cs` (`lock`), `AzureFaceService.cs` (`SemaphoreSlim`)
- **ViewModel-First Navigation** — `ContentControl` + implicit `DataTemplate` in `MainWindow.xaml`
- **Infrastructure as Code** — `infra/main.bicep` with parameterized Bicep templates

## License

Copyright (c) Tech901 2025-2026. All rights reserved.
