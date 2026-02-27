using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.ViewModels;

public partial class RosterMatchViewModel : ObservableObject
{
    private readonly ILogger<RosterMatchViewModel> _logger;

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

    public ObservableCollection<RosterMatch> Matches { get; } = [];

    public event Action<Student>? MatchConfirmed;
    public event Action? MatchDenied;

    public RosterMatchViewModel(ILogger<RosterMatchViewModel> logger)
    {
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
}
