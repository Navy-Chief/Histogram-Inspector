namespace HistogramInspector.Core;

public readonly record struct AduRange(int Minimum, int Maximum);
public sealed record HistogramBin(int First, int Last, long Count);

public static class HistogramViewPolicy {
    public static AduRange Auto(IEnumerable<HistogramDistribution> distributions, bool includeZero) {
        var populated = distributions.Where(d => d.SampleCount > 0).ToArray();
        if (populated.Length == 0) return new(0, 1);
        double lo = populated.Min(d => d.Percentile(0.1)!.Value);
        double hi = populated.Max(d => d.Percentile(99.9)!.Value);
        double padding = Math.Max(1, (hi - lo) * 0.05);
        int first = includeZero ? 0 : Math.Max(0, (int)Math.Floor(lo - padding));
        int last = Math.Min(65535, (int)Math.Ceiling(hi + padding));
        if (last <= first) { if (first == 65535) first--; else last = first + 1; }
        return new(first, last);
    }

    public static IReadOnlyList<HistogramBin> Aggregate(HistogramDistribution distribution, AduRange range, int maxBins) {
        if (range.Minimum < 0 || range.Maximum > 65535 || range.Minimum > range.Maximum || maxBins <= 0)
            throw new ArgumentOutOfRangeException(nameof(range));
        int step = Math.Max(1, (int)Math.Ceiling((range.Maximum - range.Minimum + 1) / (double)maxBins));
        var bins = new List<HistogramBin>();
        for (int first = range.Minimum; first <= range.Maximum; first += step) {
            int last = Math.Min(range.Maximum, first + step - 1);
            bins.Add(new(first, last, distribution.CountBetween(first, last)));
        }
        return bins;
    }
}
