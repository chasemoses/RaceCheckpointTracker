using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RaceTracker.Api.Data;
using RaceTracker.Core;

namespace RaceTracker.Api.Demo;

/// <summary>
/// Builds two races already in progress, with the kinds of mistakes real volunteers make:
/// double taps, a missed checkpoint, a course cut, no-shows. Crossings in the future are
/// handed to <see cref="DemoSimulator"/>, which records them as their time arrives.
/// </summary>
public sealed class DemoRace
{
    private static readonly string[] Women = ["Brooke", "Eliza", "Grace", "Ivy", "Kate", "Maren", "Olivia", "Rachel", "Tessa", "Violet", "Kenzie", "Sadie", "Lydia", "Hannah", "Abby"];
    private static readonly string[] Men = ["Caleb", "Dallin", "Finn", "Hyrum", "Jared", "Logan", "Nate", "Porter", "Spencer", "Uriah", "Wyatt", "Brigham", "Tanner", "McKay", "Ezra"];
    private static readonly string[] Last = ["Allen", "Bennett", "Christensen", "Davis", "Erickson", "Fowler", "Hansen", "Jensen", "Larsen", "Madsen", "Nielsen", "Olsen", "Peterson", "Rasmussen", "Smith", "Taylor", "Walker", "Young", "Barney", "Hale"];

    public ConcurrentQueue<Split> Upcoming { get; } = new();

    public async Task SeedAsync(RaceDbContext db, DateTime now)
    {
        var rng = new Random(2026);
        var tenK = new Race
        {
            Name = "Rexburg Summer 10K", Route = RouteType.Linear, StartTime = now.AddMinutes(-50), Laps = 1, LapDistanceKm = 10,
            Checkpoints =
            [
                new() { Name = "2.5K Main St", Sequence = 1, DistanceKm = 2.5 },
                new() { Name = "5K Porter Park", Sequence = 2, DistanceKm = 5 },
                new() { Name = "7.5K Center St", Sequence = 3, DistanceKm = 7.5 },
                new() { Name = "Finish", Sequence = 4, DistanceKm = 10 },
            ],
        };
        var loop = new Race
        {
            Name = "Porter Park Loop 6K", Route = RouteType.Loop, StartTime = now.AddMinutes(-18), Laps = 4, LapDistanceKm = 1.5,
            Checkpoints =
            [
                new() { Name = "Pavilion", Sequence = 1, DistanceKm = 0.75 },
                new() { Name = "Lap line", Sequence = 2, DistanceKm = 1.5 },
            ],
        };
        tenK.Runners = Runners(rng, 101, 90);
        loop.Runners = Runners(rng, 501, 45);
        db.Races.AddRange(tenK, loop);
        await db.SaveChangesAsync();

        var planned = Plan(tenK, rng).Concat(Plan(loop, rng)).OrderBy(s => s.Time).ToList();
        db.Splits.AddRange(planned.Where(s => s.Time <= now));
        await db.SaveChangesAsync();
        foreach (var s in planned.Where(s => s.Time > now)) Upcoming.Enqueue(s);
    }

    private static List<Runner> Runners(Random rng, int firstBib, int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var female = rng.Next(2) == 0;
            var age = rng.Next(12, 68);
            var band = age < 20 ? "19 & under" : age < 40 ? "20-39" : "40+";
            return new Runner
            {
                Bib = firstBib + i,
                Name = $"{(female ? Women : Men)[rng.Next(15)]} {Last[rng.Next(Last.Length)]}",
                Division = $"{(female ? "F" : "M")} {band}",
            };
        }).ToList();

    private static IEnumerable<Split> Plan(Race race, Random rng)
    {
        var course = ResultCalculator.Course(race);
        var cutter = race.Runners[2]; // one fast runner skips the midpoint checkpoint: a course cut to review
        foreach (var runner in race.Runners)
        {
            var roll = rng.NextDouble();
            if (roll < 0.04 && runner != cutter) continue; // registered, didn't start

            var secondsPerKm = runner == cutter ? 250 : 210 + rng.NextDouble() * 300; // 3:30 to 8:30 per km
            var elapsed = 0.0;
            var previousKm = 0.0;
            foreach (var pass in course)
            {
                elapsed += (pass.DistanceKm - previousKm) * secondsPerKm * (0.94 + rng.NextDouble() * 0.12);
                previousKm = pass.DistanceKm;

                // ~2%: the volunteer missed this runner.
                if (rng.NextDouble() < 0.02 || (runner == cutter && pass.Index == course.Count / 2 - 1)) continue;

                var time = race.StartTime.AddSeconds(elapsed);
                yield return new Split { RaceId = race.Id, Bib = runner.Bib, CheckpointId = pass.Checkpoint.Id, Time = time, RecordedBy = "volunteer" };
                if (rng.NextDouble() < 0.03) // double tap
                    yield return new Split { RaceId = race.Id, Bib = runner.Bib, CheckpointId = pass.Checkpoint.Id, Time = time.AddSeconds(2 + rng.Next(6)), RecordedBy = "volunteer" };
            }
        }
    }
}

/// <summary>Records planned crossings as their time arrives, so the leaderboard moves during the demo.</summary>
public sealed class DemoSimulator(DemoRace demo, IServiceScopeFactory scopes, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stopping))
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var due = new List<Split>();
            while (demo.Upcoming.TryPeek(out var next) && next.Time <= now && demo.Upcoming.TryDequeue(out var s)) due.Add(s);
            if (due.Count == 0) continue;

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RaceDbContext>();
            db.Splits.AddRange(due);
            await db.SaveChangesAsync(stopping);
        }
    }
}
