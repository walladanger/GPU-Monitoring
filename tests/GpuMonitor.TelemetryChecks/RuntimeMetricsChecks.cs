using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using GpuMonitor;

internal static class RuntimeMetricsChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var text = "# TYPE test counter\ntest{model_name=\"a, b\\\"c\", engine=\"0\"} 1.25e2 1000 # {trace_id=\"SECRET\"} 3\nmissing NaN";
        var points = PrometheusText.Parse(text);
        check(points.Count == 2 && points[0].Value == 125 && points[0].Labels["model_name"] == "a, b\"c" &&
            points[0].TimestampMilliseconds == 1000 && points[1].Value == null,
            "Prometheus parser retains escaped labels, scientific values/timestamps and unavailable data, omitting exemplars");
        bool rejected = false;
        try { PrometheusText.Parse("test{a=\"b\"} 1\ntest{a=\"b\"} 2"); } catch (FormatException) { rejected = true; }
        check(rejected, "Duplicate Prometheus series cannot be double counted");
        var rate = new CumulativeRate(1);
        long epochTick = DateTimeOffset.Parse("2026-10-24T12:00:00Z").UtcTicks;
        bool preciseEpochRates = true;
        for (int offset = 0; offset < 256; offset++)
        {
            var epochRate = new CumulativeRate(TimeSpan.TicksPerSecond);
            epochRate.Observe(100, epochTick + offset);
            preciseEpochRates &= Math.Abs(epochRate.Observe(120, epochTick + offset + TimeSpan.TicksPerSecond)!.Value - 20) < 1e-12;
        }
        check(preciseEpochRates, "Epoch-sized source timestamps retain exact elapsed ticks before rate conversion");
        check(rate.Observe(100, 1) == null && rate.Observe(140, 3) == 20 && rate.Observe(140, 3) == null &&
            rate.Observe(160, 4) == 20, "Counter rates ignore replayed timestamps without corrupting their baseline");
        check(rate.Observe(2, 5) == null && rate.Observe(12, 6) == 10 && rate.Observe(50, 7, "restart") == null,
            "Counter resets and explicit source restarts cannot create throughput spikes");
        using var runtime = new RuntimeMetricsCollector("local", new Uri("http://127.0.0.1:8000/metrics"), RuntimeMetricsCollector.StandardMaps("vllm"));
        string Snapshot(int tokens, int latencySum, int count) => $"vllm:generation_tokens_total{{model_name=\"test-model\"}} {tokens}\nvllm:kv_cache_usage_perc{{model_name=\"test-model\"}} 0.25\nvllm:time_to_first_token_seconds_sum{{model_name=\"test-model\"}} {latencySum}\nvllm:time_to_first_token_seconds_count{{model_name=\"test-model\"}} {count}";
        var now = DateTimeOffset.UtcNow;
        var first = runtime.Normalize(Snapshot(100, 20, 10), now, 0);
        var next = runtime.Normalize(Snapshot(140, 24, 12), now.AddSeconds(2), Stopwatch.Frequency * 2);
        double? Value(string key) => next.Samples.Single(s => s.MetricKey == key).Value;
        check(first.Devices.Count == 1 && Value("tokens.output.rate") == 20 && Value("cache.usage") == 25 &&
            Value("latency.ttft.mean") == 2 && Value("requests.waiting") == null,
            "vLLM adapter derives actual token rates/interval latency, scales cache ratio and leaves missing metrics unavailable");
        check(next.Devices[0].Id == first.Devices[0].Id && next.Devices[0].Name == "test-model",
            "Runtime labels create stable model/worker identities");
        var reset = runtime.Normalize(Snapshot(2, 1, 1), now.AddSeconds(3), Stopwatch.Frequency * 3);
        check(reset.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null,
            "Runtime restart creates a throughput gap");
        var handler = new Handler();
        using var http = new RuntimeMetricsCollector("http", new Uri("http://127.0.0.1:30000/metrics"), RuntimeMetricsCollector.StandardMaps("sglang"), handler);
        handler.Text = "sglang:generation_tokens_total 10";
        await http.ReadAsync(CancellationToken.None);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        rejected = false; try { await http.ReadAsync(CancellationToken.None); } catch (IOException) { rejected = true; }
        handler.Status = HttpStatusCode.OK; handler.Text = "sglang:generation_tokens_total 30";
        var recovered = await http.ReadAsync(CancellationToken.None);
        check(rejected && recovered.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null,
            "HTTP failure resets rate baselines so reconnects cannot bridge an outage");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        rejected = false; try { await http.ReadAsync(cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        check(rejected, "Optional runtime scrape honors caller cancellation");
        check(!JsonSerializer.Serialize(next).Contains("SECRET"), "Runtime output contains no exemplar/trace payload");
        using var custom = new RuntimeMetricsCollector("custom", new Uri("http://127.0.0.1:9000/metrics"),
            [new("custom_mean", "custom.mean", "Custom source mean", "ms")]);
        check(custom.Normalize("custom_mean 25", now, 0).Samples.Single().Value == 25,
            "Explicit custom mean mappings do not collide with derived latency metrics");
        handler.Text = new string('x', 4 * 1024 * 1024 + 1);
        rejected = false; try { await http.ReadAsync(CancellationToken.None); } catch (IOException) { rejected = true; }
        check(rejected, "Oversized optional metrics responses fail within the configured bound");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Text { get; set; } = "";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Text) }); }
    }
}
