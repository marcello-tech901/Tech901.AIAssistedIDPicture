using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class RosterMatchViewModel : ObservableObject, IDisposable
{
    private readonly ISpeechService _speech;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<RosterMatchViewModel> _logger;
    private CancellationTokenSource? _voiceCts;

    private static readonly string[] ConfirmPhrases =
        ["yes", "that's me", "correct", "confirm"];

    private static readonly string[] DenyPhrases =
        ["no", "not me", "wrong", "try again"];

    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["1"] = 1, ["2"] = 2, ["3"] = 3, ["4"] = 4, ["5"] = 5,
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5,
    };

    [ObservableProperty]
    private RosterMatch? _selectedMatch;

    [ObservableProperty]
    private bool _isSingleMatch;

    [ObservableProperty]
    private bool _isMultipleMatch;

    [ObservableProperty]
    private bool _isNoMatch;

    [ObservableProperty]
    private string? _topMatchName;

    [ObservableProperty]
    private string? _topMatchStudentId;

    [ObservableProperty]
    private bool _isListening;

    public ObservableCollection<RosterMatch> Matches { get; } = [];

    public event Action<Student>? MatchConfirmed;
    public event Action? MatchDenied;

    public RosterMatchViewModel(
        ISpeechService speech,
        IDispatcher dispatcher,
        ILogger<RosterMatchViewModel> logger)
    {
        _speech = speech;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void LoadMatches(IReadOnlyList<RosterMatch> matches)
    {
        Matches.Clear();
        foreach (var m in matches)
            Matches.Add(m);

        IsNoMatch = matches.Count == 0;
        IsSingleMatch = matches.Count == 1;
        IsMultipleMatch = matches.Count > 1;

        if (matches.Count >= 1)
        {
            var top = matches[0];
            TopMatchName = $"{top.Student.FirstName} {top.Student.LastName}";
            TopMatchStudentId = top.Student.StudentId;
            SelectedMatch = top;
        }

        _logger.LogInformation("Loaded {Count} roster matches", matches.Count);
    }

    public void StartVoiceListening()
    {
        if (!_speech.IsAvailable)
            return;

        _voiceCts?.Cancel();
        _voiceCts?.Dispose();
        _voiceCts = new CancellationTokenSource();
        _dispatcher.Invoke(() => IsListening = true);
        FireAndForget(VoiceListenLoopAsync(_voiceCts.Token));
    }

    private async Task VoiceListenLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Roster match voice listening started");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var text = await _speech.ListenAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                    break;

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var lower = text.ToLowerInvariant();

                if (IsMultipleMatch)
                {
                    // Try number words first
                    foreach (var (word, index) in NumberWords)
                    {
                        if (lower.Contains(word) && index >= 1 && index <= Matches.Count)
                        {
                            _logger.LogInformation("Voice number selection detected: \"{Text}\" -> index {Index}", text, index);
                            _dispatcher.Invoke(() =>
                            {
                                SelectedMatch = Matches[index - 1];
                                Confirm();
                            });
                            return;
                        }
                    }

                    // Try name matching
                    for (var i = 0; i < Matches.Count; i++)
                    {
                        var student = Matches[i].Student;
                        var firstName = student.FirstName?.ToLowerInvariant();
                        var lastName = student.LastName?.ToLowerInvariant();

                        if ((!string.IsNullOrEmpty(firstName) && lower.Contains(firstName)) ||
                            (!string.IsNullOrEmpty(lastName) && lower.Contains(lastName)))
                        {
                            _logger.LogInformation("Voice name match detected: \"{Text}\" -> {First} {Last}", text, student.FirstName, student.LastName);
                            var matchIndex = i;
                            _dispatcher.Invoke(() =>
                            {
                                SelectedMatch = Matches[matchIndex];
                                Confirm();
                            });
                            return;
                        }
                    }

                    // Fall through to confirm/deny for current selection
                }

                if (Array.Exists(ConfirmPhrases, phrase => lower.Contains(phrase)) && !IsNoMatch)
                {
                    _logger.LogInformation("Voice confirm detected: \"{Text}\"", text);
                    _dispatcher.Invoke(Confirm);
                    break;
                }

                if (Array.Exists(DenyPhrases, phrase => lower.Contains(phrase)))
                {
                    _logger.LogInformation("Voice deny detected: \"{Text}\"", text);
                    _dispatcher.Invoke(Deny);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during roster match voice listening");
        }
        finally
        {
            _dispatcher.Invoke(() => IsListening = false);
        }

        _logger.LogInformation("Roster match voice listening stopped");
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedMatch is null)
            return;

        _logger.LogInformation("Match confirmed: {StudentId}", SelectedMatch.Student.StudentId);
        MatchConfirmed?.Invoke(SelectedMatch.Student);
    }

    [RelayCommand]
    private void Deny()
    {
        _logger.LogInformation("Match denied");
        MatchDenied?.Invoke();
    }

    public void Dispose()
    {
        try
        {
            _voiceCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        _voiceCts?.Dispose();
    }

    private void FireAndForget(Task task, [CallerMemberName] string? caller = null)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled exception in background task from {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
