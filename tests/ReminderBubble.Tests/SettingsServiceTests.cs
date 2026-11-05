using System.Text.Json;
using ReminderBubble.Data;
using ReminderBubble.Services;
using Xunit;

namespace ReminderBubble.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    readonly TestDb _t = new();
    public void Dispose() => _t.Dispose();

    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void Fresh_database_reports_first_run_with_sensible_defaults()
    {
        var s = new SettingsService(_t.Db);
        Assert.True(s.IsFirstRun);
        Assert.Equal("dark", s.Current.Theme);
        Assert.Equal(64, s.Current.BubbleSize);
        Assert.True(s.Current.SnapToEdge);
        Assert.Equal(10, s.Current.DefaultSnooze);
    }

    [Fact]
    public void Patch_merges_only_the_given_keys_and_persists()
    {
        var s = new SettingsService(_t.Db);
        var raised = 0;
        s.Changed += _ => raised++;

        s.Patch(Json("""{ "theme": "light", "accent": "rose", "sound": false }"""));

        Assert.Equal(1, raised);
        Assert.Equal("light", s.Current.Theme);
        Assert.Equal("rose", s.Current.Accent);
        Assert.False(s.Current.Sound);
        Assert.Equal(64, s.Current.BubbleSize);                    // untouched

        var reloaded = new SettingsService(_t.Db);
        Assert.False(reloaded.IsFirstRun);
        Assert.Equal("light", reloaded.Current.Theme);
        Assert.Equal("rose", reloaded.Current.Accent);
    }

    [Fact]
    public void Patch_clamps_out_of_range_and_unknown_values()
    {
        var s = new SettingsService(_t.Db);
        s.Patch(Json("""{ "bubbleSize": 500, "bubbleOpacity": 0.01, "defaultSnooze": 0, "theme": "neon", "accent": "x" }"""));
        Assert.Equal(96, s.Current.BubbleSize);
        Assert.Equal(0.35, s.Current.BubbleOpacity);
        Assert.Equal(1, s.Current.DefaultSnooze);
        Assert.Equal("dark", s.Current.Theme);
        Assert.Equal("violet", s.Current.Accent);
    }

    [Fact]
    public void Patch_ignores_unknown_properties_and_never_lets_the_ui_set_the_bubble_position()
    {
        var s = new SettingsService(_t.Db);
        s.SetBubblePosition(100, 200);

        s.Patch(Json("""{ "bubbleX": 1, "bubbleY": 2, "nonsense": true }"""));

        Assert.Equal(100, s.Current.BubbleX);
        Assert.Equal(200, s.Current.BubbleY);
    }

    [Fact]
    public void Turning_off_snap_also_turns_off_auto_hide()
    {
        var s = new SettingsService(_t.Db);
        s.Patch(Json("""{ "snapToEdge": false }"""));
        Assert.False(s.Current.AutoHide);
    }

    [Fact]
    public void Bubble_position_is_persisted_without_raising_Changed()
    {
        var s = new SettingsService(_t.Db);
        var raised = 0;
        s.Changed += _ => raised++;

        s.SetBubblePosition(1820, 410);

        Assert.Equal(0, raised);
        var reloaded = new SettingsService(_t.Db);
        Assert.Equal(1820, reloaded.Current.BubbleX);
        Assert.Equal(410, reloaded.Current.BubbleY);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults_instead_of_crashing()
    {
        using (var c = _t.Db.Open())
            Database.Exec(c, "INSERT INTO settings (key, value) VALUES ('app', '{ not json')");

        var s = new SettingsService(_t.Db);
        Assert.Equal("dark", s.Current.Theme);
    }
}
