using ReminderBubble.Data;
using ReminderBubble.Models;
using ReminderBubble.Services;
using Xunit;

namespace ReminderBubble.Tests;

public sealed class ReminderServiceTests : IDisposable
{
    readonly TestDb _t = new();
    readonly ReminderService _svc;

    public ReminderServiceTests() => _svc = new ReminderService(_t.Db);
    public void Dispose() => _t.Dispose();

    static ReminderInput Input(string title = "Pay rent", DateTime? due = null, string repeat = "none") =>
        new() { Title = title, DueAt = due ?? DateTime.UtcNow.AddHours(1), Repeat = repeat };

    [Fact]
    public void Add_then_list_round_trips_every_field_and_keeps_utc()
    {
        var due = new DateTime(2030, 5, 17, 13, 45, 0, DateTimeKind.Utc);
        var r = _svc.Add(new ReminderInput { Title = "  Dentist ", Notes = " bring card ", DueAt = due, Repeat = "weekly", Color = "teal", Priority = 2 });

        Assert.True(r.Id > 0);
        var got = Assert.Single(_svc.List());
        Assert.Equal("Dentist", got.Title);                 // trimmed
        Assert.Equal("bring card", got.Notes);
        Assert.Equal(due, got.DueAt);
        Assert.Equal(DateTimeKind.Utc, got.DueAt.Kind);
        Assert.Equal("weekly", got.Repeat);
        Assert.Equal("teal", got.Color);
        Assert.Equal(2, got.Priority);
        Assert.False(got.IsDone);
        Assert.False(got.Notified);
        Assert.Null(got.CompletedAt);
    }

    [Fact]
    public void Add_rejects_an_empty_title_and_sanitises_bad_values()
    {
        Assert.Throws<ArgumentException>(() => _svc.Add(Input("   ")));

        var r = _svc.Add(new ReminderInput { Title = "x", DueAt = DateTime.UtcNow, Repeat = "hourly", Color = "pink", Priority = 99 });
        Assert.Equal("none", r.Repeat);
        Assert.Equal("violet", r.Color);
        Assert.Equal(2, r.Priority);
    }

    [Fact]
    public void Add_converts_non_utc_input_to_utc()
    {
        var local = new DateTime(2030, 1, 1, 9, 0, 0, DateTimeKind.Local);
        var r = _svc.Add(Input(due: local));
        Assert.Equal(local.ToUniversalTime(), r.DueAt);
    }

    [Fact]
    public void List_orders_open_reminders_by_due_time_and_puts_done_last()
    {
        var now = DateTime.UtcNow;
        var late = _svc.Add(Input("late", now.AddDays(2)));
        var soon = _svc.Add(Input("soon", now.AddHours(1)));
        var done = _svc.Add(Input("done", now.AddMinutes(1)));
        _svc.Complete(done.Id);

        Assert.Equal(new[] { soon.Id, late.Id, done.Id }, _svc.List().Select(r => r.Id));
    }

    [Fact]
    public void Update_changes_fields_and_rearms_the_alert_only_when_the_time_moves()
    {
        var past = DateTime.UtcNow.AddMinutes(-5);
        var r = _svc.Add(Input("old", past));
        _svc.Tick();
        Assert.True(_svc.Get(r.Id)!.Notified);

        // text-only edit: alert stays "already shown"
        var edited = _svc.Update(r.Id, new ReminderInput { Title = "renamed", DueAt = past, Repeat = "daily", Color = "rose", Priority = 0 })!;
        Assert.Equal("renamed", edited.Title);
        Assert.Equal("daily", edited.Repeat);
        Assert.True(edited.Notified);

        // moving the due time re-arms it
        var moved = _svc.Update(r.Id, new ReminderInput { Title = "renamed", DueAt = past.AddHours(3) })!;
        Assert.False(moved.Notified);
    }

    [Fact]
    public void Delete_and_Restore_bring_back_the_identical_row()
    {
        var r = _svc.Add(Input("keep me", DateTime.UtcNow.AddDays(1), "monthly"));
        var snapshot = _svc.Get(r.Id)!;

        _svc.Delete(r.Id);
        Assert.Empty(_svc.List());

        _svc.Restore(snapshot);
        var back = Assert.Single(_svc.List());
        Assert.Equal(snapshot.Id, back.Id);
        Assert.Equal(snapshot.Title, back.Title);
        Assert.Equal(snapshot.DueAt, back.DueAt);
        Assert.Equal(snapshot.Repeat, back.Repeat);
        Assert.Equal(snapshot.CreatedAt, back.CreatedAt);
    }

    [Fact]
    public void Complete_marks_a_one_off_reminder_done()
    {
        var r = _svc.Add(Input());
        var done = _svc.Complete(r.Id)!;
        Assert.True(done.IsDone);
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(0, _svc.ActiveCount());           // a done reminder is never "active"
    }

    [Fact]
    public void Complete_on_a_repeating_reminder_advances_it_and_keeps_it_open()
    {
        var due = DateTime.UtcNow.AddMinutes(-30);
        due = new DateTime(due.Ticks - due.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);   // the database keeps milliseconds
        var r = _svc.Add(Input("standup", due, "daily"));
        _svc.Tick();                                   // fires -> notified

        var next = _svc.Complete(r.Id)!;

        Assert.False(next.IsDone);
        Assert.False(next.Notified);                   // armed again for the next occurrence
        Assert.True(next.DueAt > DateTime.UtcNow);
        Assert.NotNull(next.CompletedAt);              // "done today" still counts for the progress ring
        Assert.Equal(due.ToLocalTime().TimeOfDay, next.DueAt.ToLocalTime().TimeOfDay);
    }

    [Fact]
    public void Snooze_moves_the_due_time_and_rearms()
    {
        var r = _svc.Add(Input(due: DateTime.UtcNow.AddMinutes(-1)));
        _svc.Tick();
        Assert.Equal(1, _svc.ActiveCount());

        var s = _svc.Snooze(r.Id, 15)!;

        Assert.False(s.Notified);
        Assert.InRange((s.DueAt - DateTime.UtcNow).TotalMinutes, 14.5, 15.1);
        Assert.Equal(0, _svc.ActiveCount());
    }

    [Fact]
    public void Tick_fires_each_due_reminder_exactly_once()
    {
        var now = DateTime.UtcNow;
        var a = _svc.Add(Input("due a", now.AddMinutes(-10)));
        var b = _svc.Add(Input("due b", now.AddMinutes(-1)));
        _svc.Add(Input("later", now.AddHours(2)));

        var fired = new List<long>();
        var changes = 0;
        _svc.Due += list => fired.AddRange(list.Select(r => r.Id));
        _svc.Changed += () => changes++;

        _svc.Tick();
        Assert.Equal(new[] { a.Id, b.Id }, fired);     // oldest first
        Assert.Equal(1, changes);
        Assert.Equal(2, _svc.ActiveCount());
        Assert.Equal(new[] { a.Id, b.Id }, _svc.Active().Select(r => r.Id));

        _svc.Tick();                                   // nothing new -> silent
        Assert.Equal(2, fired.Count);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Tick_picks_up_reminders_that_came_due_while_the_app_was_closed()
    {
        _svc.Add(Input("missed", DateTime.UtcNow.AddDays(-3)));
        var fired = 0;
        _svc.Due += l => fired += l.Count;
        _svc.Tick();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ClearDone_removes_only_completed_reminders()
    {
        var keep = _svc.Add(Input("keep"));
        var gone = _svc.Add(Input("gone"));
        _svc.Complete(gone.Id);

        _svc.ClearDone();

        Assert.Equal(keep.Id, Assert.Single(_svc.List()).Id);
    }

    [Fact]
    public void Titles_with_quotes_and_unicode_survive_the_database()
    {
        const string title = "It's \"quoted\" — naïve 你好 ✨ ; DROP TABLE reminders;--";
        _svc.Add(Input(title));
        Assert.Equal(title, Assert.Single(_svc.List()).Title);
    }

    [Fact]
    public void Data_persists_across_service_instances()
    {
        _svc.Add(Input("persisted"));
        var again = new ReminderService(new Database(_t.Db.Path));
        Assert.Equal("persisted", Assert.Single(again.List()).Title);
    }
}
