namespace GpuMonitor;

public sealed class GpuPanelSettings
{
    public double SurfaceWidth { get; set; }
    public double SurfaceHeight { get; set; }
    public bool FollowGpuInventory { get; set; }
    public List<WorkspaceSeries>? Sensors { get; set; }
    public string Sensor { get; set; } = "utilization.gpu";
    public bool Lanes { get; set; }
    public bool Area { get; set; } = true;
    public double Height { get; set; } = 128;
    public int Minutes { get; set; } = 5;
    public bool Processes { get; set; }
    public void Normalize()
    {
        SurfaceWidth = double.IsFinite(SurfaceWidth) && SurfaceWidth > 0 ? Math.Clamp(SurfaceWidth, 320, 2000) : 0;
        SurfaceHeight = double.IsFinite(SurfaceHeight) && SurfaceHeight > 0 ? Math.Clamp(SurfaceHeight, 180, 1600) : 0;
        if (Sensors != null) {
            try { WorkspacePresetStore.Validate(new(1, "GPU panel", [new("gpu", "GPU", 1, 1, "line", Sensors)])); }
            catch (Exception error) when (error is FormatException or NullReferenceException) { Sensors = null; }
        }
        if (Lanes && Sensors?.Count > 8) Lanes = false;
        if (!Metrics.All.Any(m => m.Key == Sensor)) Sensor = "utilization.gpu";
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 100, 800) : 128;
        if (!new[] { 1, 5, 15, 30 }.Contains(Minutes)) Minutes = 5;
    }
}
