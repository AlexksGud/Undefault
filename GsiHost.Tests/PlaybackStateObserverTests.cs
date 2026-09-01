using Core.Music;
using FluentAssertions;
using GsiHost.Configuration;
using GsiHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests;

/// <summary>
/// Unit tests for <see cref="PlaybackStateObserver"/> transition detection, skip behavior, and gating.
/// Drives the single-cycle <see cref="PlaybackStateObserver.PollAsync"/> seam directly with a controllable
/// fake <see cref="IMusicPlayer"/> and a spy <see cref="IPlaybackEventRecorder"/> for deterministic coverage.
/// </summary>
public sealed class PlaybackStateObserverTests
{
    [Fact]
    public async Task PauseTransition_TrueToFalse_RecordsPausedOnceWithTimestamp()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync(); // establishes playing baseline, no record

        player.State = Paused();

        await observer.PollAsync(); // true -> false

        recorder.PausedCount.Should().Be(1);
        recorder.ResumedCount.Should().Be(0);
        recorder.PausedTimestamps.Single().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        player.GetStateCalls.Should().Be(2);
    }

    [Fact]
    public async Task ResumeTransition_FalseToTrue_RecordsResumedOnce()
    {
        var player = new FakeMusicPlayer { Available = true, State = Paused() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync(); // establishes paused baseline, no record

        player.State = Playing();

        await observer.PollAsync(); // false -> true

        recorder.ResumedCount.Should().Be(1);
        recorder.PausedCount.Should().Be(0);
        recorder.ResumedTimestamps.Single().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SameStateAcrossPolls_RecordsNothing()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync();
        await observer.PollAsync();
        await observer.PollAsync();

        recorder.Calls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(3);
    }

    [Fact]
    public async Task PlayerUnavailable_DoesNotPollOrRecord()
    {
        var player = new FakeMusicPlayer { Available = false, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync();

        player.IsAvailableCalls.Should().Be(1);
        player.GetStateCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task NullPlayback_DoesNotRecordOrCrash_AndKeepsState()
    {
        var player = new FakeMusicPlayer { Available = true, State = null };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        var act = async () => await observer.PollAsync();
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(1);

        // A null-state poll must not seed a baseline that suppresses the next real transition.
        player.State = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty("the first usable observation after a null state is a baseline");

        player.State = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task NoTrack_DoesNotRecord_AndDoesNotSeedBaseline()
    {
        var player = new FakeMusicPlayer { Available = true, State = PlayingWithoutTrack() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync();

        recorder.Calls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(1);

        // Skipping the no-track poll must keep _lastIsPlaying unset so the next track-bearing poll
        // is treated as a fresh baseline rather than a spurious transition.
        player.State = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty();

        player.State = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task ApiThrows_DoesNotRecordOrCrash_AndLoopContinues()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            GetStateException = new InvalidOperationException("Player unavailable")
        };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        var act = async () => await observer.PollAsync();
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(1);

        // The throw must not seed a baseline; subsequent usable polls keep working (loop continues).
        player.GetStateException = null;
        player.State = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty("first usable observation after a throw is a baseline");

        player.State = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelledPoll_ReturnsSilently_WithoutRecording()
    {
        // Simulates host shutdown cancelling an in-flight poll: the player call throws
        // OperationCanceledException, and the observer must return silently (no "poll failed"
        // log, no spurious record) rather than treating it as a failure.
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            GetStateException = new TaskCanceledException("shutdown")
        };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await observer.PollAsync(cts.Token);
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty("cancellation during an in-flight poll is not a transition");
        player.GetStateCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnabledFalse_DoesNotPollOrRecord()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder, enabled: false);

        observer.IsEnabled.Should().BeFalse();

        await observer.PollAsync();

        player.IsAvailableCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ScenarioPlaybackRuntime_DoesNotPollOrRecord()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder, runtimeMode: RuntimeModes.ScenarioPlayback);

        observer.IsEnabled.Should().BeFalse();

        await observer.PollAsync();

        player.IsAvailableCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public void PollInterval_ReflectsConfiguredSeconds_ClampedToOne()
    {
        var player = new FakeMusicPlayer();
        var recorder = new SpyPlaybackEventRecorder();

        CreateObserver(player, recorder, pollIntervalSeconds: 5).PollInterval
            .Should().Be(TimeSpan.FromSeconds(5));
        CreateObserver(player, recorder, pollIntervalSeconds: 0).PollInterval
            .Should().Be(TimeSpan.FromSeconds(1), "values below 1 are clamped up");
        CreateObserver(player, recorder, pollIntervalSeconds: -3).PollInterval
            .Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SingleTransition_RecordsExactlyOnce_NoDuplicate()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync(); // baseline playing
        player.State = Paused();

        await observer.PollAsync(); // transition -> one paused record
        await observer.PollAsync(); // same state -> no record
        await observer.PollAsync(); // same state -> no record

        recorder.PausedCount.Should().Be(1);
        recorder.ResumedCount.Should().Be(0);
    }

    [Fact]
    public async Task RecorderFailure_UpdatesBaseline_AndDoesNotDuplicateOnNextPoll()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new ThrowingPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync(); // baseline playing
        player.State = Paused();

        var act = async () => await observer.PollAsync();
        await act.Should().NotThrowAsync();
        recorder.PausedAttempts.Should().Be(1);

        // Same paused state on the next poll must not retry the transition.
        await observer.PollAsync();
        recorder.PausedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Reset_ClearsBaseline_SoNextPollDoesNotEmitSpuriousTransition()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(player, recorder);

        await observer.PollAsync(); // baseline playing
        player.State = Paused();
        await observer.PollAsync(); // records paused
        recorder.PausedCount.Should().Be(1);

        observer.Reset();

        // After reset the next usable poll is a fresh baseline even if state flipped again.
        player.State = Playing();
        await observer.PollAsync();
        recorder.ResumedCount.Should().Be(0, "first observation after reset is a baseline");

        player.State = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(2);
    }

    private static PlaybackStateObserver CreateObserver(
        IMusicPlayer player,
        IPlaybackEventRecorder recorder,
        bool enabled = true,
        string runtimeMode = RuntimeModes.IntentCapture,
        int pollIntervalSeconds = 2)
    {
        return new PlaybackStateObserver(
            player,
            recorder,
            Options.Create(new PlaybackObserverOptions
            {
                Enabled = enabled,
                PollIntervalSeconds = pollIntervalSeconds
            }),
            Options.Create(new RuntimeOptions { Mode = runtimeMode }),
            NullLogger<PlaybackStateObserver>.Instance);
    }

    private static MusicPlaybackState Playing() => State(PlaybackStatus.Playing, SampleTrack());

    private static MusicPlaybackState PlayingWithoutTrack() => State(PlaybackStatus.Playing, track: null);

    private static MusicPlaybackState Paused() => State(PlaybackStatus.Paused, SampleTrack());

    private static MusicPlaybackState State(PlaybackStatus status, MusicTrack? track) => new(
        Status: status,
        Track: track,
        VolumePercent: 70);

    private static MusicTrack SampleTrack() => new(
        Id: "track-1",
        Title: "Song",
        Artist: "Artist",
        Album: null);

    private sealed class SpyPlaybackEventRecorder : IPlaybackEventRecorder
    {
        private readonly List<(string Kind, DateTimeOffset Timestamp)> _calls = new();

        public IReadOnlyList<(string Kind, DateTimeOffset Timestamp)> Calls => _calls;
        public int PausedCount => _calls.Count(c => c.Kind == "paused");
        public int ResumedCount => _calls.Count(c => c.Kind == "resumed");
        public IReadOnlyList<DateTimeOffset> PausedTimestamps => _calls.Where(c => c.Kind == "paused").Select(c => c.Timestamp).ToList();
        public IReadOnlyList<DateTimeOffset> ResumedTimestamps => _calls.Where(c => c.Kind == "resumed").Select(c => c.Timestamp).ToList();

        public Task RecordPausedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        {
            _calls.Add(("paused", timestampUtc));
            return Task.CompletedTask;
        }

        public Task RecordResumedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        {
            _calls.Add(("resumed", timestampUtc));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPlaybackEventRecorder : IPlaybackEventRecorder
    {
        public int PausedAttempts;

        public Task RecordPausedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PausedAttempts);
            return Task.FromException(new InvalidOperationException("timeline unavailable"));
        }

        public Task RecordResumedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("timeline unavailable"));
    }

    private sealed class FakeMusicPlayer : IMusicPlayer
    {
        public bool Available { get; set; } = true;
        public MusicPlaybackState? State { get; set; }
        public Exception? GetStateException { get; set; }
        public int IsAvailableCalls;
        public int GetStateCalls;

        public MusicPlayerCapabilities Capabilities => MusicPlayerCapabilities.Mvp;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref IsAvailableCalls);
            return Task.FromResult(Available);
        }

        public Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref GetStateCalls);
            return GetStateException is null
                ? Task.FromResult(State)
                : Task.FromException<MusicPlaybackState?>(GetStateException);
        }

        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
