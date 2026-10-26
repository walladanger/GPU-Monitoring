using GpuMonitor;

internal static class TelemetryContractChecks
{
    public static void Run(Action<bool, string> check)
    {
        var time = DateTimeOffset.Parse("2026-10-23T08:00:00-04:00");
        string Row(int index, string id, string fan = "50") =>
            $"{index}, NVIDIA GeForce RTX 3090, GPU-{id}, 00000000:01:00.0, 24576, 30, 1024, 45, 120, {fan}, 1500, 9750";
        foreach (int count in new[] { 0, 1, 2, 4, 8 })
        {
            var legacy = CsvTelemetry.Parse(string.Join('\n', Enumerable.Range(0, count).Select(i => Row(i, $"fixture-{i}"))), time);
            var batch = NvidiaTelemetry.Normalize(legacy);
            check(batch.Devices.Count == count && batch.Samples.Count == count * 8 && batch.Events.Count == 0,
                $"Normalized {count}-GPU fixture has only measured devices/metrics and no invented events");
            check(batch.Samples.All(s => s.Value == legacy.Single(g => g.Id == s.Device.Id).Metrics[s.MetricKey] && s.Time == time && s.Time.Offset == TimeSpan.Zero),
                $"Normalized {count}-GPU values exactly match legacy values and UTC instant");
        }
        var missing = NvidiaTelemetry.Normalize(CsvTelemetry.Parse(Row(0, "A", "[N/A]"), time));
        check(missing.Samples.Single(s => s.MetricKey == "fan.speed").Value == null &&
            missing.Capabilities.Single(c => c.MetricKey == "fan.speed").Availability == MetricAvailability.Unavailable,
            "Missing fan produces a gap and unavailable capability, not zero or unsupported claim");
        check(NvidiaTelemetry.Definitions.Single(m => m.Key == "memory.used").Unit == "MiB" &&
            NvidiaTelemetry.Definitions.Single(m => m.Key == "memory.total").Unit == "MiB" &&
            NvidiaTelemetry.Definitions.Single(m => m.Key == "utilization.gpu").Scale == 100,
            "Definitions retain NVIDIA units and percent scale");
        var before = NvidiaTelemetry.Normalize(CsvTelemetry.Parse(Row(0, "A"), time)).Devices[0];
        var after = NvidiaTelemetry.Normalize(CsvTelemetry.Parse(Row(1, "A"), time.AddSeconds(1))).Devices[0];
        var buffer = new TelemetrySeriesBuffer(3, TimeSpan.FromSeconds(5));
        TelemetrySample Sample(int second, double? value = 30) => new(before, "utilization.gpu", time.AddSeconds(second), value);
        check(buffer.Append(Sample(0)) && buffer.Append(new(after, "utilization.gpu", time.AddSeconds(1), 40)),
            "Stable UUID series accepts display-index changes");
        check(!buffer.Append(Sample(1)) && !buffer.Append(Sample(-1)) && buffer.Count == 2,
            "Duplicate and out-of-order samples are rejected without changing history");
        buffer.Append(Sample(2, null));
        var frozen = buffer.Snapshot();
        buffer.Append(Sample(3, 60));
        check(buffer.Count == 3 && buffer.Snapshot()[0].Time == time.AddSeconds(1), "Ring buffer evicts oldest at capacity");
        check(buffer.Snapshot()[1].Value == null, "Explicit unavailable sample remains a chart gap");
        check(frozen[0].Time == time && frozen.Count == 3, "Snapshot remains stable after later appends");
        buffer.Append(Sample(8));
        check(buffer.Count == 2 && buffer.Snapshot()[0].Time == time.AddSeconds(3), "Retention keeps exact boundary and expires older data");
        buffer.Prune(time.AddSeconds(14));
        check(buffer.Count == 0 && buffer.Snapshot().Count == 0, "Stopped provider history can expire without fake samples");
        check(!buffer.Append(Sample(8)) && buffer.Append(Sample(15)), "Pruning retains timestamp watermark against replay after an outage");
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            check(rejected, message);
        }
        Reject(() => buffer.Append(new(before, "memory.used", time, 100)), "Cannot mix physical metrics in one series buffer");
        Reject(() => buffer.Append(new(before with { Source = "other" }, "utilization.gpu", time, 30)), "Cannot mix providers in one series buffer");
        Reject(() => buffer.Append(new(before with { Id = "GPU-B" }, "utilization.gpu", time, 30)), "Cannot mix device UUIDs in one series buffer");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Reject(() => _ = new TelemetrySample(before, "utilization.gpu", time, value), "Reject non-finite sample " + value);
        Reject(() => _ = new TelemetrySeriesBuffer(0), "Reject zero buffer capacity");
        Reject(() => _ = new TelemetrySeriesBuffer(1, TimeSpan.Zero), "Reject nonpositive retention");
        var one = CsvTelemetry.Parse(Row(0, "A"), time)[0];
        Reject(() => NvidiaTelemetry.Normalize(new[] { one, one }), "Normalization rejects duplicate UUIDs even outside parser");
        var single = new TelemetrySeriesBuffer(1);
        single.Append(Sample(0)); single.Append(Sample(1));
        check(single.Count == 1 && single.Snapshot()[0].Time == time.AddSeconds(1), "Capacity-one ring wraps correctly");
        check(typeof(ITelemetryProvider).IsAssignableFrom(typeof(NvidiaCollector)), "Existing collector implements optional normalized provider boundary");
    }
}
