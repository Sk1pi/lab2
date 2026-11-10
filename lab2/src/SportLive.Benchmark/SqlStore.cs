using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SportLive.Benchmark;

public sealed class SqlStore : IMatchStore
{
    private const string DatabaseName = "SportLiveDb";
    private readonly string _masterConnection;
    private readonly string _dbConnection;

    public SqlStore(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        _masterConnection = builder.ConnectionString;
        builder.InitialCatalog = DatabaseName;
        _dbConnection = builder.ConnectionString;
    }

    public string Name => "SQL Server";

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_dbConnection);
        connection.Open();
        return connection;
    }

    private static string Script(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sql", file));

    public void Reset(DataGenerator generator)
    {
        using (var master = new SqlConnection(_masterConnection))
        {
            master.Open();
            master.Execute($"IF DB_ID('{DatabaseName}') IS NULL CREATE DATABASE {DatabaseName};");
            master.Execute($"ALTER DATABASE {DatabaseName} SET RECOVERY SIMPLE;");
        }

        using var c = Open();
        c.Execute(Script("01_live_schema.sql"), commandTimeout: 0);

        var teams = new DataTable();
        teams.Columns.Add("TeamId", typeof(int));
        teams.Columns.Add("Name", typeof(string));
        teams.Columns.Add("Sport", typeof(string));

        var players = new DataTable();
        players.Columns.Add("PlayerId", typeof(int));
        players.Columns.Add("TeamId", typeof(int));
        players.Columns.Add("FullName", typeof(string));

        foreach (var t in generator.AllTeams)
        {
            teams.Rows.Add(t.TeamId, t.Name, t.Sport);
            foreach (var p in t.Players)
                players.Rows.Add(p.PlayerId, p.TeamId, p.FullName);
        }

        Bulk(c, null, "dbo.Teams", teams);
        Bulk(c, null, "dbo.Players", players);
    }

    public void Load(IReadOnlyList<Match> matches)
    {
        var tables = new MatchTables();
        foreach (var m in matches) tables.Add(m);

        using var c = Open();
        tables.WriteTo(c, null);
    }

    public void InsertBatch(IReadOnlyList<Match> matches)
    {
        var tables = new MatchTables();
        foreach (var m in matches) tables.Add(m);

        using var c = Open();
        using var tx = c.BeginTransaction();
        tables.WriteTo(c, tx);
        tx.Commit();
    }

    public void CreateIndexes()
    {
        using var c = Open();
        c.Execute(Script("02_live_indexes.sql"), commandTimeout: 0);
    }

    public double StorageMb()
    {
        using var c = Open();
        return c.ExecuteScalar<double>(@"
            SELECT CAST(SUM(ps.reserved_page_count) * 8 / 1024.0 AS FLOAT)
            FROM sys.dm_db_partition_stats ps
            JOIN sys.tables t ON t.object_id = ps.object_id;");
    }

    private const string CardSql = @"
SELECT m.MatchId, m.Sport, m.TournamentId, m.TournamentName, m.VenueName, m.City, m.StartTime, m.Status,
       m.HomeTeamId, ht.Name AS HomeTeamName, m.HomeScore,
       m.AwayTeamId, awt.Name AS AwayTeamName, m.AwayScore
FROM dbo.Matches m
JOIN dbo.Teams ht  ON ht.TeamId = m.HomeTeamId
JOIN dbo.Teams awt ON awt.TeamId = m.AwayTeamId
WHERE m.MatchId = @id;

SELECT l.TeamId, l.PlayerId, p.FullName, l.ShirtNumber, l.Position, l.IsStarter
FROM dbo.MatchLineups l
JOIN dbo.Players p ON p.PlayerId = l.PlayerId
WHERE l.MatchId = @id;

SELECT e.Seq, e.Minute, e.EventType, e.TeamId, e.PlayerId, p.FullName
FROM dbo.MatchEvents e
JOIN dbo.Players p ON p.PlayerId = e.PlayerId
WHERE e.MatchId = @id
ORDER BY e.Seq;

SELECT Seq, DetailKey, DetailValue FROM dbo.MatchEventDetails WHERE MatchId = @id;

SELECT StatName, HomeValue, AwayValue FROM dbo.MatchStats WHERE MatchId = @id;";

    public Match? GetMatchCard(int matchId)
    {
        using var c = Open();
        using var grid = c.QueryMultiple(CardSql, new { id = matchId });

        var head = grid.ReadSingleOrDefault<MatchRow>();
        if (head is null) return null;

        var lineups = grid.Read<LineupRow>().AsList();
        var events = grid.Read<EventRow>().AsList();
        var details = grid.Read<DetailRow>().ToLookup(d => d.Seq);
        var stats = grid.Read<StatRow>().AsList();

        var match = new Match
        {
            Id = head.MatchId,
            Sport = head.Sport,
            TournamentId = head.TournamentId,
            TournamentName = head.TournamentName,
            Venue = new Venue { Name = head.VenueName, City = head.City },
            StartTime = head.StartTime,
            Status = head.Status,
            Home = new TeamSide { TeamId = head.HomeTeamId, Name = head.HomeTeamName, Score = head.HomeScore },
            Away = new TeamSide { TeamId = head.AwayTeamId, Name = head.AwayTeamName, Score = head.AwayScore }
        };

        foreach (var l in lineups)
        {
            var side = l.TeamId == head.HomeTeamId ? match.Home : match.Away;
            side.Lineup.Add(new LineupEntry
            {
                PlayerId = l.PlayerId,
                Name = l.FullName,
                Number = l.ShirtNumber,
                Position = l.Position,
                IsStarter = l.IsStarter
            });
        }

        foreach (var e in events)
        {
            match.Timeline.Add(new TimelineEvent
            {
                Seq = e.Seq,
                Minute = e.Minute,
                Type = e.EventType,
                TeamId = e.TeamId,
                PlayerId = e.PlayerId,
                PlayerName = e.FullName,
                Details = details[e.Seq].ToDictionary(d => d.DetailKey, d => d.DetailValue)
            });
        }

        foreach (var s in stats)
            match.Stats[s.StatName] = new StatPair { Home = s.HomeValue, Away = s.AwayValue };

        return match;
    }

    public int GetTeamMatches(int teamId, DateTime from, DateTime to)
    {
        using var c = Open();
        return c.Query(@"
SELECT TOP 20 m.MatchId, m.Sport, m.TournamentName, m.VenueName, m.City, m.StartTime, m.Status,
       m.HomeTeamId, ht.Name AS HomeTeamName, m.HomeScore,
       m.AwayTeamId, awt.Name AS AwayTeamName, m.AwayScore
FROM dbo.Matches m
JOIN dbo.Teams ht  ON ht.TeamId = m.HomeTeamId
JOIN dbo.Teams awt ON awt.TeamId = m.AwayTeamId
WHERE (m.HomeTeamId = @teamId OR m.AwayTeamId = @teamId)
  AND m.StartTime >= @from AND m.StartTime < @to
ORDER BY m.StartTime DESC;", new { teamId, from, to }).AsList().Count;
    }

    public IReadOnlyList<(int PlayerId, int Goals)> GetTopScorers(int tournamentId)
    {
        using var c = Open();
        return c.Query<ScorerRow>(@"
SELECT TOP 10 e.PlayerId, p.FullName, COUNT(*) AS Goals
FROM dbo.MatchEvents e
JOIN dbo.Matches m ON m.MatchId = e.MatchId
JOIN dbo.Players p ON p.PlayerId = e.PlayerId
WHERE m.TournamentId = @tournamentId AND e.EventType = N'goal'
GROUP BY e.PlayerId, p.FullName
ORDER BY Goals DESC, e.PlayerId;", new { tournamentId }, commandTimeout: 0)
            .Select(r => (r.PlayerId, r.Goals))
            .ToList();
    }

    public long CountFinishedInCity(string city)
    {
        using var c = Open();
        return c.ExecuteScalar<long>(
            "SELECT COUNT_BIG(*) FROM dbo.Matches WHERE City = @city AND Status = N'Finished';",
            new { city }, commandTimeout: 0);
    }

    public void AppendGoal(GoalOp op)
    {
        using var c = Open();
        c.Execute(@"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO dbo.MatchEvents (MatchId, Seq, Minute, EventType, TeamId, PlayerId)
VALUES (@MatchId, @Seq, @Minute, N'goal', @TeamId, @PlayerId);
INSERT INTO dbo.MatchEventDetails (MatchId, Seq, DetailKey, DetailValue)
VALUES (@MatchId, @Seq, N'bodyPart', N'right'), (@MatchId, @Seq, N'assistId', @AssistId);
UPDATE dbo.Matches
   SET HomeScore = HomeScore + CASE WHEN @IsHome = 1 THEN 1 ELSE 0 END,
       AwayScore = AwayScore + CASE WHEN @IsHome = 1 THEN 0 ELSE 1 END
 WHERE MatchId = @MatchId;
COMMIT TRANSACTION;",
            new
            {
                op.MatchId,
                op.Seq,
                Minute = (short)op.Minute,
                op.TeamId,
                op.PlayerId,
                AssistId = op.AssistId.ToString(),
                op.IsHome
            });
    }

    public void UpdatePossession(int matchId, decimal home)
    {
        using var c = Open();
        c.Execute(@"
UPDATE dbo.MatchStats
   SET HomeValue = @home, AwayValue = 100 - @home
 WHERE MatchId = @matchId AND StatName = N'possession';", new { matchId, home });
    }

    private static void Bulk(SqlConnection c, SqlTransaction? tx, string table, DataTable data)
    {
        using var bulk = new SqlBulkCopy(c, SqlBulkCopyOptions.TableLock, tx)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 0
        };
        foreach (DataColumn column in data.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        bulk.WriteToServer(data);
    }

    private sealed class MatchTables
    {
        private readonly DataTable _matches = new();
        private readonly DataTable _lineups = new();
        private readonly DataTable _events = new();
        private readonly DataTable _details = new();
        private readonly DataTable _stats = new();

        public MatchTables()
        {
            _matches.Columns.Add("MatchId", typeof(int));
            _matches.Columns.Add("Sport", typeof(string));
            _matches.Columns.Add("TournamentId", typeof(int));
            _matches.Columns.Add("TournamentName", typeof(string));
            _matches.Columns.Add("VenueName", typeof(string));
            _matches.Columns.Add("City", typeof(string));
            _matches.Columns.Add("StartTime", typeof(DateTime));
            _matches.Columns.Add("Status", typeof(string));
            _matches.Columns.Add("HomeTeamId", typeof(int));
            _matches.Columns.Add("AwayTeamId", typeof(int));
            _matches.Columns.Add("HomeScore", typeof(short));
            _matches.Columns.Add("AwayScore", typeof(short));

            _lineups.Columns.Add("MatchId", typeof(int));
            _lineups.Columns.Add("PlayerId", typeof(int));
            _lineups.Columns.Add("TeamId", typeof(int));
            _lineups.Columns.Add("ShirtNumber", typeof(byte));
            _lineups.Columns.Add("Position", typeof(string));
            _lineups.Columns.Add("IsStarter", typeof(bool));

            _events.Columns.Add("MatchId", typeof(int));
            _events.Columns.Add("Seq", typeof(int));
            _events.Columns.Add("Minute", typeof(short));
            _events.Columns.Add("EventType", typeof(string));
            _events.Columns.Add("TeamId", typeof(int));
            _events.Columns.Add("PlayerId", typeof(int));

            _details.Columns.Add("MatchId", typeof(int));
            _details.Columns.Add("Seq", typeof(int));
            _details.Columns.Add("DetailKey", typeof(string));
            _details.Columns.Add("DetailValue", typeof(string));

            _stats.Columns.Add("MatchId", typeof(int));
            _stats.Columns.Add("StatName", typeof(string));
            _stats.Columns.Add("HomeValue", typeof(decimal));
            _stats.Columns.Add("AwayValue", typeof(decimal));
        }

        public void Add(Match m)
        {
            _matches.Rows.Add(m.Id, m.Sport, m.TournamentId, m.TournamentName, m.Venue.Name, m.Venue.City,
                m.StartTime, m.Status, m.Home.TeamId, m.Away.TeamId, (short)m.Home.Score, (short)m.Away.Score);

            foreach (var side in new[] { m.Home, m.Away })
                foreach (var l in side.Lineup)
                    _lineups.Rows.Add(m.Id, l.PlayerId, side.TeamId, (byte)l.Number, l.Position, l.IsStarter);

            foreach (var e in m.Timeline)
            {
                _events.Rows.Add(m.Id, e.Seq, (short)e.Minute, e.Type, e.TeamId, e.PlayerId);
                foreach (var d in e.Details)
                    _details.Rows.Add(m.Id, e.Seq, d.Key, d.Value);
            }

            foreach (var s in m.Stats)
                _stats.Rows.Add(m.Id, s.Key, s.Value.Home, s.Value.Away);
        }

        public void WriteTo(SqlConnection c, SqlTransaction? tx)
        {
            Bulk(c, tx, "dbo.Matches", _matches);
            Bulk(c, tx, "dbo.MatchLineups", _lineups);
            Bulk(c, tx, "dbo.MatchEvents", _events);
            Bulk(c, tx, "dbo.MatchEventDetails", _details);
            Bulk(c, tx, "dbo.MatchStats", _stats);
        }
    }

    internal sealed class MatchRow
    {
        public int MatchId { get; set; }
        public string Sport { get; set; } = "";
        public int TournamentId { get; set; }
        public string TournamentName { get; set; } = "";
        public string VenueName { get; set; } = "";
        public string City { get; set; } = "";
        public DateTime StartTime { get; set; }
        public string Status { get; set; } = "";
        public int HomeTeamId { get; set; }
        public string HomeTeamName { get; set; } = "";
        public short HomeScore { get; set; }
        public int AwayTeamId { get; set; }
        public string AwayTeamName { get; set; } = "";
        public short AwayScore { get; set; }
    }

    internal sealed class LineupRow
    {
        public int TeamId { get; set; }
        public int PlayerId { get; set; }
        public string FullName { get; set; } = "";
        public byte ShirtNumber { get; set; }
        public string Position { get; set; } = "";
        public bool IsStarter { get; set; }
    }

    internal sealed class EventRow
    {
        public int Seq { get; set; }
        public short Minute { get; set; }
        public string EventType { get; set; } = "";
        public int TeamId { get; set; }
        public int PlayerId { get; set; }
        public string FullName { get; set; } = "";
    }

    internal sealed class DetailRow
    {
        public int Seq { get; set; }
        public string DetailKey { get; set; } = "";
        public string DetailValue { get; set; } = "";
    }

    internal sealed class ScorerRow
    {
        public int PlayerId { get; set; }
        public string FullName { get; set; } = "";
        public int Goals { get; set; }
    }

    internal sealed class StatRow
    {
        public string StatName { get; set; } = "";
        public decimal HomeValue { get; set; }
        public decimal AwayValue { get; set; }
    }
}
