using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GpuMonitor;

internal static class RuntimeTransportChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var server = new FixtureServer();
        var serving = server.RunAsync(stop.Token);
        try
        {
            using var providers = BackendProviderSet.Create(new() {
                CollectProcesses = false,
                Runtimes = [
                    new() { Enabled = true, Id = "sglang-ready", Kind = "sglang", Endpoint = server.Endpoint("sglang").ToString() },
                    new() { Enabled = true, Id = "vllm-ready", Kind = "vllm", Endpoint = server.Endpoint("vllm").ToString() }
                ]
            });
            foreach (var kind in new[] { "sglang", "vllm" })
            {
                var provider = providers.Providers.Single(p => p.SourceId == "runtime:" + kind + "-ready");
                var session = new TelemetrySession(provider);
                await session.RefreshAsync(stop.Token);
                check(session.Current!.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null,
                    kind + " real HTTP connection warms up token counters without inventing a rate");
                await session.RefreshAsync(stop.Token);
                var samples = session.Current!.Samples;
                double? Value(string key) => samples.Single(s => s.MetricKey == key).Value;
                check(Value("tokens.output.rate") == 20 && Value("tokens.prompt.rate") == 40 &&
                    Value("requests.running") == 3 && Value("requests.waiting") == 1 && Value("cache.usage") == 25,
                    kind + " configured adapter reads actual HTTP bodies into source-specific token/queue/cache metrics");
                check(Math.Abs(Value("latency.inter_token.mean")!.Value - 0.02) < 1e-9 &&
                    Value("latency.ttft.mean") == 0.5 && Value("latency.decode.mean") == 0.25,
                    kind + " preserves distinct TTFT, inter-token and per-request decode histogram means");
                server.Fail = true;
                bool failed = false;
                try { await session.RefreshAsync(stop.Token); } catch (IOException) { failed = true; }
                check(failed && session.Health == ProviderHealth.Stale && session.Current!.Devices.Count == 1,
                    kind + " endpoint failure retains inventory and reports stale health");
                server.Fail = false;
                await session.RefreshAsync(stop.Token);
                check(session.Health == ProviderHealth.Live && session.Current!.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null,
                    kind + " real HTTP reconnect resets rate baselines");
            }
            check(server.OnlyMetricsGets, "Both adapters issue only metrics GET requests without inference or credentials");
            using var defaults = BackendProviderSet.Create(new() { Runtimes = [
                new() { Enabled = true, Id = "sglang-default", Kind = "sglang" },
                new() { Enabled = true, Id = "vllm-default", Kind = "vllm" }
            ] });
            check(defaults.Providers.Count(p => p.SourceId.StartsWith("runtime:", StringComparison.Ordinal)) == 2,
                "Ready SGLang/vLLM presets can be constructed without hand-written metric mappings or endpoint URLs");
        }
        finally
        {
            stop.Cancel(); server.Stop();
            try { await serving; } catch (OperationCanceledException) { } catch (SocketException) when (stop.IsCancellationRequested) { }
        }
    }

    private sealed class FixtureServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, int> calls = [];
        private readonly long start = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
        public volatile bool Fail;
        public bool OnlyMetricsGets { get; private set; } = true;
        public FixtureServer() => listener.Start();
        public Uri Endpoint(string kind) => new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/{kind}/metrics");
        public async Task RunAsync(CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string request = await reader.ReadLineAsync(cancellation) ?? "";
                string kind = request.Contains("/sglang/metrics", StringComparison.Ordinal) ? "sglang" : "vllm";
                OnlyMetricsGets &= request == $"GET /{kind}/metrics HTTP/1.1";
                string? header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(cancellation)))
                    OnlyMetricsGets &= !header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase) && !header.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase);
                calls.TryGetValue(kind, out int previous); int call = previous + 1; calls[kind] = call;
                string body = Fail ? "unavailable" : Metrics(kind, call);
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                string response = $"HTTP/1.1 {(Fail ? "503 Service Unavailable" : "200 OK")}\r\nContent-Type: text/plain; version=0.0.4\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellation);
                await stream.WriteAsync(bytes, cancellation);
            }
        }
        private string Metrics(string kind, int call)
        {
            string Row(string name, double value) => $"{kind}:{name}{{model_name=\"fixture-model\",engine=\"0\"}} {value.ToString("R", CultureInfo.InvariantCulture)} {start + call * 1000}\n";
            string text = Row("generation_tokens_total", 100 + call * 20) + Row("prompt_tokens_total", 200 + call * 40) +
                Row(kind == "sglang" ? "num_running_reqs" : "num_requests_running", 3) +
                Row(kind == "sglang" ? "num_queue_reqs" : "num_requests_waiting", 1) +
                Row(kind == "sglang" ? "token_usage" : "kv_cache_usage_perc", .25) +
                Row("time_to_first_token_seconds_sum", 2 + call) + Row("time_to_first_token_seconds_count", 4 + call * 2) +
                Row("inter_token_latency_seconds_sum", 1 + call * .04) + Row("inter_token_latency_seconds_count", 50 + call * 2) +
                Row("request_time_per_output_token_seconds_sum", 2 + call * .5) + Row("request_time_per_output_token_seconds_count", 8 + call * 2);
            if (kind == "sglang") text += Row("cache_hit_rate", .5);
            else text += Row("time_per_output_token_seconds_sum", 9999) + Row("time_per_output_token_seconds_count", 1);
            return text;
        }
        public void Stop() => listener.Stop();
        public void Dispose() => listener.Stop();
    }
}
