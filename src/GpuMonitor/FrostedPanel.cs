using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GpuMonitor;

/// <summary>Blur only a sampled backdrop; keep foreground text and controls sharp.</summary>
public sealed class FrostedPanel : Border
{
    public FrameworkElement? Backdrop { get; set; }
    private readonly Rectangle backdrop = new() { IsHitTestVisible = false, Effect = new BlurEffect { Radius = 10 } };
    private bool wrapped;
    public FrostedPanel()
    {
        CornerRadius = new(12); BorderThickness = new(1); BorderBrush = OverviewTheme.Brush("#668FAABC");
        Loaded += (_, _) => {
            if (!wrapped) {
                wrapped = true; var content = Child; Child = null;
                var layers = new Grid { ClipToBounds = true }; layers.Children.Add(backdrop);
                layers.Children.Add(new Border { Background = OverviewTheme.Gradient("#DE213B50", "#D510202E"), IsHitTestVisible = false });
                if (content != null) layers.Children.Add(content); Child = layers;
                layers.SizeChanged += (_, _) => layers.Clip = new RectangleGeometry(new Rect(layers.RenderSize), 11, 11);
            }
            _ = Dispatcher.BeginInvoke(new Action(RefreshBackdrop));
        };
        SizeChanged += (_, _) => RefreshBackdrop();
    }
    internal void RefreshBackdrop()
    {
        var source = Backdrop ?? System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)?.Content as FrameworkElement
            ?? System.Windows.Application.Current?.MainWindow?.Content as FrameworkElement;
        if (source == null || source == this || !source.IsLoaded || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        try {
            var origin = source.PointFromScreen(PointToScreen(new Point()));
            backdrop.Fill = new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(origin, new Size(ActualWidth, ActualHeight)), Stretch = Stretch.Fill };
        } catch (InvalidOperationException) { }
    }
}
