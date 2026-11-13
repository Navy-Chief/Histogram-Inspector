using System.ComponentModel;
using System.IO;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;
using HistogramInspector.Plugin;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Plugin.Interfaces;

internal static class Program {
 [STAThread]
 static void Main(string[] args) {
  var host = args.Length > 0 ? args[0] : @"C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy";
  AssemblyLoadContext.Default.Resolving += (_, name) => {
   var path = System.IO.Path.Combine(host, name.Name + ".dll");
   return System.IO.File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
  };
  Run(args.Length > 1 ? args[1] : null);
 }
 [MethodImpl(MethodImplOptions.NoInlining)]
 static void Run(string? fixture) {
  var app = new Application();
  var source = DispatchProxy.Create<IImageControlVM, Fake>();
  var fake = (Fake)(object)source;
  using var catalog = new AssemblyCatalog(typeof(InspectorPanel).Assembly);
  using var container = new CompositionContainer(catalog);
  container.ComposeExportedValue(source);
  // N.I.N.A. loads plugin exports on a worker, then binds dockables on the UI thread.
  var exports = Task.Run(() => (
   Manifest: container.GetExportedValue<IPluginManifest>() ?? throw new Exception("Manifest missing"),
   Panel: container.GetExportedValue<IDockableVM>() as InspectorPanel ?? throw new Exception("Panel missing"),
   Template: container.GetExportedValue<ResourceDictionary>() ?? throw new Exception("Template missing")
  )).GetAwaiter().GetResult();
  var (manifest, panel, template) = exports;
  Check(panel.ImageGeometry.IsFrozen && panel.ImageGeometry.CheckAccess(), "worker-created icon is usable on UI thread");
  var icon = new System.Windows.Shapes.Path { DataContext = panel };
  icon.SetBinding(System.Windows.Shapes.Path.DataProperty, new System.Windows.Data.Binding(nameof(InspectorPanel.ImageGeometry)));
  icon.Measure(new Size(32, 32)); icon.Arrange(new Rect(0, 0, 32, 32)); Pump();
  Check(ReferenceEquals(icon.Data, panel.ImageGeometry), "dock icon binding after background composition");
  Check(template.Contains(new DataTemplateKey(typeof(InspectorPanel))), "MEF exports and WPF data template compose");
  var panelTemplate = (DataTemplate)template[new DataTemplateKey(typeof(InspectorPanel))];
  var view = (FrameworkElement)panelTemplate.LoadContent();
  view.DataContext = panel;
  view.Measure(new Size(640, 480)); view.Arrange(new Rect(0, 0, 640, 480)); Pump();
  Check(view.ActualWidth == 640, "worker-created panel template renders on UI dispatcher");
  var options = (DataTemplate)template[manifest.Name + "_Options"];
  var optionsView = (FrameworkElement)options.LoadContent();
  optionsView.Measure(new Size(640, 480)); optionsView.Arrange(new Rect(0, 0, 640, 480)); Pump();
  Check(optionsView.ActualWidth > 0 && optionsView is System.Windows.Controls.StackPanel { Children.Count: 3 }, "plugin options template renders on UI dispatcher");
  var a = Raw(); var b = Raw();
  void Select(IImageData? raw) {
   var rendered = raw is null ? null : DispatchProxy.Create<IRenderedImage, Fake>();
   if (rendered is not null) ((Fake)(object)rendered).Values["RawImageData"] = raw;
   fake.Values["RenderedImage"] = rendered;
   fake!.Changed?.Invoke(source, new(nameof(IImageControlVM.RenderedImage)));
  }
  Select(a); Pump();
  Check(panel.Diagnostics.Contains("Int32[]") && panel.Diagnostics.Contains("Distinct selections: 1"), "integer representation without ushort conversion");
  Check(panel.Diagnostics.Contains("Actual output ceiling: Unknown") && panel.Diagnostics.Contains("Exposure (s): Unknown"), "unknown range and metadata remain explicit");
  Select(a); Pump();
  Check(panel.Diagnostics.Contains("Distinct selections: 1"), "re-render does not advance identity");
  panel.IsVisible = false; Select(b); Pump(); panel.IsVisible = true;
  Check(panel.Diagnostics.Contains("Distinct selections: 2"), "hidden panel continues tracking");
  Select(a); Pump(); Check(panel.Diagnostics.Contains("Distinct selections: 3"), "historical selection advances identity");
  Select(b); Select(null); Pump();
  Check(panel.Diagnostics.StartsWith("No selected image."), "latest clear supersedes queued result");
  Select(a); manifest.Teardown().GetAwaiter().GetResult(); Pump();
  Check(fake!.Changed is null, "teardown unsubscribes host event");
  Check(panel.Diagnostics.StartsWith("No selected image."), "teardown prevents queued publication");
  using var restarted = new InspectorPanel(source);
  Pump();
  Check(restarted.Diagnostics.Contains("Distinct selections: 1"), "startup reads image already selected before subscription");
  Select(Raw(unsigned: true)); Pump();
  Check(restarted.Diagnostics.Contains("UInt16[]") && restarted.Diagnostics.Contains("Samples: 4; expected: 4"), "unsigned array representation and dimensions");
  var cfa = Raw(unsigned: true);
  cfa.MetaData.Camera.SensorType = NINA.Core.Enum.SensorType.RGGB;
  cfa.MetaData.Camera.BayerOffsetX = 1;
  cfa.MetaData.Camera.BayerOffsetY = 0;
  Select(cfa); Pump();
  Check(restarted.Diagnostics.Contains("N.I.N.A. Bayered flag: False") &&
        restarted.Diagnostics.Contains("Per-image sensor/pattern metadata: RGGB"), "CFA metadata remains visible with false rendering flag");
  Check(restarted.Diagnostics.Contains("Per-image Bayer selection: Auto") &&
        restarted.Diagnostics.Contains("Per-image Bayer offsets X/Y: 1 / 0"), "unresolved Bayer selection and per-image offsets remain separate");
  Select(Raw()); Pump();
  Check(restarted.Diagnostics.Contains("Per-image sensor/pattern metadata: Monochrome") &&
        !restarted.Diagnostics.Contains("metadata: RGGB"), "new selection does not retain previous CFA pattern");
  var reusedRaw = Raw();
  for (int capture = 100; capture < 104; capture++) {
   reusedRaw.MetaData.Image.Id = capture;
   reusedRaw.MetaData.Image.ExposureTime = capture - 99;
   reusedRaw.MetaData.Image.ImageType = "LIGHT";
   Select(reusedRaw); Pump();
   Check(restarted.Diagnostics.Contains($"N.I.N.A. image ID: {capture}") &&
         restarted.Diagnostics.Contains($"Exposure (s): {capture - 99}"),
         $"reused simulator raw object publishes capture {capture}");
  }
  var beforeRestretch = restarted.Diagnostics;
  Select(reusedRaw); Pump();
  Check(restarted.Diagnostics == beforeRestretch, "re-stretch after reused-buffer sequence remains deduplicated");
  var loaded = Raw();
  loaded.MetaData.GenericHeaders.Add(new StringMetaDataHeader("IMAGETYP", "'LIGHT'"));
  loaded.MetaData.GenericHeaders.Add(new IntMetaDataHeader("XBINNING", 1));
  loaded.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("YBINNING", 1));
  Select(loaded); Pump();
  Check(restarted.Diagnostics.Contains("Image type: LIGHT (per-image header)") &&
        restarted.Diagnostics.Contains("Binning: 1x1 (per-image headers)"), "reopened image metadata recovered from per-image headers");
  loaded.MetaData.Image.ImageType = "SNAPSHOT";
  loaded.MetaData.Image.Binning = "2x2";
  loaded.MetaData.Image.Id = 200;
  Select(loaded); Pump();
  Check(restarted.Diagnostics.Contains("Image type: SNAPSHOT") && restarted.Diagnostics.Contains("Binning: 2x2"),
        "explicit capture context takes precedence over retained file headers");
  var missing = Raw();
  missing.MetaData.GenericHeaders.Add(new StringMetaDataHeader("XBINNING", "0"));
  missing.MetaData.GenericHeaders.Add(new StringMetaDataHeader("YBINNING", "invalid"));
  Select(missing); Pump();
  Check(restarted.Diagnostics.Contains("Binning: Unknown") && restarted.Diagnostics.Contains("Image type: Unknown"),
        "missing type and invalid header binning remain unknown");
  WaitUntil(() => restarted.AnalysisText.StartsWith("Unsupported"));
  Check(restarted.AnalysisText.Contains("Below 0: 1; above 65535: 1"), "worker rejects integer range without clamping");
  restarted.IsVisible = false;
  Select(Raw(unsigned: true));
  WaitUntil(() => restarted.AnalysisText.StartsWith("Ready"));
  Check(restarted.CurrentAnalysis?.Combined?.Median == 8192 && restarted.AnalysisText.Contains("median=8192 ADU"),
        "hidden panel receives exact worker statistics and caches immutable result");
  var readyBeforeStretch = restarted.CurrentAnalysis;
  fake.Changed?.Invoke(source, new(nameof(IImageControlVM.RenderedImage))); Pump();
  Check(ReferenceEquals(readyBeforeStretch, restarted.CurrentAnalysis), "same selection re-stretch does not repeat analysis");
  var rawCfa = Raw(unsigned: true);
  ((Fake)(object)rawCfa).Values["Properties"] = new ImageProperties(2, 2, 16, true, -1, -1);
  rawCfa.MetaData.Camera.SensorType = NINA.Core.Enum.SensorType.RGGB;
  rawCfa.MetaData.Image.Binning = "1x1";
  Select(rawCfa); WaitUntil(() => restarted.AnalysisText.StartsWith("Ready"));
  Check(restarted.CurrentAnalysis?.Red?.SampleCount == 1 && restarted.CurrentAnalysis.Green?.SampleCount == 2 &&
        restarted.AnalysisText.Contains("Green (both sites)"), "eligible per-image CFA context reaches worker and display");
  rawCfa.MetaData.Image.Binning = "2x2"; rawCfa.MetaData.Image.Id = 300;
  Select(rawCfa); WaitUntil(() => restarted.AnalysisText.StartsWith("Ready"));
  Check(restarted.CurrentAnalysis?.Red is null && restarted.AnalysisText.Contains("not verified 1x1"),
        "explicit binned context disables worker CFA splitting");
  var malformed = Raw(unsigned: true);
  ((Fake)(object)malformed).Values["Properties"] = new ImageProperties(3, 3, 16, false, -1, -1);
  Select(malformed); WaitUntil(() => restarted.AnalysisText.StartsWith("Error"));
  Check(restarted.CurrentAnalysis is null, "worker failure clears prior statistics");
  // Supersede a large scan immediately, without giving the UI dispatcher time to publish it.
  var large = Raw(unsigned: true);
  ((Fake)(object)large).Values["Properties"] = new ImageProperties(4000, 4000, 16, false, -1, -1);
  ((Fake)(object)large).Values["Data"] = new ImageArray(new ushort[16_000_000]);
  Select(large); Select(null); Pump();
  Check(restarted.AnalysisText == "No selected image." && restarted.CurrentAnalysis is null,
        "clear invalidates pending and active histogram publication");
  for (int i = 0; i < 25; i++) Select(Raw(unsigned: true));
  Select(rawCfa); WaitUntil(() => restarted.AnalysisText.StartsWith("Ready"));
  Check(restarted.Diagnostics.Contains("N.I.N.A. image ID: 300") && restarted.AnalysisText.Contains("Skipped analyses:"),
        "rapid arrival coalescing publishes only latest image context");
  if (fixture is not null) {
   using var file = new BinaryReader(System.IO.File.OpenRead(fixture));
   var signature = System.Text.Encoding.ASCII.GetString(file.ReadBytes(8));
   if (signature != "XISF0100") throw new Exception("Expected XISF fixture");
   int headerSize = file.ReadInt32(); file.ReadInt32();
   if (headerSize <= 0 || headerSize > 1048576) throw new Exception("Invalid fixture header");
   var header = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(file.ReadBytes(headerSize)));
   var image = header.Descendants().Single(e => e.Name.LocalName == "Image");
   var dimensions = image.Attribute("geometry")!.Value.Split(':').Select(int.Parse).ToArray();
   if (dimensions.Length != 3 || dimensions[2] != 1 || image.Attribute("compression") is not null ||
       image.Attribute("sampleFormat")?.Value != "UInt16") throw new Exception("Unsupported test fixture");
   var attachment = image.Attribute("location")!.Value.Split(':');
   file.BaseStream.Position = long.Parse(attachment[1]);
   var bytes = file.ReadBytes(int.Parse(attachment[2]));
   if ("sha-256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() !=
       image.Attribute("checksum")!.Value) throw new Exception("Fixture checksum mismatch");
   var pixels = new ushort[bytes.Length / 2]; Buffer.BlockCopy(bytes, 0, pixels, 0, bytes.Length);
   var real = Raw(unsigned: true);
   ((Fake)(object)real).Values["Data"] = new ImageArray(pixels);
   ((Fake)(object)real).Values["Properties"] = new ImageProperties(dimensions[0], dimensions[1], 16, true, -1, -1);
   real.MetaData.Camera.SensorType = NINA.Core.Enum.SensorType.RGGB;
   real.MetaData.Image.Binning = "1x1";
   Select(real); WaitUntil(() => restarted.AnalysisText.StartsWith("Ready"));
   var combined = restarted.CurrentAnalysis!.Combined!;
   Check(combined.SampleCount == 25_941_632 && combined.Median == 600 && combined.Mad == 66,
         "real source XISF worker result matches offline median/MAD reference");
   Check(restarted.CurrentAnalysis.Red?.SampleCount == 6_485_408 &&
         restarted.CurrentAnalysis.Green?.SampleCount == 12_970_816 &&
         restarted.CurrentAnalysis.Blue?.SampleCount == 6_485_408,
         "real source XISF worker CFA populations match reference");
  }
  restarted.IsVisible = true;
  Select(large); restarted.StopAsync().GetAwaiter().GetResult(); Pump();
  var stoppedText = restarted.AnalysisText;
  Check(fake.Changed is null, "worker shutdown completes and unsubscribes with active input");
  Pump(); Check(restarted.AnalysisText == stoppedText, "worker cannot publish after shutdown");
  restarted.Dispose();
  var profileService = DispatchProxy.Create<NINA.Profile.Interfaces.IProfileService, Fake>();
  var profile = DispatchProxy.Create<NINA.Profile.Interfaces.IProfile, Fake>();
  var settings = DispatchProxy.Create<NINA.Profile.Interfaces.ICameraSettings, Fake>();
  ((Fake)(object)settings).Values["BitDepth"] = 12.0;
  ((Fake)(object)profile).Values["CameraSettings"] = settings;
  ((Fake)(object)profileService).Values["ActiveProfile"] = profile;
  using (var profilePanel = new InspectorPanel(source, profileService)) {
   profilePanel.Chart.SetVisible(true); profilePanel.Chart.Zoom = HistogramInspector.Plugin.HistogramZoom.FullScale;
   var captured = Raw(unsigned: true); captured.MetaData.Image.Id = 401;
   Select(captured); WaitUntil(() => profilePanel.AnalysisText.StartsWith("Ready"));
   Check(profilePanel.Chart.RangeText.Contains("0–4,095") && profilePanel.Chart.RangeText.Contains("Options > Equipment"),
         "live capture snapshots configured camera bit depth for Full Scale");
   ((Fake)(object)settings).Values["BitDepth"] = 14.0;
   Select(captured); Pump();
   Check(profilePanel.Chart.RangeText.Contains("0–4,095"), "profile changes do not reinterpret same displayed capture");
   Select(Raw(unsigned: true)); WaitUntil(() => profilePanel.AnalysisText.StartsWith("Ready"));
   Check(profilePanel.Chart.RangeText.Contains("0–16,383") && profilePanel.Chart.RangeText.Contains("per-image reported depth"),
         "history image uses own depth rather than current camera profile");
   var stars = new StarDetectionAnalysis { DetectedStars = 48, HFR = 2.5, HFRStDev = 0.25 };
   var starImage = Raw(unsigned: true);
   ((Fake)(object)starImage).Values["StarDetectionAnalysis"] = stars;
   Select(starImage); WaitUntil(() => profilePanel.AnalysisText.StartsWith("Ready"));
   string Stat(string label) => profilePanel.Chart.Details[0].Values.Single(v => v.Label == label).Value;
   Check(Stat("#Stars") == "48" && Stat("HFR") == "2.50" && Stat("HFR SD") == "0.25",
         "selected-image public star analysis populates stars HFR and HFR SD");
   var beforeStarRefresh = profilePanel.CurrentAnalysis;
   var identityBefore = profilePanel.Diagnostics;
   stars.DetectedStars = 55; stars.HFR = 3.0; stars.HFRStDev = 0.5; Pump();
   Check(Stat("#Stars") == "55" && Stat("HFR") == "3.00" && Stat("HFR SD") == "0.50" &&
         ReferenceEquals(beforeStarRefresh, profilePanel.CurrentAnalysis) && profilePanel.Diagnostics == identityBefore,
         "late star analysis updates without histogram recomputation or advancing selection");
   var noStars = Raw(unsigned: true);
   ((Fake)(object)noStars).Values["StarDetectionAnalysis"] = new StarDetectionAnalysis();
   Select(noStars); WaitUntil(() => profilePanel.AnalysisText.StartsWith("Ready"));
   stars.DetectedStars = 999; Pump();
   Check(Stat("#Stars") == "Unknown" && Stat("HFR") == "Unknown" && Stat("HFR SD") == "Unknown",
         "unavailable analysis remains Unknown and previous image updates are ignored");
   profilePanel.StopAsync().GetAwaiter().GetResult();
  }
  ChartChecks.Run();
  app.Shutdown();
 }
 static IImageData Raw(bool unsigned = false) {
  var raw = DispatchProxy.Create<IImageData, Fake>(); var f = (Fake)(object)raw;
  f.Values["Properties"] = new ImageProperties(2, 2, 14, false, -1, -1);
  f.Values["MetaData"] = new ImageMetaData();
  f.Values["Data"] = unsigned ? new ImageArray(new ushort[] {0, 1, 16383, 65535}) : new IntOnly();
  return raw;
 }
 static void Pump() {
  var frame = new DispatcherFrame();
  Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
  Dispatcher.PushFrame(frame);
 }
 static void WaitUntil(Func<bool> condition) {
  var limit = DateTime.UtcNow.AddSeconds(10);
  do { Pump(); if (condition()) return; Thread.Sleep(1); } while (DateTime.UtcNow < limit);
  throw new Exception("Timed out awaiting worker result");
 }
 static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
}
public class Fake : DispatchProxy {
 public Dictionary<string, object?> Values { get; } = new();
 public PropertyChangedEventHandler? Changed;
 protected override object? Invoke(MethodInfo? method, object?[]? args) {
  if (method!.Name == "add_PropertyChanged") { Changed += (PropertyChangedEventHandler)args![0]!; return null; }
  if (method.Name == "remove_PropertyChanged") { Changed -= (PropertyChangedEventHandler)args![0]!; return null; }
  if (method.Name.StartsWith("get_")) return Values.GetValueOrDefault(method.Name[4..]);
  throw new NotSupportedException(method.Name);
 }
}
internal sealed class IntOnly : IImageArray {
 public int[] FlatArrayInt { get; } = [-1, 0, 16383, 70000];
 public ushort[] FlatArray => throw new Exception("Must not convert integer array");
 public byte[] RAWData => null!;
 public string RAWType => null!;
}
