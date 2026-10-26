using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GpuMonitor;

public sealed class CollectorConfiguration
{
    public int Version { get; set; } = 1;
    public bool CollectProcesses { get; set; } = true;
    public List<RuntimeSourceConfiguration> Runtimes { get; set; } = [];
    public List<MetadataFeedConfiguration> Feeds { get; set; } = [];
    public static CollectorConfiguration Load(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new IOException("Collector configuration exceeds 64 KiB.");
        var result = JsonSerializer.Deserialize<CollectorConfiguration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 })
            ?? throw new FormatException("Collector configuration is empty.");
        if (result.Version != 1 || result.Runtimes == null || result.Feeds == null || result.Runtimes.Count + result.Feeds.Count > 16)
            throw new FormatException("Unsupported collector configuration or too many sources.");
        return result;
    }
}
public sealed class RuntimeSourceConfiguration
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "vllm";
    public string Endpoint { get; set; } = "";
    public List<RuntimeMetricMap> Metrics { get; set; } = [];
}
public sealed class MetadataFeedConfiguration
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "agui";
    public string Path { get; set; } = "";
}

/// <summary>Owns native/HTTP resources. Dispose only after awaiting engine shutdown.</summary>
public sealed class BackendProviderSet : IDisposable
{
    private readonly List<ITelemetryProvider> providers = [];
    public IReadOnlyList<ITelemetryProvider> Providers => providers.AsReadOnly();
    public static BackendProviderSet Create(CollectorConfiguration? configuration = null)
    {
        configuration ??= new();
        if (configuration.Version != 1 || configuration.Runtimes == null || configuration.Feeds == null || configuration.Runtimes.Count + configuration.Feeds.Count > 16)
            throw new FormatException("Invalid collector configuration.");
        var result = new BackendProviderSet();
        try
        {
            result.providers.AddRange([new NvidiaCollector(), new WindowsSystemCollector(), new NetworkCollector(), new VolumeCollector(), new DiskCollector()]);
            if (configuration.CollectProcesses) result.providers.AddRange([new ProcessCollector(), new NvidiaProcessCollector()]);
            foreach (var source in configuration.Runtimes.Where(s => s.Enabled))
            {
                ValidateId(source.Id);
                var endpoint = string.IsNullOrWhiteSpace(source.Endpoint) ? null : new Uri(source.Endpoint, UriKind.Absolute);
                result.providers.Add(source.Kind switch
                {
                    "sglang" => RuntimeMetricsCollector.CreateSglang(source.Id, endpoint),
                    "vllm" => RuntimeMetricsCollector.CreateVllm(source.Id, endpoint),
                    "prometheus" => new RuntimeMetricsCollector(source.Id, endpoint ?? throw new FormatException("Custom metrics require an endpoint."), source.Metrics),
                    _ => throw new FormatException("Unknown runtime kind.")
                });
            }
            foreach (var feed in configuration.Feeds.Where(f => f.Enabled))
            {
                ValidateId(feed.Id);
                if (!System.IO.Path.IsPathFullyQualified(feed.Path) || feed.Path.StartsWith(@"\\", StringComparison.Ordinal))
                    throw new FormatException("Metadata feeds must use explicit local absolute paths.");
                string id = feed.Kind + ":" + feed.Id;
                IMetadataDecoder decoder = feed.Kind switch
                { "agui" => new AgentEventDecoder(id), "usage" => new UsageMetadataDecoder(id), "otlp" => new OtlpMetadataDecoder(id), _ => throw new FormatException("Unknown metadata feed kind.") };
                result.providers.Add(new JsonlTelemetryProvider(id, feed.Path, decoder));
            }
            if (result.providers.Select(p => p.SourceId).Distinct(StringComparer.Ordinal).Count() != result.providers.Count)
                throw new FormatException("Duplicate collector source ID.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && !"_-".Contains(c)))
            throw new FormatException("Source ID must use 1–64 letters, digits, hyphens or underscores.");
    }
    public void Dispose() { foreach (var provider in providers.OfType<IDisposable>()) provider.Dispose(); }
}
