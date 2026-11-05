using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ReminderBubble.Data;

/// <summary>Thin SQLite wrapper: schema/migrations plus a few helpers. Connections are pooled by Microsoft.Data.Sqlite.</summary>
public sealed class Database
{
    readonly string _connectionString;
    public string Path { get; }

    public Database(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
        Migrate();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    void Migrate()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, "PRAGMA synchronous=NORMAL;");
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;") ?? 0, CultureInfo.InvariantCulture);
        if (version < 1)
        {
            Exec(c, """
                CREATE TABLE IF NOT EXISTS reminders (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    title        TEXT    NOT NULL,
                    notes        TEXT    NOT NULL DEFAULT '',
                    due_at       TEXT    NOT NULL,
                    repeat       TEXT    NOT NULL DEFAULT 'none',
                    color        TEXT    NOT NULL DEFAULT 'violet',
                    priority     INTEGER NOT NULL DEFAULT 1,
                    is_done      INTEGER NOT NULL DEFAULT 0,
                    notified     INTEGER NOT NULL DEFAULT 0,
                    completed_at TEXT    NULL,
                    created_at   TEXT    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_reminders_due ON reminders (is_done, due_at);
                CREATE TABLE IF NOT EXISTS settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """);
        }
    }

    // ---- helpers -------------------------------------------------------------------------

    public static SqliteCommand Cmd(SqliteConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, ps);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = Cmd(c, sql, ps);
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    /// <summary>Fixed-width ISO-8601 UTC so that string comparison in SQL equals chronological comparison.</summary>
    public static string ToDb(DateTime d) =>
        d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static DateTime FromDb(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    public static void ShutdownPools() => SqliteConnection.ClearAllPools();
}
