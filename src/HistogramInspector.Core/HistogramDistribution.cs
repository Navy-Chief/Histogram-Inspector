namespace HistogramInspector.Core;

public sealed record ThresholdCounts(long LowCount, double LowPercentage,
    long HighCount, double HighPercentage);

/// <summary>Immutable one-ADU bins and cumulative counts; percentages use this population.</summary>
public sealed class HistogramDistribution {
    private readonly long[] counts;
    private readonly long[] cumulative;
    internal HistogramDistribution(long[] ownedCounts, CancellationToken token) {
        counts = ownedCounts;
        cumulative = new long[65536];
        long total = 0;
        long weightedSum = 0;
        for (int i = 0; i < counts.Length; i++) {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            cumulative[i] = total += counts[i];
            weightedSum += (long)i * counts[i];
        }
        SampleCount = total;
        if (total == 0) return; // A tiny/odd mosaic can have an empty channel.
        double mean = weightedSum / (double)total;
        Mean = mean;
        double squaredDeviations = 0;
        int minimum = AtRank(0), maximum = AtRank(total - 1);
        Minimum = minimum; Maximum = maximum;
        double median = (AtRank((total - 1) / 2) + AtRank(total / 2)) / 2.0;
        Median = median;
        // Doubled distances preserve half-ADU medians exactly without floating-point bins.
        var deviations = new long[131071];
        int twiceMedian = (int)(median * 2);
        long bestScore = -1;
        for (int i = minimum; i <= maximum; i++) {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if (counts[i] == 0) continue;
            squaredDeviations += counts[i] * (i - mean) * (i - mean);
            deviations[Math.Abs(2 * i - twiceMedian)] += counts[i];
            long score = 0;
            for (int d = -2; d <= 2; d++)
                if (i + d >= 0 && i + d < counts.Length) score += (3 - Math.Abs(d)) * counts[i + d];
            if (score > bestScore) { bestScore = score; PeakAdu = i; }
        }
        long sum = 0, lowerRank = (total - 1) / 2, upperRank = total / 2;
        int lower = -1, upper = -1;
        for (int i = 0; i < deviations.Length; i++) {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            sum += deviations[i];
            if (lower < 0 && sum > lowerRank) lower = i;
            if (sum > upperRank) { upper = i; break; }
        }
        Mad = (lower + upper) / 4.0;
        StandardDeviation = Math.Sqrt(squaredDeviations / total);
    }
    public long SampleCount { get; }
    public int? Minimum { get; }
    public int? Maximum { get; }
    public double? Median { get; }
    public double? Mad { get; }
    public double? Mean { get; }
    /// <summary>Population standard deviation, matching N.I.N.A.'s count denominator.</summary>
    public double? StandardDeviation { get; }
    /// <summary>Five-bin triangular-smoothed distribution peak, restricted to occupied bins; lowest ADU wins ties.</summary>
    public int? PeakAdu { get; }
    public long CountAt(int adu) {
        if ((uint)adu > 65535) throw new ArgumentOutOfRangeException(nameof(adu));
        return counts[adu];
    }
    /// <summary>Linear interpolation at zero-based rank (N-1)*p/100 (type 7).</summary>
    public double? Percentile(double percentage) {
        if (!double.IsFinite(percentage) || percentage < 0 || percentage > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage));
        if (SampleCount == 0) return null;
        double rank = (SampleCount - 1) * (percentage / 100);
        long lo = (long)Math.Floor(rank), hi = (long)Math.Ceiling(rank);
        var low = AtRank(lo); var high = AtRank(hi);
        return low + (high - low) * (rank - lo);
    }
    private int AtRank(long rank) {
        int lo = 0, hi = 65535;
        while (lo < hi) {
            int mid = lo + (hi - lo) / 2;
            if (cumulative[mid] > rank) hi = mid; else lo = mid + 1;
        }
        return lo;
    }
    public long CountAtOrBelow(int threshold) => threshold < 0 ? 0
        : threshold >= 65535 ? SampleCount : cumulative[threshold];
    public long CountAtOrAbove(int threshold) => threshold <= 0 ? SampleCount
        : threshold > 65535 ? 0 : SampleCount - cumulative[threshold - 1];
    public ThresholdCounts Thresholds(int lowInclusive, int highInclusive) {
        var low = CountAtOrBelow(lowInclusive); var high = CountAtOrAbove(highInclusive);
        return new(low, SampleCount == 0 ? 0 : low * 100.0 / SampleCount,
            high, SampleCount == 0 ? 0 : high * 100.0 / SampleCount);
    }
    /// <summary>Sum inclusive contiguous ADU bins; supports count-preserving rendering later.</summary>
    public long CountBetween(int firstInclusive, int lastInclusive) {
        if (firstInclusive > lastInclusive) throw new ArgumentException("Range endpoints are reversed.");
        return CountAtOrBelow(lastInclusive) - (firstInclusive <= 0 ? 0 : CountAtOrBelow(firstInclusive - 1));
    }
}
