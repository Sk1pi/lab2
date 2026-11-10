namespace SportLive.Benchmark;

public sealed record TimingResult(
    int Scale, string Test, string Store, int Iterations,
    double MeanMs, double MedianMs, double P95Ms, double MinMs, double MaxMs)
{
    public static TimingResult From(int scale, string test, string store, double[] times)
    {
        var sorted = times.OrderBy(t => t).ToArray();
        return new TimingResult(
            scale, test, store, sorted.Length,
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            sorted[0],
            sorted[^1]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var rank = p * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}

public sealed record MetricResult(int Scale, string Metric, string Store, double Value, string Unit, bool HigherIsBetter);
