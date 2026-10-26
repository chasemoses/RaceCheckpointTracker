using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using RaceTracker.Api;
using RaceTracker.Api.Data;
using RaceTracker.Core;

namespace RaceTracker.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string _db = $"DataSource=file:race-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly int _raceId;
    private readonly int _finishId;
    private readonly int _midId;

    public ApiTests()
    {
        _keepAlive = new SqliteConnection(_db); // an in-memory SQLite database lives while a connection is open
        _keepAlive.Open();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Races", _db);
            b.UseSetting("Database:Provider", "Sqlite");
            b.UseSetting("Demo:Enabled", "false");
        });
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDbContext>();
        var race = new Race
        {
            Name = "Test 5K", Route = RouteType.Linear, StartTime = DateTime.UtcNow.AddMinutes(-30), LapDistanceKm = 5,
            Checkpoints = [new() { Name = "Mid", Sequence = 1, DistanceKm = 2.5 }, new() { Name = "Finish", Sequence = 2, DistanceKm = 5 }],
            Runners = [new() { Bib = 7, Name = "Eliza Jensen", Division = "F 20-39" }, new() { Bib = 8, Name = "Caleb Olsen", Division = "M 20-39" }],
        };
        db.Races.Add(race);
        db.SaveChanges();
        (_raceId, _midId, _finishId) = (race.Id, race.Checkpoints[0].Id, race.Checkpoints[1].Id);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _keepAlive.Dispose();
    }

    private Task<HttpResponseMessage> PostSplit(int bib, int checkpointId, DateTime? time = null) =>
        _client.PostAsJsonAsync($"/api/races/{_raceId}/splits", new RecordSplitRequest(bib, checkpointId, time, "test"));

    [Fact]
    public async Task Recording_a_split_returns_the_runner_so_the_volunteer_can_confirm()
    {
        var response = await PostSplit(7, _midId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var split = await response.Content.ReadFromJsonAsync<SplitDto>();
        Assert.Equal(("Eliza Jensen", "Mid", (string?)null), (split!.RunnerName, split.Checkpoint, split.Warning));
    }

    [Fact]
    public async Task Unknown_bib_is_a_validation_problem_with_a_useful_message()
    {
        var response = await PostSplit(77, _midId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Bib 77 isn't registered", Assert.Single(problem!.Errors["bib"]));
    }

    [Fact]
    public async Task Future_time_is_rejected()
    {
        var response = await PostSplit(7, _midId, DateTime.UtcNow.AddMinutes(10));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Double_tap_is_accepted_but_warned()
    {
        var t = DateTime.UtcNow.AddMinutes(-5);
        await PostSplit(7, _midId, t);
        var second = await (await PostSplit(7, _midId, t.AddSeconds(3))).Content.ReadFromJsonAsync<SplitDto>();

        Assert.StartsWith("Duplicate entry", second!.Warning);
    }

    [Fact]
    public async Task Results_rank_finishers_and_count_statuses()
    {
        var start = DateTime.UtcNow.AddMinutes(-30);
        await PostSplit(7, _midId, start.AddMinutes(11));
        await PostSplit(7, _finishId, start.AddMinutes(22));

        var results = await _client.GetFromJsonAsync<ResultsDto>($"/api/races/{_raceId}/results");

        Assert.Equal((1, 0, 1), (results!.Finished, results.OnCourse, results.NotStarted));
        var winner = results.Rows[0];
        Assert.Equal((7, 1, "Finished"), (winner.Bib, winner.Place!.Value, winner.Status));
        Assert.InRange(winner.FinishSeconds!.Value, 22 * 60 - 1, 22 * 60 + 1);
    }

    [Fact]
    public async Task Division_filter_returns_only_that_division()
    {
        var results = await _client.GetFromJsonAsync<ResultsDto>($"/api/races/{_raceId}/results?division=M%2020-39");
        Assert.Equal(8, Assert.Single(results!.Rows).Bib);
    }

    [Fact]
    public async Task Undo_deletes_the_split_once()
    {
        var split = await (await PostSplit(7, _midId)).Content.ReadFromJsonAsync<SplitDto>();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/races/{_raceId}/splits/{split!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/races/{_raceId}/splits/{split.Id}")).StatusCode);
    }

    [Fact]
    public async Task Runner_detail_lists_missed_checkpoints()
    {
        await PostSplit(7, _finishId, DateTime.UtcNow.AddMinutes(-2));

        var detail = await _client.GetFromJsonAsync<RunnerDetailDto>($"/api/races/{_raceId}/runners/7");

        Assert.True(detail!.Result.NeedsReview);
        Assert.Equal(["Mid"], detail.Missed);
    }

    [Fact]
    public async Task Times_are_sent_as_utc_so_browsers_convert_them_correctly()
    {
        await PostSplit(7, _midId, DateTime.UtcNow.AddMinutes(-3));

        var json = await _client.GetStringAsync($"/api/races/{_raceId}/checkpoints/{_midId}/splits");
        var races = await _client.GetStringAsync("/api/races");

        Assert.Matches("\"time\":\"[^\"]+Z\"", json);
        Assert.Matches("\"startTime\":\"[^\"]+Z\"", races);
    }

    [Fact]
    public async Task Runner_detail_place_is_from_the_whole_race()
    {
        var start = DateTime.UtcNow.AddMinutes(-30);
        await PostSplit(8, _finishId, start.AddMinutes(20));  // 8 is faster
        await PostSplit(7, _midId, start.AddMinutes(11));
        await PostSplit(7, _finishId, start.AddMinutes(22));

        var detail = await _client.GetFromJsonAsync<RunnerDetailDto>($"/api/races/{_raceId}/runners/7");

        Assert.Equal(2, detail!.Result.Place);
    }

    [Fact]
    public async Task Unknown_race_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/races/999/results")).StatusCode);
    }
}
