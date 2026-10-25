using System.IO;
using Microsoft.Data.Sqlite;

namespace GpuMonitor;

public sealed record HistoryOptions(string Path, int RetentionDays = 7, int MaximumSamples = 250000, long MaximumBytes = 128 * 1024 * 1024)
{
    public void Validate()
    {
        if (!System.IO.Path.IsPathFullyQualified(Path) || Path.StartsWith(@"\\", StringComparison.Ordinal) || RetentionDays is < 1 or > 30 ||
            MaximumSamples is < 1 or > 1000000 || MaximumBytes is < 65536 or > 512L * 1024 * 1024)
            throw new ArgumentException("History requires a local absolute path, 1–30 days and bounded rows/disk space.");
    }
}
public sealed record HistorySensor(WorkspaceSeries Selection, string Unit, string Name)
{
    public override string ToString() => $"{Selection.Source} · {Selection.DeviceId} · {Name} · {Selection.MetricKey} [{Unit}]";
}

/// <summary>Single-writer numeric archive. All calls belong on a background worker, never the WPF thread.</summary>
public sealed class HistoryStore : IDisposable
{
    private const long ApplicationId = 0x47504D32;
    private readonly SqliteConnection db;
    private readonly HistoryOptions options;
    public long Count { get; private set; }

    public HistoryStore(HistoryOptions options)
    {
        options.Validate(); this.options = options;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(options.Path)!);
        db = Open(options.Path, false);
        try
        {
            long version = Scalar("PRAGMA user_version"), app = Scalar("PRAGMA application_id");
            if (version is not (0 or 1) || (app != 0 && app != ApplicationId) || (version == 1 && app != ApplicationId))
                throw new FormatException("Unsupported history database schema or application identity; file left unchanged.");
            if (version == 0)
            {
                if (Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'") != 0)
                    throw new FormatException("History path contains an unrelated database; file left unchanged.");
                Execute("PRAGMA auto_vacuum=INCREMENTAL");
                using var transaction = db.BeginTransaction();
                Execute("""
                    CREATE TABLE samples (
                      source TEXT NOT NULL, device TEXT NOT NULL, metric TEXT NOT NULL, ticks INTEGER NOT NULL,
                      name TEXT NOT NULL, kind INTEGER NOT NULL, display_index TEXT NOT NULL, bus TEXT,
                      unit TEXT NOT NULL, value REAL,
                      PRIMARY KEY(source,device,metric,ticks));
                    CREATE INDEX sample_time ON samples(ticks);
                    CREATE INDEX sample_unit ON samples(source,device,metric,unit,ticks);
                    PRAGMA user_version=1;
                    PRAGMA application_id=1196444978;
                    """, transaction);
                transaction.Commit();
            }
            Execute($"PRAGMA max_page_count={options.MaximumBytes / Scalar("PRAGMA page_size")}; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-2048; PRAGMA wal_autocheckpoint=256; PRAGMA journal_size_limit=4194304;");
            Count = Scalar("SELECT COUNT(*) FROM samples");
            using var prune = db.BeginTransaction(); Prune(DateTimeOffset.UtcNow, prune); prune.Commit();
        }
        catch { db.Dispose(); throw; }
    }

    internal static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 2 }.ToString());
        try { connection.Open(); return connection; } catch { connection.Dispose(); throw; }
    }

    public void Append(IReadOnlyList<TelemetrySample> samples, IReadOnlyList<MetricDefinition> definitions, DateTimeOffset now)
    {
        if (samples.Count > 8192) throw new FormatException("History batch exceeds 8192 measurements.");
        var units = definitions.ToDictionary(d => (d.Source, d.Key), d => d.Unit);
        using var transaction = db.BeginTransaction();
        using var insert = db.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = "INSERT OR IGNORE INTO samples VALUES($source,$device,$metric,$ticks,$name,$kind,$index,$bus,$unit,$value)";
        foreach (string name in new[] { "$source", "$device", "$metric", "$ticks", "$name", "$kind", "$index", "$bus", "$unit", "$value" })
            insert.Parameters.AddWithValue(name, DBNull.Value);
        insert.Prepare(); long added = 0;
        foreach (var sample in samples.Where(s => s.Time >= now.AddDays(-options.RetentionDays)))
        {
            var device = sample.Device; string unit = units.GetValueOrDefault((device.Source, sample.MetricKey), "");
            if (device.Source.Length > 128 || device.Id.Length > 512 || sample.MetricKey.Length > 128 || device.Name.Length > 512 || unit.Length > 64 || (device.Bus?.Length ?? 0) > 128 || device.DisplayIndex.Length > 64)
                throw new FormatException("History identity/label exceeds storage limits.");
            object[] values = [device.Source, device.Id, sample.MetricKey, sample.Time.UtcTicks, device.Name, (int)device.Kind, device.DisplayIndex,
                (object?)device.Bus ?? DBNull.Value, unit, (object?)sample.Value ?? DBNull.Value];
            for (int i = 0; i < values.Length; i++) insert.Parameters[i].Value = values[i];
            added += insert.ExecuteNonQuery();
        }
        // Count is updated only after commit so a rejected batch cannot corrupt accounting.
        long prior = Count;
        try { Count += added; Prune(now, transaction); transaction.Commit(); }
        catch { Count = prior; throw; }
    }

    private void Prune(DateTimeOffset now, SqliteTransaction transaction)
    {
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM samples WHERE ticks < $cutoff";
        command.Parameters.AddWithValue("$cutoff", now.AddDays(-options.RetentionDays).UtcTicks);
        Count -= command.ExecuteNonQuery();
        if (Count > options.MaximumSamples)
        {
            command.CommandText = "DELETE FROM samples WHERE rowid IN (SELECT rowid FROM samples ORDER BY ticks,rowid LIMIT $excess)";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$excess", Count - options.MaximumSamples);
            Count -= command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<PlotExportRow> Read(WorkspaceSeries selection, string unit, DateTimeOffset from, DateTimeOffset to, int limit = 10000)
        => Read(db, selection, unit, from, to, limit);
    public IReadOnlyList<HistorySensor> Catalog(string query = "") => Catalog(db, query);
    public static IReadOnlyList<PlotExportRow> ReadFile(string path, WorkspaceSeries selection, string unit, DateTimeOffset from, DateTimeOffset to, int limit = 10000)
    { using var connection = OpenExisting(path); return Read(connection, selection, unit, from, to, limit); }
    public static IReadOnlyList<HistorySensor> CatalogFile(string path, string query = "")
    { using var connection = OpenExisting(path); return Catalog(connection, query); }
    private static SqliteConnection OpenExisting(string path)
    {
        var connection = Open(path, true);
        try
        {
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
            long version = (long)command.ExecuteScalar()!; command.CommandText = "PRAGMA application_id";
            if (version == 1 && (long)command.ExecuteScalar()! == ApplicationId) return connection;
            throw new FormatException("Unsupported history schema or application identity.");
        }
        catch { connection.Dispose(); throw; }
    }

    private static IReadOnlyList<PlotExportRow> Read(SqliteConnection connection, WorkspaceSeries selection, string unit, DateTimeOffset from, DateTimeOffset to, int limit)
    {
        if (limit is < 1 or > 10000 || to < from) throw new ArgumentException("Invalid history query range or limit.");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM (SELECT * FROM samples WHERE source=$source AND device=$device AND metric=$metric AND unit=$unit
            AND ticks BETWEEN $from AND $to ORDER BY ticks DESC LIMIT $limit) ORDER BY ticks;
            """;
        command.Parameters.AddWithValue("$source", selection.Source); command.Parameters.AddWithValue("$device", selection.DeviceId);
        command.Parameters.AddWithValue("$metric", selection.MetricKey); command.Parameters.AddWithValue("$unit", unit);
        command.Parameters.AddWithValue("$from", from.UtcTicks); command.Parameters.AddWithValue("$to", to.UtcTicks); command.Parameters.AddWithValue("$limit", limit);
        using var rows = command.ExecuteReader(); var result = new List<PlotExportRow>();
        while (rows.Read()) result.Add(new(new(new(rows.GetString(0), rows.GetString(1), (DeviceKind)rows.GetInt32(5), rows.GetString(4), rows.GetString(6), rows.IsDBNull(7) ? null : rows.GetString(7)),
            rows.GetString(2), new DateTimeOffset(rows.GetInt64(3), TimeSpan.Zero), rows.IsDBNull(9) ? null : rows.GetDouble(9)), rows.GetString(8)));
        return result.AsReadOnly();
    }
    private static IReadOnlyList<HistorySensor> Catalog(SqliteConnection connection, string query)
    {
        if (query.Length > 128) throw new ArgumentException("Search exceeds 128 characters.");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source,device,metric,unit,name FROM (
              SELECT *,ROW_NUMBER() OVER(PARTITION BY source,device,metric,unit ORDER BY ticks DESC) AS row_number
              FROM samples WHERE instr(lower(source || ' ' || device || ' ' || metric || ' ' || name),lower($query)) > 0)
            WHERE row_number=1 ORDER BY source,name,metric,unit LIMIT 256;
            """;
        command.Parameters.AddWithValue("$query", query);
        using var rows = command.ExecuteReader(); var result = new List<HistorySensor>();
        while (rows.Read()) result.Add(new(new(rows.GetString(0), rows.GetString(1), rows.GetString(2)), rows.GetString(3), rows.GetString(4)));
        return result.AsReadOnly();
    }
    private long Scalar(string sql) { using var command = db.CreateCommand(); command.CommandText = sql; return (long)command.ExecuteScalar()!; }
    private void Execute(string sql, SqliteTransaction? transaction = null)
    { using var command = db.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
    public void Dispose() => db.Dispose();
}
