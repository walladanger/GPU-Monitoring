# Optional local runtime metrics

The opt-in llama.cpp and vLLM CLI flags use the shared runtime adapter alongside
the newer SGLang, Prometheus and metadata-feed configuration. Hardware collection
continues independently when a runtime fails. No endpoint is contacted by default.

## Collecting

Start the runtime separately and enable its metrics endpoint. For llama.cpp,
enable the server's --metrics option. For example:

```powershell
$p = Start-Process .\GpuMonitor.exe -WindowStyle Hidden -ArgumentList @(
    '--collect', 'C:\GpuCapture', '--duration', '30', '--interval', '2',
    '--llama-metrics', 'http://127.0.0.1:8080/metrics',
    '--vllm-metrics', 'http://127.0.0.1:8000/metrics'
) -Wait -PassThru
$p.ExitCode
```

Either flag may be omitted. These flags require explicit loopback HTTP(S)
 /metrics URLs without credentials, query strings or fragments. They can be
combined with --sources configuration. Source IDs are runtime:llamacpp-cli and
runtime:vllm-cli; configured sources must use different IDs. The shared
configuration also accepts Kind = "llamacpp". See
[the adapter guide](GPU_MONITOR_V2_ADAPTERS.md) for configuration and metadata feeds.

Exit zero means at least one source is Live. Inspect each runtime's health in
telemetry.json or telemetry.txt. The caller owns additionalProviders passed to
HeadlessCollector.RunAsync and must dispose them after engine shutdown.

## Output mapping

| Output | llama.cpp input | vLLM input | Unit |
| --- | --- | --- | --- |
| tokens.output | llamacpp:tokens_predicted_total | vllm:generation_tokens_total | tokens |
| tokens.prompt | llamacpp:prompt_tokens_total | vllm:prompt_tokens_total | tokens |
| requests.running | llamacpp:requests_processing | vllm:num_requests_running | count |
| requests.waiting | llamacpp:requests_deferred | vllm:num_requests_waiting | count |

Counter metrics also expose .rate in tokens/s. The shared adapter retains
separate model/worker identities and reports gaps for warmup, resets and recovery
after failed scrapes. It preserves supported source timestamps and otherwise
uses monotonic scrape time. vLLM includes supported cache and latency mappings;
llama.cpp does not invent those measurements. This supersedes the earlier
endpoint-aggregate llm.* metric names.

The shared transport sends only GET requests, disables redirects, proxies and
cookies, uses a three-second timeout and bounds responses to four MiB. Runtime
histories are capped at 900 points and thirty minutes. Prompt bodies, raw labels,
tool arguments and inference requests are not exported or sent.

## Verification

Production-source regression checks cover parsing, exact counter arithmetic,
unavailable samples, failure recovery, cancellation and bounded HTTP responses.
Real loopback fixtures exercise llama.cpp, SGLang and vLLM. Windows CI also
collects both CLI sources through tests/runtime_metrics_fixture.py.
These fixtures do not establish a connection to the user's inference servers.
