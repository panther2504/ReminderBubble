using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace ReminderBubble.Services;

/// <summary>
/// Background updater: checks GitHub Releases, downloads the new installer, verifies it, and on request runs it
/// silently and restarts the app. Always used from the UI thread (its events are posted straight to the web UI).
/// </summary>
public sealed class UpdateService
{
    readonly SettingsService _settings;
    readonly HttpClient _http;
    readonly Version _current;
    readonly UpdateFeed _feed = new();
    readonly string _dir = Path.Combine(Log.Dir, "updates");
    string? _file;

    /// <summary>idle | checking | uptodate | available | downloading | ready | error</summary>
    public string State { get; private set; } = "idle";
    public UpdateInfo? Latest { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastChecked { get; private set; }

    public event Action<object>? Changed;

    public UpdateService(SettingsService settings)
    {
        _settings = settings;
        _current = UpdateChecker.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0));
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ReminderBubble/{_current.ToString(3)}");
        TryCleanDownloads();
    }

    public object Snapshot => new
    {
        current = _current.ToString(3),
        state = State,
        latest = Latest?.Version,
        notes = Latest?.Notes,
        error = Error,
        lastChecked = LastChecked,
    };

    void Notify() => Changed?.Invoke(Snapshot);

    void TryCleanDownloads()
    {
        // Leftovers from an earlier update or an interrupted download.
        try { if (Directory.Exists(_dir)) foreach (var f in Directory.GetFiles(_dir)) File.Delete(f); }
        catch (Exception ex) { Log.Write("Update cleanup: " + ex.Message); }
    }

    /// <summary>Checks for a newer release; downloads it when auto-update is on (or when the user asked explicitly).</summary>
    public async Task CheckAsync(bool manual)
    {
        if (State is "checking" or "downloading") return;
        var wasReady = State == "ready";
        if (!wasReady) { State = "checking"; Error = null; Notify(); }
        try
        {
            var info = await UpdateChecker.CheckAsync(_http, _current, _feed).ConfigureAwait(true);
            LastChecked = DateTime.UtcNow;
            if (info is null) { if (!wasReady) { State = "uptodate"; Latest = null; } }
            else if (wasReady && Latest?.Version == info.Version) { /* already downloaded */ }
            else
            {
                Latest = info;
                State = "available";
                Notify();
                if (_settings.Current.AutoUpdate || manual) await DownloadAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Update check failed: " + ex.Message);
            if (!wasReady) { State = "error"; Error = ex is TaskCanceledException ? "The update server did not answer in time." : ex.Message; }
        }
        Notify();
    }

    async Task DownloadAsync()
    {
        if (Latest is null) return;
        State = "downloading"; Error = null; Notify();
        try
        {
            Directory.CreateDirectory(_dir);
            var target = Path.Combine(_dir, Path.GetFileName(Latest.FileName));
            var part = target + ".part";
            using (var resp = await _http.GetAsync(Latest.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(true);
                await using var dst = File.Create(part);
                await src.CopyToAsync(dst).ConfigureAwait(true);
            }

            if (Latest.Sha256 is { Length: > 0 } expected)
            {
                string actual;
                await using (var fs = File.OpenRead(part))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(fs).ConfigureAwait(true)).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part);
                    throw new InvalidOperationException("The downloaded update failed its integrity check and was discarded.");
                }
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
            _file = target;
            State = "ready";
        }
        catch (Exception ex)
        {
            Log.Write("Update download failed: " + ex.Message);
            State = "error"; Error = ex.Message;
        }
    }

    /// <summary>
    /// Starts the downloaded installer silently (it closes this app, upgrades in place, then a relaunch follows).
    /// Returns true when the caller should now exit the app.
    /// </summary>
    public bool Install()
    {
        if (State != "ready" || _file is null || !File.Exists(_file)) return false;
        try
        {
            var exe = Environment.ProcessPath ?? "";
            // One hidden cmd: run the installer to completion, then start the app again.
            var args = $"/c \"\"{_file}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- & start \"\" \"{exe}\"\"";
            Process.Start(new ProcessStartInfo("cmd.exe", args) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Update launch failed: " + ex.Message);
            State = "error"; Error = "Could not start the installer: " + ex.Message; Notify();
            return false;
        }
    }
}
