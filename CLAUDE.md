# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build                                          # Build entire solution
dotnet test                                           # Run all tests
dotnet test tests/Tech901.IdPhoto.Core.Tests          # Run one test project
dotnet test --filter "FullyQualifiedName~RosterServiceTests"  # Run specific test class
dotnet run --project src/Tech901.IdPhoto.App          # Launch the kiosk app
```

Build enforces `TreatWarningsAsErrors=true` and `Nullable=enable` (see Directory.Build.props). No separate lint step — warnings-as-errors serves that role.

## Architecture

.NET 10 WPF kiosk application for capturing ID photos of adult training cohort participants.

**Projects & dependency graph:**
```
App (WPF, WinExe) → ViewModels → Core
                  → Infrastructure → Core
```

- **Core** — Domain models (`Student`, `RosterMatch`, `FaceDetectionResult`, `SessionState`, `CropSettings`, `RosterImportResult`), service interfaces (`ICameraService`, `IRosterService`, `IImageProcessingService`, `IFaceDetectionService`, `ISpeechService`), core service implementations (`RosterService`, `ImageProcessingService`), enums (`KioskState`, `MatchConfidence`).
- **ViewModels** — CommunityToolkit.Mvvm v8 (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`). One ViewModel per kiosk state, coordinated by `KioskFlowViewModel`.
- **Infrastructure** — Azure service implementations (Speech, Face API), camera via OpenCvSharp4. Null-service fallbacks when Azure keys are absent.
- **App** — Generic Host DI in `App.xaml.cs`, ContentControl + implicit DataTemplate navigation (ViewModel-first).

**State machine** (`KioskFlowViewModel.cs`):
`Idle → Detected → NameCapture → RosterMatch → Positioning → Capture → Review → Processing → Complete`

Child ViewModels fire events; `KioskFlowViewModel` handles transitions and swaps `CurrentViewModel`.

## DI & Configuration

Services are wired in `App.xaml.cs` and `Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`. Key behavior:
- Azure services use **resolution-time factory lambdas** (not registration-time config checks) so all config sources are guaranteed loaded.
- If `Azure:Speech:Key` + `Region` are set → `AzureSpeechService`; otherwise → `NullSpeechService`
- If `Azure:Face:Key` + `Endpoint` are set → `AzureFaceService`; otherwise → `NullFaceDetectionService`
- `KioskFlowViewModel` is Singleton; other ViewModels are Transient.

**Configuration layering** (last wins): `appsettings.json` → User Secrets → Environment Variables.

- **Dev machine**: Azure credentials stored via .NET User Secrets (ID: `Tech901.IdPhoto.App`), populated by `infra/deploy.ps1`.
- **Kiosk machine**: Azure credentials provided via **environment variables** (User Secrets are per-machine and don't ship with the published app). Use `__` as section separator:
  ```
  Azure__Speech__Key, Azure__Speech__Region, Azure__Face__Key, Azure__Face__Endpoint
  ```
  Run `infra/Test-AzureConfig.ps1` on the target to verify. `Install-Kiosk.ps1 -ConfigureAzure` sets them automatically.

Config lives in `src/Tech901.IdPhoto.App/appsettings.json`.

## Publishing & Deployment

Self-contained folder deployment (not MSIX/ClickOnce). OpenCvSharp4 and Azure Speech SDK native DLLs require `PublishSingleFile=false`; WPF/DI reflection requires `PublishTrimmed=false`.

```powershell
# Create dev signing certificate (elevated, one-time)
./infra/New-CodeSigningCert.ps1 -TrustCert

# Build, test, sign, and package
./infra/publish.ps1 -Version 1.0.0 -CertPath .\certs\Tech901-IdPhoto-Dev.pfx

# Deploy to kiosk machine (elevated, on target)
./infra/Install-Kiosk.ps1 -SourcePath .\Tech901-IdPhoto-1.0.0-win-x64.zip -AutoStart
```

Publish profile for Visual Studio: `src/Tech901.IdPhoto.App/Properties/PublishProfiles/FolderProfile.pubxml`.

Version is set in `Directory.Build.props` (`VersionPrefix`) and can be overridden via `-Version` on `publish.ps1`.

## Azure Infrastructure

```powershell
./infra/deploy.ps1 -Env dev -Location eastus    # Deploy Speech + Face API, store keys in User Secrets
./infra/teardown.ps1 -Env dev                    # Delete resource group + purge soft-deleted services
```

Bicep template: `infra/main.bicep` (Speech Service + Face API).

## Testing

xUnit + Moq + FluentAssertions. Tests follow `_sut` naming and Arrange/Act/Assert structure.

## Important Constraints

- **Adult cohort**: Never use K-12 language (no "homeroom", "grade", "pupil").
- **Azure.AI.Vision.Face**: Only available as beta (`1.0.0-beta.*`).
- **Brand colors**: Navy `#003763`, Lime Green `#A4CC27`, Near-Black `#141414`, White `#FFFFFF`, Gold `#FFBF3B`.
- **Admin access**: PIN `9019`, triggered via Ctrl+Shift+A or gear icon.
- **Code style**: 4-space indent (2 for XML/XAML/JSON/Bicep), CRLF, UTF-8 (see `.editorconfig`).
