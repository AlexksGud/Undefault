using Core.Music;
using GsiHost.Configuration;
using GsiHost.Tooling.Timeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GsiHost.Services;

/// <summary>
/// GsiHost <see cref="IPlaybackEventRecorder"/> that persists confirmed player playback state
/// transitions to the JSONL timeline through <see cref="TimelineCaptureService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Recording is observe-only: it issues no player calls and performs no <c>RulesEngine.ActionMap</c> routing.
/// The <c>PlaybackStateObserver</c> detects playing/not-playing transitions by polling
/// <see cref="IMusicPlayer"/> and is the single source of truth for pause/resume timeline entries
/// (UND-77); this recorder only appends a timeline entry after a transition has already been observed.
/// </para>
/// <para>
/// Persistence is gated to the leftover <c>intent_capture</c> runtime via
/// <see cref="TimelineOptions.IsEnabled(RuntimeOptions)"/>: transitions are recorded only when timeline
/// capture is <see cref="TimelineOptions.Enabled"/> AND the runtime is <c>intent_capture</c>. No playback
/// transitions are recorded in <c>scenario_playback</c>, preserving the default mode's regression baseline.
/// </para>
/// <para>
/// <see cref="TimelineCaptureService"/> is resolved lazily to break a construction-time DI cycle
/// (<c>MusicPlaybackControlCoordinator</c> -&gt; <c>IPlaybackEventRecorder</c> -&gt;
/// <c>TimelineCaptureService</c> -&gt; <c>GsiProcessingService</c> -&gt; <c>RulesEngine</c> -&gt;
/// <c>MusicControlProfileAction</c> -&gt; <c>IMusicPlaybackControl</c> -&gt; coordinator). By deferring
/// the resolution to first use, every singleton is already constructed when the recorder asks for it.
/// </para>
/// </remarks>
public sealed class PlaybackEventRecorder : IPlaybackEventRecorder
{
    private readonly Lazy<TimelineCaptureService> _timeline;
    private readonly TimelineOptions _options;
    private readonly RuntimeOptions _runtime;
    private readonly ILogger<PlaybackEventRecorder> _logger;

    public PlaybackEventRecorder(
        IServiceProvider services,
        IOptions<TimelineOptions> options,
        IOptions<RuntimeOptions> runtime,
        ILogger<PlaybackEventRecorder> logger)
    {
        _timeline = new Lazy<TimelineCaptureService>(
            () => services.GetRequiredService<TimelineCaptureService>(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _options = options.Value;
        _runtime = runtime.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RecordPausedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        => RecordAsync(TimelinePlaybackEvents.Paused, timestampUtc);

    /// <inheritdoc />
    public Task RecordResumedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        => RecordAsync(TimelinePlaybackEvents.Resumed, timestampUtc);

    private Task RecordAsync(string eventKey, DateTimeOffset timestampUtc)
    {
        if (!_options.IsEnabled(_runtime))
        {
            return Task.CompletedTask;
        }

        try
        {
            _timeline.Value.RecordPlaybackTransition(eventKey, timestampUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record playback transition {EventKey}.", eventKey);
        }

        return Task.CompletedTask;
    }
}
