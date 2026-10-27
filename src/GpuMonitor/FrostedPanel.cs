using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GpuMonitor;

/// <summary>Blur the backdrop and its feathered surround, never the foreground controls.</summary>
public sealed class FrostedPanel : Border
{
    public const double Halo = 72; // 3/4 inch at reference DPI; scales with the display.
    public FrameworkElement? Backdrop { get; set; }
    private readonly Rectangle backdrop = new() { IsHitTestVisible = false, Effect = new BlurEffect { Radius = 24 } };
    private readonly Grid surround = new() { IsHitTestVisible = false, Margin = new(-Halo) };
    private bool wrapped;
    private Size maskSize;

    public FrostedPanel()
    {
        CornerRadius = new(12); BorderThickness = new(0); BorderBrush = Brushes.Transparent;
        Loaded += (_, _) => {
            if (!wrapped) {
                wrapped = true; var content = Child; Child = null; var padding = Padding; Padding = new(0);
                var layers = new Grid();
                surround.Children.Add(backdrop);
                surround.Children.Add(new Border { Background = OverviewTheme.PopupHalo });
                layers.Children.Add(surround);
                layers.Children.Add(new Border { CornerRadius = new(12), Background = OverviewTheme.PopupSurface, Padding = padding, Child = content });
                Child = layers;
            }
            RefreshBackdrop();
        };
        SizeChanged += (_, _) => RefreshBackdrop();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) RefreshBackdrop(); };
        Unloaded += (_, _) => backdrop.Fill = null;
    }

    internal void RefreshBackdrop()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        double width = ActualWidth + Halo * 2, height = ActualHeight + Halo * 2;
        try {
            // Explicit chart/owner samples stay live. Popup HWNDs sample the desktop
            // before their first render, including areas beyond the owner window.
            if (Backdrop is { IsLoaded: true } source) {
                var origin = source.PointFromScreen(PointToScreen(new Point(-Halo, -Halo)));
                backdrop.Fill = new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(origin, new Size(width, height)), Stretch = Stretch.Fill };
            } else if (backdrop.Fill == null) {
                var dpi = VisualTreeHelper.GetDpi(this);
                var origin = PointToScreen(new Point(-Halo, -Halo));
                using var bitmap = new System.Drawing.Bitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY));
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, bitmap.Size);
                var handle = bitmap.GetHbitmap();
                try {
                    var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze(); backdrop.Fill = new ImageBrush(image) { Stretch = Stretch.Fill };
                } finally { DeleteObject(handle); }
            }
            if (maskSize == new Size(width, height)) return;
            var shape = new Border { Width = width, Height = height, Background = Brushes.Transparent,
                Child = new Border { Margin = new(Halo), CornerRadius = new(12), Background = Brushes.White,
                    Effect = new BlurEffect { Radius = 64 } } };
            shape.Measure(new Size(width, height)); shape.Arrange(new Rect(0, 0, width, height));
            var mask = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            mask.Render(shape);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) {
                drawing.DrawImage(mask, new Rect(0, 0, width, height));
                drawing.DrawRoundedRectangle(Brushes.White, null, new Rect(Halo, Halo, ActualWidth, ActualHeight), 12, 12);
            }
            var combined = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            combined.Render(visual); combined.Freeze(); surround.OpacityMask = new ImageBrush(combined);
            maskSize = new Size(width, height);
        } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException) {
            DevelopmentLog.Write("popup-backdrop", error.Message);
        }
    }

    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
