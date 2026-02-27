# Tech901.IdPhoto.App

WPF host application. Wires up dependency injection, defines XAML views and theming, and provides the application entry point.

## Startup

`App.xaml.cs` builds a Generic Host with:

- Service registration via `ServiceCollectionExtensions.AddInfrastructure()`
- Serilog logging (file + console)
- User Secrets (ID: `Tech901.IdPhoto.App`)
- `appsettings.json` configuration

## Navigation

ViewModel-first navigation using `ContentControl` with implicit `DataTemplate` mappings. `MainWindow.xaml` binds its content to `KioskFlowViewModel.CurrentViewModel`; WPF resolves the matching View automatically.

## Views

Each kiosk state has a corresponding XAML view:

| View                    | ViewModel                |
|-------------------------|--------------------------|
| `IdleView`              | `IdleViewModel`          |
| `NameCaptureView`       | `NameCaptureViewModel`   |
| `RosterMatchView`       | `RosterMatchViewModel`   |
| `PositioningView`       | `PositioningViewModel`   |
| `CaptureView`           | `CaptureViewModel`       |
| `ReviewView`            | `ReviewViewModel`        |
| `CompleteView`          | `CompleteViewModel`      |
| `BatchProcessView`      | `BatchProcessViewModel`  |
| `AdminView`             | `AdminViewModel`         |
| `ErrorView`             | `ErrorViewModel`         |

## Theming

`Themes/Tech901Theme.xaml` defines brand resources (Navy `#003763`, Lime Green `#A4CC27`, Near-Black `#141414`, White, Gold `#FFBF3B`) and shared styles. `Themes/Controls.xaml` provides custom control templates.

## Converters

- `ByteArrayToImageConverter` — Camera frame bytes to `BitmapImage`
- `BoolToVisibilityConverter` — Standard bool-to-visibility
- `NotNullToVisibilityConverter` — Shows element when value is non-null

## Services

- `WpfDispatcher` — Implements `IDispatcher` for UI thread marshaling via `Application.Current.Dispatcher`

## Publishing

A Visual Studio publish profile is at `Properties/PublishProfiles/FolderProfile.pubxml`. Release builds are self-contained win-x64 with ReadyToRun pre-compilation. Use `infra/publish.ps1` for the full pipeline (build, test, sign, package).

## Dependencies

- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Configuration.UserSecrets
- CommunityToolkit.Mvvm v8
- Serilog (Hosting, File, Console, Configuration, Thread enricher)
- Tech901.IdPhoto.ViewModels
- Tech901.IdPhoto.Infrastructure
