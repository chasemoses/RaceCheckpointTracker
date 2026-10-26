namespace RaceTracker.Core;

/// <summary>
/// Linear: point to point, each checkpoint passed once (a 10K through town).
/// Loop: the same checkpoints passed every lap, and the start line is also the lap line.
/// </summary>
public enum RouteType { Linear, Loop }

public class Race
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public RouteType Route { get; set; }

    /// <summary>Gun time, UTC. Every runner's elapsed time is measured from here.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>1 for a linear course.</summary>
    public int Laps { get; set; } = 1;

    /// <summary>For a loop, the length of one lap; for a linear course, the full distance.</summary>
    public double LapDistanceKm { get; set; }

    /// <summary>Two taps of the same bib at the same checkpoint closer than this are one crossing.</summary>
    public int DuplicateWindowSeconds { get; set; } = 30;

    public List<Checkpoint> Checkpoints { get; set; } = [];
    public List<Runner> Runners { get; set; } = [];
    public List<Split> Splits { get; set; } = [];

    public double TotalDistanceKm => LapDistanceKm * Laps;
}

public class Checkpoint
{
    public int Id { get; set; }
    public int RaceId { get; set; }
    public required string Name { get; set; }

    /// <summary>Order along the course (within one lap, for a loop). The last one is the finish or lap line.</summary>
    public int Sequence { get; set; }

    /// <summary>Distance from the start (within one lap, for a loop).</summary>
    public double DistanceKm { get; set; }
}

public class Runner
{
    public int Id { get; set; }
    public int RaceId { get; set; }
    public int Bib { get; set; }
    public required string Name { get; set; }
    public required string Division { get; set; }
}

/// <summary>
/// One raw timing entry exactly as a volunteer recorded it. Splits are never "fixed" in place:
/// interpretation (which lap, duplicate or not) happens when results are calculated, so a
/// correction to the rules re-interprets every entry consistently.
/// </summary>
public class Split
{
    public long Id { get; set; }
    public int RaceId { get; set; }
    public int Bib { get; set; }
    public int CheckpointId { get; set; }
    public DateTime Time { get; set; }
    public required string RecordedBy { get; set; }
}
