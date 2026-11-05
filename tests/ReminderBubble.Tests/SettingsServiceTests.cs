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
        Assert.False(s.Current.SnapToEdge);                        // the bubble stays wherever it is dropped
        Assert.False(s.Current.AutoHide);
        Assert.Equal(10, s.Current.DefaultSnooze);
        Assert.True(s.Current.AutoUpdate);                         // updates are checked in the background by default
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
        Assert.Equal(160, s.Current.BubbleSize);
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
        s.Patch(Json("""{ "snapToEdge": true, "autoHide": true }"""));
        Assert.True(s.Current.AutoHide);

        s.Patch(Json("""{ "snapToEdge": false }"""));
        Assert.False(s.Current.AutoHide);
    }

    [Fact]
    public void Update_check_interval_defaults_to_six_hours_and_is_configurable_from_instant_to_a_week()
    {
        var s = new SettingsService(_t.Db);
        Assert.Equal(360, s.Current.UpdateIntervalMinutes);

        s.Patch(Json("""{ "updateIntervalMinutes": 1 }"""));            // "instant": every minute
        Assert.Equal(1, s.Current.UpdateIntervalMinutes);
        s.Patch(Json("""{ "updateIntervalMinutes": 90 }"""));           // 1 h 30 min
        Assert.Equal(90, s.Current.UpdateIntervalMinutes);
        s.Patch(Json("""{ "updateIntervalMinutes": 0 }"""));
        Assert.Equal(1, s.Current.UpdateIntervalMinutes);
        s.Patch(Json("""{ "updateIntervalMinutes": 999999 }"""));
        Assert.Equal(7 * 24 * 60, s.Current.UpdateIntervalMinutes);

        Assert.Equal(7 * 24 * 60, new SettingsService(_t.Db).Current.UpdateIntervalMinutes);   // persisted
    }

    [Fact]
    public void Bubble_size_can_be_anywhere_in_the_supported_range()
    {
        var s = new SettingsService(_t.Db);
        s.Patch(Json("""{ "bubbleSize": 40 }"""));
        Assert.Equal(40, s.Current.BubbleSize);
        s.Patch(Json("""{ "bubbleSize": 160 }"""));
        Assert.Equal(160, s.Current.BubbleSize);
        s.Patch(Json("""{ "bubbleSize": 10 }"""));
        Assert.Equal(40, s.Current.BubbleSize);
    }

    [Fact]
    public void Settings_saved_by_the_edge_snapping_version_are_moved_to_free_placement_once()
    {
        // what version 1 stored: snapping + auto-hide on, no version number, a custom size
        using (var c = _t.Db.Open())
            Database.Exec(c, "INSERT INTO settings (key, value) VALUES ('app', @v)",
                ("@v", """{ "theme": "light", "bubbleSize": 80, "snapToEdge": true, "autoHide": true, "welcomed": true }"""));

        var s = new SettingsService(_t.Db);

        Assert.False(s.Current.SnapToEdge);
        Assert.False(s.Current.AutoHide);
        Assert.Equal("light", s.Current.Theme);                    // everything else is kept
        Assert.Equal(80, s.Current.BubbleSize);
        Assert.Equal(ReminderBubble.Models.AppSettings.CurrentVersion, s.Current.Version);

        // the user turns snapping back on: the migration must not run again
        s.Patch(Json("""{ "snapToEdge": true }"""));
        Assert.True(new SettingsService(_t.Db).Current.SnapToEdge);
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
