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
    public static readonly Brush Canvas = Gradient("#101A23", "#091923"), Surface = Gradient("#182630", "#10212D"), Border = Brush("#263B49"),
        Muted = Brush("#AFBFCD"), Green = Brush("#35E544"), Blue = Brush("#229DFF");
    public static Brush Gradient(string start, string end) => new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), new Point(0, 0), new Point(1, 1));
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Brush("#F0F4F8"), VerticalAlignment = VerticalAlignment.Center };
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    { Child = child, Background = Surface, BorderBrush = Border, BorderThickness = new(1), CornerRadius = new(8), Padding = padding ?? new(16) };
    public static Button Command(string label, Action action, string? accessibleName = null)
    {
        var button = new Button { Content = label, Padding = new(12, 7, 12, 7), Margin = new(3),
            Background = Surface, Foreground = Brushes.White, BorderBrush = Border, BorderThickness = new(1), Cursor = System.Windows.Input.Cursors.Hand };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
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
        menu.Items.Add(MenuCommand("Zoom in   Ctrl++", () => plot.Zoom(.5)));
        menu.Items.Add(MenuCommand("Zoom out   Ctrl+-", () => plot.Zoom(2)));
        menu.Items.Add(MenuCommand("Pan earlier   Ctrl+Left", () => plot.Pan(-.25)));
        menu.Items.Add(MenuCommand("Pan later   Ctrl+Right", () => plot.Pan(.25)));
        var pause = MenuCommand("Pause chart view   Ctrl+Space", () => { if (plot.IsViewPaused) plot.ResetView(); else plot.PauseView(); });
        pause.IsCheckable = true; menu.Items.Add(pause);
        menu.Items.Add(MenuCommand("Reset to live   Ctrl+Home", plot.ResetView));
        var visibility = new MenuItem { Header = "Visible series" }; menu.Items.Add(visibility);
        menu.Opened += (_, _) =>
        {
            pause.IsChecked = plot.IsViewPaused; visibility.Items.Clear();
            for (int index = 0; index < plot.SeriesCount; index++)
            {
                int selected = index;
                var item = MenuCommand(plot.SeriesNames[index], () => plot.ShowSeries(selected, !plot.IsSeriesVisible(selected)));
                item.IsCheckable = true; item.IsChecked = plot.IsSeriesVisible(index); visibility.Items.Add(item);
            }
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuCommand("Export chart as PNG…", () => Export("png")));
        menu.Items.Add(MenuCommand("Export visible data as CSV…", () => Export("csv")));
        menu.Items.Add(MenuCommand("Export visible data as JSON…", () => Export("json")));
        plot.ContextMenu = menu;
        void Export(string extension)
        {
            var dialog = new SaveFileDialog { Filter = $"{extension.ToUpperInvariant()} files|*.{extension}", FileName = title + "." + extension };
            if (dialog.ShowDialog(Window.GetWindow(plot)) != true) return;
            try
            {
                if (extension == "png") plot.SavePng(dialog.FileName);
                else if (extension == "csv")
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    PlotData.WriteCsv(writer, plot.ExportRows(rawUnit));
                }
                else File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(plot.ExportRows(rawUnit).Select(row => new
                { timestamp_utc = row.Sample.Time, source = row.Sample.Device.Source, device_id = row.Sample.Device.Id, device_name = row.Sample.Device.Name,
                    metric = row.Sample.MetricKey, unit = row.Unit, value = row.Sample.Value }), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { MessageBox.Show(Window.GetWindow(plot), error.Message, "Export failed"); }
        }
    }
}
