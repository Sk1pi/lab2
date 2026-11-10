using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SportLive.Benchmark;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var scales = new List<int> { 1_000, 10_000, 100_000 };
var sqlConnection = "Server=localhost,1433;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";
var dataDir = "data";
var outputDir = "results";
var demoOnly = args.Contains("--demo-only");

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--scales":
            scales = args[i + 1].Split(',').Select(int.Parse).ToList();
            break;
        case "--sql":
            sqlConnection = args[i + 1];
            break;
        case "--data":
            dataDir = args[i + 1];
            break;
        case "--out":
            outputDir = args[i + 1];
            break;
    }
}

Directory.CreateDirectory(outputDir);
var liteStore = new LiteDbStore(Path.Combine(dataDir, "sportlive.db"));

if (!demoOnly)
{
    var environment =
        $"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}. ОС: {RuntimeInformation.OSDescription}. " +
        $"Логічних ядер: {Environment.ProcessorCount}. .NET {Environment.Version}. " +
        $"Обсяги: {string.Join(", ", scales.Select(s => s.ToString("N0")))} матчів.";
    Console.WriteLine(environment);

    var generator = new DataGenerator();
    IMatchStore[] stores = { liteStore, new SqlStore(sqlConnection) };
    var report = new ReportWriter(outputDir, environment);

    foreach (var scale in scales)
    {
        Console.WriteLine($"\n===== {scale:N0} матчів =====");
        new BenchmarkRunner(scale, generator, stores, report).Run();
        report.Save();
    }

    liteStore.Dispose();
}

Console.WriteLine("\n===== Базові запити LiteDB (документна) =====");
using (var writer = new TeeWriter(Path.Combine(outputDir, "litedb_queries_output.txt")))
    LiteDbQueriesDemo.Run(liteStore.FilePath, writer);

Console.WriteLine("\n===== Базові запити DBreeze (key-value) =====");
using (var writer = new TeeWriter(Path.Combine(outputDir, "keyvalue_output.txt")))
    KeyValueDemo.Run(Path.Combine(dataDir, "kv"), writer);

Console.WriteLine($"\nГотово. Результати: {Path.GetFullPath(outputDir)}");

internal sealed class TeeWriter : TextWriter
{
    private readonly StreamWriter _file;

    public TeeWriter(string path) => _file = new StreamWriter(path, false, new UTF8Encoding(false));

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        Console.Write(value);
        _file.Write(value);
    }

    public override void Write(string? value)
    {
        Console.Write(value);
        _file.Write(value);
    }

    public override void WriteLine(string? value)
    {
        Console.WriteLine(value);
        _file.WriteLine(value);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _file.Dispose();
        base.Dispose(disposing);
    }
}
