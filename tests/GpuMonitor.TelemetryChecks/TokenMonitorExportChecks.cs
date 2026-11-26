using GpuMonitor;
using System.Text.Json;

internal static class TokenMonitorExportChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "gm2-token-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "token-monitor-export.json");
        var now = DateTimeOffset.Parse("2026-11-15T12:00:00Z");
        string Export(DateTimeOffset time, int count) => JsonSerializer.Serialize(new { generatedAt = time.ToString("O"), app = new { name = "token-monitor", version = "0.19.0" },
            snapshot = new { today = new { clients = new { codex = count }, models = new { model = count } }, allTime = new { clients = new { codex = count }, models = new { model = count } } },
            messages = "SECRET_PROMPT", account = "SECRET_ACCOUNT", daily = new[] { new { date = "2026-11-14", tokens = 99999 } } });
        try
        {
            var collector = new TokenMonitorExportCollector("tokenmonitor:fixture", path);
            await File.WriteAllTextAsync(path, Export(now, 100));
            var initial = await collector.ReadAsync(default);
            check(initial.Devices.Count == 4 && initial.Samples.Where(s => s.MetricKey == "tokenmonitor.total").All(s => s.Value == 100) && initial.Samples.All(s => s.Time == now),
                "Token Monitor export separates tool/model and daily/all-time totals with exact generatedAt source time");
            check(initial.Samples.Where(s => s.MetricKey == "tokenmonitor.rate").All(s => s.Value == null), "Token Monitor first observation cannot invent an instantaneous rate");
            await File.WriteAllTextAsync(path, Export(now.AddSeconds(30), 160));
            var next = await collector.ReadAsync(default);
            check(next.Samples.Count(s => s.MetricKey == "tokenmonitor.rate" && s.Value == 2) == 2 && next.Samples.Count(s => s.MetricKey == "tokenmonitor.rate" && s.Value == null) == 2,
                "Token Monitor derives only all-time interval rates without mixing tool/model or daily reset counters");
            check(!JsonSerializer.Serialize(next).Contains("SECRET"), "Token Monitor adapter discards unrelated prompt/account/history fields");
            var repeated = await collector.ReadAsync(default);
            check(repeated.Samples.Where(s => s.MetricKey == "tokenmonitor.rate").All(s => s.Value == null), "Unchanged export timestamps cannot create a new rate observation");
            await File.WriteAllTextAsync(path, Export(now, 1)); await collector.ReadAsync(default);
            await File.WriteAllTextAsync(path, Export(now.AddSeconds(45), 190));
            check((await collector.ReadAsync(default)).Samples.Count(s => s.MetricKey == "tokenmonitor.rate" && s.Value == 2) == 2,
                "Out-of-order Token Monitor exports do not rewind counter baselines or inflate future rates");
            await File.WriteAllTextAsync(path, Export(now.AddMinutes(1), 10));
            check((await collector.ReadAsync(default)).Samples.Where(s => s.MetricKey == "tokenmonitor.rate").All(s => s.Value == null), "Token Monitor counter resets produce gaps rather than negative rates");
            await File.WriteAllTextAsync(path, "malformed"); bool failed = false;
            try { await collector.ReadAsync(default); } catch (JsonException) { failed = true; }
            await File.WriteAllTextAsync(path, Export(now.AddMinutes(2), 100));
            check(failed && (await collector.ReadAsync(default)).Samples.Where(s => s.MetricKey == "tokenmonitor.rate").All(s => s.Value == null), "Malformed exports fail independently and recovery warms rate counters again");
            using var providers = BackendProviderSet.Create(new() { Feeds = [new() { Enabled = true, Id = "fixture", Kind = "tokenmonitor", Path = path }] });
            check(providers.Providers.OfType<TokenMonitorExportCollector>().Single().SourceId == "tokenmonitor:fixture", "Explicit Token Monitor source configuration selects the production export adapter");
        }
        finally { Directory.Delete(root, true); }
    }
}
