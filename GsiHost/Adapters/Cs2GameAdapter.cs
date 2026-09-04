using Core.Adapters;
using Core.Models;
using Core.Music;
using GsiHost.Dtos;
using GsiHost.Mapping;

namespace GsiHost.Adapters;

public sealed class Cs2GameAdapter : IGameAdapter<GsiPayloadDto>
{
    // Conservative threshold so a slow CS2 client or paused producer surfaces as stale
    // rather than as Safe.
    public static readonly TimeSpan ProviderTimestampStaleThreshold = TimeSpan.FromSeconds(5);

    private readonly GsiSnapshotMapper _mapper;

    public Cs2GameAdapter(GsiSnapshotMapper mapper)
    {
        _mapper = mapper;
    }

    public string TitleId => "cs2";

    public AdapterObservation Adapt(GsiPayloadDto payload, DateTimeOffset receivedAt)
    {
        var snapshot = _mapper.Map(payload, receivedAt);
        var round = snapshot.GetModule<RoundModule>();
        var vitals = snapshot.GetModule<VitalsModule>();
        var combat = snapshot.GetModule<CombatModule>();

        var matchPhase = MapMatchPhase(round?.Phase);

        var clock = new GameClockSnapshot(
            WallTimeUtc: receivedAt,
            GameTimeSeconds: null,
            IsGamePaused: false,
            MatchPhase: matchPhase,
            RoundIndex: round?.Round);

        var neutral = BuildNeutralContext(payload, vitals, combat, receivedAt);
        var safety = BuildSafetyFacts(payload, vitals, matchPhase, receivedAt);

        return new AdapterObservation(
            Raw: snapshot,
            Clock: clock,
            Neutral: neutral,
            Safety: safety);
    }

    // The only place CS2 phase strings are allowed to cross into shared music types.
    public static MatchPhaseNeutral MapMatchPhase(string? cs2Phase)
    {
        if (string.IsNullOrWhiteSpace(cs2Phase))
        {
            return MatchPhaseNeutral.Unknown;
        }

        return cs2Phase.ToLowerInvariant() switch
        {
            "live" => MatchPhaseNeutral.Live,
            "freezetime" => MatchPhaseNeutral.PreLive,
            "warmup" => MatchPhaseNeutral.PreLive,
            "intermission" => MatchPhaseNeutral.Intermission,
            "gameover" => MatchPhaseNeutral.PostMatch,
            _ => MatchPhaseNeutral.Unknown,
        };
    }

    private static NeutralContext BuildNeutralContext(
        GsiPayloadDto payload,
        VitalsModule? vitals,
        CombatModule? combat,
        DateTimeOffset observedAtUtc)
    {
        var isAlive = vitals?.IsAlive;
        var spectator = ResolveSpectatorOrObserver(payload);
        var engagement = ResolveEngagementPressure(vitals, combat);

        var transport = isAlive == false
            ? TransportIntentNeutral.PreferPause
            : TransportIntentNeutral.NoChange;

        return new NeutralContext(
            IsAlive: isAlive,
            EngagementPressure: engagement,
            ObjectivePressure: null,
            SpectatorOrObserver: spectator,
            TransportIntent: transport,
            ObservedAtUtc: observedAtUtc);
    }

    // Heuristic without a dedicated CS2 DTO field: non-"playing" activity, or a player
    // block without a state sub-block (commonly seen in spectator/menu shapes).
    private static bool? ResolveSpectatorOrObserver(GsiPayloadDto payload)
    {
        var player = payload.Player;
        if (player is null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(player.Activity))
        {
            return !string.Equals(player.Activity, "playing", StringComparison.OrdinalIgnoreCase);
        }

        if (player.State is null)
        {
            return true;
        }

        return null;
    }

    // 0..1 pressure: base 0.5 in combat, plus low-health bump while alive.
    private static float? ResolveEngagementPressure(VitalsModule? vitals, CombatModule? combat)
    {
        if (vitals is null)
        {
            return null;
        }

        var pressure = 0f;
        if (combat?.InCombatHint == true)
        {
            pressure += 0.5f;
        }

        if (vitals.IsAlive && vitals.Health < 50)
        {
            pressure += (50f - vitals.Health) / 100f;
        }

        if (pressure < 0f) pressure = 0f;
        if (pressure > 1f) pressure = 1f;
        return pressure;
    }

    private static SafetyFacts BuildSafetyFacts(
        GsiPayloadDto payload,
        VitalsModule? vitals,
        MatchPhaseNeutral matchPhase,
        DateTimeOffset receivedAt)
    {
        var (isStale, staleNote) = ResolveStaleness(payload, receivedAt);

        MusicSafetyState state;
        string baseReason;
        if (vitals is { IsAlive: false })
        {
            state = MusicSafetyState.Danger;
            baseReason = "player-dead";
        }
        else if (vitals is { IsAlive: true } && matchPhase == MatchPhaseNeutral.Live)
        {
            state = MusicSafetyState.Safe;
            baseReason = "live-and-alive";
        }
        else
        {
            state = MusicSafetyState.Unknown;
            baseReason = vitals is null ? "no-vitals" : "non-live-or-unknown-phase";
        }

        var reason = staleNote is null ? baseReason : $"{baseReason};{staleNote}";
        return new SafetyFacts(state, reason, isStale);
    }

    private static (bool IsStale, string? StaleNote) ResolveStaleness(
        GsiPayloadDto payload,
        DateTimeOffset receivedAt)
    {
        var providerTs = payload.Provider?.Timestamp;
        if (!providerTs.HasValue || providerTs.Value <= 0)
        {
            return (false, "no-provider-timestamp");
        }

        var providerAt = DateTimeOffset.FromUnixTimeSeconds(providerTs.Value);
        var age = receivedAt - providerAt;
        if (age > ProviderTimestampStaleThreshold)
        {
            return (true, "stale-provider-timestamp");
        }

        return (false, null);
    }
}
