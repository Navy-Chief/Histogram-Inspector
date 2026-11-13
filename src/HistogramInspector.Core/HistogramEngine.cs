namespace HistogramInspector.Core;

public enum CfaPattern { RGGB, BGGR, GRBG, GBRG }

/// <summary>Pattern at the unshifted origin; offsets move the image origin within that mosaic.</summary>
public sealed record CfaLayout(CfaPattern Pattern, int OffsetX = 0, int OffsetY = 0,
    int BinX = 1, int BinY = 1);

public sealed record HistogramAnalysis(
    long SampleCount, int ObservedMinimum, int ObservedMaximum,
    long BelowUnsigned16, long AboveUnsigned16,
    HistogramDistribution? Combined, HistogramDistribution? Red,
    HistogramDistribution? Green, HistogramDistribution? Blue, string CfaStatus) {
    public bool IsSupported => Combined is not null;
}

/// <summary>Exact delivered-code counts. Does not infer camera range or physical saturation.</summary>
public static class HistogramEngine {
    public static HistogramAnalysis Analyze(ReadOnlySpan<ushort> pixels, int width, int height,
        CfaLayout? cfa = null, CancellationToken cancellationToken = default) =>
        Scan(pixels, default, false, width, height, cfa, cancellationToken);

    public static HistogramAnalysis Analyze(ReadOnlySpan<int> pixels, int width, int height,
        CfaLayout? cfa = null, CancellationToken cancellationToken = default) =>
        Scan(default, pixels, true, width, height, cfa, cancellationToken);

    private static HistogramAnalysis Scan(ReadOnlySpan<ushort> unsigned, ReadOnlySpan<int> integers,
        bool isInteger, int width, int height, CfaLayout? cfa, CancellationToken token) {
        var length = isInteger ? integers.Length : unsigned.Length;
        if (width <= 0 || height <= 0 || (long)width * height != length)
            throw new ArgumentException("Positive dimensions must match the sample count.");
        if (cfa is not null && (!Enum.IsDefined(cfa.Pattern) || cfa.BinX <= 0 || cfa.BinY <= 0))
            throw new ArgumentException("Invalid CFA layout.", nameof(cfa));
        token.ThrowIfCancellationRequested();
        var separate = cfa is { BinX: 1, BinY: 1 };
        var combined = new long[65536];
        var red = separate ? new long[65536] : null;
        var green = separate ? new long[65536] : null;
        var blue = separate ? new long[65536] : null;
        long below = 0, above = 0;
        int min = int.MaxValue, max = int.MinValue;
        for (int y = 0, index = 0; y < height; y++) {
            for (int x = 0; x < width; x++, index++) {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                int value = isInteger ? integers[index] : unsigned[index];
                min = Math.Min(min, value); max = Math.Max(max, value);
                if (value < 0) { below++; continue; }
                if (value > ushort.MaxValue) { above++; continue; }
                combined[value]++;
                if (!separate) continue;
                var px = (x & 1) ^ (cfa!.OffsetX & 1);
                var py = (y & 1) ^ (cfa.OffsetY & 1);
                // RGB indices for the four positions in each explicitly supplied pattern.
                int channel = cfa.Pattern switch {
                    CfaPattern.RGGB => py == 0 ? (px == 0 ? 0 : 1) : (px == 0 ? 1 : 2),
                    CfaPattern.BGGR => py == 0 ? (px == 0 ? 2 : 1) : (px == 0 ? 1 : 0),
                    CfaPattern.GRBG => py == 0 ? (px == 0 ? 1 : 0) : (px == 0 ? 2 : 1),
                    CfaPattern.GBRG => py == 0 ? (px == 0 ? 1 : 2) : (px == 0 ? 0 : 1),
                    _ => throw new ArgumentException("Unknown CFA pattern.")
                };
                (channel == 0 ? red! : channel == 1 ? green! : blue!)[value]++;
            }
        }
        token.ThrowIfCancellationRequested();
        var status = cfa is null ? "Combined only: no verified CFA layout supplied."
            : separate ? "Raw CFA sites; green combines both green populations."
            : "Combined only: binned data is not assumed to preserve the CFA mosaic.";
        // Do not publish even the partial in-domain histogram as an exact image result.
        if (below != 0 || above != 0)
            return new(length, min, max, below, above, null, null, null, null,
                "Unsupported integer range; no clamping or histogram statistics published.");
        return new(length, min, max, 0, 0,
            new(combined, token), red is null ? null : new(red, token),
            green is null ? null : new(green, token), blue is null ? null : new(blue, token), status);
    }
}
