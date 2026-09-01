#if WINDOWS
using Core.Music;
using GsiHost.Players.Smtc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GsiHost.Players;

/// <summary>
/// Windows SMTC adapter that commands one user-selected session by exact <c>SourceAppUserModelId</c>.
/// </summary>
/// <remarks>
/// Selection is always the configured id. Missing selection, a missing session, or a non-exact
/// id match returns <see cref="MusicCommandOutcome.Unavailable"/> and issues no command.
/// <c>GetCurrentSession()</c> is never used as a command target.
/// When the selected id is absent, <see cref="ISmtcSessionSource.ForceUpdate"/> is used as a
/// low-frequency reattach fallback (Dubya <c>SessionsChanged</c> is not subscribed).
/// </remarks>
public sealed class SmtcMusicPlayer : IMusicPlayer, IDisposable
{
    /// <summary>
    /// Capabilities for the SMTC MVP adapter: transport and skip, no volume.
    /// </summary>
    public static MusicPlayerCapabilities SmtcCapabilities { get; } = new(
        CanPlay: true,
        CanPause: true,
        CanResume: true,
        CanSkip: true,
        CanSetVolume: false);

    /// <summary>
    /// Gets the default interval for <see cref="ISmtcSessionSource.ForceUpdate"/> while the
    /// selected id is absent.
    /// </summary>
    public static TimeSpan DefaultReattachPollInterval { get; } = TimeSpan.FromSeconds(2);

    private readonly ISmtcSessionSource _sessionSource;
    private readonly SmtcOptions _options;
    private readonly ILogger<SmtcMusicPlayer> _logger;
    private readonly TimeSpan _reattachPollInterval;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _reattachLoop;
    private int _selectedMissing = 1;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtcMusicPlayer"/> class.
    /// </summary>
    /// <param name="sessionSource">The SMTC session seam. Must not be a WinRT type.</param>
    /// <param name="options">The selected <c>SourceAppUserModelId</c> binding.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    [ActivatorUtilitiesConstructor]
    public SmtcMusicPlayer(
        ISmtcSessionSource sessionSource,
        IOptions<SmtcOptions> options,
        ILogger<SmtcMusicPlayer> logger)
        : this(sessionSource, options, logger, DefaultReattachPollInterval)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtcMusicPlayer"/> class.
    /// </summary>
    /// <param name="sessionSource">The SMTC session seam. Must not be a WinRT type.</param>
    /// <param name="options">The selected <c>SourceAppUserModelId</c> binding.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    /// <param name="reattachPollInterval">The interval used to call <see cref="RefreshIfSelectedMissingAsync"/> while the selected id is absent.</param>
    public SmtcMusicPlayer(
        ISmtcSessionSource sessionSource,
        IOptions<SmtcOptions> options,
        ILogger<SmtcMusicPlayer> logger,
        TimeSpan reattachPollInterval)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        if (reattachPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(reattachPollInterval), "Reattach poll interval must be positive.");
        }

        _sessionSource = sessionSource;
        _options = options.Value ?? new SmtcOptions();
        _logger = logger;
        _reattachPollInterval = reattachPollInterval;
        _reattachLoop = RunReattachLoopAsync(_disposeCts.Token);
    }

    /// <inheritdoc />
    public MusicPlayerCapabilities Capabilities => SmtcCapabilities;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        try
        {
            _ = await _sessionSource.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC session source is not available.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var selectedId = SelectedId;
        if (selectedId is null)
        {
            return null;
        }

        try
        {
            var matches = await FindExactMatchesAsync(selectedId, cancellationToken).ConfigureAwait(false);
            if (matches.Count != 1)
            {
                return null;
            }

            var session = matches[0];
            return new MusicPlaybackState(session.PlaybackStatus, session.Track, VolumePercent: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC state could not be read.");
            return null;
        }
    }

    /// <inheritdoc />
    public Task<MusicCommandResult> PlayAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(
            "play",
            alreadySatisfied: static session => session.PlaybackStatus == PlaybackStatus.Playing,
            isEnabled: static session => session.IsPlayEnabled,
            command: (source, id, ct) => source.TryPlayAsync(id, ct),
            cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> PauseAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(
            "pause",
            alreadySatisfied: static session =>
                session.PlaybackStatus is PlaybackStatus.Paused or PlaybackStatus.Stopped,
            isEnabled: static session => session.IsPauseEnabled,
            command: (source, id, ct) => source.TryPauseAsync(id, ct),
            cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> ResumeAsync(CancellationToken cancellationToken = default)
        => PlayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> NextAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(
            "next",
            alreadySatisfied: static _ => false,
            isEnabled: static session => session.IsNextEnabled,
            command: (source, id, ct) => source.TrySkipNextAsync(id, ct),
            cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> PreviousAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(
            "previous",
            alreadySatisfied: static _ => false,
            isEnabled: static session => session.IsPreviousEnabled,
            command: (source, id, ct) => source.TrySkipPreviousAsync(id, ct),
            cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MusicCommandResult.Unsupported("SMTC cannot set volume."));
    }

    /// <summary>
    /// Refreshes the session source when the configured id is currently absent.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the refresh attempt finishes.</returns>
    /// <remarks>
    /// Used by the adapter's reattach timer and as a test hook. Not on the GSI tick path.
    /// Does nothing when no id is selected or when the selected session is already present.
    /// </remarks>
    public async Task RefreshIfSelectedMissingAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var selectedId = SelectedId;
        if (selectedId is null)
        {
            Interlocked.Exchange(ref _selectedMissing, 0);
            return;
        }

        if (Volatile.Read(ref _selectedMissing) == 0)
        {
            return;
        }

        try
        {
            var matches = await FindExactMatchesAsync(selectedId, cancellationToken).ConfigureAwait(false);
            if (matches.Count > 0)
            {
                return;
            }

            _logger.LogDebug(
                "SMTC selected SourceAppUserModelId {SourceAppUserModelId} is absent; forcing session refresh.",
                selectedId);
            _sessionSource.ForceUpdate();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC session refresh failed.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private async Task RunReattachLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_reattachPollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshIfSelectedMissingAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC reattach loop failed.");
        }
    }

    private async Task<MusicCommandResult> SendTransportAsync(
        string action,
        Func<SmtcSessionSnapshot, bool> alreadySatisfied,
        Func<SmtcSessionSnapshot, bool> isEnabled,
        Func<ISmtcSessionSource, string, CancellationToken, Task<bool?>> command,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var selectedId = SelectedId;
        if (selectedId is null)
        {
            return MusicCommandResult.Unavailable(
                "No SMTC session is selected. Set Music:Smtc:SourceAppUserModelId to an exact SourceAppUserModelId.");
        }

        try
        {
            var matches = await FindExactMatchesAsync(selectedId, cancellationToken).ConfigureAwait(false);
            if (matches.Count == 0)
            {
                _sessionSource.ForceUpdate();
                matches = await FindExactMatchesAsync(selectedId, cancellationToken).ConfigureAwait(false);
            }

            if (matches.Count == 0)
            {
                _logger.LogWarning(
                    "SMTC {Action} found no session for SourceAppUserModelId {SourceAppUserModelId}.",
                    action,
                    selectedId);
                return MusicCommandResult.Unavailable(
                    $"No SMTC session matches SourceAppUserModelId '{selectedId}'.");
            }

            if (matches.Count > 1)
            {
                _logger.LogWarning(
                    "SMTC {Action} found {Count} sessions for SourceAppUserModelId {SourceAppUserModelId}.",
                    action,
                    matches.Count,
                    selectedId);
                return MusicCommandResult.Rejected(
                    $"Multiple SMTC sessions report SourceAppUserModelId '{selectedId}'.");
            }

            var session = matches[0];
            if (alreadySatisfied(session))
            {
                return MusicCommandResult.Applied;
            }

            if (!isEnabled(session))
            {
                return MusicCommandResult.Unsupported(
                    $"SMTC {action} is not enabled for SourceAppUserModelId '{session.SourceAppUserModelId}'.");
            }

            var accepted = await command(_sessionSource, session.SourceAppUserModelId, cancellationToken)
                .ConfigureAwait(false);
            if (accepted is null)
            {
                Interlocked.Exchange(ref _selectedMissing, 1);
                return MusicCommandResult.Unavailable(
                    $"No SMTC session matches SourceAppUserModelId '{session.SourceAppUserModelId}'.");
            }

            if (!accepted.Value)
            {
                return MusicCommandResult.Rejected(
                    $"SMTC {action} returned false for SourceAppUserModelId '{session.SourceAppUserModelId}'.");
            }

            return MusicCommandResult.Applied;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC {Action} failed.", action);
            return MusicCommandResult.Failed($"SMTC {action} failed: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<SmtcSessionSnapshot>> FindExactMatchesAsync(
        string selectedId,
        CancellationToken cancellationToken)
    {
        var sessions = await _sessionSource.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        var matches = new List<SmtcSessionSnapshot>();
        foreach (var session in sessions)
        {
            if (!string.Equals(session.SourceAppUserModelId, selectedId, StringComparison.Ordinal))
            {
                continue;
            }

            matches.Add(session);
        }

        Interlocked.Exchange(ref _selectedMissing, matches.Count == 0 ? 1 : 0);
        return matches;
    }

    private string? SelectedId
        => string.IsNullOrWhiteSpace(_options.SourceAppUserModelId)
            ? null
            : _options.SourceAppUserModelId;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
#endif
