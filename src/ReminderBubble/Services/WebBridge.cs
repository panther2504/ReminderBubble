using System.Text.Json;
using ReminderBubble.Models;
using ReminderBubble.Windows;

namespace ReminderBubble.Services;

/// <summary>
/// The JS &lt;-&gt; C# bridge. The page posts <c>{ id, cmd, args }</c>; the reply is <c>{ id, ok, data | error }</c>.
/// Unsolicited events go the other way as <c>{ ev, data? }</c>.
/// </summary>
public sealed class WebBridge
{
    static readonly JsonSerializerOptions Json = SettingsService.Json;
    readonly App _app;

    public WebBridge(App app) => _app = app;

    public void Dispatch(WebHostWindow host, string json)
    {
        long id = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var n)) id = n;
            var cmd = root.GetProperty("cmd").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            var data = Handle(host, cmd, args);
            if (id != 0) host.Post(new { id, ok = true, data });
        }
        catch (Exception ex)
        {
            Log.Write($"Bridge error: {ex}");
            if (id != 0) host.Post(new { id, ok = false, error = ex.Message });
        }
    }

    object? Handle(WebHostWindow host, string cmd, JsonElement a)
    {
        var rem = _app.Reminders;
        switch (cmd)
        {
            case "app.ready":
                return new
                {
                    version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                    dbPath = _app.Db.Path,
                    settings = _app.Settings.Current,
                };

            // ---- reminders ----
            case "reminders.list": return rem.List();
            case "reminders.active": return rem.Active();
            case "reminders.add":
            {
                var r = rem.Add(a.Deserialize<ReminderInput>(Json) ?? throw new ArgumentException("Missing reminder."));
                _app.Bubble.Nudge();
                return r;
            }
            case "reminders.update":
                return rem.Update(a.GetProperty("id").GetInt64(),
                    a.GetProperty("data").Deserialize<ReminderInput>(Json) ?? throw new ArgumentException("Missing reminder."));
            case "reminders.delete": rem.Delete(a.GetProperty("id").GetInt64()); return null;
            case "reminders.complete": return rem.Complete(a.GetProperty("id").GetInt64());
            case "reminders.snooze": return rem.Snooze(a.GetProperty("id").GetInt64(), a.GetProperty("minutes").GetInt32());
            case "reminders.restore": return rem.Restore(a.Deserialize<Reminder>(Json) ?? throw new ArgumentException("Missing reminder."));
            case "reminders.clearDone": rem.ClearDone(); return null;

            // ---- settings ----
            case "settings.get": return _app.Settings.Current;
            case "settings.patch": return _app.Settings.Patch(a);

            // ---- updates ----
            case "update.status": return _app.Updates.Snapshot;
            case "update.check": _ = _app.Updates.CheckAsync(manual: true); return _app.Updates.Snapshot;
            case "update.install": _app.Dispatcher.BeginInvoke(() => { if (_app.Updates.Install()) _app.Quit(); }); return null;

            // ---- windows ----
            case "window.drag": host.BeginDrag(); return null;
            case "anim.begin": host.OnAnimBegin(a.ValueKind == JsonValueKind.Object && a.TryGetProperty("instant", out var inst) && inst.ValueKind == JsonValueKind.True); return null;
            case "panel.close": _app.Panel.HideAnimated(); return null;
            case "panel.pin": _app.Panel.Pinned = a.GetProperty("value").GetBoolean(); return null;
            case "alert.close": _app.Alert.HideAnimated(); return null;
            case "alert.size": _app.ResizeAlert(a.GetProperty("height").GetDouble()); return null;
            case "alert.openPanel": _app.Dispatcher.BeginInvoke(() => _app.OpenPanel()); return null;
            case "app.quit": _app.Dispatcher.BeginInvoke(_app.Quit); return null;

            default: throw new InvalidOperationException($"Unknown command '{cmd}'.");
        }
    }
}
