using System.Diagnostics;

namespace SportLive.Benchmark;

public sealed class BenchmarkRunner
{
    private const int ChunkSize = 2000;
    private const int BulkBatchSize = 1000;
    private const int ParallelThreads = 8;
    private static readonly TimeSpan ParallelDuration = TimeSpan.FromSeconds(5);

    private readonly int _scale;
    private readonly DataGenerator _generator;
    private readonly IMatchStore[] _stores;
    private readonly ReportWriter _report;
    private readonly Random _rnd;

    public BenchmarkRunner(int scale, DataGenerator generator, IMatchStore[] stores, ReportWriter report)
    {
        _scale = scale;
        _generator = generator;
        _stores = stores;
        _report = report;
        _rnd = new Random(7 + scale);
    }

    public void Run()
    {
        Load();
        Validate();
        RunReads();
        RunWrites();
        RunBulkInsert();
        RunParallelReads();
    }

    private void Load()
    {
        Console.WriteLine("Підготовка баз...");
        foreach (var store in _stores) store.Reset(_generator);

        var loadSeconds = new double[_stores.Length];
        for (var start = 1; start <= _scale; start += ChunkSize)
        {
            var count = Math.Min(ChunkSize, _scale - start + 1);
            var chunk = Enumerable.Range(start, count).Select(_generator.CreateMatch).ToList();

            for (var i = 0; i < _stores.Length; i++)
            {
                var sw = Stopwatch.StartNew();
                _stores[i].Load(chunk);
                loadSeconds[i] += sw.Elapsed.TotalSeconds;
            }

            Console.Write($"\r  Завантажено {start + count - 1:N0} / {_scale:N0}");
        }
        Console.WriteLine();

        for (var i = 0; i < _stores.Length; i++)
        {
            var store = _stores[i];
            _report.Add(new MetricResult(_scale, "Початкове завантаження", store.Name, loadSeconds[i], "с", false));
            _report.Add(new MetricResult(_scale, "Швидкість завантаження", store.Name, _scale / loadSeconds[i], "матчів/с", true));

            var sw = Stopwatch.StartNew();
            store.CreateIndexes();
            _report.Add(new MetricResult(_scale, "Створення індексів", store.Name, sw.Elapsed.TotalSeconds, "с", false));
            _report.Add(new MetricResult(_scale, "Розмір даних з індексами", store.Name, store.StorageMb(), "МБ", false));

            Console.WriteLine($"  {store.Name}: завантаження {loadSeconds[i]:F1} с, індекси {sw.Elapsed.TotalSeconds:F1} с");
        }
    }

    private void Validate()
    {
        Console.WriteLine("Перевірка, що обидві бази повертають однакові дані...");
        var a = _stores[0];
        var b = _stores[1];
        var problems = new List<string>();

        for (var k = 0; k < 20; k++)
        {
            var id = _rnd.Next(1, _scale + 1);
            var x = a.GetMatchCard(id);
            var y = b.GetMatchCard(id);
            if (x is null || y is null) { problems.Add($"матч {id}: не знайдено"); continue; }

            var same = x.Home.Score == y.Home.Score
                       && x.Away.Score == y.Away.Score
                       && x.Timeline.Count == y.Timeline.Count
                       && x.Home.Lineup.Count == y.Home.Lineup.Count
                       && x.Away.Lineup.Count == y.Away.Lineup.Count
                       && x.Stats.Count == y.Stats.Count
                       && x.Timeline.Sum(e => e.Details.Count) == y.Timeline.Sum(e => e.Details.Count);
            if (!same) problems.Add($"картка матчу {id} відрізняється");
        }

        foreach (var city in DataGenerator.Cities.Take(3))
            if (a.CountFinishedInCity(city) != b.CountFinishedInCity(city))
                problems.Add($"кількість матчів у місті {city} відрізняється");

        for (var t = 1; t <= 3; t++)
            if (!a.GetTopScorers(t).SequenceEqual(b.GetTopScorers(t)))
                problems.Add($"топ бомбардирів турніру {t} відрізняється");

        var from = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var team = 1; team <= 5; team++)
            if (a.GetTeamMatches(team, from, from.AddYears(1)) != b.GetTeamMatches(team, from, from.AddYears(1)))
                problems.Add($"матчі команди {team} відрізняються");

        var message = problems.Count == 0
            ? "OK: картки матчів, агрегації та фільтри збігаються в обох базах"
            : "Розбіжності: " + string.Join("; ", problems);
        _report.AddValidation(_scale, message);
        Console.WriteLine("  " + message);
    }

    private void RunReads()
    {
        Console.WriteLine("Читання:");

        var cardIds = RandomIds(1100);
        Measure("Картка матчу за id", 100, 1000, (s, i) => s.GetMatchCard(cardIds[i]));

        var baseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var teamQueries = Enumerable.Range(0, 550).Select(_ =>
        {
            var from = baseDate.AddDays(_rnd.Next(0, 5 * 365));
            return (Team: _rnd.Next(1, DataGenerator.FootballTeams + DataGenerator.BasketballTeams + 1), From: from, To: from.AddYears(1));
        }).ToArray();
        Measure("Матчі команди за рік (TOP 20)", 50, 500,
            (s, i) => s.GetTeamMatches(teamQueries[i].Team, teamQueries[i].From, teamQueries[i].To));

        Measure("Топ-10 бомбардирів турніру", 5, 30, (s, i) => s.GetTopScorers(1 + i % 20));

        Measure("Підрахунок за полем без індексу", 2, 10,
            (s, i) => s.CountFinishedInCity(DataGenerator.Cities[i % DataGenerator.Cities.Length]));
    }

    private void RunWrites()
    {
        Console.WriteLine("Запис:");

        var goals = Enumerable.Range(0, 550).Select(i =>
        {
            var match = _generator.CreateMatch(RandomFootballId());
            var isHome = _rnd.Next(2) == 0;
            var side = isHome ? match.Home : match.Away;
            var player = side.Lineup[_rnd.Next(side.Lineup.Count)];
            var assist = side.Lineup[_rnd.Next(side.Lineup.Count)];
            return new GoalOp(match.Id, 100_000 + i, 1 + _rnd.Next(90), isHome, side.TeamId,
                player.PlayerId, player.Name, assist.PlayerId);
        }).ToArray();
        Measure("Додавання голу (live)", 50, 500, (s, i) => s.AppendGoal(goals[i]));

        var possession = Enumerable.Range(0, 550)
            .Select(_ => (Id: RandomFootballId(), Value: (decimal)(35 + _rnd.Next(31))))
            .ToArray();
        Measure("Оновлення статистики матчу", 50, 500,
            (s, i) => s.UpdatePossession(possession[i].Id, possession[i].Value));
    }

    private void RunBulkInsert()
    {
        Console.WriteLine("Пакетна вставка:");
        var batches = Enumerable.Range(0, 6)
            .Select(k => Enumerable.Range(_scale + 1 + k * BulkBatchSize, BulkBatchSize)
                .Select(_generator.CreateMatch)
                .ToList())
            .ToArray();

        Measure($"Пакетна вставка {BulkBatchSize} матчів", 1, 5, (s, i) => s.InsertBatch(batches[i]));
    }

    private void RunParallelReads()
    {
        Console.WriteLine($"Паралельне читання ({ParallelThreads} потоків, {ParallelDuration.TotalSeconds:F0} с):");
        foreach (var store in _stores)
        {
            long operations = 0;
            var sw = Stopwatch.StartNew();
            var threads = Enumerable.Range(0, ParallelThreads).Select(w => new Thread(() =>
            {
                var rnd = new Random(1000 + w);
                while (sw.Elapsed < ParallelDuration)
                {
                    store.GetMatchCard(rnd.Next(1, _scale + 1));
                    Interlocked.Increment(ref operations);
                }
            })).ToList();

            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());

            var opsPerSecond = operations / sw.Elapsed.TotalSeconds;
            _report.Add(new MetricResult(_scale, $"Паралельне читання карток ({ParallelThreads} потоків)",
                store.Name, opsPerSecond, "оп/с", true));
            Console.WriteLine($"  {store.Name}: {opsPerSecond:F0} оп/с");
        }
    }

    private void Measure(string test, int warmup, int iterations, Action<IMatchStore, int> operation)
    {
        foreach (var store in _stores)
        {
            Console.Write($"  {test} [{store.Name}] ... ");

            for (var i = 0; i < warmup; i++) operation(store, i);

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var times = new double[iterations];
            var sw = new Stopwatch();
            for (var i = 0; i < iterations; i++)
            {
                sw.Restart();
                operation(store, warmup + i);
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            var result = TimingResult.From(_scale, test, store.Name, times);
            _report.Add(result);
            Console.WriteLine($"медіана {result.MedianMs:F3} мс, p95 {result.P95Ms:F3} мс");
        }
    }

    private int[] RandomIds(int count) =>
        Enumerable.Range(0, count).Select(_ => _rnd.Next(1, _scale + 1)).ToArray();

    private int RandomFootballId()
    {
        int id;
        do { id = _rnd.Next(1, _scale + 1); } while (!DataGenerator.IsFootball(id));
        return id;
    }
}
