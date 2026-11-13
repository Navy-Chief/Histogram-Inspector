using HistogramInspector.Core;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

internal static class HistogramChecks {
    public static void Run(string[] args) {
        var endpoints = HistogramEngine.Analyze(new ushort[] { 0, 65535 }, 2, 1).Combined!;
        Check(endpoints.Median == 32767.5 && endpoints.Mad == 32767.5 && endpoints.Minimum == 0 && endpoints.Maximum == 65535,
            "endpoint bins, half-ADU median and exact MAD");
        var constant = HistogramEngine.Analyze(new ushort[] { 0, 0, 0 }, 3, 1).Combined!;
        Check(constant.Median == 0 && constant.Mad == 0 && constant.PeakAdu == 0, "all-zero distribution");
        var saturated = HistogramEngine.Analyze(new ushort[] { 65535, 65535 }, 2, 1).Combined!;
        Check(saturated.Mad == 0 && saturated.PeakAdu == 65535, "all-container-maximum distribution");
        var peakPixels = Enumerable.Repeat((ushort)10, 3).Concat(Enumerable.Repeat((ushort)11, 4))
            .Concat(Enumerable.Repeat((ushort)12, 3)).Concat(Enumerable.Repeat((ushort)100, 7)).ToArray();
        Check(HistogramEngine.Analyze(peakPixels, peakPixels.Length, 1).Combined!.PeakAdu == 11,
            "smoothed peak favors populated distribution over isolated tallest bin");
        var ints = HistogramEngine.Analyze(new int[] { -3, 0, 65535, 70000 }, 2, 2);
        Check(!ints.IsSupported && ints.Combined is null && ints.BelowUnsigned16 == 1 && ints.AboveUnsigned16 == 1 &&
            ints.ObservedMinimum == -3 && ints.ObservedMaximum == 70000, "out-of-domain integers reported without clamping or partial statistics");
        Check(HistogramEngine.Analyze(new int[] { 0, 4095, 16383 }, 3, 1).Combined!.Maximum == 16383,
            "unscaled 12/14-bit codes remain in their delivered coordinates");
        var boundaries = HistogramEngine.Analyze(new ushort[] { 0, 10, 10, 20 }, 4, 1).Combined!;
        var thresholds = boundaries.Thresholds(10, 10);
        Check(thresholds.LowCount == 3 && thresholds.HighCount == 3 && thresholds.LowPercentage == 75 && thresholds.HighPercentage == 75,
            "inclusive thresholds and independent overlapping warnings");
        Check(boundaries.CountAtOrBelow(int.MinValue) == 0 && boundaries.CountAtOrAbove(int.MaxValue) == 0 &&
            boundaries.CountAtOrBelow(int.MaxValue) == 4 && boundaries.CountAtOrAbove(int.MinValue) == 4,
            "thresholds outside storage domain");
        Expect<ArgumentException>(() => HistogramEngine.Analyze(new ushort[3], 2, 2), "dimension mismatch rejected");
        Expect<ArgumentException>(() => HistogramEngine.Analyze(Array.Empty<ushort>(), 0, 0), "empty image rejected");
        Expect<ArgumentOutOfRangeException>(() => boundaries.Percentile(double.NaN), "invalid percentile rejected");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Expect<OperationCanceledException>(() => HistogramEngine.Analyze(new ushort[4], 2, 2, cancellationToken: canceled.Token),
            "cancellation honored");
        var tiny = HistogramEngine.Analyze(new ushort[] { 7 }, 1, 1, new(CfaPattern.RGGB));
        Check(tiny.Red!.SampleCount == 1 && tiny.Green!.SampleCount == 0 && tiny.Green.Median is null &&
            tiny.Green.Mad is null && tiny.Blue!.Percentile(50) is null, "empty CFA channels explicitly represented");
        Check(HistogramEngine.Analyze(new ushort[4], 2, 2).Red is null &&
            HistogramEngine.Analyze(new ushort[4], 2, 2, new(CfaPattern.RGGB, BinX: 2)).Red is null,
            "unknown pattern and binned mosaic do not fabricate RGB channels");

        var rng = new Random(104729);
        for (int trial = 0; trial < 30; trial++) {
            int width = rng.Next(1, 12), height = rng.Next(1, 12);
            var pixels = new ushort[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)rng.Next(0, 65536);
            VerifyDistribution(pixels, HistogramEngine.Analyze(pixels, width, height).Combined!);
            var fromInt = HistogramEngine.Analyze(pixels.Select(v => (int)v).ToArray(), width, height).Combined!;
            VerifyDistribution(pixels, fromInt);
            foreach (var pattern in Enum.GetValues<CfaPattern>()) {
                int ox = trial % 3 - 1, oy = trial % 4 - 2;
                var actual = HistogramEngine.Analyze(pixels, width, height, new(pattern, ox, oy));
                string grid = pattern.ToString();
                var channels = new Dictionary<char, List<ushort>> { ['R'] = [], ['G'] = [], ['B'] = [] };
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                    int px = ((x + ox) % 2 + 2) % 2, py = ((y + oy) % 2 + 2) % 2;
                    channels[grid[py * 2 + px]].Add(pixels[y * width + x]);
                }
                VerifyDistribution(channels['R'].ToArray(), actual.Red!);
                VerifyDistribution(channels['G'].ToArray(), actual.Green!);
                VerifyDistribution(channels['B'].ToArray(), actual.Blue!);
                if (actual.Red!.SampleCount + actual.Green!.SampleCount + actual.Blue!.SampleCount != pixels.Length)
                    throw new Exception("CFA populations lost");
            }
        }
        Check(true, "randomized sorted-reference statistics, integer parity, four CFA patterns, odd sizes and signed offsets");
        var rgb = HistogramEngine.Analyze(new ushort[] { 1, 2, 3, 4 }, 2, 2, new(CfaPattern.RGGB));
        Check(rgb.Red!.Thresholds(65535, 0).LowPercentage == 100 && rgb.Green!.SampleCount == 2 &&
            rgb.Green.Thresholds(65535, 0).LowPercentage == 100, "CFA percentages use each channel population");
        foreach (var distribution in new[] { endpoints, constant, saturated, boundaries }) {
            var auto = HistogramViewPolicy.Auto(new[] { distribution }, false);
            if (auto.Minimum < 0 || auto.Maximum > 65535 || auto.Minimum >= auto.Maximum)
                throw new Exception("Invalid Auto range");
            foreach (int width in new[] { 1, 7, 317, 1000, 65536 }) {
                var bins = HistogramViewPolicy.Aggregate(distribution, new(0, 65535), width);
                if (bins.Sum(b => b.Count) != distribution.SampleCount || bins.Count > width ||
                    bins[0].First != 0 || bins[^1].Last != 65535) throw new Exception("Aggregation mismatch");
            }
        }
        Check(true, "Auto handles zero/constant/maximum data and aggregation preserves all counts at multiple widths");
        Check(OutputRangePolicy.Calculate(12, OutputMapping.NativeUnsigned)?.Ceiling == 4095 &&
            OutputRangePolicy.Calculate(14, OutputMapping.NativeUnsigned)?.Ceiling == 16383 &&
            OutputRangePolicy.Calculate(16, OutputMapping.NativeUnsigned)?.Ceiling == 65535,
            "generic native ADC range arithmetic for 12 14 and 16 bits");
        Check(OutputRangePolicy.Calculate(14, OutputMapping.LeftShiftTo16)?.Ceiling == 65532 &&
            OutputRangePolicy.Calculate(12, OutputMapping.LeftShiftTo16)?.Ceiling == 65520 &&
            OutputRangePolicy.Calculate(16, OutputMapping.LeftShiftTo16)?.Ceiling == 65535,
            "shifted output ceiling uses attainable codes rather than container maximum");
        Check(OutputRangePolicy.Calculate(0, OutputMapping.NativeUnsigned) is null &&
            OutputRangePolicy.Calculate(17, OutputMapping.LeftShiftTo16) is null,
            "invalid ADC depths remain unknown");
        if (args.Length == 2 && args[0] == "--xisf") VerifyXisf(args[1]);
    }
    private static void VerifyDistribution(ushort[] pixels, HistogramDistribution actual) {
        var sorted = (ushort[])pixels.Clone(); Array.Sort(sorted);
        if (actual.SampleCount != sorted.Length) throw new Exception("Population mismatch");
        var expected = new long[65536]; foreach (var v in sorted) expected[v]++;
        for (int i = 0; i < expected.Length; i++) if (actual.CountAt(i) != expected[i]) throw new Exception("Bin mismatch");
        if (sorted.Length == 0) { if (actual.Median is not null) throw new Exception("Empty median"); return; }
        double P(double p) {
            double index = (sorted.Length - 1) * p / 100;
            int lo = (int)Math.Floor(index), hi = (int)Math.Ceiling(index);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (index - lo);
        }
        foreach (double p in new double[] { 0, 1, 10, 25, 50, 75, 90, 99, 100 })
            if (Math.Abs(actual.Percentile(p)!.Value - P(p)) > 1e-9) throw new Exception("Percentile mismatch");
        double median = P(50);
        double mean = sorted.Average(v => (double)v);
        double sd = Math.Sqrt(sorted.Average(v => ((double)v - mean) * (v - mean)));
        if (Math.Abs(actual.Mean!.Value - mean) > 1e-9 || Math.Abs(actual.StandardDeviation!.Value - sd) > 1e-8)
            throw new Exception("Mean/population SD mismatch");
        var deviations = sorted.Select(v => Math.Abs(v - median)).ToArray(); Array.Sort(deviations);
        double mad = (deviations[(deviations.Length - 1) / 2] + deviations[deviations.Length / 2]) / 2;
        if (actual.Mad != mad || actual.Minimum != sorted[0] || actual.Maximum != sorted[^1]) throw new Exception("Statistics mismatch");
        for (int threshold = -1; threshold <= 65536; threshold += 8192) {
            if (actual.CountAtOrBelow(threshold) != pixels.LongCount(v => v <= threshold) ||
                actual.CountAtOrAbove(threshold) != pixels.LongCount(v => v >= threshold)) throw new Exception("Threshold mismatch");
        }
        long aggregated = 0;
        for (int lo = 0; lo < 65536; lo += 317) aggregated += actual.CountBetween(lo, Math.Min(lo + 316, 65535));
        if (aggregated != pixels.Length) throw new Exception("Aggregation lost counts");
    }
    // Test-only reader for a single, uncompressed, little-endian UInt16 XISF attachment.
    // This is not a production file loader; N.I.N.A. remains the plugin's data source.
    private static void VerifyXisf(string path) {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "XISF0100") throw new Exception("Not XISF");
        int size = reader.ReadInt32(); reader.ReadInt32();
        if (size <= 0 || size > 1048576) throw new Exception("Unexpected header size");
        var xml = XDocument.Parse(Encoding.UTF8.GetString(reader.ReadBytes(size)));
        var image = xml.Descendants().Single(e => e.Name.LocalName == "Image");
        if ((string?)image.Attribute("sampleFormat") != "UInt16" || image.Attribute("compression") is not null ||
            ((string?)image.Attribute("byteOrder") is string order && order != "little")) throw new Exception("Unsupported test fixture format");
        var dims = image.Attribute("geometry")!.Value.Split(':').Select(int.Parse).ToArray();
        if (dims.Length != 3 || dims[2] != 1) throw new Exception("Expected one raw channel");
        var location = image.Attribute("location")!.Value.Split(':');
        if (location[0] != "attachment") throw new Exception("Expected attachment");
        int length = int.Parse(location[2]);
        if ((long)dims[0] * dims[1] * 2 != length) throw new Exception("Attachment length mismatch");
        reader.BaseStream.Position = long.Parse(location[1]); var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new Exception("Truncated attachment");
        string checksum = "sha-256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (checksum != image.Attribute("checksum")?.Value) throw new Exception("Fixture checksum mismatch");
        var pixels = new ushort[length / 2]; Buffer.BlockCopy(bytes, 0, pixels, 0, length);
        string? Keyword(string name) => image.Elements().FirstOrDefault(e => e.Name.LocalName == "FITSKeyword" &&
            (string?)e.Attribute("name") == name)?.Attribute("value")?.Value.Trim('\'');
        if (Keyword("BAYERPAT") != "RGGB" || Keyword("XBAYROFF") != "0" || Keyword("YBAYROFF") != "0")
            throw new Exception("This real-image check expects declared RGGB with zero offsets");
        var result = HistogramEngine.Analyze(pixels, dims[0], dims[1], new(CfaPattern.RGGB));
        VerifyDistribution(pixels, result.Combined!);
        var expectedCfa = new long[][] { new long[65536], new long[65536], new long[65536] };
        const string mosaic = "RGGB";
        for (int y = 0; y < dims[1]; y++) for (int x = 0; x < dims[0]; x++) {
            char site = mosaic[(y % 2) * 2 + x % 2];
            expectedCfa[site == 'R' ? 0 : site == 'G' ? 1 : 2][pixels[y * dims[0] + x]]++;
        }
        var channels = new[] { result.Red!, result.Green!, result.Blue! };
        for (int c = 0; c < 3; c++) for (int adu = 0; adu < 65536; adu++)
            if (channels[c].CountAt(adu) != expectedCfa[c][adu]) throw new Exception("Real CFA bin mismatch");
        Console.WriteLine("PASS real XISF independently counted R/G/B bins and populations");
        Console.WriteLine($"PASS real XISF checksum, all combined bins and sorted-reference statistics: {pixels.Length:N0} samples; median {result.Combined!.Median}; MAD {result.Combined.Mad}");
    }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static void Expect<T>(Action action, string label) where T : Exception {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + label);
    }
}
