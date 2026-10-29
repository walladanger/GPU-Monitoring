# Disk, process and optional AI telemetry backends

These collectors implement backend data paths only. The existing GUI is unchanged. A replacement GUI reads `TelemetryEngine.Snapshot()`; metric definitions supply units, samples supply source timestamps and null availability, and `EventHistory` supplies retained activities and per-request numeric measurements.

## Hardware and process monitoring

| Source | Measurements | Availability and identity |
| --- | --- | --- |
| windows-disks | Read/write B/s, active time %, queue depth | Windows PhysicalDisk PDH counters; language-neutral counter paths; first rate observation is unavailable. The query stays alive through warmup. No diskperf commands or device writes. `PhysicalDriveN` is the current Windows disk number, not a persistent hardware serial. |
| windows-processes | CPU % of process-visible logical CPU capacity, working set B, private memory B | PID plus UTC start time prevents reuse joining unrelated processes. Protected/exited processes are skipped; this is the readable process inventory. More than 2048 processes fails this provider explicitly. |
| nvidia-processes | Compute-process VRAM in MiB, GPU UUID correlation | `nvidia-smi --query-compute-apps`; compute processes only, not a complete list of graphics applications. Windows WDDM may expose N/A memory. GPU UUID + PID + process start time identifies each series. If the start time is inaccessible, each observation is kept separate to avoid false lifetime attribution. No per-process GPU utilization is invented. |

Physical disks and logical volume capacity remain separate sources. Disk or process provider failures do not stop GPU/CPU/RAM/network collection. Both process providers retain at most 120 samples per series and five minutes of history. Other hardware sources retain their existing bounds. No process termination, executable command line, executable path, administrator elevation or driver installation is performed.

## Optional source configuration

Copy `backend-sources.example.json` to a local configuration file and enable only the sources you run. All optional sources are disabled in the example and absent by default. Configuration version is 1; unknown property names are rejected. IDs use 1–64 letters, digits, hyphens or underscores; there may be at most 16 configured optional sources. `CollectProcesses: false` disables both process providers.

```powershell
$p = Start-Process .\GpuMonitor.exe -ArgumentList '--collect "D:\DEBUGGING LOGS\GpuMonitor\capture" --duration 60 --interval 2 --sources "D:\DEBUGGING LOGS\GpuMonitor\sources.json"' -Wait -PassThru
$p.ExitCode
```

Create the output/configuration folders first. The collector creates its output directory. Feed paths must be explicit local absolute paths; UNC feeds are rejected. The monitor never searches conversation folders or discovers inference endpoints automatically.

For GUI integration:

```csharp
using var providers = BackendProviderSet.Create(CollectorConfiguration.Load(configPath));
var engine = new TelemetryEngine(providers.Providers);
using var stop = new CancellationTokenSource();
Task polling = engine.RunAsync(TimeSpan.FromSeconds(2), stop.Token);
// A UI timer reads engine.Snapshot() and dispatches rendering to the UI thread.
// Cancel stop, then await polling (catch OperationCanceledException), before disposing providers.
```

## Inference metrics: SGLang and vLLM

Both have named ready-to-use factories: `RuntimeMetricsCollector.CreateSglang()` and `RuntimeMetricsCollector.CreateVllm()`, defaulting to local ports 30000 and 8000. Configuration entries may omit `Endpoint` to use these defaults. The installed and portable packages include `config/sglang-vllm.sources.json` with both adapters enabled and `config/SGLang-vLLM-README.txt` with the exact collection command. This preset runs only when explicitly selected; normal app launch remains unchanged.

Set `Kind` to `sglang` or `vllm` and configure its `/metrics` URL. SGLang must already have metrics enabled. These are read-only HTTP GET requests, never inference or model-loading requests. Responses are limited to 4 MiB, 20,000 samples, 32 label sets per source, 32 labels per sample and a three-second request/body deadline. Redirects, cookies and system proxies are disabled. URLs cannot embed credentials, query parameters or fragments. No credentials are required or collected by this adapter.

The adapters map prompt/output token counters and rates, running/waiting request counts, KV-cache ratios, SGLang cache-hit ratio, TTFT and request/decode histogram sums/counts. vLLM current and earlier cache names are aliases, not additive series. Each distinct label set has a stable hash identity; model names remain readable. Other labels are not copied into exports. Missing measurements are null. Histograms produce interval means from changes in sums/counts, not fabricated p95/p99 values.

Counter rates use monotonic scrape intervals unless the exporter supplies an explicit sample timestamp, in which case the source time is preserved and used. Counter decreases, duplicate timestamps and HTTP failures create gaps rather than spikes. The first scrape after reconnection warms up again. No cache tokens are added to prompt totals. Cache percentages are scaled from exporter fractions. Label sets are not aggregated across workers because that could double count distributed metrics.

Inter-token histogram means use `inter_token_latency_seconds` on current SGLang/vLLM; older vLLM `time_per_output_token_seconds` is an alias. These stay separate from per-request `request_time_per_output_token_seconds` means because the observation weighting differs. Real loopback HTTP tests exercise both configured adapters, including reconnection, metric mapping and current/legacy alias precedence, on Linux and Windows.

A `prometheus` runtime kind supports explicit custom `Metrics` mappings, including a ForgeCore exporter if it actually publishes Prometheus data. Each mapping specifies `Name`, `Key`, `Title`, `Unit`, optional positive `Multiplier`, and `Counter`. Counter mappings additionally expose `Key + ".rate"`. This does not claim compatibility with an unverified private ForgeCore API.

## AG-UI lifecycle feed

Use `Kind: agui` with a JSONL file written by an instrumented producer. Each newline-terminated record is an AG-UI event object. Supported lifecycle events are RUN_STARTED, RUN_FINISHED, RUN_ERROR, STEP_STARTED, STEP_FINISHED, TOOL_CALL_START and TOOL_CALL_END. Tool/step names are retained only as short identifier-like metadata. Message content, state, raw events, tool arguments/results and error messages are discarded.

Events use the conventional Unix-millisecond timestamp. Missing timestamps are marked `TimestampEstimated`; precise duration is unavailable when either endpoint lacks a source timestamp. Arrival order drives lifecycle matching; timestamps are not used to reorder the protocol. A source clock that goes backward cannot yield a negative duration. Producers must use consistent clocks to correlate their timelines with hardware.

For interleaved concurrent runs, wrap events lacking a runId:

```json
{"runId":"run-123","event":{"type":"TOOL_CALL_END","toolCallId":"tool-1","timestamp":1792780000000}}
```

Multiple active runs without an explicit envelope are rejected instead of misattributed. Midstream/rotated files may lack start events; those completions retain their metadata with no invented duration.

## Local token-usage feed

Use `Kind: usage` with explicit request metadata. This is an ingestion contract for instrumented local clients, not an automatic parser of third-party conversation logs or a claim of drop-in compatibility with the Token Monitor repository.

```json
{"id":"request-1","timestamp":"2026-10-23T16:00:00-04:00","provider":"local","model":"local-model","sessionId":"session-1","durationMs":2000,"usage":{"prompt_tokens":100,"completion_tokens":20,"prompt_tokens_details":{"cached_tokens":25}}}
```

The adapter accepts OpenAI-compatible `prompt_tokens`/`completion_tokens`, `input_tokens`/`output_tokens`, normalized `inputTokens`/`outputTokens`, and explicit cache read/write fields. It keeps the source's input count and cache count separate, without assuming identical accounting across providers. IDs/session IDs become stable hashes; model names are retained. ISO timestamps require an explicit offset or Z. All records require a stable request ID.

`usage.output_rate` is output tokens divided by the supplied whole-request duration. It is explicitly a request-average throughput, not an instantaneous decode speed. Without a duration it is unavailable. Chart samples expose the latest request per model/session observed in each poll; numeric measurements for every ingested request are retained in bounded events and exported to event CSV/JSON, including when multiple requests arrive in one poll. Replayed stable request IDs are deduplicated in retained event history.

## OpenTelemetry JSON file feed

Use `Kind: otlp` with an OpenTelemetry JSON file-exporter JSONL stream. A configured collector/exporter may write this stream from its OTLP inputs. GPU Monitor does not open a network OTLP receiver, bind ports, install a collector or enable producer content capture.

Supported envelopes are `resourceMetrics` and `resourceSpans`. Metrics cover `gen_ai.client.token.usage` histogram/monotonic-sum data with input/output token type and `gen_ai.client.operation.duration` histogram data. Token units must be `{token}`, `tokens` or `1`; duration units must be seconds. Delta quantities remain distinct from cumulative quantities. Delta rates use the declared start/end interval; cumulative rates use source timestamps and reset on a changed start time. Duplicate/replayed observations cannot reset the rate baseline. Missing start times make delta rates unavailable. No-recorded-value flags create gaps.

Spans preserve trace ID, span ID, parent span ID, duration, known GenAI operation kind and measured input/output/cache token counts. Span names, messages, prompts, tool bodies, arbitrary attributes and secrets are excluded from normalized output. Opaque/uninstrumented providers cannot reveal inaccessible token counts. .NET timestamps retain 100-nanosecond resolution; OTLP nanoseconds are truncated to that resolution.

Only explicit supported GenAI measurements are mapped; other metrics/logs, exponential histograms and full latency bucket distributions are not silently relabeled as supported data. A generic OTLP/gRPC server and a dedicated private ForgeCore protocol are outside this implementation.

## Retention, failure and export behavior

File feeds consume at most 4 MiB/4096 records per poll, at most 1 MiB per record, 64 runtime identities and 2048 current metrics. Only complete newline-terminated records commit. Decoder state and file cursor commit together after a valid batch; malformed records leave the source stale until corrected/truncated. Rotation or truncation resets decoder baselines. New large files start at a complete line within their last 4 MiB. File replacement should change file identity/creation time; an in-place rewrite of already-consumed bytes without truncation is not a supported producer pattern.

Runtime/file charts have a 900-sample-per-series and 30-minute bound. Activity history is deduplicated by ID/kind, bounded to 4096 events per source and 30 minutes; it survives periods with no new events. This is session memory, not database persistence. Input/feed freshness is represented by sample timestamps; a readable idle file is not an inference-server liveness probe.

Outputs are `telemetry.json`, `telemetry.csv`, `events.csv` and `telemetry.txt`. Event measurements include their metric keys and units; missing values remain missing. CSV/JSON history is bounded by the limits above. Neither an external database nor SQLite persistence is added.

## Verification and source contracts

Regression checks cover parser escaping/duplicates, HTTP failure/reconnect/cancellation, token counter resets, cache units, interval latency, AG-UI correlation/estimated times, content exclusion, partial/malformed files, event deduplication/expiry, OTLP temporality and span relationships, configuration and an actual configured headless export. Windows CI requires measured physical-disk throughput, the collector process in inventory and a configured usage fixture, alongside existing app/package checks. GPU process parsing is fixture-tested; real dual-RTX3090/WDDM and real inference-server validation still require the user's machine.

Official contracts used:
- https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw
- https://learn.microsoft.com/en-us/windows/win32/perfctrs/pdh-error-codes
- https://docs.nvidia.com/deploy/nvidia-smi/index.html
- https://docs.sglang.ai/references/production_metrics.html
- https://docs.vllm.ai/en/latest/usage/metrics/
- https://docs.ag-ui.com/spec/1.0/basic
- https://opentelemetry.io/docs/specs/otel/protocol/file-exporter/
- https://opentelemetry.io/docs/specs/otel/metrics/data-model/

## llama.cpp on the shared runtime adapter

`Kind: llamacpp` / `RuntimeMetricsCollector.CreateLlamaCpp()` uses the same bounded HTTP transport, source-scoped identities, counter-reset handling and retained history as SGLang/vLLM. Default: `http://127.0.0.1:8080/metrics`. The server must already be started with `--metrics`. The packaged `config/llamacpp.sources.json` enables only this runtime when explicitly selected. See `config/llama.cpp-README.txt` for the exact collection command.

Mapped official families: `llamacpp:prompt_tokens_total`, `llamacpp:tokens_predicted_total`, `llamacpp:requests_processing`, `llamacpp:requests_deferred`. Normalized keys remain `tokens.prompt`, `tokens.output`, `requests.running`, `requests.waiting`; cumulative counters additionally expose `.rate` in tokens/s. These are interval rates, not per-request decode speeds. No cache ratio or latency histogram is fabricated. Labels remain separate rather than aggregated across workers.

This reconciles the useful llama.cpp contract from sibling PR #23 without replacing the newer parser, SGLang/vLLM mappings or configuration. The older branch's `llm.*` keys and endpoint-wide aggregation are not a second API in this build. Neither PR is automatically merged or closed.

Configuration currently rejects all URL query parameters. The current upstream router-mode metrics endpoint requires a model query, so this adapter supports standalone model servers rather than claiming router-mode compatibility. An OpenAI-compatible LM Studio address does not itself expose these metrics. Actual deployment/driver validation remains separate from loopback HTTP fixture tests.

Official exporter contract: https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md#get-metrics-prometheus-compatible-metrics-exporter

Source-time rate arithmetic subtracts integer ticks before conversion to floating point. A deterministic regression sweeps 256 offsets at epoch-sized timestamps; this avoids timestamp rounding distorting a one-second rate. Loopback coverage includes llama.cpp alongside SGLang and vLLM, including warmup, source failure, reconnection and missing measurements.
