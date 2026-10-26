using RaceTracker.Core;

namespace RaceTracker.Api;

// The API's public shapes. Kept separate from the EF entities so the database can change
// without breaking the Angular client, and so no entity is ever bound straight from a request.

public sealed record CheckpointDto(int Id, string Name, int Sequence, double DistanceKm);

public sealed record RaceDto(int Id, string Name, string Route, DateTime StartTime, int Laps, double TotalDistanceKm, int RunnerCount, IReadOnlyList<CheckpointDto> Checkpoints);

/// <summary>Times are whole seconds; the client formats them.</summary>
public sealed record ResultRowDto(
    int Bib, string Name, string Division, string Status, int? Place, int? DivisionPlace,
    int? FinishSeconds, int? LastSeenSeconds, string? LastSeenAt, double DistanceKm, int? PaceSecondsPerKm,
    bool NeedsReview, int MissedCount, int IgnoredCount);

public sealed record ResultsDto(int RaceId, DateTime CalculatedAt, int Finished, int OnCourse, int NotStarted, IReadOnlyList<ResultRowDto> Rows);

public sealed record PassDto(int Lap, string Checkpoint, double DistanceKm, DateTime? Time, int? ElapsedSeconds);

public sealed record IgnoredSplitDto(long SplitId, string Checkpoint, DateTime Time, string Issue);

public sealed record RunnerDetailDto(ResultRowDto Result, IReadOnlyList<PassDto> Passes, IReadOnlyList<string> Missed, IReadOnlyList<IgnoredSplitDto> Ignored);

public sealed record RecordSplitRequest(int Bib, int CheckpointId, DateTime? Time, string? RecordedBy);

public sealed record SplitDto(long Id, int Bib, string? RunnerName, string Checkpoint, DateTime Time, string RecordedBy, string? Warning);

public static class Mapping
{
    public static ResultRowDto ToDto(this RunnerResult r) => new(
        r.Runner.Bib, r.Runner.Name, r.Runner.Division, r.Status.ToString(), r.Place, r.DivisionPlace,
        Seconds(r.FinishTime), Seconds(r.LastSeenElapsed), r.LastSeenAt, Math.Round(r.DistanceCoveredKm, 2), Seconds(r.PacePerKm),
        r.NeedsReview, r.Missed.Count, r.Ignored.Count);

    public static string Describe(ExpectedPass p, Race race) =>
        race.Route == RouteType.Loop ? $"{p.Checkpoint.Name} (lap {p.Lap})" : p.Checkpoint.Name;

    public static string Describe(SplitIssue issue) => issue switch
    {
        SplitIssue.Duplicate => "Duplicate entry (same checkpoint seconds apart) — ignored",
        SplitIssue.BeforeStart => "Recorded before the start — ignored",
        SplitIssue.ExtraCrossing => "More crossings than the course has — ignored",
        _ => issue.ToString(),
    };

    private static int? Seconds(TimeSpan? t) => t is null ? null : (int)Math.Round(t.Value.TotalSeconds);
}
