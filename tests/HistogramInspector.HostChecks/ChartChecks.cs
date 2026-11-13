using HistogramInspector.Core;
using HistogramInspector.Plugin;
using OxyPlot.Series;
using OxyPlot.Annotations;
using OxyPlot.Axes;

internal static class ChartChecks {
    public static void Run() {
        var source = HistogramEngine.Analyze(new ushort[] { 0, 10, 20, 65535 }, 2, 2, new(CfaPattern.RGGB));
        var chart = new HistogramChart();
        chart.SetVisible(true);
        chart.SetAnalysis(source, true);
        Check(chart.Model?.Series.Count == 1 && chart.Zoom == HistogramZoom.Auto && !chart.Percentage,
            "chart defaults to Auto combined counts");
        chart.Zoom = HistogramZoom.FullScale;
        Check(chart.Model!.Axes.First(a => a.Position == AxisPosition.Bottom).Maximum == 65535.5 &&
              chart.RangeText.Contains("Default 16-bit display scale"), "Full Scale immediately uses labeled default 16-bit range without camera presets");
        chart.Mapping = OutputMapping.ExplicitCeiling;
        Check(chart.Model is null && chart.RangeText.Contains("Full Scale unavailable") && chart.RangeText.Contains("Unknown"),
            "unknown camera range never becomes storage Full Scale");
        chart.OutputCeiling = "65535";
        var bins = ((LineSeries)chart.Model!.Series[0]).ItemsSource.Cast<HistogramChart.PlotBin>().ToArray();
        Check(bins.Sum(b => b.Count) == 4 && bins[0].First == 0 && bins[^1].Last == 65535,
            "full-scale aggregation preserves endpoint populations");
        chart.OutputCeiling = ""; chart.Mapping = OutputMapping.NativeUnsigned; chart.NativeBits = "14";
        Check(chart.Model!.Axes.First(a => a.Position == AxisPosition.Bottom).Maximum == 16383.5,
            "Full Scale recalculates when ADC mapping changes on an existing histogram");
        chart.Mapping = OutputMapping.LeftShiftTo16;
        Check(chart.Model!.Axes.First(a => a.Position == AxisPosition.Bottom).Maximum == 65532.5 &&
            chart.RangeText.Contains("65,532"), "left-shift mapping updates existing plot to exact output ceiling");
        chart.Mapping = OutputMapping.ExplicitCeiling; chart.OutputCeiling = "65535";
        chart.Percentage = true;
        bins = ((LineSeries)chart.Model!.Series[0]).ItemsSource.Cast<HistogramChart.PlotBin>().ToArray();
        Check(Math.Abs(bins.Sum(b => b.Percent) - 100) < 1e-9 && bins.All(b => b.Population == 4),
            "chart percentages and tracker denominator are explicit");
        chart.Logarithmic = true;
        Check(chart.Model!.Axes.OfType<LogarithmicAxis>().Any() &&
            ((LineSeries)chart.Model.Series[0]).ItemsSource.Cast<HistogramChart.PlotBin>().Any(b => b.Count == 0 && double.IsNaN(b.Y)),
            "log chart leaves zero-count bins as gaps");
        chart.Rgb = true;
        Check(chart.Model!.Series.Count == 3 && chart.Model.Annotations.OfType<LineAnnotation>().Count() == 9,
            "CFA channels each get min median max markers");
        Check(chart.Details.Count == 3 && chart.Details[0].Values.Any(v => v.Label == "Min" && v.Value.Contains("1×")),
            "detailed channel statistics include aligned min occurrence values");
        chart.Red = false; chart.Blue = false;
        var green = ((LineSeries)chart.Model!.Series[0]).ItemsSource.Cast<HistogramChart.PlotBin>().ToArray();
        Check(green.Sum(b => b.Count) == 2 && green.Sum(b => b.Percent) == 100,
            "green percentage uses both green-site population");
        chart.Zoom = HistogramZoom.Custom; chart.CustomMinimum = "8"; chart.CustomMaximum = "22";
        chart.SetAnalysis(HistogramEngine.Analyze(new ushort[] { 100, 101 }, 2, 1), true);
        Check(chart.CustomMinimum == "8" && chart.CustomMaximum == "22" && chart.RangeText.Contains("8 – 22"),
            "Custom remains fixed across new image selection");
        Check(chart.OutputCeiling == "", "manual image range override does not leak to next image");
        var offscreen = chart.Model!.Annotations.OfType<LineAnnotation>().ToArray();
        Check(offscreen.Any(a => a.Text?.Contains("min 100") == true && a.Text.Contains("outside view") && a.X == 22) &&
              offscreen.Any(a => a.Text?.Contains("max 101") == true && a.X == 22), "offscreen extrema use edge indicators without changing Custom zoom");
        chart.LowEnabled = true; chart.LowThreshold = "101"; chart.LowTrigger = "2";
        chart.HighEnabled = true; chart.HighThreshold = "101"; chart.HighTrigger = "2";
        Check(chart.ThresholdText.Contains("WARNING ≤101: 2") && chart.ThresholdText.Contains("OK ≥101: 1"),
            "threshold warnings use inclusive boundaries outside visible zoom");
        chart.TriggerPercentage = true; chart.HighTrigger = "50";
        Check(chart.ThresholdText.Contains("WARNING ≥101: 1 (50%"), "percentage warning triggers at equality");
        chart.CustomMinimum = "bad";
        Check(chart.RangeText.Contains("Custom range requires"), "invalid custom input is explained without crashing");
        chart.CustomMinimum = "8"; chart.Zoom = HistogramZoom.Auto; chart.IncludeZero = true;
        Check(chart.RangeText.Contains("0 –"), "Include Zero expands Auto");
        var visibleModel = chart.Model; chart.SetVisible(false);
        chart.Percentage = false;
        Check(ReferenceEquals(visibleModel, chart.Model), "hidden chart defers rendering changes");
        chart.SetVisible(true);
        Check(!ReferenceEquals(visibleModel, chart.Model), "opening hidden chart rebuilds from cached results");
        chart.SetAnalysis(null, true);
        Check(chart.Model is null, "new pending/cleared selection removes old plot");
        Check(chart.Details.Count == 0, "new pending selection clears structured statistics");
        chart.Rgb = true; chart.Red = true; chart.Green = true; chart.Blue = true;
        chart.Logarithmic = false; chart.Percentage = false; chart.Zoom = HistogramZoom.FullScale;
        var demo = new ushort[200 * 200];
        var random = new Random(73);
        for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++) {
            double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            int center = y % 2 == 0 && x % 2 == 0 ? 900 : y % 2 == 1 && x % 2 == 1 ? 1400 : 1150;
            demo[y * 200 + x] = (ushort)Math.Clamp(center + normal * 75, 0, 65535);
        }
        chart.Zoom = HistogramZoom.Auto;
        chart.SetAnalysis(HistogramEngine.Analyze(demo, 200, 200, new(CfaPattern.RGGB)), true,
            context: new ImageStatisticsContext(200, 200, 16, 100, 256));
        Check(chart.Details.All(c => c.Values.All(v => !v.Label.Contains("ADU") && !new[] { "P1", "P10", "P90", "P99" }.Contains(v.Label))) &&
              chart.Details[0].Values.Any(v => v.Label == "Width" && v.Value == "200") &&
              chart.Details[0].Values.Any(v => v.Label == "Mean") && chart.Details[0].Values.Any(v => v.Label == "SD"),
              "NINA-style statistics contain dimensions mean SD and no ADU suffix or percentile rows");
        var plot = chart.Model!;
        // Exercise the actual OxyPlot renderer as well as inspecting series records.
        System.IO.Directory.CreateDirectory("artifacts");
        using (var preview = System.IO.File.Create("artifacts/stage3-chart-preview.png"))
            new OxyPlot.Wpf.PngExporter { Width = 1000, Height = 420 }.Export(plot, preview);
        Check(System.IO.File.Exists("artifacts/stage3-chart-preview.png"), "OxyPlot WPF chart renders to PNG");
        chart.Zoom = HistogramZoom.Custom; chart.CustomMinimum = "800"; chart.CustomMaximum = "1400";
        using (var edgePreview = System.IO.File.Create("artifacts/stage3-edge-markers.png"))
            new OxyPlot.Wpf.PngExporter { Width = 1000, Height = 420 }.Export(chart.Model!, edgePreview);
        chart.Zoom = HistogramZoom.Auto;
        ThemeChecks(chart);
        DetailsPreview(chart);
    }
    private static void DetailsPreview(HistogramChart chart) {
        var app = System.Windows.Application.Current;
        app.Resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 31, 32));
        app.Resources["PrimaryBrush"] = System.Windows.Media.Brushes.WhiteSmoke;
        chart.SetVisible(true); chart.Rgb = true;
        var dictionary = new Templates();
        var template = (System.Windows.DataTemplate)dictionary[new System.Windows.DataTemplateKey(typeof(InspectorPanel))];
        var root = (System.Windows.FrameworkElement)template.LoadContent();
        root.DataContext = new { Chart = chart, AnalysisStatus = "Ready", Diagnostics = "" };
        var detail = Logical(root).OfType<System.Windows.Controls.Expander>().First(e => (string)e.Header == "Detailed statistics");
        detail.IsExpanded = true;
        root.Measure(new System.Windows.Size(1000, 1500)); root.Arrange(new System.Windows.Rect(0, 0, 1000, 1500)); root.UpdateLayout();
        Check(Logical(detail).OfType<System.Windows.Controls.ItemsControl>().First().Items.Count == 3,
            "detailed statistics template renders all active channel sections");
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        var content = detail.Content; detail.Content = null;
        var preview = new System.Windows.Controls.ContentControl { Content = content, DataContext = root.DataContext,
            Foreground = System.Windows.Media.Brushes.WhiteSmoke,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 31, 32)) };
        preview.Measure(new System.Windows.Size(1000, double.PositiveInfinity));
        preview.Arrange(new System.Windows.Rect(0, 0, 1000, preview.DesiredSize.Height)); preview.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000,
            (int)Math.Ceiling(preview.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(preview);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create("artifacts/stage3-statistics-preview.png"); encoder.Save(output);
    }
    private static void ThemeChecks(HistogramChart chart) {
        var app = System.Windows.Application.Current;
        var dark = System.Windows.Media.Color.FromRgb(30, 31, 32);
        app.Resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(dark);
        app.Resources["PrimaryBrush"] = System.Windows.Media.Brushes.WhiteSmoke;
        // Reproduce a host switch style that never displays CheckBox.Content.
        var switches = new System.Windows.Style(typeof(System.Windows.Controls.CheckBox));
        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.CheckBox));
        var text = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        text.SetValue(System.Windows.Controls.TextBlock.TextProperty, "OFF");
        text.SetValue(System.Windows.Controls.TextBlock.ForegroundProperty, System.Windows.Media.Brushes.WhiteSmoke);
        template.VisualTree = text;
        switches.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.TemplateProperty, template));
        app.Resources[typeof(System.Windows.Controls.CheckBox)] = switches;
        var hostPlotStyle = new System.Windows.Style(typeof(OxyPlot.Wpf.PlotView));
        hostPlotStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.White));
        app.Resources[typeof(OxyPlot.Wpf.PlotView)] = hostPlotStyle;
        var view = new ChartView { DataContext = chart, Width = 1100 };
        chart.Rgb = false; chart.SetVisible(true);
        view.Measure(new System.Windows.Size(1100, 1000));
        view.Arrange(new System.Windows.Rect(0, 0, 1100, 1000)); view.UpdateLayout();
        chart.SetVisible(true);
        var plotView = (OxyPlot.Wpf.PlotView)view.FindName("HistogramPlot");
        Check(plotView.Background is System.Windows.Media.SolidColorBrush b && b.Color == dark &&
              chart.Model!.Background == OxyPlot.OxyColor.FromRgb(30, 31, 32), "host white PlotView style overridden by active dark theme");
        var labels = Logical(view).OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToArray();
        Check(new[] { "Include zero (Auto)", "Log Y", "Percentage (otherwise counts)", "CFA RGB", "Red", "Green (both sites)", "Blue" }
              .All(label => labels.Contains(label)), "all switch labels remain separate from host content-hiding template");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1100, 1000, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = System.IO.File.Create("artifacts/stage3-themed-controls.png")) encoder.Save(output);
        app.Resources["BackgroundBrush"] = System.Windows.Media.Brushes.White;
        app.Resources["PrimaryBrush"] = System.Windows.Media.Brushes.Black;
        // Detached test controls have no presentation source to receive Application resource invalidation.
        // Change their local resource scope to exercise the same DynamicResource/property-change route.
        view.Resources["BackgroundBrush"] = System.Windows.Media.Brushes.White;
        view.Resources["PrimaryBrush"] = System.Windows.Media.Brushes.Black;
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        view.UpdateLayout(); chart.SetVisible(true);
        Check(chart.Model!.Background == OxyPlot.OxyColors.White && chart.Model.TextColor == OxyPlot.OxyColors.Black &&
              ((LineSeries)chart.Model.Series[0]).Color == OxyPlot.OxyColors.Black, "live theme changes update model background, axes text and combined curve");
        app.Resources.Remove(typeof(System.Windows.Controls.CheckBox));
        app.Resources.Remove(typeof(OxyPlot.Wpf.PlotView));
        System.Windows.Data.BindingOperations.ClearBinding(plotView, OxyPlot.Wpf.PlotView.ModelProperty);
        plotView.Model = null;
        view.DataContext = null;
    }
    private static IEnumerable<System.Windows.DependencyObject> Logical(System.Windows.DependencyObject root) {
        yield return root;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
            foreach (var descendant in Logical(child)) yield return descendant;
    }
    private static void Check(bool ok, string label) {
        if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label);
    }
}
