using ReminderBubble.Data;

namespace ReminderBubble.Tests;

/// <summary>A throw-away SQLite file per test.</summary>
public sealed class TestDb : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "rb-tests-" + Guid.NewGuid().ToString("N"));
    public Database Db { get; }

    public TestDb() => Db = new Database(Path.Combine(_dir, "test.db"));

    public void Dispose()
    {
        Database.ShutdownPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
