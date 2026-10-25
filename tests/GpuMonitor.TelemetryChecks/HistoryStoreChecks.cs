using GpuMonitor;
using Microsoft.Data.Sqlite;

internal static class HistoryStoreChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "gpu-history-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = Path.Combine(root, "samples.sqlite");
        var now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        var d = new DeviceIdentity("fixture", "GPU-stable", DeviceKind.Gpu, "GPU, exact", "0");
        var selection = new WorkspaceSeries("fixture", d.Id, "memory.used");
        MetricDefinition[] definitions = [new("fixture", "memory.used", "Memory", "MiB")];
        TelemetrySample Sample(int seconds, double? value) => new(d, "memory.used", now.AddSeconds(seconds), value);
        try
        {
            using (var store = new HistoryStore(new(file, MaximumSamples: 4)))
            {
                store.Append([Sample(-3, 1024.125), Sample(-2, null), Sample(-1, 2048)], definitions, now);
                store.Append([Sample(-3, 999)], definitions, now);
            }
            using (var reopened = new HistoryStore(new(file, MaximumSamples: 4)))
            {
                var rows = reopened.Read(selection, "MiB", now.AddMinutes(-1), now, 10);
                check(rows.Count == 3 && rows[0].Sample.Value == 1024.125 && rows[1].Sample.Value == null && rows[0].Unit == "MiB" && rows[0].Sample.Device == d,
                    "SQLite restart preserves exact raw values, gaps, units and stable GPU identity without duplicate overwrite");
                check(reopened.Read(selection with { DeviceId = "another-GPU" }, "MiB", now.AddMinutes(-1), now).Count == 0 &&
                    reopened.Read(selection, "B", now.AddMinutes(-1), now).Count == 0,
                    "Stored history queries never substitute another device or mix unit revisions");
                reopened.Append([Sample(0, 3000), Sample(1, 4000), Sample(2, 5000)], definitions, now.AddSeconds(2));
                var latest = reopened.Read(selection, "MiB", now.AddMinutes(-1), now.AddSeconds(2), 2);
                check(reopened.Count == 4 && latest.Select(r => r.Sample.Value).SequenceEqual(new double?[] { 4000, 5000 }),
                    "SQLite row cap removes oldest samples and bounded queries return newest points in timestamp order");
                reopened.Append([Sample(-86401, 1)], definitions, now.AddDays(8));
                check(reopened.Count == 0, "Configured retention expires old measurements on write including late arrivals");
            }
            using (var store = new HistoryStore(new(file)))
            {
                store.Append([Sample(0, 1)], definitions, now);
                var catalog = store.Catalog("exact");
                check(catalog.Single().Selection == selection && catalog.Single().Unit == "MiB" && store.Catalog("no such sensor").Count == 0,
                    "Historical picker searches actual stored sensors independently of currently connected devices");
            }
            var future = Path.Combine(root, "future.sqlite");
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = future, Pooling = false }.ToString()))
            { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=99; CREATE TABLE untouched (value TEXT); INSERT INTO untouched VALUES('preserve');"; cmd.ExecuteNonQuery(); }
            var before = File.ReadAllBytes(future); bool rejected = false;
            try { using var unsupported = new HistoryStore(new(future)); } catch (FormatException) { rejected = true; }
            check(rejected && before.SequenceEqual(File.ReadAllBytes(future)), "A newer database schema is refused without migration, deletion or modification");
            var negative = Path.Combine(root, "negative.sqlite");
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = negative, Pooling = false }.ToString()))
            { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA user_version=-1; CREATE TABLE unrelated (value TEXT);"; cmd.ExecuteNonQuery(); }
            before = File.ReadAllBytes(negative); rejected = false;
            try { using var unsupported = new HistoryStore(new(negative)); } catch (FormatException) { rejected = true; } catch (SqliteException) { }
            check(rejected && before.SequenceEqual(File.ReadAllBytes(negative)), "Negative/unknown schema versions are rejected before any writer PRAGMA changes the file");
            var corrupt = Path.Combine(root, "corrupt.sqlite"); File.WriteAllText(corrupt, "not a database"); rejected = false;
            try { using var invalid = new HistoryStore(new(corrupt)); } catch (SqliteException) { rejected = true; }
            check(rejected && File.ReadAllText(corrupt) == "not a database", "Corrupt history is reported without replacing the user's file");
        }
        finally { Directory.Delete(root, true); }
    }
}
