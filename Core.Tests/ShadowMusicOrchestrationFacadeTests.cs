using Core.Adapters;
using Core.Models;
using Core.Music;
using FluentAssertions;

namespace Core.Tests;

public sealed class ShadowMusicOrchestrationFacadeTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ShadowMusicOrchestrationFacade _facade = new();

    [Fact]
    public void EvaluateShadow_LiveAndAlive_ReportsSafe()
    {
        var observation = BuildObservation(
            isAlive: true,
            matchPhase: MatchPhaseNeutral.Live,
            safetyState: MusicSafetyState.Safe,
            reason: "live-and-alive");

        var snapshot = _facade.EvaluateShadow(observation);

        snapshot.DesiredSafetyState.Should().Be(MusicSafetyState.Safe);
        snapshot.LastSafetyTransitionReason.Should().Be("shadow:live-and-alive");
        snapshot.CapturedAtUtc.Should().Be(BaseTime);
        snapshot.Clock.Should().Be(observation.Clock);
        snapshot.LastDeviceCommands.Should().BeNull();
        snapshot.DeviceDegraded.Should().BeFalse();
        snapshot.LastSpotifyError.Should().BeNull();
        snapshot.LastMergedVolumePercent.Should().Be(100);
        snapshot.MixerChannelContributions.Should().NotBeNull();
        snapshot.MixerChannelContributions!.Should().ContainKey("adaptive").WhoseValue.Should().Be("100%");
        snapshot.MixerChannelContributions.Should().ContainKey("floor").WhoseValue.Should().Be("30%");

        snapshot.LastMusicIntent.Should().NotBeNull();
        snapshot.LastMusicIntent!.TransportIntent.Should().Be(TransportIntentNeutral.NoChange);
        snapshot.LastMusicIntent.FloorVolumePercent.Should().BeNull();
        snapshot.LastMusicIntent.CeilingVolumePercent.Should().BeNull();
        snapshot.LastMusicIntent.Reason.Should().Be("shadow:live-and-alive");
    }

    [Fact]
    public void EvaluateShadow_DeadObservation_ReportsDangerWithDeadReason()
    {
        var observation = BuildObservation(
            isAlive: false,
            matchPhase: MatchPhaseNeutral.Live,
            safetyState: MusicSafetyState.Danger,
            reason: "player-dead");

        var snapshot = _facade.EvaluateShadow(observation);

        snapshot.DesiredSafetyState.Should().Be(MusicSafetyState.Danger);
        snapshot.LastSafetyTransitionReason.Should().Be("shadow:player-dead");
        snapshot.LastMergedVolumePercent.Should().Be(0);

        snapshot.LastMusicIntent.Should().NotBeNull();
        snapshot.LastMusicIntent!.TransportIntent.Should().Be(TransportIntentNeutral.PreferSilence);
        snapshot.LastMusicIntent.FloorVolumePercent.Should().Be(0);
        snapshot.LastMusicIntent.CeilingVolumePercent.Should().Be(0);
    }

    [Fact]
    public void EvaluateShadow_UnknownAndAliveInNonLivePhase_ReportsUnknown()
    {
        var observation = BuildObservation(
            isAlive: true,
            matchPhase: MatchPhaseNeutral.PreLive,
            safetyState: MusicSafetyState.Unknown,
            reason: "non-live-or-unknown-phase");

        var snapshot = _facade.EvaluateShadow(observation);

        snapshot.DesiredSafetyState.Should().Be(MusicSafetyState.Unknown);
        snapshot.LastSafetyTransitionReason.Should().Be("shadow:non-live-or-unknown-phase");
        snapshot.LastMergedVolumePercent.Should().Be(30);

        snapshot.LastMusicIntent.Should().NotBeNull();
        snapshot.LastMusicIntent!.TransportIntent.Should().Be(TransportIntentNeutral.NoChange);
        snapshot.LastMusicIntent.CeilingVolumePercent.Should().Be(30);
        snapshot.LastMusicIntent.FloorVolumePercent.Should().BeNull();
    }

    [Fact]
    public void EvaluateShadow_UnknownAndDead_EscalatesToDangerWithFallbackReason()
    {
        var observation = BuildObservation(
            isAlive: false,
            matchPhase: MatchPhaseNeutral.Unknown,
            safetyState: MusicSafetyState.Unknown,
            reason: null);

        var snapshot = _facade.EvaluateShadow(observation);

        snapshot.DesiredSafetyState.Should().Be(MusicSafetyState.Danger);
        snapshot.LastSafetyTransitionReason.Should().Be(ShadowMusicOrchestrationFacade.DeadFallbackReason);
        snapshot.LastMergedVolumePercent.Should().Be(0);

        snapshot.LastMusicIntent.Should().NotBeNull();
        snapshot.LastMusicIntent!.TransportIntent.Should().Be(TransportIntentNeutral.PreferSilence);
        snapshot.LastMusicIntent.FloorVolumePercent.Should().Be(0);
        snapshot.LastMusicIntent.CeilingVolumePercent.Should().Be(0);
        snapshot.LastMusicIntent.Reason.Should().Be(ShadowMusicOrchestrationFacade.DeadFallbackReason);
    }

    [Fact]
    public void EvaluateShadow_StaleObservation_PropagatesStalenessIntoReason()
    {
        var observation = BuildObservation(
            isAlive: true,
            matchPhase: MatchPhaseNeutral.Live,
            safetyState: MusicSafetyState.Safe,
            reason: "live-and-alive;stale-provider-timestamp",
            isStale: true);

        var snapshot = _facade.EvaluateShadow(observation);

        snapshot.DesiredSafetyState.Should().Be(MusicSafetyState.Safe);
        snapshot.LastSafetyTransitionReason.Should().Contain("stale-provider-timestamp");
    }

    [Fact]
    public void EvaluateShadow_NullObservation_Throws()
    {
        Action act = () => _facade.EvaluateShadow(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AdapterObservation BuildObservation(
        bool? isAlive,
        MatchPhaseNeutral matchPhase,
        MusicSafetyState safetyState,
        string? reason,
        bool isStale = false)
    {
        var raw = new GameSnapshot(
            Timestamp: BaseTime,
            GameId: "cs2",
            MatchId: "match",
            PlayerId: "player",
            Modules: Array.Empty<ISnapshotModule>());

        var clock = new GameClockSnapshot(
            WallTimeUtc: BaseTime,
            GameTimeSeconds: null,
            IsGamePaused: false,
            MatchPhase: matchPhase,
            RoundIndex: 4);

        var neutral = new NeutralContext(
            IsAlive: isAlive,
            EngagementPressure: null,
            ObjectivePressure: null,
            SpectatorOrObserver: null,
            TransportIntent: isAlive == false
                ? TransportIntentNeutral.PreferPause
                : TransportIntentNeutral.NoChange,
            ObservedAtUtc: BaseTime);

        var safety = new SafetyFacts(safetyState, reason, isStale);

        return new AdapterObservation(
            Raw: raw,
            Clock: clock,
            Neutral: neutral,
            Safety: safety);
    }
}
