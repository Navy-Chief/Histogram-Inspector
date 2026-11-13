using System.ComponentModel.Composition;
using System.Windows;
namespace HistogramInspector.Plugin;
[Export(typeof(ResourceDictionary))]
public partial class Templates : ResourceDictionary {
    public Templates() => InitializeComponent();
}
