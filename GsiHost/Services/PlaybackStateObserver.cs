using Core.Music;
using Core.Spotify;
using Core.Spotify.Models;
using GsiHost.Configuration;
using Microsoft.Extensions.Options;

namespace GsiHost.Services;

/// <summary>
/// Background service that polls Spotify current playback at a configurable interval and records
/// confirmed <c>is_playing</c> transitions (<c>playback_paused</c> / <c>playback_resumed</c>) to the
/// timeline through <see cref="IPlaybackEventRecorder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The MVP observes Spotify playback state instead of binding a custom global hotkey: physical media
/// play/pause keys cannot be registered with <c>RegisterHotKey</c>, and Spotify controls that playback
/// natively. This observer is the single source of truth for pause/resume timeline entries; the
/// playback control coordinator no longer records transitions (UND-77).
/// </para>
/// <para>
/// The poll loop gates every cycle on <see cref="IsEnabled"/> (<see cref="PlaybackObserverOptions.Enabled"/>
/// AND <c>intent_capture</c> runtime). When not enabled the loop delays and continues without polling or
/// recording. Polling is skipped cleanly (no record, no crash) when Spotify is not authenticated, there
/// is no active device (<see cref="GetCurrentPlaybackAsync"/> returns <see langword="null"/>), there is
/// no current track, or the playback call throws. Repeated poll errors are logged once at Warning and
/// then at Debug to avoid log spam.
/// </para>
/// <para>
/// Transition detection keeps a nullable <c>_lastIsPlaying</c> baseline. The first usable observation
/// establishes the baseline without recording; subsequent changes record exactly one transition entry.
/// </para>
/// </remarks>
public sealed class PlaybackStateObserver : BackgroundService
{
    private readonly ISpotifyClient _spotifyClient;
    private readonly IPlaybackEventRecorder _recorder;
    private readonly PlaybackObserverOptions _options;
    private readonly RuntimeOptions _runtime;
    private readonly ILogger<PlaybackStateObserver> _logger;
    private bool? _lastIsPlaying;
    private int _consecutivePollErrors;

    public PlaybackStateObserver(
        ISpotifyClient spotifyClient,
        IPlaybackEventRecorder recorder,
        IOptions<PlaybackObserverOptions> options,
        IOptions<RuntimeOptions> runtime,
        ILogger<PlaybackStateObserver> logger)
    {
        _spotifyClient = spotifyClient;
        _recorder = recorder;
        _options = options.Value;
        _runtime = runtime.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether the observer is permitted to poll and record:
    /// <see cref="PlaybackObserverOptions.Enabled"/> AND the <c>intent_capture</c> runtime.
    /// </summary>
    public bool IsEnabled => _options.Enabled && _runtime.IsIntentCapture;

    /// <summary>Gets the delay between polls, clamped to at least one second.</summary>
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!IsEnabled)
            {
                continue;
            }

            try
            {
                await PollAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // PollAsync swallows expected Spotify failures; this guard keeps the host loop
                // alive if an unexpected exception escapes a poll cycle.
                LogPollError(ex);
            }
        }
    }

    /// <summary>
    /// Executes a single poll cycle: reads Spotify playback state and records an <c>is_playing</c>
    /// transition through <see cref="IPlaybackEventRecorder"/> when one is detected.
    /// </summary>
    /// <remarks>
    /// This is the testable single-cycle unit used by <see cref="ExecuteAsync"/>. Direct callers must
    /// not invoke it concurrently with the background loop.
    /// </remarks>
    /// <param name="cancellationToken">The token to cancel the poll operation.</param>
    /// <returns>A task that represents the asynchronous poll operation.</returns>
    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (!await _spotifyClient.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        PlaybackState? playback;
        try
        {
            playback = await _spotifyClient.GetCurrentPlaybackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown cancels an in-flight poll; this is not a poll failure.
            return;
        }
        catch (Exception ex)
        {
            LogPollError(ex);
            return;
        }

        if (playback is null || playback.Track is null)
        {
            // No active device or no current track: nothing to observe, keep the last known state.
            return;
        }

        Interlocked.Exchange(ref _consecutivePollErrors, 0);
        await EvaluateTransitionAsync(playback.IsPlaying, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the last-known <c>is_playing</c> baseline so the next usable poll re-establishes state
    /// without emitting a spurious transition. Called from <see cref="GsiResetService"/> on
    /// <c>POST /gsi/reset</c> (session boundary).
    /// </summary>
    public void Reset()
    {
        _lastIsPlaying = null;
        Interlocked.Exchange(ref _consecutivePollErrors, 0);
    }

    private async Task EvaluateTransitionAsync(bool isPlaying, CancellationToken cancellationToken)
    {
        var previous = _lastIsPlaying;
        if (previous is null)
        {
            // First usable observation establishes the baseline; no transition to record.
            _lastIsPlaying = isPlaying;
            return;
        }

        if (previous.Value == isPlaying)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        string direction;
        try
        {
            if (previous.Value && !isPlaying)
            {
                direction = "paused";
                await _recorder.RecordPausedAsync(timestamp, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                direction = "resumed";
                await _recorder.RecordResumedAsync(timestamp, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Recording is observe-only: a timeline failure must never leave the baseline stuck
            // (which would re-fire the same transition on the next poll) or kill the poll loop.
            _logger.LogWarning(ex, "Playback transition recording failed.");
            _lastIsPlaying = isPlaying;
            return;
        }

        // Visible moment-of-switch line so music on/off shows in the host log in real time.
        // The JSONL/timeline record itself is written silently by the recorder.
        _logger.LogInformation("Playback {Direction} at {Timestamp:O}", direction, timestamp);

        _lastIsPlaying = isPlaying;
    }

    private void LogPollError(Exception ex)
    {
        if (Interlocked.Increment(ref _consecutivePollErrors) == 1)
        {
            _logger.LogWarning(
                ex,
                "Playback state observer poll failed; further consecutive failures are logged at Debug.");
        }
        else
        {
            _logger.LogDebug(ex, "Playback state observer poll failed ({Count} consecutive).", _consecutivePollErrors);
        }
    }
}
