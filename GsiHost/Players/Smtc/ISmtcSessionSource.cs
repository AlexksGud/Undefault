#if WINDOWS
namespace GsiHost.Players.Smtc;

/// <summary>
/// Enumerates SMTC sessions and issues per-session transport commands.
/// </summary>
/// <remarks>
/// GsiHost-only seam. The Dubya <c>MediaManager</c> wrapper is the sole WinRT implementation.
/// Selection and tests talk to this interface, never to <c>MediaManager</c>.
/// Commands must target an exact <c>SourceAppUserModelId</c> (ordinal).
/// <c>GetCurrentSession()</c> is never a command target.
/// </remarks>
public interface ISmtcSessionSource
{
    /// <summary>
    /// Returns a snapshot of currently known sessions.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// Zero or more sessions. An empty list is idle, not a source failure.
    /// </returns>
    Task<IReadOnlyList<SmtcSessionSnapshot>> GetSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues play on the session whose id matches <paramref name="sourceAppUserModelId"/> exactly.
    /// </summary>
    /// <param name="sourceAppUserModelId">The exact id copied from an enumerated session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> when SMTC accepted the command;
    /// <see langword="false"/> when SMTC returned false;
    /// <see langword="null"/> when no session has that exact id.
    /// </returns>
    Task<bool?> TryPlayAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues pause on the session whose id matches <paramref name="sourceAppUserModelId"/> exactly.
    /// </summary>
    /// <param name="sourceAppUserModelId">The exact id copied from an enumerated session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> when SMTC accepted the command;
    /// <see langword="false"/> when SMTC returned false;
    /// <see langword="null"/> when no session has that exact id.
    /// </returns>
    Task<bool?> TryPauseAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues skip-next on the session whose id matches <paramref name="sourceAppUserModelId"/> exactly.
    /// </summary>
    /// <param name="sourceAppUserModelId">The exact id copied from an enumerated session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> when SMTC accepted the command;
    /// <see langword="false"/> when SMTC returned false;
    /// <see langword="null"/> when no session has that exact id.
    /// </returns>
    Task<bool?> TrySkipNextAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues skip-previous on the session whose id matches <paramref name="sourceAppUserModelId"/> exactly.
    /// </summary>
    /// <param name="sourceAppUserModelId">The exact id copied from an enumerated session.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <see langword="true"/> when SMTC accepted the command;
    /// <see langword="false"/> when SMTC returned false;
    /// <see langword="null"/> when no session has that exact id.
    /// </returns>
    Task<bool?> TrySkipPreviousAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the underlying session dictionary.
    /// </summary>
    /// <remarks>
    /// Called by <c>SmtcMusicPlayer</c> when the selected id is absent, both on the command
    /// path and from a low-frequency timer. Not a substitute for exact-id targeting.
    /// Dubya <c>SessionsChanged</c> is not used as the sole reattach signal.
    /// </remarks>
    void ForceUpdate();
}
#endif
