CREATE NONCLUSTERED INDEX IX_Matches_Home_Start ON dbo.Matches (HomeTeamId, StartTime DESC);
CREATE NONCLUSTERED INDEX IX_Matches_Away_Start ON dbo.Matches (AwayTeamId, StartTime DESC);
CREATE NONCLUSTERED INDEX IX_Matches_Tournament ON dbo.Matches (TournamentId);
CREATE NONCLUSTERED INDEX IX_MatchEvents_Type_Player ON dbo.MatchEvents (EventType, PlayerId) INCLUDE (MatchId);
