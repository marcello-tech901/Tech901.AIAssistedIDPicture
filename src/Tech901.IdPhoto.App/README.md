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

## Learning Objectives

- **Generic Host in WPF (Non-ASP.NET Host)** -- `App.xaml.cs` builds a `Microsoft.Extensions.Hosting.Host` to get the same DI, configuration, and logging infrastructure that ASP.NET Core provides, but inside a desktop WPF application. This demonstrates that Generic Host is not web-specific -- it works anywhere you need structured startup, DI, and graceful shutdown.

- **ViewModel-First Navigation with Implicit DataTemplates** -- `MainWindow.xaml` contains a `ContentControl` bound to `KioskFlowViewModel.CurrentViewModel`. WPF's implicit `DataTemplate` mechanism (templates with `DataType` but no `x:Key`) automatically selects the correct View for the current ViewModel type. No navigation service or manual view instantiation is needed.

- **IDispatcher Bridge to WPF Dispatcher** -- `Services/WpfDispatcher.cs` implements the Core project's `IDispatcher` interface by delegating to `Application.Current.Dispatcher.InvokeAsync()`. This thin bridge allows ViewModels to marshal work to the UI thread without taking a dependency on WPF assemblies.

- **DI Container Wiring with Resolution-Time Factories** -- In `ServiceCollectionExtensions.AddInfrastructure()`, service registrations use factory lambdas that read configuration at resolution time (when the service is first requested), not at registration time (when `ConfigureServices` runs). This ensures all configuration sources (appsettings.json, User Secrets, environment variables) are fully loaded before the factory inspects them.

## Dependencies

- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Configuration.UserSecrets
- CommunityToolkit.Mvvm v8
- Serilog (Hosting, File, Console, Configuration, Thread enricher)
- Tech901.IdPhoto.ViewModels
- Tech901.IdPhoto.Infrastructure
