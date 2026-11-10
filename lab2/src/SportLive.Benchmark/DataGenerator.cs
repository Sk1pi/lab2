namespace SportLive.Benchmark;

public sealed class DataGenerator
{
    public const int FootballTeams = 140;
    public const int BasketballTeams = 60;
    public const int PlayersPerTeam = 25;

    public static readonly string[] Cities =
        { "Київ", "Львів", "Одеса", "Харків", "Дніпро", "Запоріжжя", "Вінниця", "Полтава", "Чернігів", "Ужгород" };

    private static readonly string[] TeamWords =
        { "Сокіл", "Вітер", "Буревій", "Леви", "Орли", "Вовки", "Зубри", "Барси", "Ведмеді", "Блискавка",
          "Хвиля", "Граніт", "Метеор", "Комета", "Титан", "Фенікс", "Шторм", "Яструби", "Бізони", "Тигри" };

    private static readonly string[] FirstNames =
        { "Максим", "Денис", "Олег", "Артем", "Богдан", "Сергій", "Роман", "Віталій", "Андрій", "Тарас",
          "Ігор", "Микола", "Юрій", "Павло", "Євген", "Дмитро", "Олександр", "Назар", "Остап", "Ярослав" };

    private static readonly string[] LastNames =
        { "Коваленко", "Шевчук", "Бондар", "Ткаченко", "Мельник", "Кравець", "Олійник", "Лисенко", "Мороз", "Савчук",
          "Гнатюк", "Руденко", "Петренко", "Марченко", "Зінченко", "Клименко", "Павленко", "Литвин", "Поліщук", "Гончар" };

    private static readonly string[] FootballPositions = { "GK", "DF", "DF", "DF", "DF", "MF", "MF", "MF", "FW", "FW", "FW" };
    private static readonly string[] BasketballPositions = { "PG", "SG", "SF", "PF", "C" };

    private static readonly DateTime BaseDate = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<TeamInfo> _football = new();
    private readonly List<TeamInfo> _basketball = new();

    public DataGenerator()
    {
        var rnd = new Random(2024);
        var playerId = 1;

        for (var teamId = 1; teamId <= FootballTeams + BasketballTeams; teamId++)
        {
            var sport = teamId <= FootballTeams ? "Football" : "Basketball";
            var positions = sport == "Football" ? FootballPositions : BasketballPositions;
            var name = $"{TeamWords[teamId % TeamWords.Length]} {Cities[teamId % Cities.Length]} {teamId}";

            var players = new List<PlayerInfo>();
            for (var i = 0; i < PlayersPerTeam; i++)
            {
                var fullName = $"{FirstNames[rnd.Next(FirstNames.Length)]} {LastNames[rnd.Next(LastNames.Length)]}";
                players.Add(new PlayerInfo(playerId++, teamId, fullName, i + 1, positions[i % positions.Length]));
            }

            var team = new TeamInfo(teamId, name, sport, players);
            (sport == "Football" ? _football : _basketball).Add(team);
        }
    }

    public IEnumerable<TeamInfo> AllTeams => _football.Concat(_basketball);

    public static bool IsFootball(int matchId) => matchId % 10 < 7;

    public Match CreateMatch(int id)
    {
        var rnd = new Random(id);
        var football = IsFootball(id);
        var pool = football ? _football : _basketball;

        var home = pool[rnd.Next(pool.Count)];
        TeamInfo away;
        do { away = pool[rnd.Next(pool.Count)]; } while (away.TeamId == home.TeamId);

        var tournamentId = football ? 1 + rnd.Next(20) : 21 + rnd.Next(10);
        var city = Cities[rnd.Next(Cities.Length)];

        var statusRoll = rnd.Next(100);
        var status = statusRoll < 90 ? "Finished" : statusRoll < 95 ? "Live" : "Scheduled";

        var match = new Match
        {
            Id = id,
            Sport = football ? "Football" : "Basketball",
            TournamentId = tournamentId,
            TournamentName = football ? $"Футбольна ліга {tournamentId}" : $"Баскетбольна ліга {tournamentId}",
            Venue = new Venue { Name = $"Арена {city} {1 + rnd.Next(5)}", City = city },
            StartTime = BaseDate.AddHours(rnd.Next(0, 6 * 365 * 24)),
            Status = status,
            Home = new TeamSide { TeamId = home.TeamId, Name = home.Name },
            Away = new TeamSide { TeamId = away.TeamId, Name = away.Name }
        };

        var starters = football ? 11 : 5;
        var squad = football ? 16 : 12;
        match.Home.Lineup = BuildLineup(home, rnd, starters, squad);
        match.Away.Lineup = BuildLineup(away, rnd, starters, squad);

        if (status != "Scheduled")
        {
            if (football) FillFootball(match, rnd);
            else FillBasketball(match, rnd);
        }

        return match;
    }

    private static List<LineupEntry> BuildLineup(TeamInfo team, Random rnd, int starters, int squad) =>
        team.Players
            .OrderBy(_ => rnd.Next())
            .Take(squad)
            .Select((p, i) => new LineupEntry
            {
                PlayerId = p.PlayerId,
                Name = p.FullName,
                Number = p.Number,
                Position = p.Position,
                IsStarter = i < starters
            })
            .ToList();

    private static void FillFootball(Match m, Random rnd)
    {
        var count = rnd.Next(18, 32);
        var minutes = Enumerable.Range(0, count).Select(_ => 1 + rnd.Next(90)).OrderBy(x => x).ToList();

        for (var i = 0; i < count; i++)
        {
            var isHome = rnd.Next(2) == 0;
            var side = isHome ? m.Home : m.Away;
            var player = side.Lineup[rnd.Next(side.Lineup.Count)];
            var roll = rnd.Next(100);

            var ev = new TimelineEvent
            {
                Seq = i + 1,
                Minute = minutes[i],
                TeamId = side.TeamId,
                PlayerId = player.PlayerId,
                PlayerName = player.Name
            };

            if (roll < 10)
            {
                ev.Type = "goal";
                ev.Details["bodyPart"] = rnd.Next(3) switch { 0 => "head", 1 => "left", _ => "right" };
                ev.Details["assistId"] = side.Lineup[rnd.Next(side.Lineup.Count)].PlayerId.ToString();
                side.Score++;
            }
            else if (roll < 25)
            {
                ev.Type = "card";
                ev.Details["color"] = rnd.Next(10) == 0 ? "red" : "yellow";
            }
            else if (roll < 45)
            {
                ev.Type = "sub";
                ev.Details["playerOutId"] = side.Lineup[rnd.Next(side.Lineup.Count)].PlayerId.ToString();
            }
            else if (roll < 85)
            {
                ev.Type = "foul";
                ev.Details["zone"] = rnd.Next(3) switch { 0 => "defense", 1 => "middle", _ => "attack" };
            }
            else
            {
                ev.Type = "corner";
                ev.Details["side"] = rnd.Next(2) == 0 ? "left" : "right";
            }

            m.Timeline.Add(ev);
        }

        var possession = 35 + rnd.Next(31);
        m.Stats["possession"] = new StatPair { Home = possession, Away = 100 - possession };
        m.Stats["shots"] = Pair(rnd, 3, 25);
        m.Stats["shotsOnTarget"] = Pair(rnd, 0, 12);
        m.Stats["corners"] = Pair(rnd, 0, 12);
        m.Stats["fouls"] = Pair(rnd, 5, 20);
        m.Stats["offsides"] = Pair(rnd, 0, 6);
        m.Stats["passes"] = Pair(rnd, 250, 700);
        m.Stats["passAccuracy"] = Pair(rnd, 65, 92);
    }

    private static void FillBasketball(Match m, Random rnd)
    {
        var count = rnd.Next(25, 41);
        var quarters = Enumerable.Range(0, count).Select(_ => 1 + rnd.Next(4)).OrderBy(x => x).ToList();

        for (var i = 0; i < count; i++)
        {
            var isHome = rnd.Next(2) == 0;
            var side = isHome ? m.Home : m.Away;
            var player = side.Lineup[rnd.Next(side.Lineup.Count)];
            var roll = rnd.Next(100);

            var ev = new TimelineEvent
            {
                Seq = i + 1,
                Minute = (quarters[i] - 1) * 10 + 1 + rnd.Next(10),
                TeamId = side.TeamId,
                PlayerId = player.PlayerId,
                PlayerName = player.Name
            };
            ev.Details["quarter"] = quarters[i].ToString();

            if (roll < 60)
            {
                var points = rnd.Next(10) switch { < 2 => 1, < 8 => 2, _ => 3 };
                ev.Type = "point";
                ev.Details["points"] = points.ToString();
                side.Score += points;
            }
            else if (roll < 85)
            {
                ev.Type = "foul";
                ev.Details["kind"] = rnd.Next(4) == 0 ? "technical" : "personal";
            }
            else
            {
                ev.Type = "timeout";
            }

            m.Timeline.Add(ev);
        }

        m.Stats["rebounds"] = Pair(rnd, 25, 55);
        m.Stats["assists"] = Pair(rnd, 10, 35);
        m.Stats["steals"] = Pair(rnd, 2, 15);
        m.Stats["blocks"] = Pair(rnd, 0, 10);
        m.Stats["turnovers"] = Pair(rnd, 5, 20);
        m.Stats["fgPct"] = Pair(rnd, 35, 60);
        m.Stats["threePct"] = Pair(rnd, 20, 45);
    }

    private static StatPair Pair(Random rnd, int min, int max) =>
        new() { Home = rnd.Next(min, max + 1), Away = rnd.Next(min, max + 1) };
}
