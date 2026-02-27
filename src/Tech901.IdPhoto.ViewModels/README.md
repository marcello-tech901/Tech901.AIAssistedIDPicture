# Tech901.IdPhoto.ViewModels

MVVM ViewModels using CommunityToolkit.Mvvm v8. One ViewModel per kiosk state, coordinated by a central state-machine ViewModel.

## ViewModels

| ViewModel                | State         | Responsibility                                    |
|--------------------------|---------------|---------------------------------------------------|
| `KioskFlowViewModel`    | (orchestrator)| Singleton state machine; swaps `CurrentViewModel` on transitions |
| `IdleViewModel`          | Idle          | Welcome/attract screen                            |
| `NameCaptureViewModel`   | NameCapture   | Speech or keyboard name entry                     |
| `RosterMatchViewModel`   | RosterMatch   | Fuzzy match candidate selection and confirmation   |
| `PositioningViewModel`   | Positioning   | Camera preview with face-guide overlay             |
| `CaptureViewModel`       | Capture       | Countdown and photo capture                        |
| `ReviewViewModel`        | Review        | Photo review with retake/accept                    |
| `CompleteViewModel`      | Complete      | Success confirmation, return to idle               |
| `BatchProcessViewModel`  | (admin)       | Batch processing of existing photo directories     |
| `AdminViewModel`         | (admin)       | Roster import, config, session management          |
| `ErrorViewModel`         | Error         | Error display with recovery options                |

## Patterns

- All ViewModels inherit `ObservableObject` and use `[ObservableProperty]` for bindable properties and `[RelayCommand]` for commands.
- Child ViewModels raise events on completion; `KioskFlowViewModel` subscribes and drives state transitions.
- `KioskFlowViewModel` is registered as a Singleton; all others are Transient.
- Background work uses a `FireAndForget` helper for observed async tasks.

## Learning Objectives

- **Finite State Machine Pattern** -- `KioskFlowViewModel` (in `KioskFlowViewModel.cs`) implements a state machine that drives the kiosk through its states: Idle, Detected, NameCapture, RosterMatch, Positioning, Capture, Review, Processing, Complete, and Error. Each state maps to a child ViewModel. Transitions are triggered by events from child ViewModels, keeping transition logic centralized in one place.

- **CommunityToolkit.Mvvm Source Generators** -- All ViewModels use `[ObservableProperty]` to auto-generate property-changed notifications and `[RelayCommand]` to auto-generate `ICommand` implementations. These source generators eliminate boilerplate while keeping ViewModels testable. See any ViewModel (e.g., `CaptureViewModel.cs`) for examples.

- **Event-Driven ViewModel Composition** -- Child ViewModels raise .NET events (e.g., `CaptureCompleted`, `RetakeRequested`) when they finish their work. `KioskFlowViewModel` subscribes to these events and orchestrates transitions, creating a loosely-coupled composition where child ViewModels do not reference each other or the parent.

- **IDispatcher for Testable UI Thread Marshaling** -- ViewModels that need to update UI-bound properties from background threads use `IDispatcher.InvokeAsync()` (defined in Core's `Interfaces/IDispatcher.cs`). In production, `WpfDispatcher` delegates to WPF's `Application.Current.Dispatcher`. In tests, `TestDispatcher` executes synchronously. This abstraction allows ViewModels to be fully unit-tested without a WPF runtime.

## Dependencies

- CommunityToolkit.Mvvm v8
- Microsoft.Extensions.Logging.Abstractions
- Tech901.IdPhoto.Core (models, interfaces, enums)
