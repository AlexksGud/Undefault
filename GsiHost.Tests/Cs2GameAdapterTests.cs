using Core.Adapters;
using Core.Models;
using Core.Music;
using FluentAssertions;
using GsiHost.Adapters;
using GsiHost.Dtos;
using GsiHost.Mapping;
using GsiHost.Mapping.Modules;

namespace GsiHost.Tests;

public sealed class Cs2GameAdapterTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("live", MatchPhaseNeutral.Live)]
    [InlineData("LIVE", MatchPhaseNeutral.Live)]
    [InlineData("freezetime", MatchPhaseNeutral.PreLive)]
    [InlineData("warmup", MatchPhaseNeutral.PreLive)]
    [InlineData("intermission", MatchPhaseNeutral.Intermission)]
    [InlineData("gameover", MatchPhaseNeutral.PostMatch)]
    [InlineData("over", MatchPhaseNeutral.Unknown)]
    [InlineData("", MatchPhaseNeutral.Unknown)]
    [InlineData(null, MatchPhaseNeutral.Unknown)]
    public void MapMatchPhase_TranslatesCs2PhaseToNeutralEnum(string? cs2Phase, MatchPhaseNeutral expected)
    {
        Cs2GameAdapter.MapMatchPhase(cs2Phase).Should().Be(expected);
    }

    [Fact]
    public void Adapt_AliveAndLive_ReportsSafeAndNoTransportChange()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 100,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.IsAlive.Should().BeTrue();
        observation.Neutral.TransportIntent.Should().Be(TransportIntentNeutral.NoChange);
        observation.Neutral.SpectatorOrObserver.Should().BeFalse();
        observation.Clock.MatchPhase.Should().Be(MatchPhaseNeutral.Live);
        observation.Clock.RoundIndex.Should().Be(4);
        observation.Safety.State.Should().Be(MusicSafetyState.Safe);
        observation.Safety.Reason.Should().StartWith("live-and-alive");
        observation.Safety.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Adapt_DeadPlayer_ReportsDangerAndPreferPause()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 0,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.IsAlive.Should().BeFalse();
        observation.Neutral.TransportIntent.Should().Be(TransportIntentNeutral.PreferPause);
        observation.Safety.State.Should().Be(MusicSafetyState.Danger);
        observation.Safety.Reason.Should().StartWith("player-dead");
        observation.Safety.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Adapt_MissingPlayerBlock_LeavesNeutralFieldsNullAndUnknownSafety()
    {
        var adapter = CreateAdapter();
        var payload = new GsiPayloadDto
        {
            Provider = new ProviderDto { Timestamp = BaseTime.ToUnixTimeSeconds() },
        };

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.IsAlive.Should().BeNull();
        observation.Neutral.SpectatorOrObserver.Should().BeNull();
        observation.Neutral.EngagementPressure.Should().BeNull();
        observation.Neutral.ObjectivePressure.Should().BeNull();
        observation.Neutral.TransportIntent.Should().Be(TransportIntentNeutral.NoChange);
        observation.Clock.MatchPhase.Should().Be(MatchPhaseNeutral.Unknown);
        observation.Clock.RoundIndex.Should().BeNull();
        observation.Safety.State.Should().Be(MusicSafetyState.Unknown);
        observation.Safety.IsStale.Should().BeFalse();
    }

    [Fact]
    public void Adapt_StaleProviderTimestamp_FlagsIsStaleTrue()
    {
        var adapter = CreateAdapter();
        var providerTs = BaseTime.ToUnixTimeSeconds();
        // Receive the payload well after the provider produced it (10 seconds > 5s threshold).
        var receivedAt = BaseTime.AddSeconds(10);
        var payload = BuildPayload(
            providerTimestampSeconds: providerTs,
            health: 100,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, receivedAt);

        observation.Safety.IsStale.Should().BeTrue();
        observation.Safety.Reason.Should().Contain("stale-provider-timestamp");
    }

    [Fact]
    public void Adapt_MissingProviderTimestamp_DoesNotFlagStaleAndDocumentsReason()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: null,
            health: 100,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Safety.IsStale.Should().BeFalse();
        observation.Safety.Reason.Should().Contain("no-provider-timestamp");
    }

    [Fact]
    public void Adapt_MissingActivityWithoutState_FlagsSpectatorOrObserverTrue()
    {
        var adapter = CreateAdapter();
        var payload = new GsiPayloadDto
        {
            Provider = new ProviderDto { Timestamp = BaseTime.ToUnixTimeSeconds() },
            Map = new MapDto { Round = 1, Phase = "live" },
            Player = new PlayerDto
            {
                SteamId = "player",
                Activity = null,
                State = null,
            },
        };

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.SpectatorOrObserver.Should().BeTrue();
        observation.Neutral.IsAlive.Should().BeNull();
    }

    [Fact]
    public void Adapt_NonPlayingActivity_FlagsSpectatorOrObserverTrue()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 100,
            activity: "menu",
            round: 1,
            phase: "warmup");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.SpectatorOrObserver.Should().BeTrue();
    }

    [Fact]
    public void Adapt_LowHealthAlive_BumpsEngagementPressure()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 10,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.EngagementPressure.Should().NotBeNull();
        observation.Neutral.EngagementPressure!.Value.Should().BeApproximately(0.40f, 0.0001f);
    }

    [Fact]
    public void Adapt_FullHealthAlive_HasZeroEngagementPressure()
    {
        var adapter = CreateAdapter();
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 100,
            activity: "playing",
            round: 4,
            phase: "live");

        var observation = adapter.Adapt(payload, BaseTime);

        observation.Neutral.EngagementPressure.Should().Be(0f);
    }

    [Fact]
    public void Adapt_PreservesRawSnapshotFromMapper()
    {
        var mapper = CreateSnapshotMapper();
        var adapter = new Cs2GameAdapter(mapper);
        var payload = BuildPayload(
            providerTimestampSeconds: BaseTime.ToUnixTimeSeconds(),
            health: 100,
            activity: "playing",
            round: 7,
            phase: "freezetime");

        var direct = mapper.Map(payload, BaseTime);
        var observation = adapter.Adapt(payload, BaseTime);

        observation.Raw.Should().BeEquivalentTo(direct);
    }

    private static Cs2GameAdapter CreateAdapter()
    {
        return new Cs2GameAdapter(CreateSnapshotMapper());
    }

    private static GsiSnapshotMapper CreateSnapshotMapper()
    {
        return new GsiSnapshotMapper(new ISnapshotModuleMapper[]
        {
            new RoundModuleMapper(),
            new VitalsModuleMapper(),
            new PositionModuleMapper(),
            new CombatModuleMapper(),
        });
    }

    private static GsiPayloadDto BuildPayload(
        long? providerTimestampSeconds,
        int health,
        string? activity,
        int? round,
        string? phase)
    {
        return new GsiPayloadDto
        {
            Provider = new ProviderDto { Timestamp = providerTimestampSeconds },
            Map = new MapDto { MatchId = "match", Round = round, Phase = phase },
            Player = new PlayerDto
            {
                SteamId = "player",
                Activity = activity,
                State = new PlayerStateDto { Health = health, Armor = 0 },
            },
        };
    }
}
