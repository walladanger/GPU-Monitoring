using System.Text.Json;
using GpuMonitor;

internal static class CollectorConfigurationChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using (var defaults = BackendProviderSet.Create())
            check(defaults.Providers.All(p => !p.SourceId.StartsWith("runtime:") && !p.SourceId.StartsWith("agui:") && !p.SourceId.StartsWith("otlp:")),
                "Default collection cannot contact an inference endpoint or read an AI feed");
        using (var disabled = BackendProviderSet.Create(new() { CollectProcesses = false, Runtimes = [new() { Id = "unused", Endpoint = "not-a-url" }] }))
            check(disabled.Providers.Count == 5, "Disabled optional sources are not instantiated and process monitoring can be disabled");
        bool rejected = false;
        try { using var invalid = BackendProviderSet.Create(new() { Runtimes = [new() { Enabled = true, Id = "bad", Endpoint = "https://user:secret@example.com/metrics" }] }); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "Optional endpoint configuration rejects embedded credentials");
        string directory = Path.Combine(Path.GetTempPath(), "gpu-config-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "usage.jsonl");
            var usage = new { id = "fixture-request", timestamp = DateTimeOffset.UtcNow.ToString("O"), model = "fixture-model", durationMs = 2000,
                usage = new { prompt_tokens = 100, completion_tokens = 20 }, messages = "SECRET_PROMPT" };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(usage) + "\n");
            string configPath = Path.Combine(directory, "sources.json");
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new CollectorConfiguration {
                CollectProcesses = false, Feeds = [new() { Enabled = true, Id = "local", Kind = "usage", Path = path }] }));
            var config = CollectorConfiguration.Load(configPath);
            string output = Path.Combine(directory, "export");
            await HeadlessCollector.RunAsync(output, 3, 1, configuration: config);
            string raw = await File.ReadAllTextAsync(Path.Combine(output, "telemetry.json"));
            using var json = JsonDocument.Parse(raw);
            var source = json.RootElement.EnumerateArray().Single(s => s.GetProperty("Source").GetString() == "usage:local");
            check(source.GetProperty("Health").GetString() == "Live" && source.GetProperty("EventHistory").GetArrayLength() == 1 && !raw.Contains("SECRET_PROMPT"),
                "Configured usage feed reaches the real headless engine/export path without retaining prompt content");
            string events = await File.ReadAllTextAsync(Path.Combine(output, "events.csv"));
            check(events.Contains("usage.output_rate") && events.Contains("tokens/s") && !events.Contains("SECRET_PROMPT"),
                "Event CSV retains numeric request measurements and their actual units");
            await File.WriteAllTextAsync(configPath, "{\"Version\":1,\"Enabeld\":true}");
            rejected = false;
            try { CollectorConfiguration.Load(configPath); } catch (JsonException) { rejected = true; }
            check(rejected, "Configuration typos are rejected rather than silently enabling or disabling collection");
        }
        finally { Directory.Delete(directory, true); }
    }
}
