namespace Core.Music;

/// <summary>
/// Records confirmed player playback state transitions (pause / resume) as an observe-only side effect.
/// </summary>
/// <remarks>
/// <para>
/// Recording is invoked by <c>GsiHost.Services.PlaybackStateObserver</c> after it detects a confirmed
/// playing/paused transition while polling player playback state. Implementations must remain strictly
/// observe-only: no player API calls, no routing through <c>RulesEngine.ActionMap</c>, and no dependency
/// on host types.
/// </para>
/// <para>
/// The observer skips recording for no-op states (no usable prior baseline), authentication or device
/// failures, missing tracks, and exceptions, so only real transitions are recorded.
/// </para>
/// </remarks>
public interface IPlaybackEventRecorder
{
    /// <summary>
    /// Records that playback transitioned to paused at the supplied timestamp.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp of the confirmed state transition.</param>
    /// <param name="cancellationToken">The token to cancel the record operation.</param>
    /// <returns>A task that represents the asynchronous record operation.</returns>
    Task RecordPausedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that playback transitioned to resumed (playing) at the supplied timestamp.
    /// </summary>
    /// <param name="timestampUtc">The UTC timestamp of the confirmed state transition.</param>
    /// <param name="cancellationToken">The token to cancel the record operation.</param>
    /// <returns>A task that represents the asynchronous record operation.</returns>
    Task RecordResumedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);
}
