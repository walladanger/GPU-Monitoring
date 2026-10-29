using System.Text.Json;
using GpuMonitor;

internal static class OtlpChecks
{
    private static TelemetryBatch Decode(OtlpMetadataDecoder decoder, object record)
    { using var json = JsonDocument.Parse(JsonSerializer.Serialize(record)); return decoder.Decode(json.RootElement, DateTimeOffset.UtcNow); }
    private static object Metric(int tokens, int second, int start, int temporality)
    {
        string Nano(int s) => (1_700_000_000_000_000_000L + s * 1_000_000_000L).ToString();
        return new { resourceMetrics = new[] { new { resource = new { attributes = new[] { new { key = "service.name", value = new { stringValue = "local-inference" } } } },
            scopeMetrics = new[] { new { metrics = new[] { new { name = "gen_ai.client.token.usage", unit = "{token}",
                histogram = new { aggregationTemporality = temporality, dataPoints = new[] { new {
                    attributes = new[] { new { key = "gen_ai.token.type", value = new { stringValue = "output" } },
                        new { key = "gen_ai.request.model", value = new { stringValue = "local-model" } } },
                    startTimeUnixNano = Nano(start), timeUnixNano = Nano(second), sum = tokens, count = "1"
                } } } } } } } } } };
    }
    public static void Run(Action<bool, string> check)
    {
        var decoder = new OtlpMetadataDecoder("otel:test");
        var first = Decode(decoder, Metric(100, 2, 0, 2));
        var second = Decode(decoder, Metric(140, 4, 0, 2));
        check(first.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null &&
            second.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == 20,
            "OTLP cumulative token histograms derive rates from source timestamps");
        var delta = Decode(new("otel:delta"), Metric(100, 10, 0, 1));
        check(delta.Samples.Single(s => s.MetricKey == "tokens.output.delta").Value == 100 &&
            delta.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == 10,
            "OTLP delta temporality uses its declared interval and stays distinct from cumulative totals");
        var reset = Decode(decoder, Metric(2, 6, 5, 2));
        check(reset.Samples.Single(s => s.MetricKey == "tokens.output.rate").Value == null,
            "OTLP start-time changes create a reset gap rather than a token spike");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1_000_000_000L;
        var spans = Decode(decoder, new { resourceSpans = new[] { new { scopeSpans = new[] { new { spans = new[] { new {
            traceId = "1234567890abcdef1234567890abcdef", spanId = "1234567890abcdef", parentSpanId = "abcdef1234567890",
            startTimeUnixNano = now.ToString(), endTimeUnixNano = (now + 2_000_000_000).ToString(), name = "SECRET_PROMPT",
            attributes = new object[] { new { key = "gen_ai.usage.input_tokens", value = new { intValue = "100" } },
                new { key = "gen_ai.usage.cache_read.input_tokens", value = new { intValue = "25" } },
                new { key = "gen_ai.operation.name", value = new { stringValue = "chat" } },
                new { key = "gen_ai.input.messages", value = new { stringValue = "SECRET_MESSAGE" } } }
        } } } } } } });
        check(spans.Events.Single().Duration == TimeSpan.FromSeconds(2) && spans.Events.Single().ParentId == "abcdef1234567890" &&
            spans.Events.Single().CorrelationId == "1234567890abcdef1234567890abcdef",
            "OTLP traces preserve precise span durations, trace IDs and parent relationships");
        check(spans.Samples.Single(s => s.MetricKey == "request.tokens.input").Value == 100 &&
            spans.Samples.Single(s => s.MetricKey == "request.tokens.cache_read").Value == 25 &&
            !JsonSerializer.Serialize(spans).Contains("SECRET"),
            "OTLP retains measured token metadata while discarding span names and message bodies");
    }
}
