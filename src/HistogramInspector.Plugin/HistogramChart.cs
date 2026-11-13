using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using HistogramInspector.Core;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.Legends;

namespace HistogramInspector.Plugin;

public enum HistogramZoom { Auto, Custom, FullScale }
public sealed record ImageStatisticsContext(int Width, int Height, int BitDepth, int? Gain, int? Offset,
    int? StarCount = null, double? Hfr = null, double? HfrSd = null);

public sealed class HistogramChart : INotifyPropertyChanged {
    private HistogramAnalysis? result;
    private ImageStatisticsContext? imageContext;
    private int displayBits = 16;
    private string displayScaleSource = "default 16-bit display scale";
    private bool visible;
    private int plotWidth = 640;
    private OxyColor background = OxyColor.FromRgb(30, 30, 30), foreground = OxyColors.WhiteSmoke;
    public void SetTheme(OxyColor backgroundColor, OxyColor foregroundColor) {
        if (background == backgroundColor && foreground == foregroundColor) return;
        background = backgroundColor; foreground = foregroundColor; Rebuild();
    }
    private PlotModel? model;
    public PlotModel? Model { get => model; private set { model = value; Changed(); } }
    public Array ZoomModes => Enum.GetValues<HistogramZoom>();
    private HistogramZoom zoom;
    public HistogramZoom Zoom { get => zoom; set { zoom = value; Changed(); Rebuild(); } }
    private bool includeZero, logarithmic, percentage, rgb;
    private bool red = true, green = true, blue = true;
    public bool IncludeZero { get => includeZero; set { includeZero = value; Changed(); Rebuild(); } }
    public bool Logarithmic { get => logarithmic; set { logarithmic = value; Changed(); Rebuild(); } }
    public bool Percentage { get => percentage; set { percentage = value; Changed(); Rebuild(); } }
    public bool Rgb { get => rgb; set { rgb = value; Changed(); Rebuild(); } }
    public bool Red { get => red; set { red = value; Changed(); Rebuild(); } }
    public bool Green { get => green; set { green = value; Changed(); Rebuild(); } }
    public bool Blue { get => blue; set { blue = value; Changed(); Rebuild(); } }
    private string customMinimum = "0", customMaximum = "65535", outputCeiling = "";
    public string CustomMinimum { get => customMinimum; set { customMinimum = value; Changed(); Rebuild(); } }
    public string CustomMaximum { get => customMaximum; set { customMaximum = value; Changed(); Rebuild(); } }
    public string OutputCeiling { get => outputCeiling; set { outputCeiling = value;
        if (!string.IsNullOrWhiteSpace(value)) { outputMapping = OutputMapping.ExplicitCeiling; Changed(nameof(Mapping)); }
        Changed(); Rebuild(); } }
    private OutputMapping outputMapping = OutputMapping.ConfiguredDataDepth;
    private string nativeBits = "";
    public Array OutputMappings => Enum.GetValues<OutputMapping>();
    public OutputMapping Mapping { get => outputMapping; set { outputMapping = value; Changed(); Rebuild(); } }
    public string NativeBits { get => nativeBits; set { nativeBits = value; Changed(); Rebuild(); } }
    private string lowThreshold = "0", highThreshold = "", lowTrigger = "1", highTrigger = "1";
    private bool lowEnabled, highEnabled, triggerPercentage;
    public string LowThreshold { get => lowThreshold; set { lowThreshold = value; Changed(); Rebuild(); } }
    public string HighThreshold { get => highThreshold; set { highThreshold = value; Changed(); Rebuild(); } }
    public string LowTrigger { get => lowTrigger; set { lowTrigger = value; Changed(); Rebuild(); } }
    public string HighTrigger { get => highTrigger; set { highTrigger = value; Changed(); Rebuild(); } }
    public bool LowEnabled { get => lowEnabled; set { lowEnabled = value; Changed(); Rebuild(); } }
    public bool HighEnabled { get => highEnabled; set { highEnabled = value; Changed(); Rebuild(); } }
    public bool TriggerPercentage { get => triggerPercentage; set { triggerPercentage = value; Changed(); Rebuild(); } }
    private string rangeText = "No completed histogram.", thresholdText = "Threshold flags disabled.", statistics = "";
    public string RangeText { get => rangeText; private set { rangeText = value; Changed(); } }
    public string ThresholdText { get => thresholdText; private set { thresholdText = value; Changed(); } }
    public string Statistics { get => statistics; private set { statistics = value; Changed(); } }
    private IReadOnlyList<ChannelStatistics> details = Array.Empty<ChannelStatistics>();
    public IReadOnlyList<ChannelStatistics> Details { get => details; private set { details = value; Changed(); } }
    public sealed record StatisticValue(string Label, string Value);
    public sealed record ChannelStatistics(string Name, IReadOnlyList<StatisticValue> Values);
    public void SetVisible(bool value) { visible = value; if (visible) Rebuild(); }
    public void UpdateImageContext(ImageStatisticsContext? context) { imageContext = context; if (visible) Rebuild(); }
    public void SetWidth(double width) {
        int next = Math.Clamp((int)Math.Max(1, width - 100), 32, 4096);
        if (next != plotWidth) { plotWidth = next; Rebuild(); }
    }
    public void SetAnalysis(HistogramAnalysis? value, bool newSelection, int scaleBits = 16, string scaleSource = "default 16-bit display scale", ImageStatisticsContext? context = null) {
        result = value;
        imageContext = context;
        displayBits = scaleBits is >= 1 and <= 16 ? scaleBits : 16;
        displayScaleSource = scaleSource;
        if (newSelection) { outputCeiling = ""; nativeBits = ""; outputMapping = OutputMapping.ConfiguredDataDepth;
            Changed(nameof(OutputCeiling)); Changed(nameof(NativeBits)); Changed(nameof(Mapping));
            Model = null; Statistics = ""; Details = Array.Empty<ChannelStatistics>(); ThresholdText = "Updating selected image."; }
        // Clear even when hidden, so an obsolete plot cannot appear under new metadata.
        if (value?.IsSupported != true) { Model = null; RangeText = "No completed supported histogram."; Statistics = ""; Details = Array.Empty<ChannelStatistics>(); ThresholdText = "No completed supported histogram."; }
        if (visible) Rebuild();
    }
    private List<(string Name, HistogramDistribution Data, OxyColor Color)> Channels() {
        var items = new List<(string, HistogramDistribution, OxyColor)>();
        if (result?.Combined is null) return items;
        bool dark = background.R * 0.2126 + background.G * 0.7152 + background.B * 0.0722 < 128;
        if (!rgb || result.Red is null) { items.Add((rgb ? "Combined (CFA unavailable)" : "Combined", result.Combined, foreground)); return items; }
        if (red) items.Add(("Red", result.Red, dark ? OxyColors.OrangeRed : OxyColors.DarkRed));
        if (green) items.Add(("Green", result.Green!, dark ? OxyColors.LimeGreen : OxyColors.DarkGreen));
        if (blue) items.Add(("Blue", result.Blue!, dark ? OxyColors.DeepSkyBlue : OxyColors.DarkBlue));
        return items;
    }
    private void Rebuild() {
        if (!visible || result?.IsSupported != true) return;
        var channels = Channels();
        if (channels.Count == 0) { Model = null; RangeText = "Enable at least one CFA channel."; Statistics = ""; Details = Array.Empty<ChannelStatistics>(); ThresholdText = "Enable a channel to inspect thresholds."; return; }
        var calculated = int.TryParse(nativeBits, out int bits) && outputMapping is not (OutputMapping.ExplicitCeiling or OutputMapping.ConfiguredDataDepth)
            ? OutputRangePolicy.Calculate(bits, outputMapping) : null;
        int ceiling = 0;
        bool defaultScale = outputMapping == OutputMapping.ConfiguredDataDepth;
        bool knownCeiling = defaultScale ? (ceiling = (1 << displayBits) - 1) > 0 : outputMapping == OutputMapping.ExplicitCeiling
            ? int.TryParse(outputCeiling, out ceiling) && ceiling > 0 && ceiling <= 65535
            : calculated is not null && (ceiling = calculated.Ceiling) > 0;
        UpdateStatistics(channels, ceiling, knownCeiling);
        AduRange range;
        string message = "";
        if (zoom == HistogramZoom.Custom) {
            if (!int.TryParse(customMinimum, out int min) || !int.TryParse(customMaximum, out int max) ||
                min < 0 || max > 65535 || min >= max) { RangeText = "Custom range requires 0 ≤ minimum < maximum ≤ 65535 ADU."; return; }
            if (knownCeiling && max > ceiling) { RangeText = "Custom maximum exceeds the configured output ceiling."; return; }
            range = new(min, max);
        } else if (zoom == HistogramZoom.FullScale && knownCeiling) range = new(0, ceiling);
        else if (zoom == HistogramZoom.FullScale) {
            Model = null;
            RangeText = "Full Scale unavailable: output ceiling Unknown. Enter a verified explicit ceiling or ADC depth plus output mapping, or choose Auto/Custom.";
            return;
        }
        else {
            range = HistogramViewPolicy.Auto(channels.Select(c => c.Data), includeZero);
            if (knownCeiling) range = new(Math.Min(range.Minimum, ceiling - 1), Math.Min(range.Maximum, ceiling));
            if (zoom == HistogramZoom.FullScale) message = "Full Scale unavailable: output range unknown. Showing observed-data Auto view. ";
        }
        if (!knownCeiling && (!string.IsNullOrWhiteSpace(outputCeiling) || !string.IsNullOrWhiteSpace(nativeBits)))
            message += "Enter a valid explicit ceiling (1–65535) or ADC depth (1–16) for the selected mapping. ";
        if (knownCeiling && result!.ObservedMaximum > ceiling) message += "WARNING: observed samples exceed the manual ceiling. ";
        RangeText = message + $"Visible ADU: {range.Minimum:N0} – {range.Maximum:N0}. " +
            (defaultScale ? $"Default {displayBits}-bit display scale: 0–{ceiling:N0} ADU; source: {displayScaleSource}; exact camera ceiling unverified."
                : knownCeiling ? $"Output range: 0–{ceiling:N0}, {calculated?.Description ?? "manual explicit override for this image only"}." : "Actual output range: Unknown.");
        var plot = new PlotModel { TextColor = foreground, PlotAreaBorderColor = foreground,
            Background = background, PlotAreaBackground = background };
        plot.Legends.Add(new Legend { LegendPosition = LegendPosition.TopRight, LegendPlacement = LegendPlacement.Inside });
        plot.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Delivered ADU", Minimum = range.Minimum - 0.5,
            Maximum = range.Maximum + 0.5, IsPanEnabled = false, IsZoomEnabled = false,
            TicklineColor = foreground, MinorTicklineColor = foreground, AxislineColor = foreground });
        Axis y = logarithmic ? new LogarithmicAxis { Base = 10 } : new LinearAxis { Minimum = 0 };
        y.Position = AxisPosition.Left; y.Title = percentage ? "% of channel population per plotted bin" : "Samples per plotted bin";
        y.TicklineColor = foreground; y.MinorTicklineColor = foreground; y.AxislineColor = foreground;
        y.IsPanEnabled = false; y.IsZoomEnabled = false; plot.Axes.Add(y);
        int channelIndex = 0;
        foreach (var channel in channels) {
            var series = new LineSeries { Title = channel.Name, Color = channel.Color, StrokeThickness = 1.5,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "{0}\nADU {First}–{Last}\nCount: {Count}\n{Percent:0.#####}% of {Population} channel samples" };
            var bins = HistogramViewPolicy.Aggregate(channel.Data, range, plotWidth);
            // Tracker values must come from the original interval record, not a rounded plotted coordinate.
            // Zero bins are gaps on log Y; exact counts remain unchanged in the result.
            series.ItemsSource = bins.Select(b => new PlotBin((b.First + b.Last) / 2.0,
                logarithmic && b.Count == 0 ? double.NaN : percentage ? b.Count * 100.0 / Math.Max(1, channel.Data.SampleCount) : b.Count,
                b.First, b.Last, b.Count, b.Count * 100.0 / Math.Max(1, channel.Data.SampleCount), channel.Data.SampleCount)).ToArray();
            series.DataFieldX = nameof(PlotBin.X); series.DataFieldY = nameof(PlotBin.Y);
            plot.Series.Add(series);
            void Marker(string kind, double? value, int lane) {
                if (!value.HasValue) return;
                bool left = value < range.Minimum, right = value > range.Maximum;
                string suffix = left ? " ← outside view" : right ? " → outside view" : "";
                plot.Annotations.Add(new LineAnnotation {
                    Type = LineAnnotationType.Vertical, X = Math.Clamp(value.Value, range.Minimum, range.Maximum),
                    Color = channel.Color, LineStyle = LineStyle.Dash,
                    Text = $"{channel.Name} {kind} {value:0.###}{suffix}", TextColor = channel.Color,
                    TextOrientation = AnnotationTextOrientation.Horizontal,
                    TextHorizontalAlignment = value <= range.Minimum + (range.Maximum - range.Minimum) * 0.22
                        ? HorizontalAlignment.Left : value >= range.Minimum + (range.Maximum - range.Minimum) * 0.78
                        ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                    TextLinePosition = 0.96 - channelIndex * 0.28 - lane * 0.085,
                    Tag = $"{channel.Name}:{kind}:{value.Value.ToString(CultureInfo.InvariantCulture)}" });
            }
            Marker("min", channel.Data.Minimum, 0);
            Marker("median", channel.Data.Median, 1);
            Marker("max", channel.Data.Maximum, 2);
            channelIndex++;
        }
        Model = plot;
    }
    private void UpdateStatistics(List<(string Name, HistogramDistribution Data, OxyColor Color)> channels, int ceiling, bool knownCeiling) {
        Statistics = string.Join("\n", channels.Select(c => $"{c.Name}: median {c.Data.Median:0.###}; peak {c.Data.PeakAdu}; image min/max {c.Data.Minimum}/{c.Data.Maximum}; N={c.Data.SampleCount:N0}"));
        static string F(double? value) => value?.ToString("N2", CultureInfo.CurrentCulture) ?? "Unknown";
        static string Extreme(HistogramDistribution data, int? value) => value.HasValue
            ? $"{value.Value:N0} ({data.CountAt(value.Value):N0}×)" : "Unknown";
        Details = channels.Select(c => new ChannelStatistics(c.Name, new StatisticValue[] {
            new("Width", imageContext?.Width.ToString("N0") ?? "Unknown"), new("Height", imageContext?.Height.ToString("N0") ?? "Unknown"),
            new("Mean", F(c.Data.Mean)), new("SD", F(c.Data.StandardDeviation)),
            new("Median", F(c.Data.Median)), new("MAD", F(c.Data.Mad)),
            new("Min", Extreme(c.Data, c.Data.Minimum)), new("Max", Extreme(c.Data, c.Data.Maximum)),
            new("#Stars", imageContext?.StarCount?.ToString("N0") ?? "Unknown"), new("HFR", F(imageContext?.Hfr)),
            new("Bit depth", imageContext?.BitDepth.ToString() ?? "Unknown"), new("HFR SD", F(imageContext?.HfrSd)),
            new("Gain", imageContext?.Gain?.ToString() ?? "Unknown"), new("Offset", imageContext?.Offset?.ToString() ?? "Unknown"),
            new("Samples", c.Data.SampleCount.ToString("N0")), new("Peak", c.Data.PeakAdu?.ToString("N0") ?? "Unknown")
        })).ToArray();
        ThresholdText = string.Join("\n", channels.Select(c => ThresholdSummary(c.Name, c.Data, ceiling, knownCeiling)));
    }
    private string ThresholdSummary(string name, HistogramDistribution data, int ceiling, bool knownCeiling) {
        string Side(bool enabled, string text, string trigger, bool low) {
            if (!enabled) return low ? "Low: off" : "High: off";
            if (!int.TryParse(text, out int adu) || adu < 0 || adu > 65535 || (knownCeiling && adu > ceiling) ||
                !double.TryParse(trigger, NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) ||
                !double.IsFinite(limit) || limit < 0 || (triggerPercentage && limit > 100) ||
                (!triggerPercentage && limit != Math.Floor(limit))) return "Invalid threshold/trigger input";
            long count = low ? data.CountAtOrBelow(adu) : data.CountAtOrAbove(adu);
            double percent = count * 100.0 / Math.Max(1, data.SampleCount);
            bool flag = (triggerPercentage ? percent : count) >= limit;
            return $"{(flag ? "WARNING" : "OK")} {(low ? "≤" : "≥")}{adu}: {count:N0} ({percent:0.#####}% of {data.SampleCount:N0})";
        }
        return $"{name} — {Side(lowEnabled, lowThreshold, lowTrigger, true)}; {Side(highEnabled, highThreshold, highTrigger, false)}";
    }
    public sealed record PlotBin(double X, double Y, int First, int Last, long Count, double Percent, long Population);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
