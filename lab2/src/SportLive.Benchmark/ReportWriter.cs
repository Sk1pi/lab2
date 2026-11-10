using System.Globalization;
using System.Net;
using System.Text;

namespace SportLive.Benchmark;

public sealed class ReportWriter
{
    private const string NoSql = "LiteDB";
    private const string Sql = "SQL Server";

    private readonly string _dir;
    private readonly List<TimingResult> _timings = new();
    private readonly List<MetricResult> _metrics = new();
    private readonly Dictionary<int, string> _validation = new();
    private readonly string _environment;

    public ReportWriter(string dir, string environment)
    {
        _dir = dir;
        _environment = environment;
        Directory.CreateDirectory(dir);
    }

    public void Add(TimingResult r) => _timings.Add(r);
    public void Add(MetricResult r) => _metrics.Add(r);
    public void AddValidation(int scale, string message) => _validation[scale] = message;

    public void Save()
    {
        SaveCsv();
        File.WriteAllText(Path.Combine(_dir, "results.md"), BuildMarkdown(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(_dir, "report.html"), BuildHtml(), Encoding.UTF8);
    }

    private static string F(double v) => v.ToString(v >= 100 ? "F0" : v >= 1 ? "F2" : "F3", CultureInfo.InvariantCulture);

    private void SaveCsv()
    {
        var sb = new StringBuilder("Scale,Test,Store,Iterations,MeanMs,MedianMs,P95Ms,MinMs,MaxMs\n");
        foreach (var r in _timings)
            sb.AppendLine(string.Join(",", r.Scale, Q(r.Test), Q(r.Store), r.Iterations,
                Inv(r.MeanMs), Inv(r.MedianMs), Inv(r.P95Ms), Inv(r.MinMs), Inv(r.MaxMs)));
        File.WriteAllText(Path.Combine(_dir, "timings.csv"), sb.ToString(), new UTF8Encoding(true));

        var mb = new StringBuilder("Scale,Metric,Store,Value,Unit\n");
        foreach (var m in _metrics)
            mb.AppendLine(string.Join(",", m.Scale, Q(m.Metric), Q(m.Store), Inv(m.Value), Q(m.Unit)));
        File.WriteAllText(Path.Combine(_dir, "metrics.csv"), mb.ToString(), new UTF8Encoding(true));

        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        static string Inv(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Winner(double mongo, double sql, bool higherIsBetter)
    {
        if (mongo <= 0 || sql <= 0) return "—";
        var mongoBetter = higherIsBetter ? mongo > sql : mongo < sql;
        var ratio = mongoBetter
            ? (higherIsBetter ? mongo / sql : sql / mongo)
            : (higherIsBetter ? sql / mongo : mongo / sql);
        if (ratio < 1.1) return "≈ однаково";
        return $"{(mongoBetter ? NoSql : Sql)} ×{ratio.ToString("F1", CultureInfo.InvariantCulture)}";
    }

    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Результати тестування швидкодії");
        sb.AppendLine();
        sb.AppendLine(_environment);
        sb.AppendLine();

        foreach (var scale in _timings.Select(t => t.Scale).Concat(_metrics.Select(m => m.Scale)).Distinct().OrderBy(s => s))
        {
            sb.AppendLine($"## {scale:N0} матчів");
            sb.AppendLine();
            if (_validation.TryGetValue(scale, out var v)) sb.AppendLine($"Перевірка даних: {v}").AppendLine();

            sb.AppendLine("| Операція | LiteDB медіана, мс | SQL Server медіана, мс | LiteDB p95, мс | SQL Server p95, мс | Швидше |");
            sb.AppendLine("|---|---:|---:|---:|---:|---|");
            foreach (var test in _timings.Where(t => t.Scale == scale).Select(t => t.Test).Distinct())
            {
                var m = _timings.First(t => t.Scale == scale && t.Test == test && t.Store == NoSql);
                var s = _timings.First(t => t.Scale == scale && t.Test == test && t.Store == Sql);
                sb.AppendLine($"| {test} | {F(m.MedianMs)} | {F(s.MedianMs)} | {F(m.P95Ms)} | {F(s.P95Ms)} | {Winner(m.MedianMs, s.MedianMs, false)} |");
            }
            sb.AppendLine();

            sb.AppendLine("| Показник | LiteDB | SQL Server | Краще |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var name in _metrics.Where(x => x.Scale == scale).Select(x => x.Metric).Distinct())
            {
                var m = _metrics.First(x => x.Scale == scale && x.Metric == name && x.Store == NoSql);
                var s = _metrics.First(x => x.Scale == scale && x.Metric == name && x.Store == Sql);
                sb.AppendLine($"| {name}, {m.Unit} | {F(m.Value)} | {F(s.Value)} | {Winner(m.Value, s.Value, m.HigherIsBetter)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string BuildHtml()
    {
        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html lang="uk">
<head>
<meta charset="utf-8">
<title>LiteDB vs SQL Server</title>
<style>
body { font-family: Segoe UI, Arial, sans-serif; margin: 32px; color: #1d1d1f; max-width: 1100px; }
h1 { font-size: 26px; } h2 { font-size: 20px; margin-top: 40px; border-bottom: 1px solid #ddd; padding-bottom: 6px; }
h3 { font-size: 15px; margin: 22px 0 8px; }
.row { display: grid; grid-template-columns: 110px 1fr; align-items: center; gap: 8px; margin: 3px 0; font-size: 13px; }
.bars div { height: 16px; border-radius: 3px; margin: 2px 0; color: #fff; font-size: 11px; padding-left: 6px; line-height: 16px; white-space: nowrap; }
.mongo { background: #2e7d32; } .sql { background: #c62828; }
.legend span { display: inline-block; padding: 2px 10px; color: #fff; border-radius: 3px; margin-right: 8px; font-size: 13px; }
.note { color: #555; font-size: 13px; }
</style>
</head>
<body>
<h1>Порівняння швидкодії: LiteDB vs SQL Server</h1>
""");
        sb.Append($"<p class=\"note\">{WebUtility.HtmlEncode(_environment)}</p>");
        sb.Append("<p class=\"legend\"><span class=\"mongo\">LiteDB</span><span class=\"sql\">SQL Server</span></p>");

        sb.Append("<h2>Час операцій (медіана, мс — менше краще)</h2>");
        foreach (var test in _timings.Select(t => t.Test).Distinct())
        {
            sb.Append($"<h3>{WebUtility.HtmlEncode(test)}</h3>");
            foreach (var scale in _timings.Where(t => t.Test == test).Select(t => t.Scale).Distinct().OrderBy(s => s))
            {
                var m = _timings.First(t => t.Scale == scale && t.Test == test && t.Store == NoSql).MedianMs;
                var s = _timings.First(t => t.Scale == scale && t.Test == test && t.Store == Sql).MedianMs;
                AppendBars(sb, $"{scale:N0} матчів", m, s, "мс");
            }
        }

        sb.Append("<h2>Інші показники</h2>");
        foreach (var name in _metrics.Select(x => x.Metric).Distinct())
        {
            var unit = _metrics.First(x => x.Metric == name).Unit;
            var higher = _metrics.First(x => x.Metric == name).HigherIsBetter;
            sb.Append($"<h3>{WebUtility.HtmlEncode(name)}, {unit} — {(higher ? "більше краще" : "менше краще")}</h3>");
            foreach (var scale in _metrics.Where(x => x.Metric == name).Select(x => x.Scale).Distinct().OrderBy(s => s))
            {
                var m = _metrics.First(x => x.Scale == scale && x.Metric == name && x.Store == NoSql).Value;
                var s = _metrics.First(x => x.Scale == scale && x.Metric == name && x.Store == Sql).Value;
                AppendBars(sb, $"{scale:N0} матчів", m, s, unit);
            }
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendBars(StringBuilder sb, string label, double mongo, double sql, string unit)
    {
        var max = Math.Max(Math.Max(mongo, sql), 1e-9);
        var wm = Math.Max(mongo / max * 100, 1).ToString("F1", CultureInfo.InvariantCulture);
        var ws = Math.Max(sql / max * 100, 1).ToString("F1", CultureInfo.InvariantCulture);
        sb.Append($"<div class=\"row\"><div>{label}</div><div class=\"bars\">");
        sb.Append($"<div class=\"mongo\" style=\"width:{wm}%\">{F(mongo)} {unit}</div>");
        sb.Append($"<div class=\"sql\" style=\"width:{ws}%\">{F(sql)} {unit}</div>");
        sb.Append("</div></div>");
    }
}
