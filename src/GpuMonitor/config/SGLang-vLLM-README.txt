SGLang and vLLM adapters

Both adapters are installed with GPU Monitor and ready to configure.
This explicit preset enables SGLang at http://127.0.0.1:30000/metrics
and vLLM at http://127.0.0.1:8000/metrics. Edit Endpoint if your servers
use different addresses or ports. SGLang must have metrics enabled.
The monitor does not start, install or reconfigure either server.

From PowerShell in the installed/portable app directory:

$p = Start-Process .\GpuMonitor.exe -ArgumentList '--collect ".\capture" --duration 60 --interval 2 --sources ".\config\sglang-vllm.sources.json"' -Wait -PassThru
$p.ExitCode

The output folder is created automatically. Use a different output folder
to keep an earlier capture. Results are telemetry.json, telemetry.csv,
events.csv and telemetry.txt. Inspect runtime:sglang-local and
runtime:vllm-local health separately; missing servers do not stop hardware
monitoring, and an exit code of 0 does not mean both runtimes are live.

The preset is loaded only when explicitly selected with --sources or
passed to BackendProviderSet.Create(CollectorConfiguration.Load(path)).
An ordinary launch does not automatically enable either endpoint.

Programmatic adapters are available as:
RuntimeMetricsCollector.CreateSglang(id, optionalEndpoint)
RuntimeMetricsCollector.CreateVllm(id, optionalEndpoint)
They expose the same ITelemetryProvider contract as the hardware sources.

Available data includes prompt/output token totals and rates, request
queues, KV-cache usage, TTFT, request latency, inter-token latency and
per-request decode latency where exposed by the server. Inter-token and
per-request latency stay distinct. Earlier vLLM cache/latency names are
aliases; they are not counted twice. Missing metrics stay unavailable.
Initial connection, counter resets and reconnections warm up token rates
before reporting them. No inference requests or prompt capture occur.

This does not change the GUI. Actual server availability and metric
coverage depend on your SGLang/vLLM versions and configuration.
