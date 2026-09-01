using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Configuration;
using Core.Music;
using Core.Services;
using Cs2Simulator.Runtime;
using Cs2Simulator.Scenarios.Scenarios;
using FluentAssertions;
using GsiHost.Adapters;
using GsiHost.Dtos;
using GsiHost.Mapping;
using GsiHost.Mapping.Modules;
using GsiHost.Players;
using GsiHost.Services;
using GsiHost.Tooling.Timeline;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GsiHost.Tests;

[Collection(Cs2SetupTestCollection.Name)]
public sealed class GsiHostIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GsiHostIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GsiEndpoint_AcceptsPayload_AndCreatesEvents()
    {
        using var host = CreateTestHost();

        var response1 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100));
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        var response2 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1001, 0));
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response2.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var eventsCount = doc.RootElement.GetProperty("events").GetInt32();

        eventsCount.Should().BeGreaterThanOrEqualTo(1);

        var appState = host.Factory.Services.GetRequiredService<AppStateService>();
        appState.GetRecentEvents().Count.Should().BeGreaterThanOrEqualTo(1);

        var eventsJson = await host.Client.GetStringAsync("/events");
        using (var evDoc = JsonDocument.Parse(eventsJson))
        {
            evDoc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task StatusAndEventsEndpoints_ReturnData()
    {
        using var host = CreateTestHost();

        var status = await host.Client.GetAsync("/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await host.Client.GetAsync("/events");
        events.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GsiEndpoint_AllowsEmptyPayload()
    {
        using var host = CreateTestHost();

        var response = await host.Client.PostAsJsonAsync("/gsi", new { });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusEndpoint_HandlesMusicPlayerFailure()
    {
        using var host = CreateTestHost(new ThrowingMusicPlayer());

        var response = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await host.Client.GetAsync("/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void MusicProviderTauon_WithoutReplace_RegistersTauonMusicPlayer()
    {
        using var host = CreateTestHost(
            replaceMusicPlayer: false,
            appSettingsJson: BuildAppSettingsJson("http://127.0.0.1:5292", musicProvider: "Tauon"));

        host.Factory.Services.GetRequiredService<IMusicPlayer>().Should().BeOfType<TauonMusicPlayer>();
    }

    [Fact]
    public void MusicProviderMock_WithoutReplace_RegistersMockMusicPlayer()
    {
        using var host = CreateTestHost(
            replaceMusicPlayer: false,
            appSettingsJson: BuildAppSettingsJson("http://127.0.0.1:5292", musicProvider: "Mock"));

        host.Factory.Services.GetRequiredService<IMusicPlayer>().Should().BeOfType<MockMusicPlayer>();
    }

    [Fact]
    public async Task StatusEndpoint_ReportsMusicPlayerFieldsFromInjectedPlayer()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        var response = await host.Client.GetAsync("/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("musicProvider").GetString().Should().Be("Mock");
        root.GetProperty("musicPlayerAvailable").GetBoolean().Should().BeTrue();
        root.GetProperty("playbackState").GetString().Should().Be("Paused");
        root.TryGetProperty("leftoverSpotifyStatus", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ProfilesEndpoint_RoundTripsNewSchema()
    {
        using var host = CreateTestHost();

        var payload = new MusicProfilesConfig(
            "default",
            new List<MusicProfile>
            {
                new(
                    "default",
                    "Default",
                    new List<EventTrackRule>
                    {
                        new("death", new List<string> { "spotify:track:death_song" }),
                        new("custom:clutch_1v3", new List<string> { "spotify:track:clutch_song" })
                    })
            });

        var saveResponse = await host.Client.PutAsJsonAsync("/profiles", payload);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var roundTrip = await host.Client.GetFromJsonAsync<MusicProfilesConfig>("/profiles");

        roundTrip.Should().NotBeNull();
        roundTrip!.Profiles.Should().ContainSingle();
        roundTrip.Profiles[0].Rules.Should().HaveCount(2);
        roundTrip.Profiles[0].FindRule("CUSTOM:CLUTCH_1V3")!.Tracks.Should().ContainSingle("spotify:track:clutch_song");
    }

    [Fact]
    public async Task ControlProfilesEndpoint_RoundTripsConsoleControlSchema()
    {
        using var host = CreateTestHost();

        var payload = new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new(
                    "console-default",
                    "Console Default",
                    new List<EventControlRule>
                    {
                        new("round_start", MusicControlCommands.Duck, 15),
                        new("death", MusicControlCommands.RestoreVolume),
                        new("custom:music_off", MusicControlCommands.Pause),
                        new("custom:music_on", MusicControlCommands.Resume)
                    })
            });

        var saveResponse = await host.Client.PutAsJsonAsync("/control-profiles", payload);
        saveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var roundTrip = await host.Client.GetFromJsonAsync<ConsoleControlProfilesConfig>("/control-profiles");

        roundTrip.Should().NotBeNull();
        roundTrip!.Profiles.Should().ContainSingle();
        roundTrip.Profiles[0].Rules.Should().HaveCount(4);
        roundTrip.Profiles[0].FindRule("CUSTOM:MUSIC_OFF")!.Command.Should().Be(MusicControlCommands.Pause);
    }

    [Fact]
    public async Task Cs2SetupStatus_ReflectsAutoInstalledConfigUsingConfiguredUri()
    {
        const string gsiBaseUrl = "http://127.0.0.1:6875";
        using var host = CreateTestHost(gsiBaseUrl: gsiBaseUrl);

        var status = await host.Client.GetFromJsonAsync<Cs2SetupStatus>("/setup/cs2/status");

        status.Should().NotBeNull();
        status!.IsCs2Found.Should().BeTrue();
        status.IsCfgInstalled.Should().BeTrue();
        status.IsCfgCurrent.Should().BeTrue();
        status.IsReady.Should().BeTrue();
        status.GsiUri.Should().Be($"{gsiBaseUrl}/gsi");

        var cfgPath = Path.Combine(host.Cs2Root, "game", "csgo", "cfg", "gamestate_integration_undefaultit.cfg");
        File.Exists(cfgPath).Should().BeTrue();
        var cfgContent = await File.ReadAllTextAsync(cfgPath);
        cfgContent.Should().Contain($"{gsiBaseUrl}/gsi");
    }

    [Fact]
    public async Task PostGsiReset_ReturnsNoContent_AndClearsState()
    {
        using var host = CreateTestHost();

        var pre1 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2000, 100));
        pre1.StatusCode.Should().Be(HttpStatusCode.OK);
        var pre2 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2001, 0));
        pre2.StatusCode.Should().Be(HttpStatusCode.OK);
        var preBody = await pre2.Content.ReadAsStringAsync();
        using (var preDoc = JsonDocument.Parse(preBody))
        {
            preDoc.RootElement.GetProperty("events").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        }

        var eventsBeforeReset = await host.Client.GetStringAsync("/events");
        using (var evDoc = JsonDocument.Parse(eventsBeforeReset))
        {
            evDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        }

        var resetResponse = await host.Client.PostAsync("/gsi/reset", content: null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var eventsAfterReset = await host.Client.GetStringAsync("/events");
        using (var evDoc2 = JsonDocument.Parse(eventsAfterReset))
        {
            evDoc2.RootElement.GetArrayLength().Should().Be(0);
        }

        var post1 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(3000, 100));
        post1.StatusCode.Should().Be(HttpStatusCode.OK);
        var post1Body = await post1.Content.ReadAsStringAsync();
        using (var post1Doc = JsonDocument.Parse(post1Body))
        {
            post1Doc.RootElement.GetProperty("events").GetInt32().Should().Be(0);
        }

        var post2 = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(3001, 0));
        post2.StatusCode.Should().Be(HttpStatusCode.OK);
        var post2Body = await post2.Content.ReadAsStringAsync();
        using var post2Doc = JsonDocument.Parse(post2Body);
        post2Doc.RootElement.GetProperty("events").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task PostGsiReset_ReturnsForbidden_WhenAllowResetIsFalse()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildAppSettingsJson("http://127.0.0.1:5292", allowGsiReset: false));

        var response = await host.Client.PostAsync("/gsi/reset", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cs2Simulator_TSideRound_ViaHttpTransport_SurfacesRoundStartOnEvents()
    {
        using var host = CreateTestHost();
        EnsureClientBaseAddressHasTrailingSlash(host.Client);

        var transport = new HttpGsiTransport(host.Client, NullLogger<HttpGsiTransport>.Instance);
        var runner = new ScenarioRunner(transport, new NullStepGate(), NullLogger<ScenarioRunner>.Instance);
        await runner.RunAsync(
            new TSideRoundScenario(),
            new ScenarioRunOptions { ResetBeforeRun = true, Speed = Speed.Max, VerboseLogging = false },
            CancellationToken.None);

        var body = await host.Client.GetStringAsync("/events");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.EnumerateArray().Count(e =>
                string.Equals(
                    TryGetStringIgnoreCase(e, "eventKey"),
                    "round_start",
                    StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task DefaultControlProfile_ResumesOnRoundStart_AndPausesOnDeath()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100, round: 4, phase: "freezetime"));
        var roundStartResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1001, 100, round: 4, phase: "live"));
        roundStartResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deathResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1002, 0, round: 4, phase: "live"));
        deathResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        File.Exists(Path.Combine(host.TempRoot, "control-profiles.json")).Should().BeTrue();
        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(1);
    }

    [Fact]
    public async Task LegacySpotifyControlProfile_ResumesOnRoundStart_AndPausesOnDeath()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildAppSettingsJson(
                "http://127.0.0.1:5292",
                roundStartAction: "spotify.control_profile",
                deathAction: "spotify.control_profile"));

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100, round: 4, phase: "freezetime"));
        var roundStartResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1001, 100, round: 4, phase: "live"));
        roundStartResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deathResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1002, 0, round: 4, phase: "live"));
        deathResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(1);
    }

    [Fact]
    public async Task GsiEndpoint_WhenMusicPlayerUnavailable_StillReturnsOk()
    {
        var player = CreatePausedMockPlayer();
        player.Available = false;
        using var host = CreateTestHost(player);

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1000, 100, round: 4, phase: "freezetime"));
        var roundStartResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1001, 100, round: 4, phase: "live"));
        var deathResponse = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1002, 0, round: 4, phase: "live"));

        roundStartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        deathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        player.PlayCalls.Should().Be(0);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task GsiReset_ClearsTimelineEntries()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(4200, 100, round: 6, phase: "freezetime"));
        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(4201, 100, round: 6, phase: "live"));

        var before = await host.Client.GetStringAsync("/timeline");
        using (var beforeDoc = JsonDocument.Parse(before))
        {
            beforeDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0);
        }

        var resetResponse = await host.Client.PostAsync("/gsi/reset", content: null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await host.Client.GetStringAsync("/timeline");
        using var afterDoc = JsonDocument.Parse(after);
        afterDoc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GsiEndpoint_RoundStart_UsesSinglePlayerSideEffectPath()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1100, 100, round: 8, phase: "freezetime"));
        var response = await host.Client.PostAsJsonAsync("/gsi", CreatePayload(1101, 100, round: 8, phase: "live"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        player.PlaybackSideEffectCalls.Should().Be(1);
        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(0);
        player.VolumeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GsiEndpoint_ShadowMode_RoundStartTick_ResumesOnce_AndShadowReportsSafe()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2200, 100, round: 11, phase: "freezetime"));
        var response = await host.Client.PostAsJsonAsync(
            "/gsi",
            CreatePayload(2201, 100, round: 11, phase: "live"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(0);
        player.VolumeCalls.Should().BeEmpty();

        var shadow = await host.Client.GetStringAsync("/diagnostics/music-shadow");
        using var doc = JsonDocument.Parse(shadow);
        var latest = doc.RootElement.GetProperty("latest");
        latest.ValueKind.Should().Be(JsonValueKind.Object);
        latest.GetProperty("desiredSafetyState").GetInt32().Should().Be((int)Core.Music.MusicSafetyState.Safe);
        doc.RootElement.GetProperty("recent").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GsiEndpoint_ShadowMode_DeathTick_PausesOnce_AndShadowReportsDanger()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(player);

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2300, 100, round: 12, phase: "freezetime"));
        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2301, 100, round: 12, phase: "live"));

        var pauseCallsBeforeDeath = player.PauseCalls;

        var deathResponse = await host.Client.PostAsJsonAsync(
            "/gsi",
            CreatePayload(2302, 0, round: 12, phase: "live"));

        deathResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (player.PauseCalls - pauseCallsBeforeDeath).Should().Be(1, "death must pause exactly once via the ActionMap path");
        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.VolumeCalls.Should().BeEmpty();

        var shadow = await host.Client.GetStringAsync("/diagnostics/music-shadow");
        using var doc = JsonDocument.Parse(shadow);
        var latest = doc.RootElement.GetProperty("latest");
        latest.GetProperty("desiredSafetyState").GetInt32().Should().Be((int)Core.Music.MusicSafetyState.Danger);
    }

    [Fact]
    public async Task MusicShadowDiagnostics_ShadowModeDisabled_ReturnsEmptyAndDoesNotInvokeFacade()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildAppSettingsJson("http://127.0.0.1:5292", musicOrchestrationShadowMode: false));

        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2400, 100, round: 13, phase: "freezetime"));
        await host.Client.PostAsJsonAsync("/gsi", CreatePayload(2401, 100, round: 13, phase: "live"));

        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);

        var shadow = await host.Client.GetStringAsync("/diagnostics/music-shadow");
        using var doc = JsonDocument.Parse(shadow);
        doc.RootElement.GetProperty("latest").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("recent").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AdapterDiagnostics_ListsRegisteredCs2AndDotaAdapters()
    {
        using var host = CreateTestHost();

        var body = await host.Client.GetStringAsync("/diagnostics/adapters");

        using var doc = JsonDocument.Parse(body);
        var adapters = doc.RootElement.GetProperty("adapters");
        adapters.GetArrayLength().Should().Be(2);

        var cs2 = adapters.EnumerateArray().Single(a => a.GetProperty("titleId").GetString() == "cs2");
        cs2.GetProperty("appId").GetInt32().Should().Be(730);
        cs2.GetProperty("endpointPath").GetString().Should().Be("/gsi");
        cs2.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();

        var dota = adapters.EnumerateArray().Single(a => a.GetProperty("titleId").GetString() == "dota2");
        dota.GetProperty("appId").GetInt32().Should().Be(570);
        dota.GetProperty("endpointPath").GetString().Should().Be("/gsi/dota");
        dota.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Cs2GameAdapter_PreservesGsiSnapshotMapperOutput()
    {
        var mapper = CreateSnapshotMapper();
        var adapter = new Cs2GameAdapter(mapper);
        var payload = CreatePayloadDto(1200, 100, round: 9, phase: "live");
        var receivedAt = DateTimeOffset.UnixEpoch.AddSeconds(1200);

        var mapped = mapper.Map(payload, receivedAt);
        var observed = adapter.Adapt(payload, receivedAt);

        observed.Raw.Should().BeEquivalentTo(mapped);
    }

    [Fact]
    public async Task ScenarioPlayback_TimelineEndpoints_AreNotMapped()
    {
        using var host = CreateTestHost();

        var timeline = await host.Client.GetAsync("/timeline");
        var episodes = await host.Client.GetAsync("/timeline/episodes");

        timeline.StatusCode.Should().Be(HttpStatusCode.NotFound);
        episodes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task IntentCapture_TimelineEndpoint_ReturnsOk()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        var timeline = await host.Client.GetAsync("/timeline");

        timeline.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void ScenarioPlayback_DoesNotRegisterPlaybackStateObserverHostedService()
    {
        using var host = CreateTestHost();

        var hostedServices = host.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        hostedServices.OfType<PlaybackStateObserver>().Should().BeEmpty();
    }

    [Fact]
    public void IntentCapture_RegistersPlaybackStateObserverHostedService()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        var hostedServices = host.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        hostedServices.OfType<PlaybackStateObserver>().Should().HaveCount(1);
    }

    [Fact]
    public async Task PlaybackObserver_PauseTransition_RecordsPausedInTimelineAndJsonl()
    {
        var player = CreatePlayingMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildObserverEnabledIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        // Wait for the observer to establish a "playing" baseline before introducing the transition,
        // so the next poll detects a real true -> false change (AC #1).
        (await WaitForAsync(
            () => player.GetStateCalls >= 1,
            TimeSpan.FromSeconds(8))).Should().BeTrue("observer should poll at least once for a baseline");

        player.SeedState(PlaybackStatus.Paused);

        var paused = await WaitForPlaybackEntryAsync(host.Client, TimelinePlaybackEvents.Paused, TimeSpan.FromSeconds(8));
        paused.Should().NotBeNull();
        paused!.TimestampUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        var onDisk = ReadTimelineEntriesFromDisk(host.TempRoot);
        onDisk.Should().Contain(e =>
            e.Source == TimelineSources.Playback && e.EventKey == TimelinePlaybackEvents.Paused);
    }

    [Fact]
    public async Task PlaybackObserver_ResumeTransition_RecordsResumedInTimelineAndJsonl()
    {
        var player = CreatePausedMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildObserverEnabledIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        (await WaitForAsync(
            () => player.GetStateCalls >= 1,
            TimeSpan.FromSeconds(8))).Should().BeTrue("observer should poll at least once for a baseline");

        player.SeedState(PlaybackStatus.Playing);

        var resumed = await WaitForPlaybackEntryAsync(host.Client, TimelinePlaybackEvents.Resumed, TimeSpan.FromSeconds(8));
        resumed.Should().NotBeNull();
        resumed!.TimestampUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        var onDisk = ReadTimelineEntriesFromDisk(host.TempRoot);
        onDisk.Should().Contain(e =>
            e.Source == TimelineSources.Playback && e.EventKey == TimelinePlaybackEvents.Resumed);
    }

    [Fact]
    public async Task PlaybackObserver_ResetStartsNewJsonlSessionFile()
    {
        var player = CreatePlayingMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildObserverEnabledIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        (await WaitForAsync(
            () => player.GetStateCalls >= 1,
            TimeSpan.FromSeconds(8))).Should().BeTrue("observer should poll at least once for a baseline");

        // First transition -> playback_paused recorded to the first JSONL session file.
        player.SeedState(PlaybackStatus.Paused);
        var firstPaused = await WaitForPlaybackEntryAsync(host.Client, TimelinePlaybackEvents.Paused, TimeSpan.FromSeconds(8));
        firstPaused.Should().NotBeNull();

        var filesBefore = GetSessionFiles(host.TempRoot);
        filesBefore.Should().HaveCountGreaterThanOrEqualTo(1);

        // Reset clears the in-memory timeline, starts a new JSONL session, and clears the
        // observer baseline so the next usable poll re-establishes state without a spurious entry.
        var resetResponse = await host.Client.PostAsync("/gsi/reset", content: null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var inMemoryAfterReset = await GetTimelineEntriesAsync(host.Client);
        inMemoryAfterReset.Should().BeEmpty("reset clears the in-memory timeline");

        // Still paused after reset: first post-reset poll is a fresh baseline (no record).
        var pollsAtReset = player.GetStateCalls;
        (await WaitForAsync(
            () => player.GetStateCalls > pollsAtReset,
            TimeSpan.FromSeconds(8))).Should().BeTrue("observer should re-baseline after reset");

        (await GetTimelineEntriesAsync(host.Client)).Should().BeEmpty(
            "first usable observation after reset must not emit a transition");

        // Now a real transition writes into the new session file.
        player.SeedState(PlaybackStatus.Playing);
        var resumed = await WaitForPlaybackEntryAsync(host.Client, TimelinePlaybackEvents.Resumed, TimeSpan.FromSeconds(8));
        resumed.Should().NotBeNull();

        var filesAfter = GetSessionFiles(host.TempRoot);
        var newSessionFile = filesAfter.Except(filesBefore, StringComparer.Ordinal).ToList();
        newSessionFile.Should().HaveCount(1, "reset must start a new JSONL session file");

        var newFileEntries = ReadTimelineEntriesFromFile(newSessionFile[0]);
        newFileEntries.Should().Contain(e =>
            e.Source == TimelineSources.Playback && e.EventKey == TimelinePlaybackEvents.Resumed);
    }

    [Fact]
    public async Task PlaybackObserver_JsonlRoundTripsToTimelineEntry_AndMatchesInMemory()
    {
        var player = CreatePlayingMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildObserverEnabledIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        (await WaitForAsync(
            () => player.GetStateCalls >= 1,
            TimeSpan.FromSeconds(8))).Should().BeTrue("observer should poll at least once for a baseline");

        player.SeedState(PlaybackStatus.Paused);
        await WaitForPlaybackEntryAsync(host.Client, TimelinePlaybackEvents.Paused, TimeSpan.FromSeconds(8));

        var inMemory = await GetTimelineEntriesAsync(host.Client);
        var onDisk = ReadTimelineEntriesFromDisk(host.TempRoot);

        inMemory.Should().NotBeEmpty();
        onDisk.Should().NotBeEmpty();

        // Every in-memory entry must round-trip from JSONL with identical core fields.
        foreach (var entry in inMemory)
        {
            onDisk.Should().Contain(d =>
                d.Sequence == entry.Sequence &&
                d.Source == entry.Source &&
                d.EventKey == entry.EventKey &&
                d.TimestampUtc == entry.TimestampUtc,
                $"in-memory entry #{entry.Sequence} ({entry.Source}/{entry.EventKey}) must round-trip from JSONL");
        }

        var paused = inMemory.Single(e =>
            e.Source == TimelineSources.Playback && e.EventKey == TimelinePlaybackEvents.Paused);
        var pausedOnDisk = onDisk.Single(d => d.Sequence == paused.Sequence);
        pausedOnDisk.Should().BeEquivalentTo(paused);
    }

    [Fact]
    public async Task PlaybackTransition_ScenarioPlayback_DoesNotRecordPlaybackTransition()
    {
        var player = CreatePlayingMockPlayer();
        using var host = CreateTestHost(
            player,
            appSettingsJson: BuildAppSettingsJson(
                "http://127.0.0.1:5292",
                runtimeMode: "scenario_playback",
                enableTimeline: true));

        var playback = host.Factory.Services.GetRequiredService<IMusicPlaybackControl>();
        await playback.TryPauseAsync("gate-test", CancellationToken.None);

        player.PauseCalls.Should().Be(1);

        var timeline = host.Factory.Services.GetRequiredService<TimelineCaptureService>();
        var entries = timeline.GetRecentEntries();
        entries.Any(e => e.Source == TimelineSources.Playback)
            .Should()
            .BeFalse("playback transitions must not be recorded in scenario_playback");
    }

    [Fact]
    public async Task DotaEndpoint_AcceptsPayload_RegardlessOfRuntimeMode()
    {
        using var host = CreateTestHost();

        var response = await host.Client.PostAsJsonAsync("/gsi/dota", CreateDotaPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DotaEndpoint_AllowsEmptyPayload()
    {
        using var host = CreateTestHost();

        var response = await host.Client.PostAsJsonAsync("/gsi/dota", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DotaEndpoint_WithTimelineEnabled_FirstPayloadEstablishesBaseline_NoTransitionRecorded()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_PRE_GAME", heroAlive: true, paused: false));

        var entries = await GetTimelineEntriesAsync(host.Client);
        var dotaEntries = entries.Where(e => e.Source == TimelineSources.Dota).ToList();

        dotaEntries.Should().ContainSingle(e => e.EventKey == TimelineDotaEvents.GameStateChanged);
        dotaEntries.Should().NotContain(e =>
            e.EventKey == TimelineDotaEvents.HeroDied || e.EventKey == TimelineDotaEvents.HeroRespawned);
        dotaEntries.Should().NotContain(e =>
            e.EventKey == TimelineDotaEvents.Paused || e.EventKey == TimelineDotaEvents.Resumed);
    }

    [Fact]
    public async Task DotaEndpoint_WithTimelineEnabled_RecordsGameStateHeroDeathAndPauseTransitions()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_PRE_GAME", heroAlive: true, paused: false));
        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: true, paused: false));
        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: false, paused: false));
        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: false, paused: true));

        var entries = await GetTimelineEntriesAsync(host.Client);
        var dotaEntries = entries.Where(e => e.Source == TimelineSources.Dota).ToList();

        dotaEntries.Count(e => e.EventKey == TimelineDotaEvents.GameStateChanged).Should().Be(2);
        dotaEntries.Should().ContainSingle(e => e.EventKey == TimelineDotaEvents.HeroDied);
        dotaEntries.Should().ContainSingle(e => e.EventKey == TimelineDotaEvents.Paused);
    }

    [Fact]
    public async Task GsiReset_ClearsDotaBaselines_SoNextPayloadReestablishesWithoutSpuriousDeath()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildIntentCaptureAppSettingsJson("http://127.0.0.1:5292"));

        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: true, paused: false));

        var resetResponse = await host.Client.PostAsync("/gsi/reset", content: null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same alive=true after reset must be a fresh baseline, not a respawn/death.
        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: true, paused: false));

        var entries = await GetTimelineEntriesAsync(host.Client);
        var dotaEntries = entries.Where(e => e.Source == TimelineSources.Dota).ToList();

        dotaEntries.Should().ContainSingle(e => e.EventKey == TimelineDotaEvents.GameStateChanged);
        dotaEntries.Should().NotContain(e =>
            e.EventKey == TimelineDotaEvents.HeroDied || e.EventKey == TimelineDotaEvents.HeroRespawned);

        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: false, paused: false));

        entries = await GetTimelineEntriesAsync(host.Client);
        entries.Should().ContainSingle(e =>
            e.Source == TimelineSources.Dota && e.EventKey == TimelineDotaEvents.HeroDied);
    }

    [Fact]
    public async Task DotaEndpoint_WithTimelineDisabled_DoesNotRecordEntries()
    {
        using var host = CreateTestHost(
            appSettingsJson: BuildAppSettingsJson(
                "http://127.0.0.1:5292",
                runtimeMode: "intent_capture",
                enableTimeline: false));

        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_PRE_GAME", heroAlive: true, paused: false));
        await host.Client.PostAsJsonAsync(
            "/gsi/dota",
            CreateDotaPayload(gameState: "DOTA_GAMERULES_STATE_GAME_IN_PROGRESS", heroAlive: false, paused: false));

        var timeline = host.Factory.Services.GetRequiredService<TimelineCaptureService>();
        timeline.GetRecentEntries().Should().BeEmpty();
    }

    private static object CreateDotaPayload(string? gameState = null, bool? heroAlive = null, bool? paused = null)
    {
        return new
        {
            provider = new { name = "Dota 2", appid = 570 },
            map = new { matchid = "1234567890", game_state = gameState, paused },
            hero = new { name = "npc_dota_hero_pudge", alive = heroAlive }
        };
    }

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

    private static GsiPayloadDto CreatePayloadDto(long timestamp, int health, int? round = null, string? phase = null)
    {
        return new GsiPayloadDto
        {
            Provider = new ProviderDto { Timestamp = timestamp },
            Map = new MapDto
            {
                MatchId = "match",
                Round = round,
                Phase = phase
            },
            Player = new PlayerDto
            {
                SteamId = "player",
                Activity = "playing",
                State = new PlayerStateDto { Health = health, Armor = 0 }
            },
        };
    }

    private static GsiSnapshotMapper CreateSnapshotMapper()
    {
        return new GsiSnapshotMapper(new ISnapshotModuleMapper[]
        {
            new RoundModuleMapper(),
            new VitalsModuleMapper(),
            new PositionModuleMapper(),
            new CombatModuleMapper()
        });
    }

    private sealed class ThrowingMusicPlayer : IMusicPlayer
    {
        public MusicPlayerCapabilities Capabilities => MusicPlayerCapabilities.Mvp;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Player unavailable");

        public Task<MusicCommandResult> PlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);

        public Task<MusicCommandResult> PauseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);

        public Task<MusicCommandResult> ResumeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);

        public Task<MusicCommandResult> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);

        public Task<MusicCommandResult> PreviousAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);

        public Task<MusicCommandResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
            => Task.FromResult(MusicCommandResult.Applied);
    }

    private TestHostContext CreateTestHost(
        IMusicPlayer? musicPlayer = null,
        string gsiBaseUrl = "http://127.0.0.1:5292",
        string? appSettingsJson = null,
        Action<string>? seedContentRoot = null,
        bool replaceMusicPlayer = true)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "UndefaultIt.Tests", Guid.NewGuid().ToString("N"));
        var cs2Root = Path.Combine(tempRoot, "Counter-Strike Global Offensive");
        Directory.CreateDirectory(Path.Combine(cs2Root, "game", "csgo", "cfg"));
        Directory.CreateDirectory(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "appsettings.json"), appSettingsJson ?? BuildAppSettingsJson(gsiBaseUrl));
        seedContentRoot?.Invoke(tempRoot);

        var previousOverride = Environment.GetEnvironmentVariable("UNDEFAULTIT_CS2_PATH");
        Environment.SetEnvironmentVariable("UNDEFAULTIT_CS2_PATH", cs2Root);

        var customizedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(WebHostDefaults.ContentRootKey, tempRoot);
            builder.ConfigureServices(services =>
            {
                if (replaceMusicPlayer)
                {
                    services.AddSingleton<IMusicPlayer>(sp =>
                        musicPlayer ?? new MockMusicPlayer(sp.GetRequiredService<ILogger<MockMusicPlayer>>()));
                }
            });
        });

        return new TestHostContext(
            customizedFactory,
            customizedFactory.CreateClient(),
            tempRoot,
            cs2Root,
            previousOverride);
    }

    private static string? TryGetStringIgnoreCase(JsonElement element, string propertyName)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value.GetString();
            }
        }

        return null;
    }

    private static void EnsureClientBaseAddressHasTrailingSlash(HttpClient client)
    {
        var baseUri = client.BaseAddress;
        if (baseUri is null)
        {
            return;
        }

        var s = baseUri.ToString();
        if (!s.EndsWith('/'))
        {
            client.BaseAddress = new Uri(s + "/");
        }
    }

    private static readonly JsonSerializerOptions TimelineJsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<List<TimelineEntry>> GetTimelineEntriesAsync(HttpClient client)
    {
        var json = await client.GetStringAsync("/timeline");
        return JsonSerializer.Deserialize<List<TimelineEntry>>(json, TimelineJsonOptions)
            ?? new List<TimelineEntry>();
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task<TimelineEntry?> WaitForPlaybackEntryAsync(HttpClient client, string eventKey, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var entries = await GetTimelineEntriesAsync(client);
            var match = entries.FirstOrDefault(e =>
                e.Source == TimelineSources.Playback && e.EventKey == eventKey);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static MockMusicPlayer CreatePausedMockPlayer()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Paused, volumePercent: 61);
        return player;
    }

    private static MockMusicPlayer CreatePlayingMockPlayer()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Playing, volumePercent: 70);
        return player;
    }

    private static string[] GetSessionFiles(string tempRoot)
    {
        var directory = Path.Combine(tempRoot, "timeline");
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.jsonl")
            : Array.Empty<string>();
    }

    private static List<TimelineEntry> ReadTimelineEntriesFromDisk(string tempRoot)
    {
        var entries = new List<TimelineEntry>();
        foreach (var file in GetSessionFiles(tempRoot))
        {
            entries.AddRange(ReadTimelineEntriesFromFile(file));
        }

        return entries;
    }

    private static List<TimelineEntry> ReadTimelineEntriesFromFile(string path)
    {
        var entries = new List<TimelineEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<TimelineEntry>(line, TimelineJsonOptions);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static string BuildAppSettingsJson(
        string gsiBaseUrl,
        bool enableSmartTrackStart = false,
        string roundStartAction = "music.control_profile",
        string deathAction = "music.control_profile",
        bool allowGsiReset = true,
        string runtimeMode = "scenario_playback",
        bool enableTimeline = false,
        bool enablePlaybackObserver = false,
        int playbackObserverPollIntervalSeconds = 2,
        bool musicOrchestrationShadowMode = true,
        string musicProvider = "Mock")
    {
        return $$"""
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning"
            }
          },
          "AllowedHosts": "*",
          "Spotify": {
            "ClientId": "",
            "RedirectUri": "http://127.0.0.1:5292/callback",
            "Scopes": [
              "user-modify-playback-state",
              "user-read-playback-state"
            ]
          },
          "Gsi": {
            "Method": "POST",
            "Path": "/gsi",
            "Url": "{{gsiBaseUrl}}",
            "AllowReset": {{(allowGsiReset ? "true" : "false")}}
          },
          "UseMockSpotify": true,
          "Music": {
            "Provider": "{{musicProvider}}"
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
          "SpotifyVolumeDuck": {
            "MuteVolume": 0,
            "FallbackRestoreVolume": 50
          },
          "SmartTrackStart": {
            "Enabled": {{(enableSmartTrackStart ? "true" : "false")}},
            "PreloadOnStartup": true
          },
          "Runtime": {
            "Mode": "{{runtimeMode}}"
          },
          "Timeline": {
            "Enabled": {{(enableTimeline ? "true" : "false")}}
          },
          "PlaybackObserver": {
            "Enabled": {{(enablePlaybackObserver ? "true" : "false")}},
            "PollIntervalSeconds": {{playbackObserverPollIntervalSeconds}}
          },
          "RulesEngine": {
            "ActionMap": {
              "round_start": [ "{{roundStartAction}}" ],
              "death": [ "{{deathAction}}" ]
            }
          },
          "MusicOrchestration": {
            "ShadowMode": {{(musicOrchestrationShadowMode ? "true" : "false")}}
          }
        }
        """;
    }

    private static string BuildIntentCaptureAppSettingsJson(string gsiBaseUrl)
    {
        return BuildAppSettingsJson(
            gsiBaseUrl,
            runtimeMode: "intent_capture",
            enableTimeline: true);
    }

    private static string BuildObserverEnabledIntentCaptureAppSettingsJson(string gsiBaseUrl)
    {
        // Mirrors --mvp (intent_capture + timeline) and turns the playback state observer ON
        // with a 1-second poll so integration tests can observe transitions quickly.
        return BuildAppSettingsJson(
            gsiBaseUrl,
            runtimeMode: "intent_capture",
            enableTimeline: true,
            enablePlaybackObserver: true,
            playbackObserverPollIntervalSeconds: 1);
    }

    private sealed class NullStepGate : IStepGate
    {
        public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostContext : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly string? _previousOverride;

        public TestHostContext(
            WebApplicationFactory<Program> factory,
            HttpClient client,
            string tempRoot,
            string cs2Root,
            string? previousOverride)
        {
            _factory = factory;
            Client = client;
            TempRoot = tempRoot;
            Cs2Root = cs2Root;
            _previousOverride = previousOverride;
        }

        public HttpClient Client { get; }

        public WebApplicationFactory<Program> Factory => _factory;

        public string TempRoot { get; }

        public string Cs2Root { get; }

        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
            Environment.SetEnvironmentVariable("UNDEFAULTIT_CS2_PATH", _previousOverride);

            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
    }
}
