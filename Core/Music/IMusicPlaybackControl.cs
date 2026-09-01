using Core.Configuration;
using Core.Models;

namespace Core.Music;

/// <summary>
/// Session-level pause, resume, skip, duck, and restore semantics shared by control-profile actions.
/// </summary>
/// <remarks>
/// Provider HTTP does not live here. Implementations must fail softly: log and return rather than throw to the host.
/// </remarks>
public interface IMusicPlaybackControl
{
    /// <summary>
    /// Pauses playback when the player is available and currently playing.
    /// </summary>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result. An already-paused player is not <see cref="MusicCommandOutcome.Failed"/>.</returns>
    Task<MusicCommandResult> TryPauseAsync(string? eventKeyForLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes playback when the player is available and not already playing.
    /// </summary>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result. An already-playing player is not <see cref="MusicCommandOutcome.Failed"/>.</returns>
    Task<MusicCommandResult> TryResumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips to the next track when the player is available.
    /// </summary>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result.</returns>
    Task<MusicCommandResult> TryNextAsync(string? eventKeyForLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips to the previous track when the player is available.
    /// </summary>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result.</returns>
    Task<MusicCommandResult> TryPreviousAsync(string? eventKeyForLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lowers volume using the rule target or configured mute volume, saving restore volume when a duck is not already active.
    /// </summary>
    /// <param name="rule">The control rule that matched the event.</param>
    /// <param name="context">The normalized event that triggered the duck.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result.</returns>
    Task<MusicCommandResult> TryDuckAsync(
        EventControlRule rule,
        NormalizedEvent context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lowers volume to the specified percent, saving restore volume when a duck is not already active.
    /// </summary>
    /// <param name="volumePercent">The target volume in the range 0–100.</param>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result.</returns>
    Task<MusicCommandResult> TryDuckAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores volume saved by a prior duck when managed duck state is active.
    /// </summary>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result. No active duck is not <see cref="MusicCommandOutcome.Failed"/>.</returns>
    Task<MusicCommandResult> TryRestoreVolumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets playback volume while a managed session is active; if inactive, captures current volume as restore target then applies.
    /// </summary>
    /// <param name="volumePercent">The target volume in the range 0–100.</param>
    /// <param name="eventKeyForLog">The event key used in diagnostic logs, or <see langword="null"/> for non-event callers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The last command result.</returns>
    Task<MusicCommandResult> TrySetManagedVolumeAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default);
}
