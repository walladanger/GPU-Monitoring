# Optional local runtime metrics

The backend now supports explicitly configured llama.cpp and vLLM Prometheus
endpoints. Hardware collection works independently, and no endpoint is contacted
unless supplied. This increment changes no GUI styling or layout.

## Collecting

Start your runtime separately and enable its metrics endpoint. For llama.cpp,
the server requires `--metrics`. Example PowerShell invocation using a new output
directory and already-running local servers:

```powershell
$p = Start-Process .\GpuMonitor.exe -ArgumentList @(
    '--collect', 'C:\GpuCapture', '--duration', '30', '--interval', '2',
    '--llama-metrics', 'http://127.0.0.1:8080/metrics',
    '--vllm-metrics', 'http://127.0.0.1:8000/metrics'
) -Wait -PassThru
$p.ExitCode
```

Omit either optional flag when that runtime is not running. These ports are
examples, not discovery results. Use your actual configured server port.
Each flag accepts one endpoint; both runtimes can be collected concurrently.
The JSON/CSV/TXT exports include runtime data alongside the existing hardware
providers. Overall exit zero still means at least one source is Live; check the
individual runtime source health in `telemetry.json` or `telemetry.txt`.

For another GUI or headless caller, construct `RuntimeMetricsCollector` as an
`ITelemetryProvider`, add it to `TelemetryEngine`, and dispose the collector after
cancelling and awaiting the engine. `HeadlessCollector.RunAsync` also accepts an
optional `additionalProviders` list; the caller owns those providers' lifetimes.

## Exact mapping

| Output metric | llama.cpp input | vLLM input | Unit |
| --- | --- | --- | --- |
| llm.generation.total | llamacpp:tokens_predicted_total | vllm:generation_tokens_total | tokens |
| llm.generation.rate | Difference of generation counters / elapsed time | Difference of generation counters / elapsed time | tokens/s |
| llm.prompt.total | llamacpp:prompt_tokens_total | vllm:prompt_tokens_total | tokens |
| llm.prompt.rate | Difference of prompt counters / elapsed time | Difference of prompt counters / elapsed time | tokens/s |
| llm.requests.running | llamacpp:requests_processing | vllm:num_requests_running | requests |
| llm.requests.waiting | llamacpp:requests_deferred | vllm:num_requests_waiting | requests |

These are endpoint totals across the exposed label sets, not per-request or
per-GPU attribution. Tokens retain the runtime's counting semantics. A rate is
the average change over the scrape interval, including idle time, not the
runtime's active decoding-speed gauge. Rates use monotonic time and samples use
the UTC scrape completion time; optional Prometheus row timestamps are ignored.

The first observation, a worker-counter decrease, a changed label set, missing or
non-finite values, and the first successful scrape after failure produce rate
gaps. Valid unchanged counters produce zero. Label ordering is canonicalized;
one worker reset cannot be hidden by another worker's counter increase. A
restart whose counters exceed every previous value between scrapes cannot be
detected from these counters alone.

## I/O and limits

- GET only, explicit HTTP(S) loopback `/metrics` URL; IPv4, IPv6 and localhost.
- No redirects, proxies, credential fields, query strings, fragments or scanning.
- Five-second timeout and caller cancellation; one MiB maximum response.
- Fixed allowlist of numeric families. Raw bodies and label values are not
  exported; label values remain only in the current in-memory rate baseline.
- Unknown families are ignored; missing supported fields are unavailable.
  No supported fields, malformed supported rows or duplicate series fail the poll.
- Failed sources become Stale through the existing independent engine loops;
  hardware polling continues. Histories retain the existing 3600-point and
  thirty-minute per-series bounds.

llama.cpp router-mode model query parameters and authenticated/remote endpoints
are not supported in this increment. LM Studio's OpenAI-compatible API alone
does not establish support for this adapter. SGLang, Ollama, AG-UI, OTel, model
attribution, TTFT histograms and GUI runtime controls remain pending. No prompts,
tool arguments or inference requests are collected or sent.

## Verification and references

The portable checks exercise the production parser, monotonic arithmetic,
worker resets and label churn, exact mapping, gaps, HTTP errors, redirect
responses, URL restrictions, oversized responses and cancellation using a fake
HTTP transport. They do not establish a live runtime connection on the user's
desktop. WPF cross-compilation does not replace native Windows runtime tests.

Metric names checked against official upstream documentation on 2026-10-23:

- [llama.cpp server metrics](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#get-metrics-prometheus-compatible-metrics-exporter)
- [vLLM metrics design](https://github.com/vllm-project/vllm/blob/main/docs/design/metrics.md)
