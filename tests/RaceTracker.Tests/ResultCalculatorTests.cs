using RaceTracker.Core;

namespace RaceTracker.Tests;

public class ResultCalculatorTests
{
    private static readonly DateTime Gun = new(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc);
    private long _splitId;

    // 10K: 2.5K, 5K, 7.5K, Finish
    private static Race TenK() => new()
    {
        Id = 1, Name = "10K", Route = RouteType.Linear, StartTime = Gun, Laps = 1, LapDistanceKm = 10,
        Checkpoints =
        [
            new() { Id = 1, Name = "2.5K", Sequence = 1, DistanceKm = 2.5 },
            new() { Id = 2, Name = "5K", Sequence = 2, DistanceKm = 5 },
            new() { Id = 3, Name = "7.5K", Sequence = 3, DistanceKm = 7.5 },
            new() { Id = 4, Name = "Finish", Sequence = 4, DistanceKm = 10 },
        ],
    };

    // 3 laps of 1.5 km: Pavilion at 0.75, Line at 1.5
    private static Race Loop() => new()
    {
        Id = 2, Name = "Loop", Route = RouteType.Loop, StartTime = Gun, Laps = 3, LapDistanceKm = 1.5,
        Checkpoints =
        [
            new() { Id = 10, Name = "Pavilion", Sequence = 1, DistanceKm = 0.75 },
            new() { Id = 11, Name = "Line", Sequence = 2, DistanceKm = 1.5 },
        ],
    };

    private static Runner R(int bib, string division = "Open") => new() { Bib = bib, Name = $"Runner {bib}", Division = division };
    private Split S(int bib, int checkpoint, double minutes) =>
        new() { Id = ++_splitId, Bib = bib, CheckpointId = checkpoint, Time = Gun.AddMinutes(minutes), RecordedBy = "test" };

    [Fact]
    public void Linear_finisher_gets_time_pace_and_place()
    {
        var results = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 1, 12), S(7, 2, 24), S(7, 3, 36), S(7, 4, 48)]);

        var r = Assert.Single(results);
        Assert.Equal(RunnerStatus.Finished, r.Status);
        Assert.Equal(TimeSpan.FromMinutes(48), r.FinishTime);
        Assert.Equal(TimeSpan.FromSeconds(288), r.PacePerKm); // 4:48 per km
        Assert.Equal(1, r.Place);
        Assert.False(r.NeedsReview);
    }

    [Fact]
    public void Entries_recorded_out_of_order_are_sorted_by_time()
    {
        // The 7.5K volunteer synced late, so their entry arrives last.
        var results = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 4, 48), S(7, 1, 12), S(7, 2, 24), S(7, 3, 36)]);
        Assert.Equal(RunnerStatus.Finished, results[0].Status);
        Assert.Empty(results[0].Missed);
    }

    [Fact]
    public void Finisher_with_missing_checkpoint_is_flagged_for_review_not_disqualified()
    {
        var r = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 1, 12), S(7, 3, 36), S(7, 4, 48)]).Single();

        Assert.Equal(RunnerStatus.Finished, r.Status);
        Assert.True(r.NeedsReview);
        Assert.Equal("5K", Assert.Single(r.Missed).Checkpoint.Name);
        Assert.Equal(1, r.Place);
    }

    [Fact]
    public void Double_tap_within_window_is_one_crossing()
    {
        var r = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 1, 12), S(7, 1, 12.2), S(7, 2, 24)]).Single();

        Assert.Equal(SplitIssue.Duplicate, Assert.Single(r.Ignored).Issue);
        Assert.Equal("5K", r.LastSeenAt);
    }

    [Fact]
    public void Entry_before_the_gun_is_ignored()
    {
        var r = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 4, -5), S(7, 1, 12)]).Single();

        Assert.Equal(SplitIssue.BeforeStart, Assert.Single(r.Ignored).Issue);
        Assert.Equal(RunnerStatus.OnCourse, r.Status);
    }

    [Fact]
    public void Linear_checkpoint_seen_twice_is_an_extra_crossing()
    {
        var r = ResultCalculator.Calculate(TenK(), [R(7)], [S(7, 1, 12), S(7, 2, 24), S(7, 1, 40)]).Single();
        Assert.Equal(SplitIssue.ExtraCrossing, Assert.Single(r.Ignored).Issue);
    }

    [Fact]
    public void Loop_crossings_are_assigned_to_laps_in_order()
    {
        var r = ResultCalculator.Calculate(Loop(), [R(3)],
            [S(3, 10, 3), S(3, 11, 6), S(3, 10, 9), S(3, 11, 12), S(3, 10, 15), S(3, 11, 18)]).Single();

        Assert.Equal(RunnerStatus.Finished, r.Status);
        Assert.Equal(TimeSpan.FromMinutes(18), r.FinishTime);
        Assert.Equal([1, 1, 2, 2, 3, 3], r.Passes.Select(p => p.Pass.Lap));
        Assert.Equal(4.5, r.DistanceCoveredKm);
    }

    [Fact]
    public void Loop_runner_mid_race_shows_current_lap()
    {
        var r = ResultCalculator.Calculate(Loop(), [R(3)], [S(3, 10, 3), S(3, 11, 6), S(3, 10, 9)]).Single();

        Assert.Equal(RunnerStatus.OnCourse, r.Status);
        Assert.Equal("Pavilion, lap 2", r.LastSeenAt);
        Assert.Equal(2.25, r.DistanceCoveredKm);
    }

    [Fact]
    public void Loop_missed_lap_line_is_detected_from_the_crossings_around_it()
    {
        // Line volunteer missed lap 1. The second Pavilion crossing can only be lap 2.
        var r = ResultCalculator.Calculate(Loop(), [R(3)],
            [S(3, 10, 3), S(3, 10, 9), S(3, 11, 12), S(3, 10, 15), S(3, 11, 18)]).Single();

        Assert.Equal(RunnerStatus.Finished, r.Status);
        var missed = Assert.Single(r.Missed);
        Assert.Equal(("Line", 1), (missed.Checkpoint.Name, missed.Lap));
    }

    [Fact]
    public void Loop_lap_line_double_tap_does_not_count_as_a_lap()
    {
        var r = ResultCalculator.Calculate(Loop(), [R(3)], [S(3, 10, 3), S(3, 11, 6), S(3, 11, 6.1)]).Single();

        Assert.Equal("Line, lap 1", r.LastSeenAt);
        Assert.Equal(SplitIssue.Duplicate, r.Ignored.Single().Issue);
    }

    [Fact]
    public void Ranking_finishers_first_then_furthest_on_course_then_not_started()
    {
        var results = ResultCalculator.Calculate(TenK(),
            [R(1), R(2), R(3), R(4), R(5)],
            [
                S(1, 4, 50),                            // finished 50:00
                S(2, 4, 45),                            // finished 45:00
                S(3, 1, 10), S(3, 2, 21),               // on course at 5K
                S(4, 1, 11), S(4, 2, 20), S(4, 3, 30),  // on course at 7.5K (furthest)
                // 5: not started
            ]);

        Assert.Equal([2, 1, 4, 3, 5], results.Select(r => r.Runner.Bib));
        Assert.Equal([1, 2, null, null, null], results.Select(r => r.Place));
    }

    [Fact]
    public void Equal_times_share_a_place_and_division_places_are_separate()
    {
        var results = ResultCalculator.Calculate(TenK(),
            [R(1, "F 20-29"), R(2, "M 20-29"), R(3, "F 20-29"), R(4, "F 20-29")],
            [S(1, 4, 40), S(2, 4, 40), S(3, 4, 41), S(4, 4, 41)]);

        var byBib = results.ToDictionary(r => r.Runner.Bib);
        Assert.Equal((1, 1), (byBib[1].Place!.Value, byBib[1].DivisionPlace!.Value));
        Assert.Equal((1, 1), (byBib[2].Place!.Value, byBib[2].DivisionPlace!.Value));
        Assert.Equal((3, 2), (byBib[3].Place!.Value, byBib[3].DivisionPlace!.Value));
        Assert.Equal((3, 2), (byBib[4].Place!.Value, byBib[4].DivisionPlace!.Value));
    }

    [Fact]
    public void Splits_for_unregistered_bibs_do_not_create_runners()
    {
        var results = ResultCalculator.Calculate(TenK(), [R(1)], [S(999, 1, 10)]);
        Assert.Equal(RunnerStatus.NotStarted, Assert.Single(results).Status);
    }
}
