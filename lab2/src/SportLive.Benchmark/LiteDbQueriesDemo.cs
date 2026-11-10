using LiteDB;

namespace SportLive.Benchmark;

public static class LiteDbQueriesDemo
{
    public static void Run(string dbPath, TextWriter output)
    {
        using var db = new LiteDatabase(new ConnectionString { Filename = Path.GetFullPath(dbPath), Connection = ConnectionType.Direct }) { UtcDate = true };
        var matches = db.GetCollection<Match>("matches");
        const int id = 999_999_999;

        void Header(string title) => output.WriteLine($"\n=== {title} ===");
        void Line(string text) => output.WriteLine("  " + text);
        void Sql(string sql)
        {
            Line("> " + sql);
            using var reader = db.Execute(sql);
            while (reader.Read())
                Line("  " + JsonSerializer.Serialize(reader.Current));
        }

        Header("1. Вставка документа (Insert)");
        matches.Delete(id);
        matches.Insert(new Match
        {
            Id = id,
            Sport = "Football",
            TournamentId = 1,
            TournamentName = "Футбольна ліга 1",
            Venue = new Venue { Name = "Арена Київ 1", City = "Київ" },
            StartTime = new DateTime(2026, 11, 20, 19, 0, 0, DateTimeKind.Utc),
            Status = "Live",
            Home = new TeamSide
            {
                TeamId = 1, Name = "Сокіл Київ",
                Lineup = { new LineupEntry { PlayerId = 10, Name = "Максим Коваленко", Number = 9, Position = "FW", IsStarter = true } }
            },
            Away = new TeamSide
            {
                TeamId = 2, Name = "Вітер Львів",
                Lineup = { new LineupEntry { PlayerId = 20, Name = "Олег Бондар", Number = 7, Position = "FW", IsStarter = true } }
            },
            Stats = { ["possession"] = new StatPair { Home = 50, Away = 50 } }
        });
        Sql($"SELECT $ FROM matches WHERE _id = {id}");

        Header("2. Гол у live-режимі: зміна документа і Update");
        var match = matches.FindById(id);
        match.Timeline.Add(new TimelineEvent
        {
            Seq = 1, Minute = 12, Type = "goal", TeamId = 1, PlayerId = 10, PlayerName = "Максим Коваленко",
            Details = { ["bodyPart"] = "head", ["assistId"] = "11" }
        });
        match.Home.Score++;
        matches.Update(match);
        Sql($"SELECT {{ score: Home.Score + ':' + Away.Score, events: COUNT(Timeline) }} FROM matches WHERE _id = {id}");

        Header("3. Подія нового типу без зміни схеми (VAR)");
        match.Timeline.Add(new TimelineEvent
        {
            Seq = 2, Minute = 34, Type = "var", TeamId = 2, PlayerId = 20, PlayerName = "Олег Бондар",
            Details = { ["decision"] = "no goal", ["reason"] = "offside", ["durationSec"] = "95" }
        });
        matches.Update(match);
        Sql($"SELECT $.Timeline[@.Type = 'var'] FROM matches WHERE _id = {id}");

        Header("4. Оновлення без читання (UpdateMany з виразом)");
        var changed = matches.UpdateMany("{ Status: 'Finished' }", $"_id = {id}");
        Line($"Змінено документів: {changed}");
        Sql($"SELECT {{ _id, Status }} FROM matches WHERE _id = {id}");

        Header("5. Пошук за елементом масиву: матчі з голом у перші 5 хвилин");
        Sql("SELECT { _id, Home: Home.Name, Away: Away.Name } FROM matches WHERE COUNT($.Timeline[@.Type = 'goal' AND @.Minute <= 5]) > 0 LIMIT 3");

        Header("6. LINQ-запит: останні live-матчі з проекцією");
        foreach (var m in matches.Query()
                     .Where(x => x.Status == "Live")
                     .OrderByDescending(x => x.StartTime)
                     .Select(x => new { x.Id, x.StartTime, Home = x.Home.Name, x.Home.Score, Away = x.Away.Name, AwayScore = x.Away.Score })
                     .Limit(5)
                     .ToList())
            Line($"#{m.Id} {m.StartTime:yyyy-MM-dd HH:mm} {m.Home} {m.Score}:{m.AwayScore} {m.Away}");

        Header("7. Групування: кількість матчів і середня кількість голів по турнірах");
        Sql("SELECT { tournament: @key, matches: COUNT(*), avgGoals: AVG(*.Home.Score) + AVG(*.Away.Score) } FROM matches WHERE Sport = 'Football' AND Status = 'Finished' GROUP BY TournamentName LIMIT 5");

        Header("8. Розподіл типів подій у турнірі 1");
        var types = matches.Query()
            .Where(x => x.TournamentId == 1)
            .Select(BsonExpression.Create("{ t: $.Timeline[*].Type }"))
            .ToEnumerable()
            .SelectMany(d => d["t"].AsArray.Select(v => v.AsString))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count());
        foreach (var g in types) Line($"{g.Key}: {g.Count()}");

        Header("9. План виконання: пошук матчів команди використовує індекс");
        Sql("EXPLAIN SELECT $ FROM matches WHERE Home.TeamId = 7");

        Header("10. Індекси колекції");
        Sql("SELECT { name, expression } FROM $indexes WHERE collection = 'matches'");

        Header("11. Видалення документа");
        Line($"Delete({id}) -> {matches.Delete(id)}");
        Line($"Документів у колекції: {matches.Count()}");
    }

    private static class JsonSerializer
    {
        public static string Serialize(BsonValue value) => LiteDB.JsonSerializer.Serialize(value);
    }
}
