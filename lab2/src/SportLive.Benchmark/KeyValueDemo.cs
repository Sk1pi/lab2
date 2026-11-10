using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using DBreeze;

namespace SportLive.Benchmark;

public static class KeyValueDemo
{
    public static void Run(string dataDir, TextWriter output)
    {
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
        using var engine = new DBreezeEngine(dataDir);

        void Header(string title) => output.WriteLine($"\n=== {title} ===");
        void Line(string text) => output.WriteLine("  " + text);

        Header("1. Запис і читання за ключем: кеш live-рахунку");
        using (var tran = engine.GetTransaction())
        {
            tran.Insert<string, string>("live", "match:42:score", "2:1");
            tran.Insert<string, string>("live", "match:42:status", "Live");
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            foreach (var key in new[] { "match:42:score", "match:42:status", "match:99:score" })
            {
                var row = tran.Select<string, string>("live", key);
                Line($"GET {key} -> {(row.Exists ? row.Value : "(немає ключа)")}");
            }
        }

        Header("2. Лічильник глядачів (читання-зміна-запис у транзакції)");
        foreach (var delta in new[] { 1, 150, -1 })
        {
            using var tran = engine.GetTransaction();
            tran.SynchronizeTables("counters");
            var row = tran.Select<string, int>("counters", "match:42:viewers");
            var value = (row.Exists ? row.Value : 0) + delta;
            tran.Insert("counters", "match:42:viewers", value);
            tran.Commit();
            Line($"INCRBY match:42:viewers {delta} -> {value}");
        }

        Header("3. Об'єкт як значення: JSON-картка матчу");
        var card = new { Home = "Сокіл Київ", Away = "Вітер Львів", HomeScore = 2, AwayScore = 1, Minute = 67 };
        using (var tran = engine.GetTransaction())
        {
            tran.Insert("cards", 42, JsonSerializer.Serialize(card));
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            var json = tran.Select<int, string>("cards", 42).Value;
            Line($"GET cards[42] -> {Unescape(json)}");
            var node = JsonNode.Parse(json)!;
            node["HomeScore"] = node["HomeScore"]!.GetValue<int>() + 1;
            node["Minute"] = 71;
            tran.Insert("cards", 42, node.ToJsonString());
            tran.Commit();
            Line($"Після оновлення -> {Unescape(tran.Select<int, string>("cards", 42).Value)}");
        }

        Header("4. Впорядковані ключі: стрічка подій і діапазонні запити");
        var feed = new (int Minute, string Text)[]
        {
            (12, "Гол: Коваленко"), (25, "Жовта картка: Шевчук"), (40, "Заміна"),
            (55, "Гол: Бондар"), (67, "Гол: Коваленко"), (70, "Кутовий")
        };
        using (var tran = engine.GetTransaction())
        {
            for (var i = 0; i < feed.Length; i++)
                tran.Insert("feed", $"42:{feed[i].Minute:D3}:{i + 1:D3}", feed[i].Text);
            tran.Insert("feed", "43:010:001", "Подія іншого матчу");
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            Line("Усі події матчу 42 (префікс \"42:\"):");
            foreach (var r in tran.SelectForwardStartsWith<string, string>("feed", "42:"))
                Line($"    {r.Key} -> {r.Value}");

            Line("Останні 3 події (зворотний обхід):");
            foreach (var r in tran.SelectBackwardStartsWith<string, string>("feed", "42:").Take(3))
                Line($"    {r.Key} -> {r.Value}");

            Line("Перший тайм, хвилини 0–45 (діапазон ключів):");
            foreach (var r in tran.SelectForwardFromTo<string, string>("feed", "42:000", true, "42:045:999", true))
                Line($"    {r.Key} -> {r.Value}");
        }

        Header("5. Таблиця бомбардирів через складений ключ");
        var goals = new Dictionary<string, int> { ["Максим Коваленко"] = 7, ["Олег Бондар"] = 5, ["Роман Олійник"] = 9, ["Денис Шевчук"] = 3 };
        using (var tran = engine.GetTransaction())
        {
            tran.SynchronizeTables("scorers", "scorers_by_player");
            foreach (var g in goals) SetScore(tran, g.Key, g.Value);
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            tran.SynchronizeTables("scorers", "scorers_by_player");
            var current = tran.Select<string, string>("scorers_by_player", "Олег Бондар").Value;
            var newGoals = 999 - int.Parse(current.Split('|')[1]) + 2;
            SetScore(tran, "Олег Бондар", newGoals);
            tran.Commit();
            Line($"Олег Бондар забив ще 2 -> {newGoals}");
        }
        using (var tran = engine.GetTransaction())
        {
            Line("Топ-3 (ключі вже відсортовані за спаданням голів):");
            var place = 1;
            foreach (var r in tran.SelectForwardStartsWith<string, string>("scorers", "t1|").Take(3))
                Line($"    {place++}. {r.Value} — {999 - int.Parse(r.Key.Split('|')[1])}");
        }

        Header("6. Множини через ключі-пари: підписки на команди");
        using (var tran = engine.GetTransaction())
        {
            foreach (var (team, user) in new[] { (1, 1), (1, 2), (1, 3), (2, 2), (2, 4) })
                tran.Insert("subs", $"team:{team}|user:{user}", 1);
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            var team1 = tran.SelectForwardStartsWith<string, byte>("subs", "team:1|").Select(r => r.Key.Split('|')[1]).ToList();
            var team2 = tran.SelectForwardStartsWith<string, byte>("subs", "team:2|").Select(r => r.Key.Split('|')[1]).ToList();
            Line($"Підписники команди 1: {string.Join(", ", team1)}");
            Line($"Підписані на обидві команди: {string.Join(", ", team1.Intersect(team2))}");
            Line($"user:4 підписаний на команду 1: {tran.Select<string, byte>("subs", "team:1|user:4").Exists}");
        }

        Header("7. Бронювання місця з часом життя (аналог SET NX EX)");
        const string seat = "event:3|A|5|12";
        Line($"user:1 бронює {seat} на 10 хв -> {TryHold(engine, seat, "user:1", TimeSpan.FromMinutes(10))}");
        Line($"user:2 бронює те саме місце -> {TryHold(engine, seat, "user:2", TimeSpan.FromMinutes(10))}");
        const string seat2 = "event:3|A|5|13";
        Line($"user:1 бронює {seat2} на 1 с -> {TryHold(engine, seat2, "user:1", TimeSpan.FromSeconds(1))}");
        Thread.Sleep(1200);
        Line($"Через 1,2 с user:2 бронює {seat2} -> {TryHold(engine, seat2, "user:2", TimeSpan.FromMinutes(10))}");

        Header("8. Транзакції: фіксація і відкат");
        using (var tran = engine.GetTransaction())
        {
            tran.SynchronizeTables("live", "feed");
            tran.Insert("live", "match:42:score", "3:1");
            tran.Insert("feed", "42:080:007", "Гол: Мельник");
            tran.Commit();
        }
        using (var tran = engine.GetTransaction())
        {
            tran.Insert("live", "match:42:score", "9:9");
        }
        using (var tran = engine.GetTransaction())
        {
            Line($"Після Commit і транзакції без Commit рахунок = {tran.Select<string, string>("live", "match:42:score").Value}");
            Line($"Подія 80' у стрічці: {tran.Select<string, string>("feed", "42:080:007").Value}");
        }

        Header("9. Кількість ключів, мінімальний і максимальний ключ");
        using (var tran = engine.GetTransaction())
        {
            Line($"COUNT feed = {tran.Count("feed")}");
            Line($"MIN feed = {tran.Min<string, string>("feed").Key}");
            Line($"MAX feed = {tran.Max<string, string>("feed").Key}");
        }

        Header("10. Видалення ключа");
        using (var tran = engine.GetTransaction())
        {
            tran.RemoveKey("feed", "43:010:001", out bool removed);
            tran.Commit();
            Line($"DEL 43:010:001 -> {removed}");
            Line($"EXISTS 43:010:001 -> {tran.Select<string, string>("feed", "43:010:001").Exists}");
        }

        Header("11. Масовий запис і випадкове читання");
        const int count = 100_000;
        var sw = Stopwatch.StartNew();
        using (var tran = engine.GetTransaction())
        {
            for (var i = 0; i < count; i++)
                tran.Insert("bulk", i, $"value-{i}");
            tran.Commit();
        }
        var writeMs = sw.Elapsed.TotalMilliseconds;
        Line($"Запис {count:N0} ключів однією транзакцією: {writeMs:F0} мс ({count / sw.Elapsed.TotalSeconds:F0} ключів/с)");

        var rnd = new Random(1);
        sw.Restart();
        var found = 0;
        using (var tran = engine.GetTransaction())
        {
            for (var i = 0; i < 10_000; i++)
                if (tran.Select<int, string>("bulk", rnd.Next(count)).Exists) found++;
        }
        Line($"10 000 випадкових читань: {sw.Elapsed.TotalMilliseconds:F0} мс ({10_000 / sw.Elapsed.TotalSeconds:F0} читань/с), знайдено {found}");
    }

    private static void SetScore(DBreeze.Transactions.Transaction tran, string player, int goals)
    {
        var old = tran.Select<string, string>("scorers_by_player", player);
        if (old.Exists) tran.RemoveKey("scorers", old.Value);

        var key = $"t1|{999 - goals:D3}|{player}";
        tran.Insert("scorers", key, player);
        tran.Insert("scorers_by_player", player, key);
    }

    private static bool TryHold(DBreezeEngine engine, string seat, string user, TimeSpan ttl)
    {
        using var tran = engine.GetTransaction();
        tran.SynchronizeTables("holds");
        var row = tran.Select<string, string>("holds", seat);
        if (row.Exists && long.Parse(row.Value.Split('|')[1]) > DateTime.UtcNow.Ticks)
            return false;

        tran.Insert("holds", seat, $"{user}|{DateTime.UtcNow.Add(ttl).Ticks}");
        tran.Commit();
        return true;
    }

    private static string Unescape(string json) => JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
