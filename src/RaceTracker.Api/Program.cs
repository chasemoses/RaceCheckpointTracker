using Microsoft.EntityFrameworkCore;
using RaceTracker.Api;
using RaceTracker.Api.Data;
using RaceTracker.Api.Demo;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Races");
builder.Services.AddDbContext<RaceDbContext>(o =>
{
    if (builder.Configuration["Database:Provider"] == "Sqlite") o.UseSqlite(connection);
    else o.UseSqlServer(connection);
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

var demo = builder.Configuration.GetValue<bool>("Demo:Enabled");
if (demo)
{
    builder.Services.AddSingleton<DemoRace>();
    builder.Services.AddHostedService<DemoSimulator>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The built Angular app is served from wwwroot; any non-API path falls through to it.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapRaceApi();
app.MapFallbackToFile("index.html");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RaceDbContext>();
    if (demo)
    {
        // Demo races are generated relative to "now", so they're rebuilt on every start.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await app.Services.GetRequiredService<DemoRace>().SeedAsync(db, DateTime.UtcNow);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

app.Run();

public partial class Program; // for WebApplicationFactory in tests
