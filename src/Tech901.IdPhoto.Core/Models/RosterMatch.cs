using Tech901.IdPhoto.Core.Enums;

namespace Tech901.IdPhoto.Core.Models;

public record RosterMatch(
    Student Student,
    int Score,
    MatchConfidence Confidence);
