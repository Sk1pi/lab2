DROP TABLE IF EXISTS dbo.MatchEventDetails;
DROP TABLE IF EXISTS dbo.MatchEvents;
DROP TABLE IF EXISTS dbo.MatchLineups;
DROP TABLE IF EXISTS dbo.MatchStats;
DROP TABLE IF EXISTS dbo.Matches;
DROP TABLE IF EXISTS dbo.Players;
DROP TABLE IF EXISTS dbo.Teams;

CREATE TABLE dbo.Teams (
    TeamId INT           NOT NULL CONSTRAINT PK_Teams PRIMARY KEY,
    Name   NVARCHAR(100) NOT NULL,
    Sport  NVARCHAR(20)  NOT NULL
);

CREATE TABLE dbo.Players (
    PlayerId INT           NOT NULL CONSTRAINT PK_Players PRIMARY KEY,
    TeamId   INT           NOT NULL CONSTRAINT FK_Players_Teams REFERENCES dbo.Teams(TeamId),
    FullName NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.Matches (
    MatchId        INT           NOT NULL CONSTRAINT PK_Matches PRIMARY KEY,
    Sport          NVARCHAR(20)  NOT NULL,
    TournamentId   INT           NOT NULL,
    TournamentName NVARCHAR(100) NOT NULL,
    VenueName      NVARCHAR(100) NOT NULL,
    City           NVARCHAR(60)  NOT NULL,
    StartTime      DATETIME2(0)  NOT NULL,
    Status         NVARCHAR(20)  NOT NULL,
    HomeTeamId     INT           NOT NULL CONSTRAINT FK_Matches_Home REFERENCES dbo.Teams(TeamId),
    AwayTeamId     INT           NOT NULL CONSTRAINT FK_Matches_Away REFERENCES dbo.Teams(TeamId),
    HomeScore      SMALLINT      NOT NULL,
    AwayScore      SMALLINT      NOT NULL
);

CREATE TABLE dbo.MatchLineups (
    MatchId     INT          NOT NULL CONSTRAINT FK_Lineups_Matches REFERENCES dbo.Matches(MatchId),
    PlayerId    INT          NOT NULL CONSTRAINT FK_Lineups_Players REFERENCES dbo.Players(PlayerId),
    TeamId      INT          NOT NULL,
    ShirtNumber TINYINT      NOT NULL,
    Position    NVARCHAR(20) NOT NULL,
    IsStarter   BIT          NOT NULL,
    CONSTRAINT PK_MatchLineups PRIMARY KEY (MatchId, PlayerId)
);

CREATE TABLE dbo.MatchEvents (
    MatchId   INT          NOT NULL CONSTRAINT FK_Events_Matches REFERENCES dbo.Matches(MatchId),
    Seq       INT          NOT NULL,
    Minute    SMALLINT     NOT NULL,
    EventType NVARCHAR(20) NOT NULL,
    TeamId    INT          NOT NULL,
    PlayerId  INT          NOT NULL CONSTRAINT FK_Events_Players REFERENCES dbo.Players(PlayerId),
    CONSTRAINT PK_MatchEvents PRIMARY KEY (MatchId, Seq)
);

CREATE TABLE dbo.MatchEventDetails (
    MatchId     INT           NOT NULL,
    Seq         INT           NOT NULL,
    DetailKey   NVARCHAR(30)  NOT NULL,
    DetailValue NVARCHAR(100) NOT NULL,
    CONSTRAINT PK_MatchEventDetails PRIMARY KEY (MatchId, Seq, DetailKey),
    CONSTRAINT FK_Details_Events FOREIGN KEY (MatchId, Seq) REFERENCES dbo.MatchEvents(MatchId, Seq)
);

CREATE TABLE dbo.MatchStats (
    MatchId   INT          NOT NULL CONSTRAINT FK_Stats_Matches REFERENCES dbo.Matches(MatchId),
    StatName  NVARCHAR(30) NOT NULL,
    HomeValue DECIMAL(9,2) NOT NULL,
    AwayValue DECIMAL(9,2) NOT NULL,
    CONSTRAINT PK_MatchStats PRIMARY KEY (MatchId, StatName)
);
