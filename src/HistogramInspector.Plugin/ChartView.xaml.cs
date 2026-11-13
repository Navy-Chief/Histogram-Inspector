using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;
namespace HistogramInspector.Plugin;
public partial class ChartView : UserControl {
    public ChartView() {
        InitializeComponent();
        IsVisibleChanged += (_, _) => UpdateVisibility();
        DataContextChanged += (_, _) => { UpdateTheme(); UpdateVisibility(); };
        Loaded += (_, _) => UpdateVisibility();
        Unloaded += (_, _) => { if (DataContext is HistogramChart chart) chart.SetVisible(false); };
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty || e.Property == ForegroundProperty) UpdateTheme();
    }
    private void UpdateTheme() {
        if (DataContext is not HistogramChart chart) return;
        static OxyColor ColorOf(Brush brush, OxyColor fallback) => brush is SolidColorBrush b
            ? OxyColor.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B) : fallback;
        chart.SetTheme(ColorOf(Background, OxyColor.FromRgb(30, 30, 30)), ColorOf(Foreground, OxyColors.WhiteSmoke));
    }
    private void UpdateVisibility() { if (DataContext is HistogramChart chart) chart.SetVisible(IsVisible); }
    private void PlotSizeChanged(object sender, SizeChangedEventArgs e) {
        if (DataContext is HistogramChart chart) chart.SetWidth(e.NewSize.Width);
    }
}
