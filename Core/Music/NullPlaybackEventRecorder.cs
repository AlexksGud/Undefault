namespace Core.Music;

/// <summary>
/// A no-op <see cref="IPlaybackEventRecorder"/> that discards every recorded transition.
/// </summary>
/// <remarks>
/// Used when timeline capture is not configured (Core unit tests, <c>scenario_playback</c> without
/// recording, or when no recorder is supplied to <see cref="MusicPlaybackControlCoordinator"/>).
/// </remarks>
public sealed class NullPlaybackEventRecorder : IPlaybackEventRecorder
{
    /// <summary>Gets the singleton instance of the no-op recorder.</summary>
    public static NullPlaybackEventRecorder Instance { get; } = new();

    private NullPlaybackEventRecorder()
    {
    }

    /// <inheritdoc />
    public Task RecordPausedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task RecordResumedAsync(DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
