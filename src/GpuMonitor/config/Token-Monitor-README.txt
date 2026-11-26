Token Monitor optional file integration

In Token Monitor: Settings > Collection > Data export > Auto-export to a folder.
In GPU Monitor: Settings > Monitoring sources > tokenmonitor > Browse.
Select token-monitor-export.json, enable the source, save and restart GPU Monitor.
Then add a monitoring panel and search for Token or the relevant model/tool.

Reads the documented export's generatedAt and snapshot.today/month/allTime
clients/models totals. These are separate period and tool/model breakdowns;
never add them together as distinct token use. Missing input/output/cache splits
are not guessed. Daily history, prompts, session titles and account fields are
not imported. No network endpoint or conversation folder is searched.

The all-time rate is the difference between successive monotonic exported
totals divided by the difference in generatedAt source timestamps. It is an
export-interval activity average, not decoder throughput or instantaneous tok/s.
The first read, counter reset, duplicate timestamp and recovery produce gaps.
Today/month totals have no derived rate because their rollovers are not
monotonic counters. If auto-export skips unchanged usage, no new source sample
is invented. The last source timestamp remains visible in chart inspection.

Bounded to 4 MiB, 256 period/breakdown series and a 3-second read deadline.
Malformed or missing exports mark only this source unavailable.
This integrates the documented local export, not Token Monitor's remote hub.

Schema inspected from Javis603/token-monitor commit
55cd6bfb0e404cac25ac2ba5590c8a82ca16acbb, docs/export.md and
src/shared/exporter.js. Upstream license: MIT, Copyright (c) 2026 Javis.
This adapter is independently implemented; no upstream implementation is copied.
https://github.com/Javis603/token-monitor/blob/55cd6bfb0e404cac25ac2ba5590c8a82ca16acbb/docs/export.md
