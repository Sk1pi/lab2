using LiteDB;

namespace SportLive.Benchmark;

public sealed class LiteDbStore : IMatchStore, IDisposable
{
    private readonly string _path;
    private LiteDatabase? _db;
    private ILiteCollection<Match>? _matches;

    public LiteDbStore(string path) => _path = Path.GetFullPath(path);

    public string Name => "LiteDB";

    public string FilePath => _path;

    private LiteDatabase Db => _db ?? throw new InvalidOperationException("База не відкрита");
    private ILiteCollection<Match> Matches => _matches ?? throw new InvalidOperationException("База не відкрита");

    public void Reset(DataGenerator generator)
    {
        Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var logFile = Path.Combine(Path.GetDirectoryName(_path)!,
            Path.GetFileNameWithoutExtension(_path) + "-log" + Path.GetExtension(_path));
        foreach (var file in new[] { _path, logFile })
            if (File.Exists(file)) File.Delete(file);

        _db = new LiteDatabase(new ConnectionString { Filename = _path, Connection = ConnectionType.Direct }) { UtcDate = true };
        _matches = _db.GetCollection<Match>("matches");

        var teams = generator.AllTeams.Select(t => new BsonDocument
        {
            ["_id"] = t.TeamId,
            ["Name"] = t.Name,
            ["Sport"] = t.Sport,
            ["Players"] = new BsonArray(t.Players.Select(p => (BsonValue)new BsonDocument
            {
                ["PlayerId"] = p.PlayerId,
                ["FullName"] = p.FullName,
                ["Number"] = p.Number,
                ["Position"] = p.Position
            }))
        });
        _db.GetCollection("teams").InsertBulk(teams);
    }

    public void Load(IReadOnlyList<Match> matches) => Matches.InsertBulk(matches);

    public void CreateIndexes()
    {
        Matches.EnsureIndex("idx_home_team", x => x.Home.TeamId);
        Matches.EnsureIndex("idx_away_team", x => x.Away.TeamId);
        Matches.EnsureIndex("idx_tournament", x => x.TournamentId);
        Matches.EnsureIndex("idx_timeline_type", "$.Timeline[*].Type");
    }

    public double StorageMb()
    {
        Db.Checkpoint();
        return new FileInfo(_path).Length / 1024.0 / 1024.0;
    }

    public Match? GetMatchCard(int matchId) => Matches.FindById(matchId);

    public int GetTeamMatches(int teamId, DateTime from, DateTime to)
    {
        var home = Matches.Query()
            .Where(x => x.Home.TeamId == teamId && x.StartTime >= from && x.StartTime < to)
            .OrderByDescending(x => x.StartTime)
            .Select(x => new MatchListItem
            {
                Id = x.Id,
                StartTime = x.StartTime,
                Status = x.Status,
                HomeName = x.Home.Name,
                HomeScore = x.Home.Score,
                AwayName = x.Away.Name,
                AwayScore = x.Away.Score
            })
            .Limit(20)
            .ToList();

        var away = Matches.Query()
            .Where(x => x.Away.TeamId == teamId && x.StartTime >= from && x.StartTime < to)
            .OrderByDescending(x => x.StartTime)
            .Select(x => new MatchListItem
            {
                Id = x.Id,
                StartTime = x.StartTime,
                Status = x.Status,
                HomeName = x.Home.Name,
                HomeScore = x.Home.Score,
                AwayName = x.Away.Name,
                AwayScore = x.Away.Score
            })
            .Limit(20)
            .ToList();

        return home.Concat(away)
            .OrderByDescending(x => x.StartTime)
            .Take(20)
            .Count();
    }

    public IReadOnlyList<(int PlayerId, int Goals)> GetTopScorers(int tournamentId)
    {
        var goals = new Dictionary<int, int>();

        var rows = Matches.Query()
            .Where(x => x.TournamentId == tournamentId)
            .Select(BsonExpression.Create("{ p: $.Timeline[@.Type = 'goal'].PlayerId }"))
            .ToEnumerable();

        foreach (var row in rows)
            foreach (var playerId in row["p"].AsArray)
            {
                var id = playerId.AsInt32;
                goals[id] = goals.TryGetValue(id, out var count) ? count + 1 : 1;
            }

        return goals
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Key)
            .Take(10)
            .Select(g => (g.Key, g.Value))
            .ToList();
    }

    public long CountFinishedInCity(string city) =>
        Matches.Count(x => x.Venue.City == city && x.Status == "Finished");

    public void AppendGoal(GoalOp op)
    {
        Db.BeginTrans();
        var match = Matches.FindById(op.MatchId);
        match.Timeline.Add(new TimelineEvent
        {
            Seq = op.Seq,
            Minute = op.Minute,
            Type = "goal",
            TeamId = op.TeamId,
            PlayerId = op.PlayerId,
            PlayerName = op.PlayerName,
            Details = new Dictionary<string, string>
            {
                ["bodyPart"] = "right",
                ["assistId"] = op.AssistId.ToString()
            }
        });
        if (op.IsHome) match.Home.Score++;
        else match.Away.Score++;
        Matches.Update(match);
        Db.Commit();
    }

    public void UpdatePossession(int matchId, decimal home)
    {
        Db.BeginTrans();
        var match = Matches.FindById(matchId);
        match.Stats["possession"] = new StatPair { Home = home, Away = 100 - home };
        Matches.Update(match);
        Db.Commit();
    }

    public void InsertBatch(IReadOnlyList<Match> matches)
    {
        Db.BeginTrans();
        Matches.InsertBulk(matches);
        Db.Commit();
    }

    public void Dispose()
    {
        _db?.Dispose();
        _db = null;
        _matches = null;
    }

    public sealed class MatchListItem
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public string Status { get; set; } = "";
        public string HomeName { get; set; } = "";
        public int HomeScore { get; set; }
        public string AwayName { get; set; } = "";
        public int AwayScore { get; set; }
    }
}
