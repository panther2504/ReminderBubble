using System.Net;
using ReminderBubble.Services;
using Xunit;

namespace ReminderBubble.Tests;

public sealed class UpdateCheckerTests
{
    static readonly Version Current = new(1, 1, 3, 0);

    static string Release(string tag = "v1.2.0", bool draft = false, bool pre = false, string? assets = null) => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLower()}}, "prerelease": {{pre.ToString().ToLower()}},
          "html_url": "https://github.com/panther2504/ReminderBubble/releases/tag/{{tag}}",
          "body": "What's new: faster.",
          "assets": [{{assets ?? Asset("ReminderBubble-Setup-1.2.0.exe")}}]
        }
        """;

    static string Asset(string name, string? url = null, string? digest = null) =>
        $$"""{ "name": "{{name}}", "browser_download_url": "{{url ?? "https://github.com/panther2504/ReminderBubble/releases/download/v1.2.0/" + name}}"{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }""";

    [Fact]
    public void A_newer_release_is_offered_with_its_installer()
    {
        var info = UpdateChecker.Parse(Release(), Current);

        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version);
        Assert.Equal("ReminderBubble-Setup-1.2.0.exe", info.FileName);
        Assert.StartsWith("https://github.com/", info.Url);
        Assert.Contains("faster", info.Notes);
    }

    [Theory]
    [InlineData("v1.1.3")]     // same version
    [InlineData("v1.1.2")]     // older
    [InlineData("1.1.3")]      // same, no "v"
    [InlineData("v1.1")]       // 1.1 < 1.1.3
    public void Same_or_older_releases_are_ignored(string tag) =>
        Assert.Null(UpdateChecker.Parse(Release(tag), Current));

    [Theory]
    [InlineData("v1.1.4")]
    [InlineData("v1.2")]
    [InlineData("v2.0.0")]
    [InlineData("1.10.0")]     // 1.10 is newer than 1.9 - numeric, not alphabetical
    public void Newer_tags_are_detected(string tag) =>
        Assert.NotNull(UpdateChecker.Parse(Release(tag), new Version(1, 1, 3)));

    [Fact]
    public void Draft_and_prerelease_builds_are_never_installed()
    {
        Assert.Null(UpdateChecker.Parse(Release(draft: true), Current));
        Assert.Null(UpdateChecker.Parse(Release(pre: true), Current));
    }

    [Fact]
    public void Garbage_tags_and_json_without_the_expected_fields_are_ignored()
    {
        Assert.Null(UpdateChecker.Parse(Release("nightly"), Current));
        Assert.Null(UpdateChecker.Parse("{}", Current));
        Assert.Null(UpdateChecker.Parse("[]", Current));
        Assert.Null(UpdateChecker.Parse("""{ "tag_name": "v9.0.0" }""", Current));         // no assets
    }

    [Fact]
    public void The_small_installer_is_chosen_over_the_offline_one_and_other_files_are_skipped()
    {
        var assets = string.Join(",",
            Asset("ReminderBubble-win-x64.zip"),
            Asset("ReminderBubble-Setup-1.2.0-offline.exe"),
            Asset("ReminderBubble-Setup-1.2.0.exe"));

        Assert.Equal("ReminderBubble-Setup-1.2.0.exe", UpdateChecker.Parse(Release(assets: assets), Current)!.FileName);
    }

    [Fact]
    public void A_release_with_only_the_offline_installer_or_a_zip_is_not_auto_installed()
    {
        var assets = string.Join(",", Asset("ReminderBubble-Setup-1.2.0-offline.exe"), Asset("ReminderBubble-win-x64.zip"));
        Assert.Null(UpdateChecker.Parse(Release(assets: assets), Current));
    }

    [Theory]
    [InlineData("http://github.com/x/ReminderBubble-Setup-1.2.0.exe")]                 // not https
    [InlineData("https://evil.example.com/ReminderBubble-Setup-1.2.0.exe")]            // not GitHub
    [InlineData("https://github.com.evil.example/ReminderBubble-Setup-1.2.0.exe")]     // look-alike host
    public void Downloads_from_untrusted_places_are_refused(string url) =>
        Assert.Null(UpdateChecker.Parse(Release(assets: Asset("ReminderBubble-Setup-1.2.0.exe", url)), Current));

    [Theory]
    [InlineData("https://objects.githubusercontent.com/abc/ReminderBubble-Setup-1.2.0.exe")]
    [InlineData("https://github.com/panther2504/ReminderBubble/releases/download/v1.2.0/ReminderBubble-Setup-1.2.0.exe")]
    public void GitHub_download_hosts_are_accepted(string url) =>
        Assert.NotNull(UpdateChecker.Parse(Release(assets: Asset("ReminderBubble-Setup-1.2.0.exe", url)), Current));

    [Fact]
    public void The_sha256_digest_is_picked_up_for_verification()
    {
        var info = UpdateChecker.Parse(Release(assets: Asset("ReminderBubble-Setup-1.2.0.exe", digest: "sha256:ABCDEF0123")), Current)!;
        Assert.Equal("abcdef0123", info.Sha256);
        Assert.Null(UpdateChecker.Parse(Release(), Current)!.Sha256);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("v1.2.3-beta.1", "1.2.3")]
    public void Versions_are_parsed_leniently(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseVersion(tag, out var v));
        Assert.Equal(UpdateChecker.Normalize(Version.Parse(expected)), v);
    }

    // ---- over HTTP (stubbed) -------------------------------------------------------------------

    sealed class Stub(HttpStatusCode code, string body = "") : HttpMessageHandler
    {
        public Uri? Requested;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task CheckAsync_asks_the_projects_latest_release_endpoint()
    {
        var stub = new Stub(HttpStatusCode.OK, Release());
        var info = await UpdateChecker.CheckAsync(new HttpClient(stub), Current);

        Assert.Equal("https://api.github.com/repos/panther2504/ReminderBubble/releases/latest", stub.Requested!.ToString());
        Assert.Equal("1.2.0", info!.Version);
    }

    /// <summary>Behaves like GitHub: 200 + ETag the first time, 304 when the caller sends that ETag back.</summary>
    sealed class EtagServer(string body) : HttpMessageHandler
    {
        public List<string?> IfNoneMatch { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var sent = request.Headers.TryGetValues("If-None-Match", out var v) ? v.First() : null;
            IfNoneMatch.Add(sent);
            if (sent == "\"v1\"") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            resp.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task Repeat_checks_are_conditional_so_checking_every_minute_does_not_burn_the_rate_limit()
    {
        var server = new EtagServer(Release());
        var http = new HttpClient(server);
        var feed = new UpdateFeed();

        var first = await UpdateChecker.CheckAsync(http, Current, feed);
        var second = await UpdateChecker.CheckAsync(http, Current, feed);
        var third = await UpdateChecker.CheckAsync(http, Current, feed);

        Assert.Equal("1.2.0", first!.Version);
        Assert.Same(first, second);                 // answered from the cache
        Assert.Same(first, third);
        Assert.Equal(new string?[] { null, "\"v1\"", "\"v1\"" }, server.IfNoneMatch);
    }

    [Fact]
    public async Task A_cached_answer_of_no_update_is_remembered_too()
    {
        var server = new EtagServer(Release("v1.1.3"));       // same version as running
        var http = new HttpClient(server);
        var feed = new UpdateFeed();

        Assert.Null(await UpdateChecker.CheckAsync(http, Current, feed));
        Assert.Null(await UpdateChecker.CheckAsync(http, Current, feed));
        Assert.Equal("\"v1\"", server.IfNoneMatch[1]);
    }

    [Fact]
    public async Task CheckAsync_explains_a_missing_release_and_other_http_errors()
    {
        var notFound = await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.CheckAsync(new HttpClient(new Stub(HttpStatusCode.NotFound)), Current));
        Assert.Contains("public", notFound.Message);

        var limited = await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.CheckAsync(new HttpClient(new Stub(HttpStatusCode.Forbidden)), Current));
        Assert.Contains("403", limited.Message);
    }
}
