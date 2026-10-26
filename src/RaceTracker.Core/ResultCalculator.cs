namespace RaceTracker.Core;

public enum RunnerStatus { Finished, OnCourse, NotStarted }

public enum SplitIssue { Duplicate, BeforeStart, ExtraCrossing }

/// <summary>A point the runner is expected to cross: checkpoint X on lap N.</summary>
public sealed record ExpectedPass(int Index, int Lap, Checkpoint Checkpoint, double DistanceKm);

public sealed record PassResult(ExpectedPass Pass, DateTime? Time, TimeSpan? Elapsed);

public sealed record IgnoredSplit(Split Split, SplitIssue Issue);

public sealed record RunnerResult(
    Runner Runner,
    RunnerStatus Status,
    int? Place,
    int? DivisionPlace,
    TimeSpan? FinishTime,
    double DistanceCoveredKm,
    TimeSpan? LastSeenElapsed,
    string? LastSeenAt,
    TimeSpan? PacePerKm,
    IReadOnlyList<PassResult> Passes,
    IReadOnlyList<ExpectedPass> Missed,
    IReadOnlyList<IgnoredSplit> Ignored)
{
    /// <summary>A finisher with a missing checkpoint may have cut the course, or a volunteer missed them. A person decides.</summary>
    public bool NeedsReview => Status == RunnerStatus.Finished && Missed.Count > 0;
}

/// <summary>
/// Turns raw splits into results. A pure function of its inputs: no database, no clock,
/// so every rule is unit-testable and results can be recalculated at any time.
/// </summary>
public static class ResultCalculator
{
    public static IReadOnlyList<ExpectedPass> Course(Race race)
    {
        var ordered = race.Checkpoints.OrderBy(c => c.Sequence).ToList();
        var passes = new List<ExpectedPass>();
        for (var lap = 1; lap <= race.Laps; lap++)
            foreach (var cp in ordered)
                passes.Add(new ExpectedPass(passes.Count, lap, cp, (lap - 1) * race.LapDistanceKm + cp.DistanceKm));
        return passes;
    }

    public static IReadOnlyList<RunnerResult> Calculate(Race race, IEnumerable<Runner> runners, IEnumerable<Split> splits)
    {
        var course = Course(race);
        var byBib = splits.GroupBy(s => s.Bib).ToDictionary(g => g.Key, g => g.ToList());
        var unranked = runners.Select(r => ForRunner(race, course, r, byBib.GetValueOrDefault(r.Bib) ?? [])).ToList();
        return Rank(unranked);
    }

    private static RunnerResult ForRunner(Race race, IReadOnlyList<ExpectedPass> course, Runner runner, List<Split> splits)
    {
        var ignored = new List<IgnoredSplit>();
        var matched = new DateTime?[course.Count];
        var position = 0; // index of the next expected pass
        var lastAcceptedAt = new Dictionary<int, DateTime>();

        foreach (var split in splits.OrderBy(s => s.Time).ThenBy(s => s.Id))
        {
            if (split.Time < race.StartTime)
            {
                ignored.Add(new(split, SplitIssue.BeforeStart));
                continue;
            }
            // A second tap at the same checkpoint seconds later is the same crossing, not a new lap.
            if (lastAcceptedAt.TryGetValue(split.CheckpointId, out var previous)
                && (split.Time - previous).TotalSeconds < race.DuplicateWindowSeconds)
            {
                ignored.Add(new(split, SplitIssue.Duplicate));
                continue;
            }
            // Match to the next expected crossing of this checkpoint. Anything skipped on the way was missed.
            var next = FindNext(course, position, split.CheckpointId);
            if (next is null)
            {
                ignored.Add(new(split, SplitIssue.ExtraCrossing));
                continue;
            }
            matched[next.Value] = split.Time;
            position = next.Value + 1;
            lastAcceptedAt[split.CheckpointId] = split.Time;
        }

        var passes = course.Select((p, i) => new PassResult(p, matched[i], matched[i] - race.StartTime)).ToList();
        var lastIndex = Array.FindLastIndex(matched, t => t is not null);
        if (lastIndex < 0)
            return new RunnerResult(runner, RunnerStatus.NotStarted, null, null, null, 0, null, null, null, passes, [], ignored);

        var last = passes[lastIndex];
        var finished = lastIndex == course.Count - 1;
        var missed = passes.Take(lastIndex).Where(p => p.Time is null).Select(p => p.Pass).ToList();
        var pace = last.Pass.DistanceKm > 0 ? last.Elapsed!.Value / last.Pass.DistanceKm : (TimeSpan?)null;
        var where = race.Route == RouteType.Loop ? $"{last.Pass.Checkpoint.Name}, lap {last.Pass.Lap}" : last.Pass.Checkpoint.Name;

        return new RunnerResult(
            runner,
            finished ? RunnerStatus.Finished : RunnerStatus.OnCourse,
            null, null,
            finished ? last.Elapsed : null,
            last.Pass.DistanceKm,
            last.Elapsed,
            where,
            pace,
            passes, missed, ignored);
    }

    private static int? FindNext(IReadOnlyList<ExpectedPass> course, int from, int checkpointId)
    {
        for (var i = from; i < course.Count; i++)
            if (course[i].Checkpoint.Id == checkpointId) return i;
        return null;
    }

    /// <summary>
    /// Finishers by time (ties share a place). Runners still on course rank behind every
    /// finisher, furthest first. Places are only given to finishers.
    /// </summary>
    private static List<RunnerResult> Rank(List<RunnerResult> results)
    {
        var finishers = results.Where(r => r.Status == RunnerStatus.Finished).OrderBy(r => r.FinishTime).ThenBy(r => r.Runner.Bib).ToList();
        var placed = new List<RunnerResult>();
        var divisionCounts = new Dictionary<string, (int Count, TimeSpan? LastTime, int LastPlace)>();
        for (var i = 0; i < finishers.Count; i++)
        {
            var r = finishers[i];
            var place = i > 0 && finishers[i - 1].FinishTime == r.FinishTime ? placed[i - 1].Place : i + 1;

            var d = divisionCounts.GetValueOrDefault(r.Runner.Division);
            var count = d.Count + 1;
            var divisionPlace = d.LastTime == r.FinishTime ? d.LastPlace : count;
            divisionCounts[r.Runner.Division] = (count, r.FinishTime, divisionPlace);

            placed.Add(r with { Place = place, DivisionPlace = divisionPlace });
        }

        var onCourse = results.Where(r => r.Status == RunnerStatus.OnCourse)
            .OrderByDescending(r => r.DistanceCoveredKm).ThenBy(r => r.LastSeenElapsed).ThenBy(r => r.Runner.Bib);
        var notStarted = results.Where(r => r.Status == RunnerStatus.NotStarted).OrderBy(r => r.Runner.Bib);
        return [.. placed, .. onCourse, .. notStarted];
    }
}
