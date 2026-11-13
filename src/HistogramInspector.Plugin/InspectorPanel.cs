using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HistogramInspector.Core;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;

namespace HistogramInspector.Plugin;

[Export(typeof(IDockableVM))]
[Export(typeof(InspectorPanel))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class InspectorPanel : IDockableVM, IDisposable {
    private readonly IImageControlVM source;
    private readonly IProfileService? profiles;
    private (int Bits, string Source)? pendingScale;
    private ImageStatisticsContext? pendingImageContext;
    private ImageStatisticsContext? currentImageContext;
    private IStarDetectionAnalysis? starAnalysis;
    private bool starPublicationQueued;
    private readonly Dispatcher dispatcher;
    private readonly object gate = new();
    private readonly SelectionTracker tracker = new();
    private bool disposed;
    private bool publicationQueued;
    private (long Generation, string Text)? pending;
    private string analysisText = "No selected image.";
    public string AnalysisText { get => analysisText; private set { analysisText = value; Changed(nameof(AnalysisText)); Changed(nameof(AnalysisStatus)); } }
    public string AnalysisStatus {
        get {
            var first = analysisText.Split('\n')[0];
            int skippedAt = analysisText.LastIndexOf("Skipped analyses:", StringComparison.Ordinal);
            return skippedAt < 0 ? first : first + " — " + analysisText[skippedAt..];
        }
    }
    private string? pendingAnalysis;
    private HistogramAnalysis? pendingResult;
    public HistogramAnalysis? CurrentAnalysis { get; private set; }
    public HistogramChart Chart { get; } = new();
    private long chartGeneration = -1;
    private AnalysisRequest? waiting;
    private CancellationTokenSource? active;
    private Task worker = Task.CompletedTask;
    private bool running;
    private long skipped;
    private sealed record AnalysisRequest(long Generation, string Context, int Width, int Height,
        int[]? Integers, ushort[]? Unsigned, CfaLayout? Cfa, string CfaReason, int ScaleBits, string ScaleSource, ImageStatisticsContext ImageContext);
    private string diagnostics = "No selected image.";
    public string Diagnostics { get => diagnostics; private set { diagnostics = value; Changed(nameof(Diagnostics)); } }

    [ImportingConstructor]
    public InspectorPanel(IImageControlVM source, [Import(AllowDefault = true)] IProfileService? profiles = null) {
        this.source = source;
        this.profiles = profiles;
        dispatcher = Application.Current.Dispatcher;
        HideCommand = new HidePanelCommand(this);
        source.PropertyChanged += OnSourceChanged;
        Refresh();
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(IImageControlVM.RenderedImage)) Refresh();
    }

    private void Refresh() {
        lock (gate) {
            if (disposed) return;
            try {
                var raw = source.RenderedImage?.RawImageData;
                WatchStars(raw?.StarDetectionAnalysis);
                // The image-mode simulator reuses raw objects, but the host assigns
                // a fresh image-history ID per capture. Snapshot that ID by value.
                var captureId = raw?.MetaData?.Image?.Id;
                if (!tracker.Select(raw, captureId >= 0 ? captureId : null)) { RefreshStars(); return; }
                currentImageContext = null;
                var generation = tracker.Generation;
                // Snapshot only constant-size context. Never read statistics, scan, clone, or convert pixels.
                var text = raw is null ? "No selected image.\nDistinct selections: " + tracker.DistinctImages
                    : Describe(raw, generation, tracker.DistinctImages);
                if (waiting is not null) { skipped++; waiting = null; }
                if (active is not null && !active.IsCancellationRequested) { skipped++; active.Cancel(); }
                if (raw is null) {
                    QueuePublication(generation, text, "No selected image.");
                    return;
                }
                var ints = raw.Data?.FlatArrayInt;
                var unsigned = ints is null ? raw.Data?.FlatArray : null;
                var (cfa, reason) = ResolveCfa(raw);
                var scale = CaptureDisplayScale(raw);
                var context = ReadStars(new ImageStatisticsContext(raw.Properties.Width, raw.Properties.Height, raw.Properties.BitDepth,
                    raw.MetaData?.Camera?.Gain >= 0 ? raw.MetaData.Camera.Gain : null,
                    raw.MetaData?.Camera?.Offset >= 0 ? raw.MetaData.Camera.Offset : null));
                currentImageContext = context;
                waiting = new(generation, text, raw.Properties.Width, raw.Properties.Height, ints, unsigned, cfa, reason, scale.Bits, scale.Source, context);
                QueuePublication(generation, text, $"Updating — skipped analyses: {skipped}", scale: scale, context: context);
                if (!running) { running = true; worker = Task.Run(AnalyzePending); }
            } catch (Exception ex) {
                Logger.Error(ex);
                var generation = tracker.Generation;
                waiting = null;
                active?.Cancel();
                QueuePublication(generation, "Image context unavailable. See N.I.N.A. log.", "Error — image context unavailable.");
            }
        }
    }

    // One pending compact snapshot and at most one queued UI operation, even during rapid loops.
    private void QueuePublication(long generation, string text, string? analysis = null, HistogramAnalysis? result = null, (int Bits, string Source)? scale = null, ImageStatisticsContext? context = null) {
        pending = (generation, text);
        pendingAnalysis = analysis;
        pendingResult = result;
        pendingScale = scale;
        pendingImageContext = context;
        if (publicationQueued || dispatcher.HasShutdownStarted) return;
        publicationQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            lock (gate) {
                publicationQueued = false;
                var snapshot = pending;
                var analysis = pendingAnalysis;
                var result = pendingResult;
                var scale = pendingScale;
                var context = pendingImageContext;
                pending = null;
                pendingAnalysis = null;
                pendingResult = null;
                pendingScale = null;
                pendingImageContext = null;
                if (!disposed && snapshot.HasValue && snapshot.Value.Generation == tracker.Generation) {
                    Diagnostics = snapshot.Value.Text;
                    CurrentAnalysis = result;
                    Chart.SetAnalysis(result, chartGeneration != snapshot.Value.Generation, scale?.Bits ?? 16, scale?.Source ?? "default 16-bit display scale", currentImageContext ?? context);
                    chartGeneration = snapshot.Value.Generation;
                    Changed(nameof(CurrentAnalysis));
                    if (analysis is not null) AnalysisText = analysis;
                }
            }
        }));
    }

    private void AnalyzePending() {
        while (true) {
            AnalysisRequest request;
            CancellationTokenSource cancellation;
            lock (gate) {
                if (disposed || waiting is null) { running = false; return; }
                request = waiting; waiting = null;
                cancellation = active = new CancellationTokenSource();
            }
            try {
                var token = cancellation.Token;
                var result = request.Integers is not null
                    ? HistogramEngine.Analyze(request.Integers, request.Width, request.Height, request.Cfa, token)
                    : request.Unsigned is not null
                        ? HistogramEngine.Analyze(request.Unsigned, request.Width, request.Height, request.Cfa, token)
                        : throw new NotSupportedException("No supported raw array is available.");
                var text = FormatAnalysis(result, request.CfaReason);
                lock (gate) {
                    if (!disposed && !token.IsCancellationRequested && tracker.Generation == request.Generation)
                        QueuePublication(request.Generation, request.Context, text + $"\nSkipped analyses: {skipped}", result, (request.ScaleBits, request.ScaleSource), request.ImageContext);
                }
            } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                // A newer generation or shutdown owns the display now.
            } catch (Exception ex) {
                Logger.Error(ex);
                lock (gate) {
                    if (!disposed && tracker.Generation == request.Generation)
                        QueuePublication(request.Generation, request.Context,
                            $"Error — {ex.GetType().Name}. See N.I.N.A. log.\nSkipped analyses: {skipped}");
                }
            } finally {
                lock (gate) { active = null; cancellation.Dispose(); }
            }
        }
    }

    private void WatchStars(IStarDetectionAnalysis? next) {
        if (ReferenceEquals(starAnalysis, next)) return;
        if (starAnalysis is not null) starAnalysis.PropertyChanged -= OnStarsChanged;
        starAnalysis = next;
        if (starAnalysis is not null) starAnalysis.PropertyChanged += OnStarsChanged;
    }
    private ImageStatisticsContext ReadStars(ImageStatisticsContext context) {
        static double? Valid(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 ? value : null;
        return context with {
            StarCount = starAnalysis?.DetectedStars >= 0 ? starAnalysis.DetectedStars : null,
            Hfr = Valid(starAnalysis?.HFR), HfrSd = Valid(starAnalysis?.HFRStDev)
        };
    }
    private void OnStarsChanged(object? sender, PropertyChangedEventArgs e) {
        lock (gate) {
            if (!disposed && ReferenceEquals(sender, starAnalysis)) RefreshStars();
        }
    }
    private void RefreshStars() {
        if (disposed || currentImageContext is null) return;
        currentImageContext = ReadStars(currentImageContext);
        if (starPublicationQueued || dispatcher.HasShutdownStarted) return;
        starPublicationQueued = true;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            lock (gate) {
                starPublicationQueued = false;
                if (!disposed && chartGeneration == tracker.Generation && CurrentAnalysis is not null)
                    Chart.UpdateImageContext(currentImageContext);
            }
        }));
    }

    private static (CfaLayout?, string) ResolveCfa(IImageData raw) {
        var camera = raw.MetaData?.Camera;
        if (!raw.Properties.IsBayered || camera is null)
            return (null, "Combined only: host does not identify this image as a raw CFA mosaic.");
        var selection = camera.BayerPattern.ToString();
        var patternName = selection == "Auto" ? camera.SensorType.ToString() : selection;
        if (!Enum.TryParse<CfaPattern>(patternName, out var pattern) || !Enum.IsDefined(pattern))
            return (null, "Combined only: per-image CFA pattern is unresolved.");
        // Require explicit image binning or saved header evidence; Camera.BinX/Y default to 1.
        var binning = raw.MetaData?.Image?.Binning?.Split('x', 'X');
        bool known = binning is { Length: 2 } && int.TryParse(binning[0], out var bx) && bx == 1 &&
            int.TryParse(binning[1], out var by) && by == 1;
        if (!known) {
            static bool One(NINA.Image.ImageData.IGenericMetaDataHeader h) => h switch {
                NINA.Image.ImageData.IntMetaDataHeader i => i.Value == 1,
                NINA.Image.ImageData.DoubleMetaDataHeader d => d.Value == 1,
                NINA.Image.ImageData.StringMetaDataHeader s => s.Value.Trim().Trim('\'') == "1",
                _ => false
            };
            // Do not override explicit binned capture context with older source-file headers.
            if (binning is { Length: 2 }) return (null, "Combined only: capture binning is not verified 1x1.");
            var headers = raw.MetaData?.GenericHeaders;
            known = headers?.Any(h => h.Key == "XBINNING" && One(h)) == true &&
                headers.Any(h => h.Key == "YBINNING" && One(h));
        }
        if (!known) return (null, "Combined only: 1x1 binning is unknown or mosaic was binned.");
        return (new(pattern, camera.BayerOffsetX, camera.BayerOffsetY),
            $"CFA: {pattern}, offsets {camera.BayerOffsetX}/{camera.BayerOffsetY}; N.I.N.A. per-image metadata (offset provenance may be unknown).");
    }

    private (int Bits, string Source) CaptureDisplayScale(IImageData raw) {
        if (raw.MetaData?.Image?.Id >= 0 && profiles?.ActiveProfile?.CameraSettings is { } settings) {
            double bits = settings.BitDepth;
            if (double.IsFinite(bits) && bits == Math.Truncate(bits) && bits is >= 1 and <= 16)
                return ((int)bits, $"N.I.N.A. Options > Equipment > Camera bit depth, captured for this image" +
                    (raw.Properties.BitDepth != bits ? $" (reported image depth differs: {raw.Properties.BitDepth})" : ""));
        }
        return raw.Properties.BitDepth is >= 1 and <= 16
            ? (raw.Properties.BitDepth, "per-image reported depth; exact camera ceiling unverified")
            : (16, "default 16-bit display scale; camera range unverified");
    }

    private static string FormatAnalysis(HistogramAnalysis result, string cfaReason) {
        if (!result.IsSupported)
            return $"Unsupported — integer samples outside UInt16 domain.\nObserved min/max: {result.ObservedMinimum} / {result.ObservedMaximum}\n" +
                $"Below 0: {result.BelowUnsigned16}; above 65535: {result.AboveUnsigned16}\nNo clamped histogram statistics published.";
        static string V(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Unknown";
        static string Distribution(string name, HistogramDistribution d) =>
            $"{name}: N={d.SampleCount:N0}; min/max={d.Minimum}/{d.Maximum}; median={V(d.Median)} ADU\n" +
            $"  Mean={V(d.Mean)}; SD={V(d.StandardDeviation)}; MAD={V(d.Mad)}; peak={d.PeakAdu}";
        var text = "Ready — exact delivered-code statistics\n" + Distribution("Combined", result.Combined!) + "\n" + cfaReason;
        if (result.Red is not null) text += "\n" + Distribution("Red", result.Red) + "\n" +
            Distribution("Green (both sites)", result.Green!) + "\n" + Distribution("Blue", result.Blue!);
        return text + "\nWarning thresholds and range overrides are controlled above.\nPeak describes the distribution; it is not necessarily sky background.";
    }

    private static string Describe(IImageData raw, long generation, long count) {
        var p = raw.Properties;
        var m = raw.MetaData;
        // Int first: ImageArrayInt.FlatArray would allocate and clamp out-of-domain samples.
        var ints = raw.Data?.FlatArrayInt;
        var ushorts = ints is null ? raw.Data?.FlatArray : null;
        var representation = ints is not null ? "Int32[] (original integer samples)"
            : ushorts is not null ? "UInt16[] (original unsigned samples)" : "Unknown / unavailable";
        var length = ints?.LongLength ?? ushorts?.LongLength;
        var expected = (long)p.Width * p.Height;
        var nominal = p.BitDepth is > 0 and <= 16 ? $"0–{(1L << p.BitDepth) - 1:N0}" : "Unknown";
        string? Header(string key) {
            var header = m?.GenericHeaders?.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
            return header switch {
                NINA.Image.ImageData.StringMetaDataHeader s => s.Value?.Trim().Trim('\''),
                NINA.Image.ImageData.IntMetaDataHeader i => i.Value.ToString(CultureInfo.InvariantCulture),
                NINA.Image.ImageData.DoubleMetaDataHeader d => d.Value.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
        }
        var imageType = !string.IsNullOrWhiteSpace(m?.Image?.ImageType) ? m.Image.ImageType
            : !string.IsNullOrWhiteSpace(Header("IMAGETYP")) ? Header("IMAGETYP") + " (per-image header)" : null;
        var binning = m?.Image?.Binning;
        if (string.IsNullOrWhiteSpace(binning) &&
            int.TryParse(Header("XBINNING"), out var binX) && binX > 0 &&
            int.TryParse(Header("YBINNING"), out var binY) && binY > 0)
            binning = $"{binX}x{binY} (per-image headers)";
        static string S(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        static string N(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value.ToString(CultureInfo.InvariantCulture) : "Unknown";
        return $"Raw image context — numeric histogram analysis shown above\n" +
            $"Display generation: {generation}\nDistinct selections: {count}\n" +
            $"N.I.N.A. image ID: {(m?.Image?.Id is int id && id >= 0 ? id.ToString() : "Unknown")}\n" +
            $"Dimensions: {p.Width} × {p.Height}\nRepresentation: {representation}\n" +
            $"Array implementation: {raw.Data?.GetType().FullName ?? "Unknown"}\n" +
            $"Samples: {length?.ToString() ?? "Unknown"}; expected: {expected}" +
            $"{(length.HasValue && length != expected ? " — DIMENSION MISMATCH" : "")}\n" +
            $"Reported image bit depth: {p.BitDepth}\nNominal reported-depth codes: {nominal}\n" +
            "Sensor ADC depth: Unknown\nDelivered scaling: Unknown\nActual output ceiling: Unknown\n" +
            "Reported depth is not verified camera full scale or physical saturation.\n" +
            $"N.I.N.A. Bayered flag: {p.IsBayered}\n" +
            $"Per-image sensor/pattern metadata: {m?.Camera?.SensorType.ToString() ?? "Unknown"}\n" +
            $"Per-image Bayer selection: {m?.Camera?.BayerPattern.ToString() ?? "Unknown"}\n" +
            $"Per-image Bayer offsets X/Y: {(m?.Camera is { } c ? $"{c.BayerOffsetX} / {c.BayerOffsetY}" : "Unknown")}\n" +
            "CFA source: N.I.N.A. per-image metadata; defaults/offset provenance may be unknown.\n" +
            "Bayered=False does not prove absence of CFA. Auto is not a resolved pattern.\n" +
            $"Camera: {S(m?.Camera?.Name)}\n" +
            $"Exposure (s): {N(m?.Image?.ExposureTime)}\nFilter: {S(m?.FilterWheel?.Filter)}\n" +
            $"Gain: {N(m?.Camera?.Gain)}\nOffset: {N(m?.Camera?.Offset)}\n" +
            $"Binning: {S(binning)}\n" +
            $"Readout mode: {S(m?.Camera?.ReadoutModeName)}\n" +
            $"Image type: {S(imageType)}\n" +
            $"Capture time: {(m?.Image?.ExposureStart is DateTime t && t != DateTime.MinValue ? t.ToString("O") : "Unknown")}\n" +
            "Source: selected Imaging image; loaded-file provenance may be incomplete.\n" +
            "File-camera simulator captures may replace source-file exposure/type and CFA context.";
    }

    public void Dispose() {
        lock (gate) {
            if (disposed) return;
            disposed = true;
            pending = null;
            pendingAnalysis = null;
            pendingResult = null;
            CurrentAnalysis = null;
            waiting = null;
            active?.Cancel();
            source.PropertyChanged -= OnSourceChanged;
            WatchStars(null);
            currentImageContext = null;
            tracker.Select(null);
        }
    }
    public async Task StopAsync() { Dispose(); await worker.ConfigureAwait(false); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
    private bool visible = true, closed;
    public bool IsVisible { get => visible; set { visible = value; Changed(nameof(IsVisible)); } }
    public bool IsClosed { get => closed; set { closed = value; Changed(nameof(IsClosed)); } }
    public bool CanClose { get; set; } = true;
    public bool HasSettings { get; set; }
    public bool IsTool => false;
    public string ContentId => "HistogramInspector.Stage1";
    public string Title { get; set; } = "Histogram Inspector";
    // MEF constructs this VM on a worker thread. Frozen Freezables can be bound by
    // the UI dispatcher; an unfrozen GeometryGroup retains its creating thread.
    public GeometryGroup ImageGeometry { get; set; } = CreateIcon();
    private static GeometryGroup CreateIcon() {
        var icon = new GeometryGroup();
        icon.Children.Add(new RectangleGeometry(new Rect(0, 12, 4, 12)));
        icon.Children.Add(new RectangleGeometry(new Rect(6, 4, 4, 20)));
        icon.Children.Add(new RectangleGeometry(new Rect(12, 8, 4, 16)));
        icon.Children.Add(new RectangleGeometry(new Rect(18, 16, 4, 8)));
        icon.Freeze();
        return icon;
    }
    public ICommand HideCommand { get; }
    public void Hide(object o) => IsVisible = !IsVisible;
    private sealed class HidePanelCommand(InspectorPanel panel) : ICommand {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => panel.Hide(parameter!);
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
