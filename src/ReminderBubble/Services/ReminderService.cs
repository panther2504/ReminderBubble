using Microsoft.Data.Sqlite;
using ReminderBubble.Data;
using ReminderBubble.Models;

namespace ReminderBubble.Services;

/// <summary>All reminder persistence + the scheduler tick. Single-threaded: always called on the UI thread.</summary>
public sealed class ReminderService
{
    readonly Database _db;

    /// <summary>Raised after any change to stored reminders.</summary>
    public event Action? Changed;

    /// <summary>Raised from <see cref="Tick"/> with the reminders whose time has just arrived.</summary>
    public event Action<IReadOnlyList<Reminder>>? Due;

    public ReminderService(Database db) => _db = db;

    const string Columns = "id, title, notes, due_at, repeat, color, priority, is_done, notified, completed_at, created_at";

    static Reminder Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Title = r.GetString(1),
        Notes = r.GetString(2),
        DueAt = Database.FromDb(r.GetString(3)),
        Repeat = r.GetString(4),
        Color = r.GetString(5),
        Priority = r.GetInt32(6),
        IsDone = r.GetInt32(7) != 0,
        Notified = r.GetInt32(8) != 0,
        CompletedAt = r.IsDBNull(9) ? null : Database.FromDb(r.GetString(9)),
        CreatedAt = Database.FromDb(r.GetString(10)),
    };

    List<Reminder> Query(SqliteConnection c, string where, params (string, object?)[] ps)
    {
        var list = new List<Reminder>();
        using var cmd = Database.Cmd(c, $"SELECT {Columns} FROM reminders {where}", ps);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public List<Reminder> List()
    {
        using var c = _db.Open();
        return Query(c, "ORDER BY is_done, due_at, id");
    }

    public Reminder? Get(long id)
    {
        using var c = _db.Open();
        return Query(c, "WHERE id = @id", ("@id", id)).FirstOrDefault();
    }

    public int ActiveCount()
    {
        using var c = _db.Open();
        return Convert.ToInt32(Database.Scalar(c, "SELECT COUNT(*) FROM reminders WHERE is_done = 0 AND notified = 1") ?? 0);
    }

    public List<Reminder> Active()
    {
        using var c = _db.Open();
        return Query(c, "WHERE is_done = 0 AND notified = 1 ORDER BY due_at, id");
    }

    // ---- mutations -----------------------------------------------------------------------

    static ReminderInput Clean(ReminderInput i)
    {
        i.Title = (i.Title ?? "").Trim();
        if (i.Title.Length == 0) throw new ArgumentException("A reminder needs a title.");
        if (i.Title.Length > 200) i.Title = i.Title[..200];
        i.Notes = (i.Notes ?? "").Trim();
        if (i.Notes.Length > 2000) i.Notes = i.Notes[..2000];
        if (Array.IndexOf(Recurrence.Kinds, i.Repeat) < 0) i.Repeat = "none";
        if (Array.IndexOf(new[] { "violet", "blue", "teal", "green", "amber", "rose" }, i.Color) < 0) i.Color = "violet";
        i.Priority = Math.Clamp(i.Priority, 0, 2);
        i.DueAt = i.DueAt.ToUniversalTime();
        return i;
    }

    public Reminder Add(ReminderInput input)
    {
        var i = Clean(input);
        using var c = _db.Open();
        var id = Convert.ToInt64(Database.Scalar(c, """
            INSERT INTO reminders (title, notes, due_at, repeat, color, priority, is_done, notified, created_at)
            VALUES (@t, @n, @d, @r, @c, @p, 0, 0, @now);
            SELECT last_insert_rowid();
            """,
            ("@t", i.Title), ("@n", i.Notes), ("@d", Database.ToDb(i.DueAt)), ("@r", i.Repeat),
            ("@c", i.Color), ("@p", i.Priority), ("@now", Database.ToDb(DateTime.UtcNow))));
        Changed?.Invoke();
        return Query(c, "WHERE id = @id", ("@id", id)).First();
    }

    public Reminder? Update(long id, ReminderInput input)
    {
        var i = Clean(input);
        using var c = _db.Open();
        // Moving the due time re-arms the alert; editing text only does not.
        Database.Exec(c, """
            UPDATE reminders
               SET title = @t, notes = @n, repeat = @r, color = @c, priority = @p,
                   notified = CASE WHEN due_at <> @d THEN 0 ELSE notified END,
                   due_at = @d
             WHERE id = @id
            """,
            ("@t", i.Title), ("@n", i.Notes), ("@d", Database.ToDb(i.DueAt)), ("@r", i.Repeat),
            ("@c", i.Color), ("@p", i.Priority), ("@id", id));
        Changed?.Invoke();
        return Query(c, "WHERE id = @id", ("@id", id)).FirstOrDefault();
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        Database.Exec(c, "DELETE FROM reminders WHERE id = @id", ("@id", id));
        Changed?.Invoke();
    }

    /// <summary>Marks done. A repeating reminder instead jumps to its next occurrence and stays open.</summary>
    public Reminder? Complete(long id)
    {
        using var c = _db.Open();
        var r = Query(c, "WHERE id = @id", ("@id", id)).FirstOrDefault();
        if (r is null) return null;
        var now = DateTime.UtcNow;
        if (Recurrence.IsRecurring(r.Repeat))
        {
            Database.Exec(c, "UPDATE reminders SET due_at = @d, notified = 0, completed_at = @now WHERE id = @id",
                ("@d", Database.ToDb(Recurrence.Next(r.DueAt, r.Repeat, now))), ("@now", Database.ToDb(now)), ("@id", id));
        }
        else
        {
            Database.Exec(c, "UPDATE reminders SET is_done = 1, notified = 1, completed_at = @now WHERE id = @id",
                ("@now", Database.ToDb(now)), ("@id", id));
        }
        Changed?.Invoke();
        return Query(c, "WHERE id = @id", ("@id", id)).FirstOrDefault();
    }

    public Reminder? Snooze(long id, int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 60 * 24 * 14);
        using var c = _db.Open();
        Database.Exec(c, "UPDATE reminders SET due_at = @d, notified = 0, is_done = 0 WHERE id = @id",
            ("@d", Database.ToDb(DateTime.UtcNow.AddMinutes(minutes))), ("@id", id));
        Changed?.Invoke();
        return Query(c, "WHERE id = @id", ("@id", id)).FirstOrDefault();
    }

    /// <summary>Writes a full row back as-is (used by "Undo" for delete / complete / snooze).</summary>
    public Reminder Restore(Reminder r)
    {
        using var c = _db.Open();
        Database.Exec(c, """
            INSERT OR REPLACE INTO reminders (id, title, notes, due_at, repeat, color, priority, is_done, notified, completed_at, created_at)
            VALUES (@id, @t, @n, @d, @r, @c, @p, @done, @notified, @comp, @created)
            """,
            ("@id", r.Id), ("@t", r.Title), ("@n", r.Notes), ("@d", Database.ToDb(r.DueAt)), ("@r", r.Repeat),
            ("@c", r.Color), ("@p", r.Priority), ("@done", r.IsDone ? 1 : 0), ("@notified", r.Notified ? 1 : 0),
            ("@comp", r.CompletedAt is { } ca ? Database.ToDb(ca) : null), ("@created", Database.ToDb(r.CreatedAt)));
        Changed?.Invoke();
        return r;
    }

    public void ClearDone()
    {
        using var c = _db.Open();
        Database.Exec(c, "DELETE FROM reminders WHERE is_done = 1");
        Changed?.Invoke();
    }

    // ---- scheduler -----------------------------------------------------------------------

    /// <summary>Fires <see cref="Due"/> for every open reminder whose time has arrived and that has not alerted yet.</summary>
    public void Tick()
    {
        List<Reminder> due;
        using (var c = _db.Open())
        {
            due = Query(c, "WHERE is_done = 0 AND notified = 0 AND due_at <= @now ORDER BY due_at, id",
                ("@now", Database.ToDb(DateTime.UtcNow)));
            if (due.Count == 0) return;
            foreach (var r in due)
                Database.Exec(c, "UPDATE reminders SET notified = 1 WHERE id = @id", ("@id", r.Id));
        }
        foreach (var r in due) r.Notified = true;
        Changed?.Invoke();
        Due?.Invoke(due);
    }
}
