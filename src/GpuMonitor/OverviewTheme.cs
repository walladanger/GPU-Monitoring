using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;

namespace GpuMonitor;

internal static class OverviewTheme
{
    internal static void InstallPopupStyles()
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources == null || resources.Contains(typeof(ToolTip))) return;
        InstallScrollBarStyles(resources);
        resources[typeof(ToolTip)] = (Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:local='clr-namespace:GpuMonitor;assembly=GpuMonitor' TargetType='ToolTip'>
 <Setter Property='Foreground' Value='White'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/>
 <Setter Property='HasDropShadow' Value='False'/><Setter Property='HorizontalOffset' Value='-72'/><Setter Property='VerticalOffset' Value='-72'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Border Padding='72'><local:FrostedPanel Padding='9'><ContentPresenter/></local:FrostedPanel></Border></ControlTemplate></Setter.Value></Setter>
</Style>");
    }
    private static void InstallScrollBarStyles(ResourceDictionary resources)
    {
        var styles = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='ScrollPage' TargetType='RepeatButton'>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScrollThumb' TargetType='Thumb'>
  <Setter Property='Background' Value='#436074'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'><Border Background='{TemplateBinding Background}' CornerRadius='4' Margin='3'/></ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#7399B2'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter Property='Background' Value='#229DFF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.35'/></Trigger></Style.Triggers>
 </Style>
 <Style TargetType='ScrollBar'>
  <Setter Property='Background' Value='#0A1822'/><Setter Property='Width' Value='12'/><Setter Property='MinHeight' Value='24'/>
  <Setter Property='Stylus.IsPressAndHoldEnabled' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Border Background='{TemplateBinding Background}'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'>
    <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb Style='{StaticResource ScrollThumb}' MinHeight='24'/></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
   </Track></Border>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'>
   <Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='12'/><Setter Property='MinHeight' Value='0'/><Setter Property='MinWidth' Value='24'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
    <Border Background='{TemplateBinding Background}'><Track x:Name='PART_Track' Orientation='Horizontal' IsDirectionReversed='False'>
     <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Style='{StaticResource ScrollThumb}' MinWidth='24'/></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
    </Track></Border>
   </ControlTemplate></Setter.Value></Setter>
  </Trigger></Style.Triggers>
 </Style>
 <Style TargetType='ScrollViewer'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Grid.RowDefinitions><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Margin='{TemplateBinding Padding}' CanContentScroll='{TemplateBinding CanContentScroll}' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}'/>
    <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
    <ScrollBar x:Name='PART_HorizontalScrollBar' Grid.Row='1' Orientation='Horizontal' Maximum='{TemplateBinding ScrollableWidth}' ViewportSize='{TemplateBinding ViewportWidth}' Value='{Binding HorizontalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}' Visibility='{TemplateBinding ComputedHorizontalScrollBarVisibility}'/>
    <Border Grid.Row='1' Grid.Column='1' Background='#0A1822'/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>");
        resources.MergedDictionaries.Add(styles);
    }
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
    public static readonly Brush PopupSurface = Gradient("#B806121E", "#CC030B15"), PopupHalo = Brush("#48030B15");
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.RegisterAttached(
        "IsSelected", typeof(bool), typeof(OverviewTheme), new PropertyMetadata(false));
    public static void SetSelected(Button button, bool selected) => button.SetValue(IsSelectedProperty, selected);
    public static void ApplyAccent(string hex)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources == null) return;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        resources["ActionAccent"] = new SolidColorBrush(color);
        resources["ActionSelection"] = new SolidColorBrush(color) { Opacity = .24 };
        resources["ActionGlow"] = new System.Windows.Media.Effects.DropShadowEffect
            { Color = color, BlurRadius = 5, ShadowDepth = 0, Opacity = .38 };
    }
    public static Brush Gradient(string start, string end) => new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), new Point(0, 0), new Point(1, 1));
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Brush("#F0F4F8"), VerticalAlignment = VerticalAlignment.Center };
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    { Child = child, Background = Surface, BorderBrush = Border, BorderThickness = new(1), CornerRadius = new(8), Padding = padding ?? new(16) };
    public static Button Command(string label, Action action, string? accessibleName = null)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources != null && !resources.Contains("ActionAccent")) ApplyAccent("#229DFF");
        var button = new Button { Content = label, Padding = new(12, 7, 12, 7), Margin = new(3),
            Background = Surface, Foreground = Brushes.White, BorderBrush = Border, BorderThickness = new(1), Cursor = System.Windows.Input.Cursors.Hand };
        var border = new FrameworkElementFactory(typeof(Border), "ActionBorder");
        border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(System.Windows.Controls.Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        foreach (var property in new[] { UIElement.IsMouseOverProperty, ButtonBase.IsPressedProperty, UIElement.IsKeyboardFocusedProperty, IsSelectedProperty }) {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, new DynamicResourceExtension("ActionAccent"), "ActionBorder"));
            trigger.Setters.Add(new Setter(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1), "ActionBorder"));
            trigger.Setters.Add(new Setter(UIElement.EffectProperty, new DynamicResourceExtension("ActionGlow")));
            template.Triggers.Add(trigger);
        }
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); template.Triggers.Add(disabled);
        button.Template = template;
        button.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetName(button, accessibleName ?? label);
        return button;
    }
    public static void Input(Control input)
    {
        input.Background = Surface; input.Foreground = Brushes.White; input.BorderBrush = Border; input.Padding = new(6);
        input.Resources[SystemColors.WindowBrushKey] = Surface; input.Resources[SystemColors.WindowTextBrushKey] = Brushes.White;
        input.Resources[SystemColors.ControlBrushKey] = Surface; input.Resources[SystemColors.ControlTextBrushKey] = Brushes.White;
        if (input is ComboBox combo) FlatCombo(combo);
    }
    private static void FlatCombo(ComboBox combo)
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(Control.BackgroundProperty, Surface); toggle.SetValue(Control.BorderBrushProperty, Border);
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        var chrome = new FrameworkElementFactory(typeof(Border)); chrome.SetValue(System.Windows.Controls.Border.BackgroundProperty, Surface);
        chrome.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Border); chrome.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1)); chrome.SetValue(FrameworkElement.MinHeightProperty, 32d);
        toggle.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton)) { VisualTree = chrome }); grid.AppendChild(toggle);
        var content = new FrameworkElementFactory(typeof(DockPanel)); content.SetValue(UIElement.IsHitTestVisibleProperty, false); content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5));
        var arrow = new FrameworkElementFactory(typeof(TextBlock)); arrow.SetValue(TextBlock.TextProperty, "▾"); arrow.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        arrow.SetValue(DockPanel.DockProperty, Dock.Right); arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0)); content.AppendChild(arrow);
        var selected = new FrameworkElementFactory(typeof(ContentPresenter)); selected.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White);
        selected.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        selected.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty)); content.AppendChild(selected); grid.AppendChild(content);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup"); popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom); popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
        var padding = new FrameworkElementFactory(typeof(Border)); padding.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(FrostedPanel.Halo));
        popup.SetValue(Popup.HorizontalOffsetProperty, -FrostedPanel.Halo); popup.SetValue(Popup.VerticalOffsetProperty, -FrostedPanel.Halo);
        var border = new FrameworkElementFactory(typeof(FrostedPanel));
        border.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White);
        border.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = new(RelativeSourceMode.TemplatedParent) });
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer)); scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetBinding(FrameworkElement.MaxHeightProperty, new Binding("MaxDropDownHeight") { RelativeSource = new(RelativeSourceMode.TemplatedParent) });
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); border.AppendChild(scroll); padding.AppendChild(border); popup.AppendChild(padding); grid.AppendChild(popup);
        combo.Template = new ControlTemplate(typeof(ComboBox)) { VisualTree = grid };
    }
    public static MenuItem MenuCommand(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action(); return item;
    }
    public static ContextMenu Menu()
    {
        var menu = new ContextMenu { Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new(0), HasDropShadow = false,
            HorizontalOffset = -FrostedPanel.Halo, VerticalOffset = -FrostedPanel.Halo };
        menu.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:local='clr-namespace:GpuMonitor;assembly=GpuMonitor' TargetType='ContextMenu'>
 <Border Padding='72'><local:FrostedPanel Padding='6'><ScrollViewer MaxHeight='650' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></local:FrostedPanel></Border>
</ControlTemplate>");
        var style = (Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:GpuMonitor;assembly=GpuMonitor' TargetType='MenuItem'>
 <Setter Property='Foreground' Value='White'/><Setter Property='Padding' Value='10,7'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
  <Grid><Border x:Name='Highlight' CornerRadius='7' Background='Transparent' Padding='{TemplateBinding Padding}'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
    <TextBlock x:Name='Check' Text='✓' Visibility='Collapsed'/><ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'/><TextBlock x:Name='Arrow' Grid.Column='2' Text='›' Margin='16,0,0,0' Visibility='Collapsed'/>
   </Grid></Border>
   <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-70' VerticalOffset='-72' AllowsTransparency='True' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' Focusable='False'>
    <Border Padding='72'><local:FrostedPanel Padding='6'><ScrollViewer MaxHeight='600' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></local:FrostedPanel></Border>
   </Popup>
  </Grid>
  <ControlTemplate.Triggers>
   <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Highlight' Property='Background' Value='{DynamicResource ActionSelection}'/><Setter TargetName='Highlight' Property='BorderBrush' Value='{DynamicResource ActionAccent}'/><Setter TargetName='Highlight' Property='BorderThickness' Value='1'/><Setter TargetName='Highlight' Property='Effect' Value='{DynamicResource ActionGlow}'/></Trigger>
   <Trigger Property='IsChecked' Value='True'><Setter TargetName='Check' Property='Visibility' Value='Visible'/><Setter TargetName='Highlight' Property='BorderBrush' Value='{DynamicResource ActionAccent}'/><Setter TargetName='Highlight' Property='BorderThickness' Value='1'/><Setter TargetName='Highlight' Property='Effect' Value='{DynamicResource ActionGlow}'/></Trigger>
   <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>");
        menu.Resources[typeof(MenuItem)] = style;
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
