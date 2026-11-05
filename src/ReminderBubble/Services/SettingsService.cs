using System.Text.Json;
using System.Text.Json.Nodes;
using ReminderBubble.Data;
using ReminderBubble.Models;

namespace ReminderBubble.Services;

public sealed class SettingsService
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly Database _db;
    public AppSettings Current { get; private set; }
    public bool IsFirstRun { get; }

    /// <summary>Raised when settings change through <see cref="Patch"/>. Position-only saves do not raise it.</summary>
    public event Action<AppSettings>? Changed;

    public SettingsService(Database db)
    {
        _db = db;
        using var c = db.Open();
        var raw = Database.Scalar(c, "SELECT value FROM settings WHERE key = 'app'") as string;
        IsFirstRun = raw is null;
        try { Current = raw is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(raw, Json) ?? new AppSettings(); }
        catch (JsonException) { Current = new AppSettings(); }
        Current.Normalize();
    }

    void Save()
    {
        using var c = _db.Open();
        Database.Exec(c, "INSERT INTO settings (key, value) VALUES ('app', @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("@v", JsonSerializer.Serialize(Current, Json)));
    }

    /// <summary>Merges the properties present in <paramref name="patch"/> into the current settings.</summary>
    public AppSettings Patch(JsonElement patch)
    {
        var node = JsonSerializer.SerializeToNode(Current, Json)!.AsObject();
        foreach (var p in patch.EnumerateObject())
        {
            // The bubble position is owned by the bubble window, never by the UI.
            if (p.Name is "bubbleX" or "bubbleY") continue;
            if (node.ContainsKey(p.Name)) node[p.Name] = JsonNode.Parse(p.Value.GetRawText());
        }
        var next = node.Deserialize<AppSettings>(Json) ?? new AppSettings();
        next.Normalize();
        Current = next;
        Save();
        Changed?.Invoke(Current);
        return Current;
    }

    public void MarkWelcomed()
    {
        Current.Welcomed = true;
        Save();
    }

    public void SetBubblePosition(double x, double y)
    {
        Current.BubbleX = x;
        Current.BubbleY = y;
        Save();
    }
}
