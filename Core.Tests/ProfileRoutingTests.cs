using Core.Actions;
using Core.Adapters;
using Core.Configuration;
using Core.Diff;
using Core.Models;
using Core.Music;
using Core.Rules;
using Core.Stores;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

public class ProfileRoutingTests
{
    [Fact]
    public void ConsoleControlProfile_FindRule_UsesCaseInsensitiveEventKeys()
    {
        var profile = new ConsoleControlProfile(
            "console-default",
            "Console Default",
            new List<EventControlRule>
            {
                new("custom:music_off", MusicControlCommands.Pause)
            });

        var rule = profile.FindRule("CUSTOM:MUSIC_OFF");

        rule.Should().NotBeNull();
        rule!.Command.Should().Be(MusicControlCommands.Pause);
    }

    [Fact]
    public async Task RulesEngine_RoutesActions_UsingCanonicalEventKey()
    {
        var action = new CaptureAction();
        var engine = new RulesEngine(
            new TestSnapshotStore(),
            new SnapshotDiffer(),
            new EventDetector(new EventDetectorOptions
            {
                DeathCooldown = TimeSpan.Zero,
                CombatDebounce = TimeSpan.Zero,
                IdleDebounce = TimeSpan.FromMinutes(1)
            }),
            new[] { action },
            Options.Create(new RulesEngineOptions
            {
                ActionMap = new Dictionary<string, List<string>>
                {
                    [EventKeys.Death] = new() { action.Key }
                }
            }));

        await engine.EvaluateAsync(BuildObservation(DateTimeOffset.UtcNow, 100, isAlive: true));
        await engine.EvaluateAsync(BuildObservation(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false));

        action.Events.Should().ContainSingle();
        action.Events[0].EventKey.Should().Be(EventKeys.Death);
    }

    [Fact]
    public async Task MusicControlProfileAction_ResumesOnRoundStart_AndPausesOnDeath()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Paused, Track: null, VolumePercent: 55)
        };
        var controlProfileService = new FakeControlProfileService(new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new("console-default", "Console Default", new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Resume),
                    new(EventKeys.Death, MusicControlCommands.Pause)
                })
            }));
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow.AddMilliseconds(10), 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1.1), 0, isAlive: false)));

        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(1);
    }

    [Fact]
    public async Task MusicControlProfileAction_SwitchingActivePreset_ChangesCommands()
    {
        var profiles = CreateFlowAndFocusPresets();
        var controlProfileService = new FakeControlProfileService(
            new ConsoleControlProfilesConfig("flow", profiles));

        var flowPlayer = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Paused, Track: null, VolumePercent: 55)
        };
        await ExecuteRoundStartAndDeathAsync(flowPlayer, controlProfileService);

        flowPlayer.PlayCalls.Should().Be(1);
        flowPlayer.PauseCalls.Should().Be(1);
        flowPlayer.ResumeCalls.Should().Be(0);

        await controlProfileService.SaveAsync(new ConsoleControlProfilesConfig("focus", profiles));

        var focusPlayer = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Playing, Track: null, VolumePercent: 55)
        };
        await ExecuteRoundStartAndDeathAsync(focusPlayer, controlProfileService);

        focusPlayer.PauseCalls.Should().Be(1);
        focusPlayer.PlayCalls.Should().Be(1);
        focusPlayer.ResumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task MusicControlProfileAction_WhenPlayerUnavailable_DoesNotThrow()
    {
        var player = new FakeMusicPlayer { Available = false };
        var controlProfileService = new FakeControlProfileService(new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new("console-default", "Console Default", new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Resume),
                    new(EventKeys.Death, MusicControlCommands.Pause)
                })
            }));
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        var act = async () =>
        {
            await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
            await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));
        };

        await act.Should().NotThrowAsync();
        player.PlayCalls.Should().Be(0);
        player.ResumeCalls.Should().Be(0);
        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task MusicControlProfileAction_DucksOnRoundStart_AndRestoresOnDeath()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Playing, Track: null, VolumePercent: 72)
        };
        var controlProfileService = new FakeControlProfileService(new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new("console-default", "Console Default", new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Duck, 10),
                    new(EventKeys.Death, MusicControlCommands.RestoreVolume)
                })
            }));
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions
            {
                MuteVolume = 0,
                FallbackRestoreVolume = 50
            }),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));

        player.VolumeCalls.Should().Equal(10, 72);
        action.Key.Should().Be(MusicControlProfileAction.CanonicalKey);
    }

    [Fact]
    public void RulesEngine_FindUnregisteredActionKeys_ReturnsMissingActionMapValues()
    {
        var actionMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [EventKeys.RoundStart] = new[] { "music.control_profile" },
            [EventKeys.Death] = new[] { "spotify.control_profile" }
        };

        var missing = RulesEngine.FindUnregisteredActionKeys(actionMap, new[] { "music.control_profile" });

        missing.Should().ContainSingle().Which.Should().Be("spotify.control_profile");
    }

    [Fact]
    public async Task MusicControlProfileAction_RoutesNextAndPrevious()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Playing, Track: null, VolumePercent: 40)
        };
        var controlProfileService = new FakeControlProfileService(new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new("console-default", "Console Default", new List<EventControlRule>
                {
                    new("custom:next", MusicControlCommands.Next),
                    new("custom:previous", MusicControlCommands.Previous)
                })
            }));
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        var snapshot = BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true);
        await action.ExecuteAsync(new NormalizedEvent(
            EventType.RoundStart,
            "custom:next",
            snapshot.Timestamp,
            EventContext.FromSnapshot(snapshot),
            Duration: null,
            Detail: null));
        var previousSnapshot = BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 100, isAlive: true);
        await action.ExecuteAsync(new NormalizedEvent(
            EventType.RoundStart,
            "custom:previous",
            previousSnapshot.Timestamp,
            EventContext.FromSnapshot(previousSnapshot),
            Duration: null,
            Detail: null));

        player.NextCalls.Should().Be(1);
        player.PreviousCalls.Should().Be(1);
    }

    [Fact]
    public async Task MusicControlProfileAction_WhenCanceled_Rethrows()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Paused, Track: null, VolumePercent: 55)
        };
        var controlProfileService = new FakeControlProfileService(new ConsoleControlProfilesConfig(
            "console-default",
            new List<ConsoleControlProfile>
            {
                new("console-default", "Console Default", new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Resume)
                })
            }));
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await action.ExecuteAsync(
            NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        player.PlayCalls.Should().Be(0);
        player.ResumeCalls.Should().Be(0);
    }

    private static List<ConsoleControlProfile> CreateFlowAndFocusPresets()
    {
        return new List<ConsoleControlProfile>
        {
            new(
                "flow",
                "Flow",
                new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Resume),
                    new(EventKeys.Death, MusicControlCommands.Pause)
                }),
            new(
                "focus",
                "Focus",
                new List<EventControlRule>
                {
                    new(EventKeys.RoundStart, MusicControlCommands.Pause),
                    new(EventKeys.Death, MusicControlCommands.Resume)
                })
        };
    }

    private static async Task ExecuteRoundStartAndDeathAsync(
        FakeMusicPlayer player,
        FakeControlProfileService controlProfileService)
    {
        var playback = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
        var action = new MusicControlProfileAction(
            playback,
            controlProfileService,
            NullLogger<MusicControlProfileAction>.Instance);

        await action.ExecuteAsync(NormalizedEvent.RoundStart(BuildSnapshot(DateTimeOffset.UtcNow, 100, isAlive: true)));
        await action.ExecuteAsync(NormalizedEvent.Death(BuildSnapshot(DateTimeOffset.UtcNow.AddSeconds(1), 0, isAlive: false)));
    }

    private static GameSnapshot BuildSnapshot(
        DateTimeOffset timestamp,
        int health,
        bool isAlive)
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

    private static AdapterObservation BuildObservation(
        DateTimeOffset timestamp,
        int health,
        bool isAlive)
    {
        var raw = BuildSnapshot(timestamp, health, isAlive);
        var clock = new GameClockSnapshot(
            WallTimeUtc: timestamp,
            GameTimeSeconds: null,
            IsGamePaused: false,
            MatchPhase: MatchPhaseNeutral.Live,
            RoundIndex: null);
        var neutral = new NeutralContext(
            IsAlive: isAlive,
            EngagementPressure: null,
            ObjectivePressure: null,
            SpectatorOrObserver: null,
            TransportIntent: TransportIntentNeutral.NoChange,
            ObservedAtUtc: timestamp);
        return new AdapterObservation(raw, clock, neutral, SafetyFacts.Unknown());
    }

    private sealed class CaptureAction : IEventAction
    {
        public string Key => "capture";

        public List<NormalizedEvent> Events { get; } = new();

        public Task ExecuteAsync(NormalizedEvent normalizedEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(normalizedEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class TestSnapshotStore : ISnapshotStore
    {
        private GameSnapshot? _last;

        public GameSnapshot? GetLast() => _last;

        public void Save(GameSnapshot snapshot) => _last = snapshot;

        public void Clear() => _last = null;
    }

    private sealed class FakeControlProfileService : IControlProfileService
    {
        private ConsoleControlProfilesConfig _config;

        public FakeControlProfileService(ConsoleControlProfilesConfig config)
        {
            _config = config;
        }

        public string FilePath => "control-profiles.json";

        public Task<ConsoleControlProfilesConfig> GetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_config);
        }

        public Task SaveAsync(ConsoleControlProfilesConfig config, CancellationToken cancellationToken = default)
        {
            _config = config;
            return Task.CompletedTask;
        }
    }
}
