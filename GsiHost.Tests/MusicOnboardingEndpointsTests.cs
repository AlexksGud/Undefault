using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Music;
using FluentAssertions;
using GsiHost.Players.Smtc;
using GsiHost.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GsiHost.Tests;

[Collection(Cs2SetupTestCollection.Name)]
public sealed class MusicOnboardingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SpotifyAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
    private const string ExactSessionId = "Exact.SourceAppUserModelId";

    private readonly WebApplicationFactory<Program> _factory;

    public MusicOnboardingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSessions_WithMockProvider_ReturnsEmptyArrayAndCanonicalProvider()
    {
        using var host = CreateTestHost();

        using var response = await host.Client.GetAsync("/music/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        AssertFrozenPropertyNames(root, "provider", "selectedAppId", "sessions");
        root.GetProperty("provider").GetString().Should().Be("Mock");
        root.GetProperty("selectedAppId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("sessions").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetSessions_WithPresentSession_UsesSnapshotDisplayNameAndFrozenRowShape()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused(
            SpotifyAumid,
            track: new MusicTrack("id", "Title", "Artist", "Album"),
            isCurrent: true));
        using var host = CreateTestHost(sessionSource: source);

        using var response = await host.Client.GetAsync("/music/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = doc.RootElement.GetProperty("sessions")[0];
        AssertFrozenPropertyNames(
            session,
            "appId",
            "displayName",
            "playbackStatus",
            "track",
            "controls",
            "isWindowsCurrent",
            "isSelected");
        session.GetProperty("appId").GetString().Should().Be(SpotifyAumid);
        session.GetProperty("displayName").GetString().Should().Be("Spotify");
        session.GetProperty("playbackStatus").ValueKind.Should().Be(JsonValueKind.String);
        session.GetProperty("playbackStatus").GetString().Should().Be("Paused");
        session.GetProperty("track").GetProperty("title").GetString().Should().Be("Title");
        session.GetProperty("track").GetProperty("artist").GetString().Should().Be("Artist");
        session.GetProperty("controls").GetProperty("canPlay").GetBoolean().Should().BeTrue();
        session.GetProperty("controls").GetProperty("canPause").GetBoolean().Should().BeFalse();
        session.GetProperty("controls").GetProperty("canNext").GetBoolean().Should().BeTrue();
        session.GetProperty("isWindowsCurrent").GetBoolean().Should().BeTrue();
        session.GetProperty("isSelected").GetBoolean().Should().BeFalse();
        session.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetSessions_WhenSnapshotHasNoRow_UsesRawAppIdAsDisplayName()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused("Unknown.Player.AppId"));
        using var host = CreateTestHost(sessionSource: source);

        using var doc = JsonDocument.Parse(await host.Client.GetStringAsync("/music/sessions"));
        var session = doc.RootElement.GetProperty("sessions")[0];
        session.GetProperty("displayName").GetString().Should().Be("Unknown.Player.AppId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostSession_WhenAppIdMissingOrEmpty_Returns400(string? appId)
    {
        using var host = CreateTestHost();

        using var response = await host.Client.PostAsJsonAsync("/music/session", new { appId });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSession_WhenNoSessionsArePresent_Returns409AndDoesNotPersist()
    {
        using var host = CreateTestHost();

        using var response = await host.Client.PostAsJsonAsync("/music/session", new { appId = ExactSessionId });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var sessions = JsonDocument.Parse(await host.Client.GetStringAsync("/music/sessions"));
        sessions.RootElement.GetProperty("selectedAppId").ValueKind.Should().Be(JsonValueKind.Null);
        ReadPersistedAppId(host.TempRoot).Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostSession_WhenIdIsNotExactOrdinalMatch_Returns409AndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused(ExactSessionId));
        using var host = CreateTestHost(sessionSource: source);

        using var response = await host.Client.PostAsJsonAsync(
            "/music/session",
            new { appId = ExactSessionId.ToLowerInvariant() });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        source.Commands.Should().BeEmpty();

        using var sessions = JsonDocument.Parse(await host.Client.GetStringAsync("/music/sessions"));
        sessions.RootElement.GetProperty("selectedAppId").ValueKind.Should().Be(JsonValueKind.Null);
        sessions.RootElement.GetProperty("sessions")[0].GetProperty("isSelected").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task PostSession_WhenExactIdIsPresent_PersistsAndSurvivesRestart()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused(ExactSessionId));
        var tempRoot = CreateTempRoot();
        try
        {
            using (var host = CreateTestHost(sessionSource: source, tempRoot: tempRoot, ownsTempRoot: false))
            {
                using var response = await host.Client.PostAsJsonAsync(
                    "/music/session",
                    new { appId = ExactSessionId });
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                source.Commands.Should().BeEmpty();

                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                AssertFrozenPropertyNames(body.RootElement, "selectedAppId");
                body.RootElement.GetProperty("selectedAppId").GetString().Should().Be(ExactSessionId);

                using var sessions = JsonDocument.Parse(await host.Client.GetStringAsync("/music/sessions"));
                sessions.RootElement.GetProperty("selectedAppId").GetString().Should().Be(ExactSessionId);
                sessions.RootElement.GetProperty("sessions")[0].GetProperty("isSelected").GetBoolean().Should().BeTrue();
                ReadPersistedAppId(tempRoot).Should().Be(ExactSessionId);
            }

            using (var restarted = CreateTestHost(
                tempRoot: tempRoot,
                ownsTempRoot: false,
                writeAppSettings: false))
            {
                using var sessions = JsonDocument.Parse(await restarted.Client.GetStringAsync("/music/sessions"));
                sessions.RootElement.GetProperty("selectedAppId").GetString().Should().Be(ExactSessionId);
                sessions.RootElement.GetProperty("sessions").GetArrayLength().Should().Be(0);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TestPauseAndResume_Return200WithOutcome_AndNeverCommandHttp5xx()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        using var pause = await host.Client.PostAsync("/music/test/pause", content: null);
        pause.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await pause.Content.ReadAsStringAsync()))
        {
            AssertFrozenPropertyNames(body.RootElement, "outcome", "reason");
            body.RootElement.GetProperty("outcome").GetString().Should().Be("Applied");
            body.RootElement.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        }

        player.PauseCalls.Should().Be(1);

        using var resume = await host.Client.PostAsync("/music/test/resume", content: null);
        resume.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await resume.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("outcome").GetString().Should().Be("Applied");
        }

        player.ResumeCalls.Should().Be(1);
    }

    [Fact]
    public async Task TestPause_WhenPlayerUnavailable_Returns200WithNonAppliedOutcome()
    {
        var player = CreatePausedMockPlayer();
        player.Available = false;
        using var host = CreateTestHost(player);

        using var response = await host.Client.PostAsync("/music/test/pause", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("outcome").GetString().Should().Be("Unavailable");
        body.RootElement.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LastCommand_DistinguishesTestSourceFromGameSource()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        using var empty = JsonDocument.Parse(await host.Client.GetStringAsync("/music/last-command"));
        AssertFrozenPropertyNames(
            empty.RootElement,
            "command",
            "source",
            "targetAppId",
            "outcome",
            "reason",
            "atUtc");
        empty.RootElement.GetProperty("command").ValueKind.Should().Be(JsonValueKind.Null);
        empty.RootElement.GetProperty("source").ValueKind.Should().Be(JsonValueKind.Null);

        using var testPause = await host.Client.PostAsync("/music/test/pause", content: null);
        testPause.StatusCode.Should().Be(HttpStatusCode.OK);

        using var afterTest = JsonDocument.Parse(await host.Client.GetStringAsync("/music/last-command"));
        afterTest.RootElement.GetProperty("command").GetString().Should().Be("pause");
        afterTest.RootElement.GetProperty("source").GetString().Should().Be("test");
        afterTest.RootElement.GetProperty("outcome").GetString().Should().Be("Applied");
        DateTimeOffset.Parse(afterTest.RootElement.GetProperty("atUtc").GetString()!).Should()
            .BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100, round: 4, phase: "freezetime"));
        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1001, 100, round: 4, phase: "live"));
        using var death = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1002, 0, round: 4, phase: "live"));
        death.StatusCode.Should().Be(HttpStatusCode.OK);

        using var afterGame = JsonDocument.Parse(await host.Client.GetStringAsync("/music/last-command"));
        afterGame.RootElement.GetProperty("command").GetString().Should().Be("pause");
        afterGame.RootElement.GetProperty("source").GetString().Should().Be("game");
        afterGame.RootElement.GetProperty("outcome").GetString().Should().Be("Applied");
    }

    [Fact]
    public async Task Preset_GetAndPost_RoundTripFlowAndFocus_AndRejectUnknownName()
    {
        using var host = CreateTestHost();

        using var initial = JsonDocument.Parse(await host.Client.GetStringAsync("/music/preset"));
        AssertFrozenPropertyNames(initial.RootElement, "preset");
        initial.RootElement.GetProperty("preset").GetString().Should().Be("Flow");

        using var focus = await host.Client.PostAsJsonAsync("/music/preset", new { preset = "Focus" });
        focus.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await focus.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("preset").GetString().Should().Be("Focus");
        }

        using var afterFocus = JsonDocument.Parse(await host.Client.GetStringAsync("/music/preset"));
        afterFocus.RootElement.GetProperty("preset").GetString().Should().Be("Focus");

        using var flow = await host.Client.PostAsJsonAsync("/music/preset", new { preset = "Flow" });
        flow.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = JsonDocument.Parse(await flow.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty("preset").GetString().Should().Be("Flow");
        }

        using var unknown = await host.Client.PostAsJsonAsync("/music/preset", new { preset = "Jukebox" });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void DisplayNameCatalog_ResolvesSnapshotRow_AndNeverReturnsEmpty()
    {
        var catalog = new MediaPlayerDisplayNameCatalog(
            new StubWebHostEnvironment(Path.GetTempPath()),
            NullLogger<MediaPlayerDisplayNameCatalog>.Instance);

        catalog.Resolve(SpotifyAumid).Should().Be("Spotify");
        catalog.Resolve("Tauon.exe").Should().Be("Tauon");
        catalog.Resolve("missing-id").Should().Be("missing-id");
        catalog.Resolve(null).Should().NotBeNullOrWhiteSpace();
        catalog.Resolve("").Should().NotBeNullOrWhiteSpace();
    }

    private TestHostContext CreateTestHost(
        IMusicPlayer? musicPlayer = null,
        FakeSmtcSessionSource? sessionSource = null,
        string? tempRoot = null,
        bool ownsTempRoot = true,
        bool writeAppSettings = true)
    {
        var root = tempRoot ?? CreateTempRoot();
        var cs2Root = Path.Combine(root, "Counter-Strike Global Offensive");
        Directory.CreateDirectory(Path.Combine(cs2Root, "game", "csgo", "cfg"));
        Directory.CreateDirectory(root);
        if (writeAppSettings)
        {
            File.WriteAllText(Path.Combine(root, "appsettings.json"), BuildAppSettingsJson());
        }

        var previousOverride = Environment.GetEnvironmentVariable("UNDEFAULTIT_CS2_PATH");
        Environment.SetEnvironmentVariable("UNDEFAULTIT_CS2_PATH", cs2Root);

        var customizedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.ContentRootKey, root);
            builder.ConfigureServices(services =>
            {
                if (sessionSource is not null)
                {
                    services.AddSingleton<ISmtcSessionSource>(sessionSource);
                }

                services.AddSingleton<IMusicPlayer>(sp =>
                    musicPlayer ?? new MockMusicPlayer(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MockMusicPlayer>>()));
            });
        });

        return new TestHostContext(
            customizedFactory,
            customizedFactory.CreateClient(),
            root,
            previousOverride,
            ownsTempRoot);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "UndefaultIt.Tests", "onboarding", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static MockMusicPlayer CreatePausedMockPlayer()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Paused, volumePercent: 61);
        return player;
    }

    private static SmtcSessionSnapshot Paused(
        string id,
        MusicTrack? track = null,
        bool isCurrent = false)
        => new(
            SourceAppUserModelId: id,
            PlaybackStatus: PlaybackStatus.Paused,
            Track: track,
            IsPlayEnabled: true,
            IsPauseEnabled: false,
            IsNextEnabled: true,
            IsPreviousEnabled: true,
            IsCurrentSession: isCurrent);

    private static object CreatePayload(long timestamp, int health, int? round = null, string? phase = null)
    {
        return new
        {
            provider = new { timestamp },
            map = new { matchid = "match", round, phase },
            player = new
            {
                steamid = "player",
                activity = "playing",
                state = new { health, armor = 0 }
            }
        };
    }

    private static void AssertFrozenPropertyNames(JsonElement element, params string[] expected)
    {
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        names.Should().Equal(expected);
    }

    private static string? ReadPersistedAppId(string tempRoot)
    {
        var path = Path.Combine(tempRoot, "appsettings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        return root?["Music"]?["Smtc"]?["SourceAppUserModelId"]?.GetValue<string>();
    }

    private static string BuildAppSettingsJson()
    {
        return """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning"
            }
          },
          "AllowedHosts": "*",
          "Gsi": {
            "Method": "POST",
            "Path": "/gsi",
            "Url": "http://127.0.0.1:5292",
            "AllowReset": true
          },
          "Music": {
            "Provider": "Mock",
            "Smtc": {
              "SourceAppUserModelId": ""
            }
          },
          "Tauon": {
            "BaseUrl": "http://127.0.0.1:7814",
            "TimeoutSeconds": 2
          },
          "EventDetector": {
            "EnableRoundStart": true,
            "EnableDeath": true,
            "EnableCombat": false,
            "EnableIdle": false,
            "RoundStartPhase": "live",
            "DeathCooldown": "00:00:01"
          },
          "VolumeDuck": {
            "MuteVolume": 0,
            "FallbackRestoreVolume": 50
          },
          "Runtime": {
            "Mode": "scenario_playback"
          },
          "RulesEngine": {
            "ActionMap": {
              "round_start": [ "music.control_profile" ],
              "death": [ "music.control_profile" ]
            }
          },
          "MusicOrchestration": {
            "ShadowMode": false
          }
        }
        """;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "GsiHost.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public string ContentRootPath { get; set; }

        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TestHostContext : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string? _previousOverride;
        private readonly bool _ownsTempRoot;

        public TestHostContext(
            WebApplicationFactory<Program> factory,
            HttpClient client,
            string tempRoot,
            string? previousOverride,
            bool ownsTempRoot)
        {
            _factory = factory;
            Client = client;
            TempRoot = tempRoot;
            _previousOverride = previousOverride;
            _ownsTempRoot = ownsTempRoot;
        }

        public HttpClient Client { get; }

        public string TempRoot { get; }

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
            Environment.SetEnvironmentVariable("UNDEFAULTIT_CS2_PATH", _previousOverride);

            if (_ownsTempRoot && Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }
}
