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

## Dependencies

- CommunityToolkit.Mvvm v8
- Microsoft.Extensions.Logging.Abstractions
- Tech901.IdPhoto.Core (models, interfaces, enums)
