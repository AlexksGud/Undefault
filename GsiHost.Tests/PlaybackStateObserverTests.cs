using Core.Spotify;
using Core.Spotify.Models;
using FluentAssertions;
using GsiHost.Configuration;
using GsiHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests;

/// <summary>
/// Unit tests for <see cref="PlaybackStateObserver"/> transition detection, skip behavior, and gating.
/// Drives the single-cycle <see cref="PlaybackStateObserver.PollAsync"/> seam directly with a controllable
/// fake <see cref="ISpotifyClient"/> and a spy <see cref="IPlaybackEventRecorder"/> for deterministic coverage.
/// </summary>
public sealed class PlaybackStateObserverTests
{
    [Fact]
    public async Task PauseTransition_TrueToFalse_RecordsPausedOnceWithTimestamp()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync(); // establishes playing baseline, no record

        client.CurrentPlayback = Paused();

        await observer.PollAsync(); // true -> false

        recorder.PausedCount.Should().Be(1);
        recorder.ResumedCount.Should().Be(0);
        recorder.PausedTimestamps.Single().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        client.GetCurrentPlaybackCalls.Should().Be(2);
    }

    [Fact]
    public async Task ResumeTransition_FalseToTrue_RecordsResumedOnce()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Paused() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync(); // establishes paused baseline, no record

        client.CurrentPlayback = Playing();

        await observer.PollAsync(); // false -> true

        recorder.ResumedCount.Should().Be(1);
        recorder.PausedCount.Should().Be(0);
        recorder.ResumedTimestamps.Single().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SameStateAcrossPolls_RecordsNothing()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync();
        await observer.PollAsync();
        await observer.PollAsync();

        recorder.Calls.Should().BeEmpty();
        client.GetCurrentPlaybackCalls.Should().Be(3);
    }

    [Fact]
    public async Task NotAuthenticated_DoesNotPollOrRecord()
    {
        var client = new FakeSpotifyClient { Authenticated = false, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync();

        client.IsAuthenticatedCalls.Should().Be(1);
        client.GetCurrentPlaybackCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task NullPlayback_DoesNotRecordOrCrash_AndKeepsState()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = null };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        var act = async () => await observer.PollAsync();
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty();
        client.GetCurrentPlaybackCalls.Should().Be(1);

        // A null-device poll must not seed a baseline that suppresses the next real transition.
        client.CurrentPlayback = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty("the first usable observation after a null device is a baseline");

        client.CurrentPlayback = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task NoTrack_DoesNotRecord_AndDoesNotSeedBaseline()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing(track: null) };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync();

        recorder.Calls.Should().BeEmpty();
        client.GetCurrentPlaybackCalls.Should().Be(1);

        // Skipping the no-track poll must keep _lastIsPlaying unset so the next track-bearing poll
        // is treated as a fresh baseline rather than a spurious transition.
        client.CurrentPlayback = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty();

        client.CurrentPlayback = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task ApiThrows_DoesNotRecordOrCrash_AndLoopContinues()
    {
        var client = new FakeSpotifyClient
        {
            Authenticated = true,
            CurrentPlayback = Playing(),
            GetCurrentPlaybackException = new InvalidOperationException("Spotify unavailable")
        };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        var act = async () => await observer.PollAsync();
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty();
        client.GetCurrentPlaybackCalls.Should().Be(1);

        // The throw must not seed a baseline; subsequent usable polls keep working (loop continues).
        client.GetCurrentPlaybackException = null;
        client.CurrentPlayback = Playing();
        await observer.PollAsync();
        recorder.Calls.Should().BeEmpty("first usable observation after a throw is a baseline");

        client.CurrentPlayback = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelledPoll_ReturnsSilently_WithoutRecording()
    {
        // Simulates host shutdown cancelling an in-flight poll: the HTTP call throws
        // OperationCanceledException, and the observer must return silently (no "poll failed"
        // log, no spurious record) rather than treating it as a failure.
        var client = new FakeSpotifyClient
        {
            Authenticated = true,
            CurrentPlayback = Playing(),
            GetCurrentPlaybackException = new TaskCanceledException("shutdown")
        };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await observer.PollAsync(cts.Token);
        await act.Should().NotThrowAsync();

        recorder.Calls.Should().BeEmpty("cancellation during an in-flight poll is not a transition");
        client.GetCurrentPlaybackCalls.Should().Be(1);
    }

    [Fact]
    public async Task EnabledFalse_DoesNotPollOrRecord()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder, enabled: false);

        observer.IsEnabled.Should().BeFalse();

        await observer.PollAsync();

        client.IsAuthenticatedCalls.Should().Be(0);
        client.GetCurrentPlaybackCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ScenarioPlaybackRuntime_DoesNotPollOrRecord()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder, runtimeMode: RuntimeModes.ScenarioPlayback);

        observer.IsEnabled.Should().BeFalse();

        await observer.PollAsync();

        client.IsAuthenticatedCalls.Should().Be(0);
        client.GetCurrentPlaybackCalls.Should().Be(0);
        recorder.Calls.Should().BeEmpty();
    }

    [Fact]
    public void PollInterval_ReflectsConfiguredSeconds_ClampedToOne()
    {
        var client = new FakeSpotifyClient();
        var recorder = new SpyPlaybackEventRecorder();

        CreateObserver(client, recorder, pollIntervalSeconds: 5).PollInterval
            .Should().Be(TimeSpan.FromSeconds(5));
        CreateObserver(client, recorder, pollIntervalSeconds: 0).PollInterval
            .Should().Be(TimeSpan.FromSeconds(1), "values below 1 are clamped up");
        CreateObserver(client, recorder, pollIntervalSeconds: -3).PollInterval
            .Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SingleTransition_RecordsExactlyOnce_NoDuplicate()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync(); // baseline playing
        client.CurrentPlayback = Paused();

        await observer.PollAsync(); // transition -> one paused record
        await observer.PollAsync(); // same state -> no record
        await observer.PollAsync(); // same state -> no record

        recorder.PausedCount.Should().Be(1);
        recorder.ResumedCount.Should().Be(0);
    }

    [Fact]
    public async Task RecorderFailure_UpdatesBaseline_AndDoesNotDuplicateOnNextPoll()
    {
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new ThrowingPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync(); // baseline playing
        client.CurrentPlayback = Paused();

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
        var client = new FakeSpotifyClient { Authenticated = true, CurrentPlayback = Playing() };
        var recorder = new SpyPlaybackEventRecorder();
        var observer = CreateObserver(client, recorder);

        await observer.PollAsync(); // baseline playing
        client.CurrentPlayback = Paused();
        await observer.PollAsync(); // records paused
        recorder.PausedCount.Should().Be(1);

        observer.Reset();

        // After reset the next usable poll is a fresh baseline even if state flipped again.
        client.CurrentPlayback = Playing();
        await observer.PollAsync();
        recorder.ResumedCount.Should().Be(0, "first observation after reset is a baseline");

        client.CurrentPlayback = Paused();
        await observer.PollAsync();
        recorder.PausedCount.Should().Be(2);
    }

    private static PlaybackStateObserver CreateObserver(
        ISpotifyClient client,
        IPlaybackEventRecorder recorder,
        bool enabled = true,
        string runtimeMode = RuntimeModes.IntentCapture,
        int pollIntervalSeconds = 2)
    {
        return new PlaybackStateObserver(
            client,
            recorder,
            Options.Create(new PlaybackObserverOptions
            {
                Enabled = enabled,
                PollIntervalSeconds = pollIntervalSeconds
            }),
            Options.Create(new RuntimeOptions { Mode = runtimeMode }),
            NullLogger<PlaybackStateObserver>.Instance);
    }

    private static PlaybackState Playing(Track? track = null) => State(isPlaying: true, track);

    private static PlaybackState Paused(Track? track = null) => State(isPlaying: false, track);

    private static PlaybackState State(bool isPlaying, Track? track) => new(
        IsPlaying: isPlaying,
        VolumePercent: 70,
        Track: track ?? SampleTrack(),
        DeviceId: "device",
        DeviceName: "Desktop");

    private static Track SampleTrack() => new(
        Id: "track-1",
        Name: "Song",
        Uri: "spotify:track:track-1",
        DurationMs: 180_000,
        Artists: new List<Artist> { new("artist-1", "Artist") },
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

    private sealed class FakeSpotifyClient : ISpotifyClient
    {
        public bool Authenticated { get; set; }
        public PlaybackState? CurrentPlayback { get; set; }
        public Exception? GetCurrentPlaybackException { get; set; }
        public int IsAuthenticatedCalls;
        public int GetCurrentPlaybackCalls;

        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref IsAuthenticatedCalls);
            return Task.FromResult(Authenticated);
        }

        public Task<PlaybackState?> GetCurrentPlaybackAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref GetCurrentPlaybackCalls);
            return GetCurrentPlaybackException is null
                ? Task.FromResult(CurrentPlayback)
                : Task.FromException<PlaybackState?>(GetCurrentPlaybackException);
        }

        public Task PlayAsync(string? uri = null, int? positionMs = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
