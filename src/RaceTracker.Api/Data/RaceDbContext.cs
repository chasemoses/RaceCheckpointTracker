using Microsoft.EntityFrameworkCore;
using RaceTracker.Core;

namespace RaceTracker.Api.Data;

public class RaceDbContext(DbContextOptions<RaceDbContext> options) : DbContext(options)
{
    public DbSet<Race> Races => Set<Race>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();
    public DbSet<Runner> Runners => Set<Runner>();
    public DbSet<Split> Splits => Set<Split>();

    /// <summary>
    /// All times are stored as UTC, but SQL Server's datetime2 doesn't remember that, so values
    /// come back as DateTimeKind.Unspecified and serialize without a "Z". Browsers then read
    /// them as local time, hours off. Mark them UTC on the way out of the database.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder c) =>
        c.Properties<DateTime>().HaveConversion<UtcConverter>();

    private sealed class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Race>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Route).HasConversion<string>().HasMaxLength(10);
            e.Ignore(x => x.TotalDistanceKm);
            e.HasMany(x => x.Checkpoints).WithOne().HasForeignKey(c => c.RaceId);
            e.HasMany(x => x.Runners).WithOne().HasForeignKey(r => r.RaceId);
            e.HasMany(x => x.Splits).WithOne().HasForeignKey(s => s.RaceId);
        });
        b.Entity<Checkpoint>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50);
            e.HasIndex(x => new { x.RaceId, x.Sequence }).IsUnique();
        });
        b.Entity<Runner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Division).HasMaxLength(30);
            // A bib identifies a runner within a race; it's what volunteers type.
            e.HasIndex(x => new { x.RaceId, x.Bib }).IsUnique();
        });
        b.Entity<Split>(e =>
        {
            e.Property(x => x.RecordedBy).HasMaxLength(50);
            e.HasIndex(x => new { x.RaceId, x.Bib });
            e.HasIndex(x => new { x.RaceId, x.CheckpointId, x.Id });
        });
    }
}
