#if WINDOWS
using Core.Music;
using Windows.Media.Control;
using WindowsMediaController;

namespace GsiHost.Players.Smtc;

/// <summary>
/// Dubya <see cref="MediaManager"/> implementation of <see cref="ISmtcSessionSource"/>.
/// </summary>
/// <remarks>
/// Does not subscribe to Dubya <c>SessionsChanged</c>. Reattachment is
/// <see cref="ForceUpdate"/> from <c>SmtcMusicPlayer</c> while the selected id is absent.
/// </remarks>
internal sealed class WindowsMediaControllerSessionSource : ISmtcSessionSource, IDisposable
{
    private readonly MediaManager _mediaManager;
    private readonly ILogger<WindowsMediaControllerSessionSource> _logger;
    private readonly object _sync = new();
    private bool _startAttempted;
    private Exception? _startError;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsMediaControllerSessionSource"/> class.
    /// </summary>
    /// <param name="logger">The logger used for WinRT and start-up diagnostics.</param>
    public WindowsMediaControllerSessionSource(ILogger<WindowsMediaControllerSessionSource> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _mediaManager = new MediaManager { Logger = logger };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmtcSessionSnapshot>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();

        var focusedId = TryReadFocusedId();
        var sessions = _mediaManager.CurrentMediaSessions.Values.ToArray();
        var snapshots = new List<SmtcSessionSnapshot>(sessions.Length);

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = session.Id;
            snapshots.Add(await ReadSnapshotAsync(session, id, focusedId, cancellationToken).ConfigureAwait(false));
        }

        return snapshots;
    }

    /// <inheritdoc />
    public Task<bool?> TryPlayAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => TryControlAsync(sourceAppUserModelId, "play", static session => session.TryPlayAsync().AsTask(), cancellationToken);

    /// <inheritdoc />
    public Task<bool?> TryPauseAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => TryControlAsync(sourceAppUserModelId, "pause", static session => session.TryPauseAsync().AsTask(), cancellationToken);

    /// <inheritdoc />
    public Task<bool?> TrySkipNextAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => TryControlAsync(sourceAppUserModelId, "next", static session => session.TrySkipNextAsync().AsTask(), cancellationToken);

    /// <inheritdoc />
    public Task<bool?> TrySkipPreviousAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => TryControlAsync(
            sourceAppUserModelId,
            "previous",
            static session => session.TrySkipPreviousAsync().AsTask(),
            cancellationToken);

    /// <inheritdoc />
    public void ForceUpdate()
    {
        EnsureStarted();
        _mediaManager.ForceUpdate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mediaManager.Dispose();
    }

    private async Task<bool?> TryControlAsync(
        string sourceAppUserModelId,
        string action,
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppUserModelId);
        EnsureStarted();

        if (!TryGetSessionByExactId(sourceAppUserModelId, out var session))
        {
            return null;
        }

        var controlSession = session.ControlSession;
        if (controlSession is null)
        {
            return null;
        }

        var accepted = await command(controlSession).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!accepted)
        {
            _logger.LogWarning(
                "SMTC {Action} returned false for SourceAppUserModelId {SourceAppUserModelId}.",
                action,
                sourceAppUserModelId);
        }

        return accepted;
    }

    private bool TryGetSessionByExactId(string sourceAppUserModelId, out MediaManager.MediaSession session)
    {
        // Dictionary lookup is not a command target selector by itself: confirm ordinal equality
        // on the copied id. GetFocusedSession / GetCurrentSession are never used here.
        if (_mediaManager.CurrentMediaSessions.TryGetValue(sourceAppUserModelId, out session!)
            && string.Equals(session.Id, sourceAppUserModelId, StringComparison.Ordinal))
        {
            return true;
        }

        session = null!;
        return false;
    }

    private string? TryReadFocusedId()
    {
        try
        {
            return _mediaManager.GetFocusedSession()?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC current-session hint could not be read.");
            return null;
        }
    }

    private async Task<SmtcSessionSnapshot> ReadSnapshotAsync(
        MediaManager.MediaSession session,
        string id,
        string? focusedId,
        CancellationToken cancellationToken)
    {
        PlaybackStatus status = PlaybackStatus.Unknown;
        var isPlayEnabled = false;
        var isPauseEnabled = false;
        var isNextEnabled = false;
        var isPreviousEnabled = false;

        try
        {
            var controlSession = session.ControlSession;
            if (controlSession is not null)
            {
                var playbackInfo = controlSession.GetPlaybackInfo();
                status = MapStatus(playbackInfo.PlaybackStatus);
                var controls = playbackInfo.Controls;
                isPlayEnabled = controls.IsPlayEnabled;
                isPauseEnabled = controls.IsPauseEnabled;
                isNextEnabled = controls.IsNextEnabled;
                isPreviousEnabled = controls.IsPreviousEnabled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC playback info could not be read for {SourceAppUserModelId}.", id);
        }

        var isCurrent = focusedId is not null
            && string.Equals(id, focusedId, StringComparison.Ordinal);

        return new SmtcSessionSnapshot(
            SourceAppUserModelId: id,
            PlaybackStatus: status,
            Track: await TryReadTrackAsync(session, id, cancellationToken).ConfigureAwait(false),
            IsPlayEnabled: isPlayEnabled,
            IsPauseEnabled: isPauseEnabled,
            IsNextEnabled: isNextEnabled,
            IsPreviousEnabled: isPreviousEnabled,
            IsCurrentSession: isCurrent);
    }

    private async Task<MusicTrack?> TryReadTrackAsync(
        MediaManager.MediaSession session,
        string id,
        CancellationToken cancellationToken)
    {
        try
        {
            var controlSession = session.ControlSession;
            if (controlSession is null)
            {
                return null;
            }

            var properties = await controlSession.TryGetMediaPropertiesAsync()
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var title = EmptyToNull(properties.Title);
            var artist = EmptyToNull(properties.Artist);
            var album = EmptyToNull(properties.AlbumTitle);
            if (title is null && artist is null && album is null)
            {
                return null;
            }

            return new MusicTrack(Id: null, Title: title, Artist: artist, Album: album);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC track properties could not be read for {SourceAppUserModelId}.", id);
            return null;
        }
    }

    private void EnsureStarted()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_startAttempted)
            {
                if (_startError is not null)
                {
                    throw new InvalidOperationException("SMTC session manager failed to start.", _startError);
                }

                return;
            }

            _startAttempted = true;
            try
            {
                _mediaManager.Start();
            }
            catch (Exception ex)
            {
                _startError = ex;
                _logger.LogWarning(ex, "SMTC session manager failed to start.");
                throw new InvalidOperationException("SMTC session manager failed to start.", ex);
            }
        }
    }

    private static PlaybackStatus MapStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
        => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackStatus.Stopped,
            _ => PlaybackStatus.Unknown
        };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
#endif
