using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace AiUsageWidget.Core;
public sealed record QuotaNotice(string ProviderId, string Label, double RemainingPercent);
public sealed class HistoryStore : IDisposable
{
    private readonly string databasePath;
    private SqliteConnection connection;
    private readonly object sync = new();
    public HistoryStore(string root)
    {
        Directory.CreateDirectory(root);
        databasePath = Path.Combine(root, "history.db");
        connection = OpenHealthyDatabase();
    }
    private SqliteConnection OpenHealthyDatabase()
    {
        try
        {
            var candidate = OpenDatabase();
            try
            {
                using var check = candidate.CreateCommand();
                check.CommandText = "PRAGMA quick_check";
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The history database failed its integrity check.");
                InitializeSchema(candidate);
                return candidate;
            }
            catch
            {
                candidate.Dispose();
                throw;
            }
        }
        catch (InvalidDataException)
        {
            PreserveCorruptDatabase();
            var replacement = OpenDatabase();
            try { InitializeSchema(replacement); return replacement; }
            catch { replacement.Dispose(); throw; }
        }
        catch (SqliteException error) when (IsCorruption(error))
        {
            PreserveCorruptDatabase();
            var replacement = OpenDatabase();
            try { InitializeSchema(replacement); return replacement; }
            catch { replacement.Dispose(); throw; }
        }
    }
    private static bool IsCorruption(SqliteException error) => error.SqliteErrorCode is 11 or 26;
    private SqliteConnection OpenDatabase()
    {
        var result = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        try
        {
            result.Open();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
    private static void InitializeSchema(SqliteConnection target)
    {
        using var cmd = target.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS readings(provider TEXT NOT NULL, account TEXT NOT NULL, stamp INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(provider,account,stamp));
            CREATE INDEX IF NOT EXISTS readings_time ON readings(stamp);
            CREATE TABLE IF NOT EXISTS token_days(provider TEXT NOT NULL, account TEXT NOT NULL, day TEXT NOT NULL, tokens INTEGER NOT NULL, PRIMARY KEY(provider,account,day));
            CREATE TABLE IF NOT EXISTS alerts(provider TEXT NOT NULL, account TEXT NOT NULL, quota TEXT NOT NULL, period TEXT NOT NULL, mask INTEGER NOT NULL, PRIMARY KEY(provider,account,quota));
            """;
        cmd.ExecuteNonQuery();
    }
    private void PreserveCorruptDatabase()
    {
        SqliteConnection.ClearAllPools();
        if (!File.Exists(databasePath)) return;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backup = Path.Combine(Path.GetDirectoryName(databasePath)!, $"history.corrupt-{stamp}.db");
        for (var suffix = 2; File.Exists(backup); suffix++)
            backup = Path.Combine(Path.GetDirectoryName(databasePath)!, $"history.corrupt-{stamp}-{suffix}.db");
        File.Move(databasePath, backup);
        MoveSidecar(databasePath + "-wal", backup + "-wal");
        MoveSidecar(databasePath + "-shm", backup + "-shm");
    }
    private static void MoveSidecar(string source, string destination)
    {
        if (File.Exists(source)) File.Move(source, destination);
    }
    public void Record(UsageSnapshot snapshot)
    {
        if (snapshot.Status != UsageStatus.Ready || snapshot.ReceivedAt == DateTimeOffset.MinValue) return;
        lock (sync)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO readings VALUES($p,$a,$t,$j); DELETE FROM readings WHERE stamp < $old";
            cmd.Parameters.AddWithValue("$p", snapshot.ProviderId); cmd.Parameters.AddWithValue("$a", snapshot.AccountKey);
            cmd.Parameters.AddWithValue("$t", snapshot.ReceivedAt.ToUnixTimeMilliseconds()); cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(snapshot with { TokenHistory = null }, Json.Options));
            cmd.Parameters.AddWithValue("$old", DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeMilliseconds()); cmd.ExecuteNonQuery();
            if (snapshot.TokenHistory != null)
            {
                using var tx = connection.BeginTransaction();
                foreach (var day in snapshot.TokenHistory.Where(d => d.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-31))))
                {
                    using var save = connection.CreateCommand(); save.Transaction = tx; save.CommandText = "INSERT OR REPLACE INTO token_days VALUES($p,$a,$d,$n)";
                    save.Parameters.AddWithValue("$p", snapshot.ProviderId); save.Parameters.AddWithValue("$a", snapshot.AccountKey); save.Parameters.AddWithValue("$d", day.Date.ToString("yyyy-MM-dd")); save.Parameters.AddWithValue("$n", day.Tokens); save.ExecuteNonQuery();
                }
                using var prune = connection.CreateCommand(); prune.Transaction = tx; prune.CommandText = "DELETE FROM token_days WHERE day < $old"; prune.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-31).ToString("yyyy-MM-dd")); prune.ExecuteNonQuery(); tx.Commit();
            }
        }
    }
    public IReadOnlyList<TokenDay> ReadTokens(string provider, string account)
    {
        lock (sync)
        {
            using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT day,tokens FROM token_days WHERE provider=$p AND account=$a ORDER BY day"; cmd.Parameters.AddWithValue("$p",provider); cmd.Parameters.AddWithValue("$a",account);
            using var rows = cmd.ExecuteReader(); var days = new List<TokenDay>(); while (rows.Read()) days.Add(new(DateOnly.ParseExact(rows.GetString(0), "yyyy-MM-dd"), rows.GetInt64(1))); return days;
        }
    }
    public IReadOnlyList<UsageSnapshot> Read(string provider, int days, string? account = null)
    {
        lock (sync)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT payload FROM readings WHERE provider=$p AND stamp >= $t AND ($a IS NULL OR account=$a) ORDER BY stamp";
            cmd.Parameters.AddWithValue("$p", provider); cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds()); cmd.Parameters.AddWithValue("$a", (object?)account ?? DBNull.Value);
            using var reader = cmd.ExecuteReader(); var result = new List<UsageSnapshot>();
            while (reader.Read()) { try { if (JsonSerializer.Deserialize<UsageSnapshot>(reader.GetString(0), Json.Options) is { } s) result.Add(s); } catch (JsonException) { } }
            return result;
        }
    }
    public IReadOnlyList<QuotaNotice> CheckNotifications(UsageSnapshot s)
    {
        if (s.Status != UsageStatus.Ready) return [];
        lock (sync)
        {
            using var tx = connection.BeginTransaction(); var result = new List<QuotaNotice>();
            foreach (var q in s.Windows.Where(q => !q.Unlimited && q.RemainingPercent.HasValue && (q.ResetsAt == null || q.ResetsAt > DateTimeOffset.UtcNow)))
            {
                var period = q.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown"; var mask = 0;
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = tx; read.CommandText = "SELECT period,mask FROM alerts WHERE provider=$p AND account=$a AND quota=$q"; Bind(read, s, q);
                    using var row = read.ExecuteReader(); if (row.Read() && row.GetString(0) == period) mask = row.GetInt32(1);
                }
                var remaining = q.RemainingPercent!.Value;
                if (remaining > 20) mask = 0; // Recovery also re-arms quotas without a reset timestamp.
                var flag = remaining <= 10 ? 2 : remaining <= 20 ? 1 : 0;
                if (flag != 0 && (mask & flag) == 0) { result.Add(new(s.ProviderId, q.Label, remaining)); mask |= flag == 2 ? 3 : 1; }
                using var save = connection.CreateCommand(); save.Transaction = tx;
                save.CommandText = "INSERT OR REPLACE INTO alerts VALUES($p,$a,$q,$period,$mask)"; Bind(save, s, q); save.Parameters.AddWithValue("$period", period); save.Parameters.AddWithValue("$mask", mask); save.ExecuteNonQuery();
            }
            tx.Commit(); return result;
        }
    }
    private static void Bind(SqliteCommand cmd, UsageSnapshot s, QuotaWindow q)
    { cmd.Parameters.AddWithValue("$p", s.ProviderId); cmd.Parameters.AddWithValue("$a", s.AccountKey); cmd.Parameters.AddWithValue("$q", q.Id); }
    public void Dispose() { lock (sync) connection.Dispose(); }
}
