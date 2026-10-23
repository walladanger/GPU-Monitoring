using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using GpuMonitor;

internal static class RuntimeMetricsChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var time = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        long tick = Stopwatch.Frequency;
        string Llama(int generation, int prompt = 200) => $"# HELP ignored\nllamacpp:tokens_predicted_total {generation}\nllamacpp:prompt_tokens_total {prompt}\nllamacpp:requests_processing 2\nllamacpp:requests_deferred 3\n";
        double? Value(TelemetryBatch batch, string key) => batch.Samples.Single(s => s.MetricKey == key).Value;
        var reader = new RuntimeMetricsReader(RuntimeMetricsKind.LlamaCpp, "fixture");
        var warmup = reader.Observe(Llama(100), time, tick);
        check(Value(warmup, "llm.generation.total") == 100 && Value(warmup, "llm.generation.rate") == null,
            "Runtime first counter snapshot yields exact total and unavailable rate");
        var second = reader.Observe(Llama(160, 240), time.AddHours(-1), tick * 3);
        check(Value(second, "llm.generation.rate") == 30 && Value(second, "llm.prompt.rate") == 20,
            "Runtime rates use monotonic elapsed time despite wall clock changes");
        check(Value(second, "llm.requests.running") == 2 && Value(second, "llm.requests.waiting") == 3,
            "Runtime request gauges retain exact measured queue counts");
        check(second.Devices.Single().Kind == DeviceKind.Runtime && second.Events.Count == 0 && second.Samples.Count == 6,
            "Runtime observation creates one endpoint device without fabricated agent events");
        var reset = reader.Observe(Llama(1, 2), time, tick * 4);
        check(Value(reset, "llm.generation.rate") == null && Value(reset, "llm.prompt.rate") == null,
            "Runtime counter reset creates rate gaps");
        check(Value(reader.Observe(Llama(1, 2), time, tick * 5), "llm.generation.rate") == 0,
            "Unchanged valid runtime counter reports measured zero throughput");
        reader.Reset();
        check(Value(reader.Observe(Llama(1000), time, tick * 6), "llm.generation.rate") == null,
            "Reset after scrape failure prevents throughput averaging across outage");
        var absent = reader.Observe("llamacpp:requests_processing 1", time, tick * 7);
        check(Value(absent, "llm.prompt.total") == null && Value(absent, "llm.generation.rate") == null,
            "Unexposed runtime metrics remain unavailable instead of zero");
        var nonfinite = reader.Observe("llamacpp:tokens_predicted_total NaN\nllamacpp:requests_processing -1", time, tick * 8);
        check(nonfinite.Samples.All(s => s.Value == null), "Nonfinite and negative runtime readings remain gaps");

        var vllm = new RuntimeMetricsReader(RuntimeMetricsKind.Vllm, "vllm-fixture");
        var batch = vllm.Observe("vllm:generation_tokens_total{model_name=\"sensitive-label\",engine=\"0\"} 100\nvllm:generation_tokens_total{engine=\"1\",model_name=\"sensitive-label\"} 200", time, tick);
        check(Value(batch, "llm.generation.total") == 300 && !JsonSerializer.Serialize(batch).Contains("sensitive-label"),
            "Labeled runtime series aggregate endpoint totals without exporting labels");
        batch = vllm.Observe("vllm:generation_tokens_total{engine=\"0\",model_name=\"sensitive-label\"} 120\nvllm:generation_tokens_total{model_name=\"sensitive-label\",engine=\"1\"} 240", time, tick * 3);
        check(Value(batch, "llm.generation.rate") == 30, "Label ordering cannot change runtime counter identity");
        batch = vllm.Observe("vllm:generation_tokens_total{engine=\"0\",model_name=\"sensitive-label\"} 1\nvllm:generation_tokens_total{engine=\"1\",model_name=\"sensitive-label\"} 500", time, tick * 4);
        check(Value(batch, "llm.generation.rate") == null, "One worker reset cannot be hidden by another worker counter increase");
        batch = vllm.Observe("vllm:generation_tokens_total{engine=\"2\"} 2000", time, tick * 5);
        check(Value(batch, "llm.generation.rate") == null, "Runtime label-set changes invalidate rate baseline");
        void Reject(string text, string reason)
        {
            bool rejected = false;
            try { vllm.Observe(text, time, tick); } catch (FormatException) { rejected = true; }
            check(rejected, reason);
        }
        Reject("<html>not metrics</html>", "HTML response is not accepted as live runtime telemetry");
        Reject("vllm:generation_tokens_total 1\nvllm:generation_tokens_total 2", "Duplicate runtime series are rejected");
        Reject("vllm:generation_tokens_total{a=\"x\",a=\"y\"} 1", "Duplicate runtime labels are rejected");
        Reject("vllm:generation_tokens_total{broken} 1", "Malformed runtime labels are rejected");
        Reject("vllm:generation_tokens_total{a=\"x\"} 1 trailing", "Malformed supported metric row is rejected");
        Reject("vllm:generation_tokens_total", "Supported metric without a value is rejected");
        foreach (var url in new[] { "https://example.com/metrics", "http://127.0.0.1/v1/chat/completions", "http://user:pass@localhost/metrics", "http://localhost/metrics?key=secret", "file:///metrics" })
        {
            bool rejected = false;
            try { using var unused = new RuntimeMetricsCollector(RuntimeMetricsKind.LlamaCpp, new Uri(url)); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "Reject unapproved runtime endpoint " + new Uri(url).Scheme + ":" + new Uri(url).AbsolutePath);
        }
        using (var ipv6 = new RuntimeMetricsCollector(RuntimeMetricsKind.Vllm, new Uri("http://[::1]:8000/metrics")))
            check(ipv6.Definitions.Count == 6, "IPv6 loopback runtime endpoint is supported");

        var handler = new FixtureHandler { Body = Llama(10) };
        using var client = new HttpClient(handler);
        using var collector = new RuntimeMetricsCollector(RuntimeMetricsKind.LlamaCpp, new Uri("http://127.0.0.1:8080/metrics"), client);
        var live = await collector.ReadAsync(CancellationToken.None);
        check(handler.Requests == 1 && handler.Method == HttpMethod.Get && handler.Path == "/metrics" && Value(live, "llm.generation.total") == 10,
            "Runtime HTTP collector issues only configured GET metrics request");
        handler.Status = HttpStatusCode.Redirect;
        bool redirect = false;
        try { await collector.ReadAsync(CancellationToken.None); } catch (IOException) { redirect = true; }
        check(redirect && handler.Requests == 2, "Redirect response fails instead of accepting another endpoint");
        handler.Status = HttpStatusCode.OK; handler.Body = Llama(100);
        check(Value(await collector.ReadAsync(CancellationToken.None), "llm.generation.rate") == null,
            "HTTP failure clears runtime rate baseline for recovery");
        handler.Body = new string('x', 1024 * 1024 + 1);
        bool oversize = false;
        try { await collector.ReadAsync(CancellationToken.None); } catch (IOException) { oversize = true; }
        check(oversize, "Oversized runtime metrics response is rejected");
        handler.Streaming = true; oversize = false;
        try { await collector.ReadAsync(CancellationToken.None); } catch (IOException) { oversize = true; }
        check(oversize, "Runtime response byte limit also applies without Content-Length");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool cancelled = false;
        try { await collector.ReadAsync(stop.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Runtime HTTP collector honors caller cancellation");
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public bool Streaming { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Requests++; Method = request.Method; Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = Streaming
                ? new StreamContent(new StreamingBody(Encoding.UTF8.GetBytes(Body))) : new StringContent(Body) });
        }
    }

    private sealed class StreamingBody(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
