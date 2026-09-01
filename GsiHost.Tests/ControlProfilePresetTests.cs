using Core.Actions;
using Core.Configuration;
using Core.Models;
using Core.Music;
using FluentAssertions;
using GsiHost.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace GsiHost.Tests;

public sealed class ControlProfilePresetTests
{
    [Fact]
    public async Task GetAsync_MissingFile_SeedsFlowAndFocus_WithFlowActive()
    {
        using var root = new TempContentRoot();
        var service = CreateService(root.Path);

        var config = await service.GetAsync();

        config.ActiveProfileId.Should().Be(JsonControlProfileService.FlowProfileId);
        config.Profiles.Select(profile => profile.Id).Should().Equal(
            JsonControlProfileService.FlowProfileId,
            JsonControlProfileService.FocusProfileId);
        AssertFlowRules(config.Profiles[0]);
        AssertFocusRules(config.Profiles[1]);
        File.Exists(service.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ExistingCustomFile_DoesNotOverwriteUserProfiles()
    {
        using var root = new TempContentRoot();
        var path = Path.Combine(root.Path, "control-profiles.json");
        const string customJson = """
            {
              "ActiveProfileId": "custom",
              "Profiles": [
                {
                  "Id": "custom",
                  "Name": "Custom Rules",
                  "Rules": [
                    { "EventKey": "round_start", "Command": "pause" },
                    { "EventKey": "death", "Command": "pause" }
                  ]
                },
                {
                  "Id": "extra",
                  "Name": "Extra",
                  "Rules": []
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, customJson);
        var service = CreateService(root.Path);

        var config = await service.GetAsync();

        config.ActiveProfileId.Should().Be("custom");
        config.Profiles.Select(profile => profile.Id).Should().Equal("custom", "extra");
        config.Profiles[0].FindRule(EventKeys.RoundStart)!.Command.Should().Be(MusicControlCommands.Pause);
        (await File.ReadAllTextAsync(path)).Should().Be(customJson);
    }

    [Fact]
    public async Task GetAsync_LegacyConsoleDefaultProfile_KeepsActiveIdAndRules()
    {
        using var root = new TempContentRoot();
        var path = Path.Combine(root.Path, "control-profiles.json");
        const string legacyJson = """
            {
              "ActiveProfileId": "console-default",
              "Profiles": [
                {
                  "Id": "console-default",
                  "Name": "Console Default",
                  "Rules": [
                    { "EventKey": "round_start", "Command": "resume" },
                    { "EventKey": "death", "Command": "pause" }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, legacyJson);
        var service = CreateService(root.Path);

        var config = await service.GetAsync();

        config.ActiveProfileId.Should().Be(JsonControlProfileService.ConsoleDefaultAliasId);
        config.Profiles.Should().ContainSingle(profile => profile.Id == JsonControlProfileService.ConsoleDefaultAliasId);
        AssertFlowRules(config.Profiles[0]);
        (await File.ReadAllTextAsync(path)).Should().Be(legacyJson);
    }

    [Fact]
    public async Task GetAsync_ConsoleDefaultActiveId_WithoutThatProfile_ResolvesToFlow()
    {
        using var root = new TempContentRoot();
        var path = Path.Combine(root.Path, "control-profiles.json");
        const string aliasedJson = """
            {
              "ActiveProfileId": "console-default",
              "Profiles": [
                {
                  "Id": "flow",
                  "Name": "Flow",
                  "Rules": [
                    { "EventKey": "round_start", "Command": "resume" },
                    { "EventKey": "death", "Command": "pause" }
                  ]
                },
                {
                  "Id": "focus",
                  "Name": "Focus",
                  "Rules": [
                    { "EventKey": "round_start", "Command": "pause" },
                    { "EventKey": "death", "Command": "resume" }
                  ]
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, aliasedJson);
        var service = CreateService(root.Path);

        var config = await service.GetAsync();

        config.ActiveProfileId.Should().Be(JsonControlProfileService.FlowProfileId);
        config.Profiles.Select(profile => profile.Id).Should().Equal(
            JsonControlProfileService.FlowProfileId,
            JsonControlProfileService.FocusProfileId);
        (await File.ReadAllTextAsync(path)).Should().Be(aliasedJson);
    }

    [Fact]
    public async Task SaveAsync_PersistsActiveProfileId_AcrossNewServiceInstance()
    {
        using var root = new TempContentRoot();
        var first = CreateService(root.Path);
        var seeded = await first.GetAsync();
        seeded.ActiveProfileId.Should().Be(JsonControlProfileService.FlowProfileId);

        await first.SaveAsync(seeded with { ActiveProfileId = JsonControlProfileService.FocusProfileId });

        var second = CreateService(root.Path);
        var reloaded = await second.GetAsync();

        reloaded.ActiveProfileId.Should().Be(JsonControlProfileService.FocusProfileId);
        reloaded.Profiles.Select(profile => profile.Id).Should().Equal(
            JsonControlProfileService.FlowProfileId,
            JsonControlProfileService.FocusProfileId);
        AssertFocusRules(reloaded.Profiles.Single(profile => profile.Id == JsonControlProfileService.FocusProfileId));
    }

    [Fact]
    public async Task DefaultFlowSelection_ResumesOnRoundStart_AndPausesOnDeath()
    {
        using var root = new TempContentRoot();
        var service = CreateService(root.Path);
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Paused, volumePercent: 61);
        var action = CreateAction(player, service);

        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));

        player.PlayCalls.Should().Be(1);
        player.PauseCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task SavedFocusSelection_ChangesCommands_AfterReload()
    {
        using var root = new TempContentRoot();
        var writer = CreateService(root.Path);
        var seeded = await writer.GetAsync();
        await writer.SaveAsync(seeded with { ActiveProfileId = JsonControlProfileService.FocusProfileId });

        var reader = CreateService(root.Path);
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Playing, volumePercent: 70);
        var action = CreateAction(player, reader);

        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));

        player.PauseCalls.Should().Be(1);
        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
    }

    private static JsonControlProfileService CreateService(string contentRoot)
    {
        return new JsonControlProfileService(
            new TestWebHostEnvironment(contentRoot),
            NullLogger<JsonControlProfileService>.Instance);
    }

    private static MusicControlProfileAction CreateAction(IMusicPlayer player, IControlProfileService profiles)
    {
        var playback = new MusicPlaybackControlCoordinator(
            player,
            duckOptions: null,
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        return new MusicControlProfileAction(
            playback,
            profiles,
            NullLogger<MusicControlProfileAction>.Instance);
    }

    private static void AssertFlowRules(ConsoleControlProfile profile)
    {
        profile.FindRule(EventKeys.RoundStart)!.Command.Should().Be(MusicControlCommands.Resume);
        profile.FindRule(EventKeys.Death)!.Command.Should().Be(MusicControlCommands.Pause);
    }

    private static void AssertFocusRules(ConsoleControlProfile profile)
    {
        profile.FindRule(EventKeys.RoundStart)!.Command.Should().Be(MusicControlCommands.Pause);
        profile.FindRule(EventKeys.Death)!.Command.Should().Be(MusicControlCommands.Resume);
    }

    private static GameSnapshot BuildSnapshot(DateTimeOffset timestamp, int health, bool isAlive)
    {
        return new GameSnapshot(
            Timestamp: timestamp,
            GameId: "cs2",
            MatchId: "match",
            PlayerId: "player",
            Modules: new ISnapshotModule[]
            {
                new VitalsModule(Health: health, Armor: 0, IsAlive: isAlive),
                new PositionModule(Position: Vector3.Zero, IsMoving: false),
                new CombatModule(InCombatHint: false, LastDamageDealtAt: null, LastDamageReceivedAt: null)
            });
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "GsiHost.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public string ContentRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempContentRoot : IDisposable
    {
        public TempContentRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UndefaultIt.Tests",
                "control-presets",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
