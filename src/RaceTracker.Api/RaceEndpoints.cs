using Microsoft.EntityFrameworkCore;
using RaceTracker.Api.Data;
using RaceTracker.Core;

namespace RaceTracker.Api;

public static class RaceEndpoints
{
    public static void MapRaceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/races");

        api.MapGet("/", async (RaceDbContext db) =>
            await db.Races.AsNoTracking().OrderBy(r => r.StartTime)
                .Select(r => new RaceDto(r.Id, r.Name, r.Route.ToString(), r.StartTime, r.Laps, r.LapDistanceKm * r.Laps, r.Runners.Count,
                    r.Checkpoints.OrderBy(c => c.Sequence).Select(c => new CheckpointDto(c.Id, c.Name, c.Sequence, c.DistanceKm)).ToList()))
                .ToListAsync());

        api.MapGet("/{raceId:int}/results", async (int raceId, string? division, RaceDbContext db, TimeProvider clock) =>
        {
            var race = await LoadAsync(db, raceId);
            if (race is null) return Results.NotFound();
            var results = ResultCalculator.Calculate(race, race.Runners, race.Splits);
            var rows = results.Where(r => division is null || r.Runner.Division == division).Select(r => r.ToDto()).ToList();
            return Results.Ok(new ResultsDto(raceId, clock.GetUtcNow().UtcDateTime,
                results.Count(r => r.Status == RunnerStatus.Finished),
                results.Count(r => r.Status == RunnerStatus.OnCourse),
                results.Count(r => r.Status == RunnerStatus.NotStarted),
                rows));
        });

        api.MapGet("/{raceId:int}/runners/{bib:int}", async (int raceId, int bib, RaceDbContext db) =>
        {
            var race = await LoadAsync(db, raceId);
            var runner = race?.Runners.SingleOrDefault(r => r.Bib == bib);
            if (race is null || runner is null) return Results.NotFound();

            // Calculate the whole race: place depends on everyone else's times.
            var r = ResultCalculator.Calculate(race, race.Runners, race.Splits).Single(x => x.Runner.Bib == bib);
            var names = race.Checkpoints.ToDictionary(c => c.Id, c => c.Name);
            return Results.Ok(new RunnerDetailDto(
                r.ToDto(),
                r.Passes.Select(p => new PassDto(p.Pass.Lap, p.Pass.Checkpoint.Name, p.Pass.DistanceKm, p.Time,
                    p.Elapsed is null ? null : (int)Math.Round(p.Elapsed.Value.TotalSeconds))).ToList(),
                r.Missed.Select(m => Mapping.Describe(m, race)).ToList(),
                r.Ignored.Select(i => new IgnoredSplitDto(i.Split.Id, names[i.Split.CheckpointId], i.Split.Time, Mapping.Describe(i.Issue))).ToList()));
        });

        api.MapGet("/{raceId:int}/checkpoints/{checkpointId:int}/splits", async (int raceId, int checkpointId, int? take, RaceDbContext db) =>
        {
            var checkpoint = await db.Checkpoints.AsNoTracking().SingleOrDefaultAsync(c => c.Id == checkpointId && c.RaceId == raceId);
            if (checkpoint is null) return Results.NotFound();
            var splits = await db.Splits.AsNoTracking()
                .Where(s => s.RaceId == raceId && s.CheckpointId == checkpointId)
                .OrderByDescending(s => s.Id).Take(Math.Clamp(take ?? 15, 1, 100)).ToListAsync();
            var bibs = splits.Select(s => s.Bib).Distinct().ToList();
            var names = await db.Runners.Where(r => r.RaceId == raceId && bibs.Contains(r.Bib)).ToDictionaryAsync(r => r.Bib, r => r.Name);
            return Results.Ok(splits.Select(s => new SplitDto(s.Id, s.Bib, names.GetValueOrDefault(s.Bib), checkpoint.Name, s.Time, s.RecordedBy, null)));
        });

        // Volunteers type a bib and press Enter. The response says right away if the entry
        // looks wrong, while the volunteer can still see the runner.
        api.MapPost("/{raceId:int}/splits", async (int raceId, RecordSplitRequest request, RaceDbContext db, TimeProvider clock) =>
        {
            var race = await db.Races.AsNoTracking().Include(r => r.Checkpoints).SingleOrDefaultAsync(r => r.Id == raceId);
            if (race is null) return Results.NotFound();

            var now = clock.GetUtcNow().UtcDateTime;
            var time = request.Time?.ToUniversalTime() ?? now;
            var errors = new Dictionary<string, string[]>();
            var runner = await db.Runners.AsNoTracking().SingleOrDefaultAsync(r => r.RaceId == raceId && r.Bib == request.Bib);
            if (runner is null) errors["bib"] = [$"Bib {request.Bib} isn't registered for this race. Check the number and try again."];
            var checkpoint = race.Checkpoints.SingleOrDefault(c => c.Id == request.CheckpointId);
            if (checkpoint is null) errors["checkpointId"] = ["That checkpoint isn't part of this race."];
            if (time > now.AddMinutes(1)) errors["time"] = ["The time can't be in the future."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var split = new Split { RaceId = raceId, Bib = request.Bib, CheckpointId = checkpoint!.Id, Time = time, RecordedBy = Clean(request.RecordedBy) };
            db.Splits.Add(split);
            await db.SaveChangesAsync();

            // Recalculate this runner to see how the entry was interpreted.
            var runnerSplits = await db.Splits.AsNoTracking().Where(s => s.RaceId == raceId && s.Bib == request.Bib).ToListAsync();
            var result = ResultCalculator.Calculate(race, [runner!], runnerSplits).Single();
            var issue = result.Ignored.FirstOrDefault(i => i.Split.Id == split.Id)?.Issue;
            var warning = issue is null ? null : Mapping.Describe(issue.Value);

            return Results.Created($"/api/races/{raceId}/runners/{split.Bib}",
                new SplitDto(split.Id, split.Bib, runner!.Name, checkpoint.Name, split.Time, split.RecordedBy, warning));
        });

        // Undo for a mistyped bib. The entry is deleted; results recalculate on the next read.
        api.MapDelete("/{raceId:int}/splits/{splitId:long}", async (int raceId, long splitId, RaceDbContext db) =>
        {
            var deleted = await db.Splits.Where(s => s.RaceId == raceId && s.Id == splitId).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static Task<Race?> LoadAsync(RaceDbContext db, int raceId) =>
        db.Races.AsNoTracking().AsSplitQuery()
            .Include(r => r.Checkpoints).Include(r => r.Runners).Include(r => r.Splits)
            .SingleOrDefaultAsync(r => r.Id == raceId);

    private static string Clean(string? recordedBy) =>
        string.IsNullOrWhiteSpace(recordedBy) ? "volunteer" : recordedBy.Trim()[..Math.Min(50, recordedBy.Trim().Length)];
}
