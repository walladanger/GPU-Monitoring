using System.IO;
namespace GpuMonitor;

public static class SmokeChecks
{
    public static void Run()
    {
        var inventory = new Inventory(); var time = DateTimeOffset.Now;
        string Row(int index, string id, string fan = "50") => $"{index}, NVIDIA GeForce RTX 3090, GPU-{id}, 00000000:01:00.0, 24576, 30, 1024, 45, 120, {fan}, 1500, 9750";
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        inventory.Apply(CsvTelemetry.Parse(Row(0, "A") + "\n" + Row(1, "B"), time));
        Check(inventory.Current.Count == 2, "Two cards must be discovered");
        inventory.Apply(CsvTelemetry.Parse(Row(0, "B") + "\n" + Row(1, "A") + "\n" + Row(2, "C", "[N/A]"), time.AddSeconds(2)));
        Check(inventory.Current.Count == 3 && inventory.History["GPU-A"].Count == 2, "Adding a third card and reordering must preserve UUID history");
        Check(inventory.Current[2].Metrics["fan.speed"] == null, "Unsupported sensors must remain unavailable");
        inventory.Apply(CsvTelemetry.Parse(Row(0, "C"), time.AddSeconds(4)));
        Check(inventory.History.Count == 1 && inventory.Current.Count == 1, "Removed GPUs must leave active inventory");
        var renamed = CsvTelemetry.Parse(Row(0, "C").Replace("NVIDIA GeForce RTX 3090", "\"NVIDIA, Test GPU\""), time);
        Check(renamed[0].Name == "NVIDIA, Test GPU", "Quoted GPU names must parse");
        bool malformed = false; try { CsvTelemetry.Parse("bad output", time); } catch (FormatException) { malformed = true; }
        Check(malformed, "Malformed output must fail without mutating inventory");
        inventory.Apply([]); Check(inventory.Current.Count == 0 && inventory.History.Count == 0, "Zero-GPU discovery must clear panels");
        for (int i = 0; i < 4000; i++) inventory.Apply(CsvTelemetry.Parse(Row(0, "A"), time.AddSeconds(i)));
        Check(inventory.History["GPU-A"].Count <= 1801, "History must expire after thirty minutes");
        var dashboard = new Dashboard(startMonitoring: false);
        try
        {
            dashboard.ApplyReadings(CsvTelemetry.Parse(Row(0, "A") + "\n" + Row(1, "B"), time)); dashboard.VerifyInventory(2);
            dashboard.ApplyReadings(CsvTelemetry.Parse(Row(0, "B") + "\n" + Row(1, "A") + "\n" + Row(2, "C", "[N/A]"), time)); dashboard.VerifyInventory(3);
            dashboard.ApplyReadings(CsvTelemetry.Parse(Row(0, "C"), time)); dashboard.VerifyInventory(1);
            dashboard.ApplyReadings([]); dashboard.VerifyInventory(0);
        }
        finally { dashboard.Close(); }
        var oldReadings = CsvTelemetry.Parse(Row(0, "A"), time.AddHours(-1)).ToArray();
        foreach (var metric in Metrics.All)
        {
            var chart = new HistoryChart(oldReadings, metric, 5, 24576) { Width = 400, Height = 65 };
            chart.Measure(new System.Windows.Size(400, 65)); chart.Arrange(new System.Windows.Rect(0, 0, 400, 65));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(400, 65, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(chart);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: inventory discovery, third GPU, UUID reorder, removal, unsupported sensor, quoted CSV, malformed output, zero GPUs, history expiry, native panel/filter changes, empty-window chart rendering");
    }
}
