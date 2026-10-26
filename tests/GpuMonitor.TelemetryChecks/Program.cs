using GpuMonitor;

var time = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
string Row(int index, string id, string fan = "50") =>
    $"{index}, NVIDIA GeForce RTX 3090, GPU-{id}, 00000000:01:00.0, 24576, 30, 1024, 45, 120, {fan}, 1500, 9750";
string Fixture(int count) => string.Join('\n', Enumerable.Range(0, count).Select(i => Row(i, $"fixture-{i}")));
void Reject(string csv)
{
    bool rejected = false;
    try { CsvTelemetry.Parse(csv, time); } catch (FormatException) { rejected = true; }
    Check(rejected, "Reject malformed CSV: " + csv.Split('\n')[0]);
}

foreach (int count in new[] { 0, 1, 2, 4, 8 })
{
    var inventory = new Inventory();
    inventory.Apply(CsvTelemetry.Parse(Fixture(count), time));
    Check(inventory.Current.Count == count && inventory.History.Count == count,
        $"{count}-GPU fixture has exactly {count} devices and histories");
}
var readings = CsvTelemetry.Parse(Row(0, "A"), time);
Check(readings[0].Time == time && readings[0].Metrics["memory.total"] == 24576 &&
    readings[0].Metrics["utilization.gpu"] == 30 && readings[0].Metrics["memory.used"] == 1024 &&
    readings[0].Metrics["temperature.gpu"] == 45 && readings[0].Metrics["power.draw"] == 120 &&
    readings[0].Metrics["fan.speed"] == 50 && readings[0].Metrics["clocks.current.graphics"] == 1500 &&
    readings[0].Metrics["clocks.current.memory"] == 9750, "Exact numeric values, units and source timestamp survive parsing");
foreach (string unavailable in new[] { "[N/A]", "NaN", "Infinity", "-1", "" })
    Check(CsvTelemetry.Parse(Row(0, "A", unavailable), time)[0].Metrics["fan.speed"] == null,
        $"Unavailable/non-finite/negative fan value '{unavailable}' stays null");
Check(CsvTelemetry.Parse(Row(0, "A").Replace("NVIDIA GeForce RTX 3090", "\"NVIDIA, \"\"Test\"\" GPU\""), time)[0].Name ==
    "NVIDIA, \"Test\" GPU", "Quoted comma and escaped quotes survive parsing");
Reject("bad output");
Reject(Row(0, "A") + "\n" + Row(1, "A"));
Reject(Row(0, "A").Replace("NVIDIA GeForce RTX 3090", "\"Unclosed GPU"));

var active = new Inventory();
active.Apply(CsvTelemetry.Parse(Row(0, "A") + "\n" + Row(1, "B"), time));
active.Apply(CsvTelemetry.Parse(Row(0, "B") + "\n" + Row(1, "A") + "\n" + Row(2, "C"), time.AddSeconds(2)));
Check(active.History["GPU-A"].Count == 2 && active.Current[0].Id == "GPU-B", "Index reorder and addition preserve UUID history");
active.Apply(CsvTelemetry.Parse(Row(0, "C"), time.AddSeconds(4)));
Check(active.History.Count == 1 && active.Current.Single().Id == "GPU-C", "Removal drops only absent device histories");
for (int i = 0; i < 4000; i++) active.Apply(CsvTelemetry.Parse(Row(0, "C"), time.AddSeconds(i + 5)));
Check(active.History["GPU-C"].Count == 1801, "Thirty-minute history includes boundary and expires older samples");
var fast = new Inventory();
for (int i = 0; i < 4000; i++) fast.Apply(CsvTelemetry.Parse(Row(0, "A"), time.AddMilliseconds(i)));
Check(fast.History["GPU-A"].Count == 3600, "Fast sampling is capped at 3600 readings per GPU");
active.Apply([]);
Check(active.Current.Count == 0 && active.History.Count == 0, "Successful zero-device result clears active inventory");
TelemetryContractChecks.Run(Check);
PlotDataChecks.Run(Check);
await TelemetrySessionChecks.Run(Check);
await TelemetryEngineChecks.Run(Check);
await DashboardTelemetryChecks.Run(Check);
await WorkspaceLayoutChecks.Run(Check);
WorkspacePresentationChecks.Run(Check);
WorkspaceActionChecks.Run(Check);
await SystemCollectorChecks.Run(Check);
await CpuCoreChecks.Run(Check);
await ProcessCollectorChecks.Run(Check);
await RuntimeMetricsChecks.Run(Check);
await RuntimeTransportChecks.Run(Check);
await MetadataFeedChecks.Run(Check);
OtlpChecks.Run(Check);
await CollectorConfigurationChecks.Run(Check);
Console.WriteLine($"PASS: {passed} telemetry regression checks (no hardware or WPF required).");
