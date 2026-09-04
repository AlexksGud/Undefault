using Core.Adapters;
using Core.Diff;
using Core.Models;
using Core.Music;
using Core.Rules;
using FluentAssertions;

namespace Core.Tests;

public class EventDetectorTests
{
    [Fact]
    public void Detect_EmitsDeath_OnIsAliveTransitionAndRespectsCooldown()
    {
        var options = new EventDetectorOptions
        {
            EnableRoundStart = false,
            EnableDeath = true,
            EnableCombat = false,
            EnableIdle = false,
            DeathCooldown = TimeSpan.FromSeconds(5),
            CombatDebounce = TimeSpan.Zero,
            IdleDebounce = TimeSpan.Zero
        };
        var detector = new EventDetector(options);
        var t0 = DateTimeOffset.UtcNow;

        var first = BuildContext(previous: null, current: BuildObs(t0, isAlive: true), isFirst: true);
        detector.Detect(first);

        var alivePrev = BuildObs(t0, isAlive: true);
        var deadCurr = BuildObs(t0.AddSeconds(1), isAlive: false);
        var deathContext = BuildContext(alivePrev, deadCurr);

        var events = detector.Detect(deathContext);

        events.Should().ContainSingle(e => e.Type == EventType.Death && e.EventKey == EventKeys.Death);

        var stillDead = BuildObs(t0.AddSeconds(2), isAlive: false);
        var coolingContext = BuildContext(deadCurr, stillDead);
        var nextEvents = detector.Detect(coolingContext);

        nextEvents.Should().NotContain(e => e.Type == EventType.Death);
    }

    [Fact]
    public void Detect_EmitsDeath_OnIsAliveTrueToFalseTransition()
    {
        var detector = new EventDetector(new EventDetectorOptions
        {
            EnableRoundStart = false,
            EnableDeath = true,
            EnableCombat = false,
            EnableIdle = false,
            DeathCooldown = TimeSpan.Zero,
        });
        var t0 = DateTimeOffset.UtcNow;

        detector.Detect(BuildContext(previous: null, current: BuildObs(t0, isAlive: true), isFirst: true));

        var prev = BuildObs(t0, isAlive: true);
        var curr = BuildObs(t0.AddSeconds(1), isAlive: false);

        var events = detector.Detect(BuildContext(prev, curr));

        events.Should().ContainSingle(e => e.EventKey == EventKeys.Death);
    }

    [Fact]
    public void Detect_DoesNotEmitDeath_WhenIsAliveTransitionsFromUnknown()
    {
        var detector = new EventDetector(new EventDetectorOptions
        {
            EnableRoundStart = false,
            EnableDeath = true,
            EnableCombat = false,
            EnableIdle = false,
            DeathCooldown = TimeSpan.Zero,
        });
        var t0 = DateTimeOffset.UtcNow;

        detector.Detect(BuildContext(previous: null, current: BuildObs(t0, isAlive: null), isFirst: true));

        var prev = BuildObs(t0, isAlive: null);
        var curr = BuildObs(t0.AddSeconds(1), isAlive: false);

        var events = detector.Detect(BuildContext(prev, curr));

        events.Should().NotContain(e => e.EventKey == EventKeys.Death);
    }

    [Fact]
    public void Detect_EmitsCombat_WhenCombatHintIsTrue()
    {
        var options = new EventDetectorOptions
        {
            EnableRoundStart = false,
            EnableDeath = false,
            EnableCombat = true,
            EnableIdle = false,
            CombatCooldown = TimeSpan.Zero,
            CombatDebounce = TimeSpan.Zero
        };
        var detector = new EventDetector(options);
        var differ = new SnapshotDiffer();
        var t0 = DateTimeOffset.UtcNow;

        var prev = BuildObs(t0, isAlive: true, inCombat: false);
        detector.Detect(BuildContext(previous: null, current: prev, isFirst: true));

        var curr = BuildObs(t0.AddSeconds(1), isAlive: true, inCombat: true);
        var diff = differ.Compute(prev.Raw, curr.Raw);
        var context = new NeutralDetectorContext(curr, prev, diff.Activity, IsFirstObservation: false);

        var events = detector.Detect(context);

        events.Should().ContainSingle(e => e.Type == EventType.Combat && e.EventKey == EventKeys.Combat);
    }

    [Fact]
    public void Detect_EmitsIdle_WhenNoActivityAfterDebounce()
    {
        var options = new EventDetectorOptions
        {
            EnableRoundStart = false,
            EnableDeath = false,
            EnableCombat = false,
            EnableIdle = true,
            IdleCooldown = TimeSpan.Zero,
            IdleDebounce = TimeSpan.Zero
        };
        var detector = new EventDetector(options);
        var differ = new SnapshotDiffer();
        var t0 = DateTimeOffset.UtcNow;

        var prev = BuildObs(t0, isAlive: true);
        detector.Detect(BuildContext(previous: null, current: prev, isFirst: true));

        var curr = BuildObs(t0.AddSeconds(1), isAlive: true);
        var diff = differ.Compute(prev.Raw, curr.Raw);
        var context = new NeutralDetectorContext(curr, prev, diff.Activity, IsFirstObservation: false);

        var events = detector.Detect(context);

        events.Should().ContainSingle(e => e.Type == EventType.Idle && e.EventKey == EventKeys.Idle);
    }

    [Fact]
    public void Detect_EmitsRoundStart_OnPreLiveToLiveTransition()
    {
        var detector = new EventDetector(new EventDetectorOptions
        {
            EnableRoundStart = true,
            EnableDeath = false,
            EnableCombat = false,
            EnableIdle = false,
        });
        var t0 = DateTimeOffset.UtcNow;

        var prev = BuildObs(t0, isAlive: true, matchPhase: MatchPhaseNeutral.PreLive, roundIndex: 2);
        detector.Detect(BuildContext(previous: null, current: prev, isFirst: true));

        var curr = BuildObs(t0.AddSeconds(1), isAlive: true, matchPhase: MatchPhaseNeutral.Live, roundIndex: 2);

        var events = detector.Detect(BuildContext(prev, curr));

        events.Should().ContainSingle(e => e.Type == EventType.RoundStart && e.EventKey == EventKeys.RoundStart);
        events[0].Detail.Should().Be("round=2");
    }

    [Fact]
    public void Detect_EmitsRoundStart_OnRoundIndexIncrement()
    {
        var detector = new EventDetector(new EventDetectorOptions
        {
            EnableRoundStart = true,
            EnableDeath = false,
            EnableCombat = false,
            EnableIdle = false,
        });
        var t0 = DateTimeOffset.UtcNow;

        var prev = BuildObs(t0, isAlive: true, matchPhase: MatchPhaseNeutral.Live, roundIndex: 3);
        detector.Detect(BuildContext(previous: null, current: prev, isFirst: true));

        var curr = BuildObs(t0.AddSeconds(1), isAlive: true, matchPhase: MatchPhaseNeutral.Live, roundIndex: 4);

        var events = detector.Detect(BuildContext(prev, curr));

        events.Should().ContainSingle(e => e.EventKey == EventKeys.RoundStart);
        events[0].Detail.Should().Be("round=4");
    }

    [Fact]
    public void Detect_DoesNotEmitRoundStart_WhenPhaseStaysLive()
    {
        var detector = new EventDetector(new EventDetectorOptions
        {
            EnableRoundStart = true,
            EnableDeath = false,
            EnableCombat = false,
            EnableIdle = false,
        });
        var t0 = DateTimeOffset.UtcNow;

        var prev = BuildObs(t0, isAlive: true, matchPhase: MatchPhaseNeutral.Live, roundIndex: 5);
        detector.Detect(BuildContext(previous: null, current: prev, isFirst: true));

        var curr = BuildObs(t0.AddSeconds(1), isAlive: true, matchPhase: MatchPhaseNeutral.Live, roundIndex: 5);

        var events = detector.Detect(BuildContext(prev, curr));

        events.Should().NotContain(e => e.EventKey == EventKeys.RoundStart);
    }

    private static NeutralDetectorContext BuildContext(
        AdapterObservation? previous,
        AdapterObservation current,
        bool isFirst = false)
    {
        var differ = new SnapshotDiffer();
        var diff = differ.Compute(previous?.Raw, current.Raw);
        return new NeutralDetectorContext(current, previous, diff.Activity, isFirst);
    }

    private static AdapterObservation BuildObs(
        DateTimeOffset timestamp,
        bool? isAlive,
        bool inCombat = false,
        MatchPhaseNeutral matchPhase = MatchPhaseNeutral.Unknown,
        int? roundIndex = null)
    {
        var modules = new List<ISnapshotModule>
        {
            new VitalsModule(Health: isAlive == false ? 0 : 100, Armor: 0, IsAlive: isAlive ?? false),
            new PositionModule(Position: Vector3.Zero, IsMoving: false),
            new CombatModule(InCombatHint: inCombat, LastDamageDealtAt: null, LastDamageReceivedAt: null),
        };

        var raw = new GameSnapshot(
            Timestamp: timestamp,
            GameId: "cs2",
            MatchId: "match",
            PlayerId: "player",
            Modules: modules);

        var clock = new GameClockSnapshot(
            WallTimeUtc: timestamp,
            GameTimeSeconds: null,
            IsGamePaused: false,
            MatchPhase: matchPhase,
            RoundIndex: roundIndex);

        var neutral = new NeutralContext(
            IsAlive: isAlive,
            EngagementPressure: null,
            ObjectivePressure: null,
            SpectatorOrObserver: null,
            TransportIntent: TransportIntentNeutral.NoChange,
            ObservedAtUtc: timestamp);

        var safety = SafetyFacts.Unknown();

        return new AdapterObservation(raw, clock, neutral, safety);
    }
}
