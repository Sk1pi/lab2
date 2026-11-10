USE SportLiveDb;
GO

DECLARE @MatchId INT = 42;

SELECT m.MatchId, m.Sport, m.TournamentId, m.TournamentName, m.VenueName, m.City, m.StartTime, m.Status,
       m.HomeTeamId, ht.Name AS HomeTeamName, m.HomeScore,
       m.AwayTeamId, awt.Name AS AwayTeamName, m.AwayScore
FROM dbo.Matches m
JOIN dbo.Teams ht  ON ht.TeamId = m.HomeTeamId
JOIN dbo.Teams awt ON awt.TeamId = m.AwayTeamId
WHERE m.MatchId = @MatchId;

SELECT l.TeamId, l.PlayerId, p.FullName, l.ShirtNumber, l.Position, l.IsStarter
FROM dbo.MatchLineups l
JOIN dbo.Players p ON p.PlayerId = l.PlayerId
WHERE l.MatchId = @MatchId;

SELECT e.Seq, e.Minute, e.EventType, e.TeamId, e.PlayerId, p.FullName
FROM dbo.MatchEvents e
JOIN dbo.Players p ON p.PlayerId = e.PlayerId
WHERE e.MatchId = @MatchId
ORDER BY e.Seq;

SELECT Seq, DetailKey, DetailValue FROM dbo.MatchEventDetails WHERE MatchId = @MatchId;

SELECT StatName, HomeValue, AwayValue FROM dbo.MatchStats WHERE MatchId = @MatchId;
GO

DECLARE @TeamId INT = 7;

SELECT TOP 20 m.MatchId, m.StartTime, m.Status, ht.Name AS HomeTeamName, m.HomeScore, awt.Name AS AwayTeamName, m.AwayScore
FROM dbo.Matches m
JOIN dbo.Teams ht  ON ht.TeamId = m.HomeTeamId
JOIN dbo.Teams awt ON awt.TeamId = m.AwayTeamId
WHERE (m.HomeTeamId = @TeamId OR m.AwayTeamId = @TeamId)
  AND m.StartTime >= '2021-01-01' AND m.StartTime < '2024-01-01'
ORDER BY m.StartTime DESC;
GO

DECLARE @TournamentId INT = 3;

SELECT TOP 10 e.PlayerId, p.FullName, COUNT(*) AS Goals
FROM dbo.MatchEvents e
JOIN dbo.Matches m ON m.MatchId = e.MatchId
JOIN dbo.Players p ON p.PlayerId = e.PlayerId
WHERE m.TournamentId = @TournamentId AND e.EventType = N'goal'
GROUP BY e.PlayerId, p.FullName
ORDER BY Goals DESC, e.PlayerId;
GO

SELECT COUNT(*) AS FinishedInLviv
FROM dbo.Matches
WHERE City = N'Львів' AND Status = N'Finished';
GO

SELECT t.name AS TableName,
       SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.row_count ELSE 0 END) AS RowsCount,
       SUM(ps.reserved_page_count) * 8 / 1024.0 AS ReservedMb
FROM sys.dm_db_partition_stats ps
JOIN sys.tables t ON t.object_id = ps.object_id
GROUP BY t.name
ORDER BY ReservedMb DESC;
GO
