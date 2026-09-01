namespace Core.Music;

/// <summary>
/// Small player-backend contract. HTTP, OAuth, and vendor URLs stay in adapters.
/// </summary>
public interface IMusicPlayer
{
    /// <summary>
    /// Gets the operations this adapter supports.
    /// </summary>
    MusicPlayerCapabilities Capabilities { get; }

    /// <summary>
    /// Returns whether the player is reachable. This is not an authentication check.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true" /> if the player is reachable; otherwise, <see langword="false" />.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current playback snapshot, or <see langword="null" /> when state cannot be read.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The playback snapshot, or <see langword="null" /> when the player is unavailable.</returns>
    Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts playback.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result. Implementations must not throw for ordinary player or transport failures.</returns>
    Task<MusicCommandResult> PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses playback.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result. An already-paused player is not <see cref="MusicCommandOutcome.Failed"/>.</returns>
    Task<MusicCommandResult> PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes playback. Implementations must be idempotent when already playing.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result. An already-playing player is not <see cref="MusicCommandOutcome.Failed"/>.</returns>
    Task<MusicCommandResult> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips to the next track.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result.</returns>
    Task<MusicCommandResult> NextAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips to the previous track.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result.</returns>
    Task<MusicCommandResult> PreviousAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets playback volume.
    /// </summary>
    /// <param name="volumePercent">The target volume in the range 0–100.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The command result.</returns>
    Task<MusicCommandResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default);
}
