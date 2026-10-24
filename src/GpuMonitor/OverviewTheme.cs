using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;

namespace GpuMonitor;

internal static class OverviewTheme
{
    public static TextBlock DialogHeading(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        { CaptionHeight = 0, ResizeBorderThickness = new(6), GlassFrameThickness = new(0), CornerRadius = new(0) });
        var heading = Text(window.Title, 20); heading.Margin = new(0, 0, 0, 14);
        heading.MouseLeftButtonDown += (_, _) => window.DragMove(); return heading;
    }
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public static readonly Brush Canvas = Brush("#10171D"), Surface = Brush("#171E27"), Border = Brush("#262E37"),
        Muted = Brush("#A5AEB7"), Green = Brush("#33DF32"), Blue = Brush("#2B82F8");
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    { Child = child, Background = Surface, BorderBrush = Border, BorderThickness = new(1), CornerRadius = new(9), Padding = padding ?? new(16) };
    public static Button Command(string label, Action action, string? accessibleName = null)
    {
        var button = new Button { Content = label, Padding = new(12, 7, 12, 7), Margin = new(3),
            Background = Surface, Foreground = Brushes.White, BorderBrush = Border, BorderThickness = new(1), Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetName(button, accessibleName ?? label);
        return button;
    }
    public static MenuItem MenuCommand(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action(); return item;
    }
    public static ContextMenu Menu()
    {
        var menu = new ContextMenu { Background = Surface, Foreground = Brushes.White, BorderBrush = Border };
        menu.Resources[SystemColors.MenuBrushKey] = Surface;
        menu.Resources[SystemColors.MenuTextBrushKey] = Brushes.White;
        menu.Resources[SystemColors.ControlBrushKey] = Surface;
        menu.Resources[SystemColors.ControlTextBrushKey] = Brushes.White;
        return menu;
    }
    public static void PlotMenu(TelemetryPlot plot, string rawUnit, string title)
    {
        var menu = Menu();
        menu.Items.Add(MenuCommand("Export visible data as CSV…", () => Export("csv")));
        menu.Items.Add(MenuCommand("Export visible data as JSON…", () => Export("json")));
        plot.ContextMenu = menu;
        void Export(string extension)
        {
            var dialog = new SaveFileDialog { Filter = $"{extension.ToUpperInvariant()} files|*.{extension}", FileName = title + "." + extension };
            if (dialog.ShowDialog(Window.GetWindow(plot)) != true) return;
            try
            {
                if (extension == "csv")
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    PlotData.WriteCsv(writer, plot.VisibleSamples, rawUnit);
                }
                else File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(plot.VisibleSamples.Select(s => new
                { timestamp_utc = s.Time, source = s.Device.Source, device_id = s.Device.Id, device_name = s.Device.Name,
                    metric = s.MetricKey, unit = rawUnit, value = s.Value }), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { MessageBox.Show(Window.GetWindow(plot), error.Message, "Export failed"); }
        }
    }
}
