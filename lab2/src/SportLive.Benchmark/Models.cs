namespace SportLive.Benchmark;

public sealed class Match
{
    public int Id { get; set; }
    public string Sport { get; set; } = "";
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = "";
    public Venue Venue { get; set; } = new();
    public DateTime StartTime { get; set; }
    public string Status { get; set; } = "";
    public TeamSide Home { get; set; } = new();
    public TeamSide Away { get; set; } = new();
    public List<TimelineEvent> Timeline { get; set; } = new();
    public Dictionary<string, StatPair> Stats { get; set; } = new();
}

public sealed class Venue
{
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class TeamSide
{
    public int TeamId { get; set; }
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public List<LineupEntry> Lineup { get; set; } = new();
}

public sealed class LineupEntry
{
    public int PlayerId { get; set; }
    public string Name { get; set; } = "";
    public int Number { get; set; }
    public string Position { get; set; } = "";
    public bool IsStarter { get; set; }
}

public sealed class TimelineEvent
{
    public int Seq { get; set; }
    public int Minute { get; set; }
    public string Type { get; set; } = "";
    public int TeamId { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public Dictionary<string, string> Details { get; set; } = new();
}

public sealed class StatPair
{
    public decimal Home { get; set; }
    public decimal Away { get; set; }
}

public sealed record TeamInfo(int TeamId, string Name, string Sport, IReadOnlyList<PlayerInfo> Players);

public sealed record PlayerInfo(int PlayerId, int TeamId, string FullName, int Number, string Position);
