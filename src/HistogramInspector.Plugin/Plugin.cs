using System.ComponentModel.Composition;
using System.Reflection;
using System.Runtime.InteropServices;
using NINA.Plugin;
using NINA.Plugin.Interfaces;

[assembly: Guid("d8dc4f70-90f1-4628-b385-7e4a1d47c2ad")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("Repository", "https://github.com/Navy-Chief/Histogram-Inspector")]
[assembly: AssemblyMetadata("Tags", "histogram,diagnostics,raw")]

namespace HistogramInspector.Plugin;

[Export(typeof(IPluginManifest))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class Plugin : PluginBase {
    private readonly InspectorPanel panel;
    [ImportingConstructor]
    public Plugin(InspectorPanel panel) => this.panel = panel;
    public override Task Teardown() => panel.StopAsync();
}
