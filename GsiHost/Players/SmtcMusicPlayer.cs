#if WINDOWS
using Core.Music;
using GsiHost.Players.Smtc;
using Microsoft.Extensions.Options;

namespace GsiHost.Players;

/// <summary>
/// Windows SMTC adapter that commands one user-selected session by exact <c>SourceAppUserModelId</c>.
/// </summary>
/// <remarks>
/// Selection is always the configured id. Missing selection, a missing session, or a non-exact
/// id match returns <see cref="MusicCommandOutcome.Unavailable"/> and issues no command.
/// <c>GetCurrentSession()</c> is never used as a command target.
/// </remarks>
public sealed class SmtcMusicPlayer : IMusicPlayer
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

    private readonly ISmtcSessionSource _sessionSource;
    private readonly SmtcOptions _options;
    private readonly ILogger<SmtcMusicPlayer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtcMusicPlayer"/> class.
    /// </summary>
    /// <param name="sessionSource">The SMTC session seam. Must not be a WinRT type.</param>
    /// <param name="options">The selected <c>SourceAppUserModelId</c> binding.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public SmtcMusicPlayer(
        ISmtcSessionSource sessionSource,
        IOptions<SmtcOptions> options,
        ILogger<SmtcMusicPlayer> logger)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _sessionSource = sessionSource;
        _options = options.Value ?? new SmtcOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public MusicPlayerCapabilities Capabilities => SmtcCapabilities;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
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
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MusicCommandResult.Unsupported("SMTC cannot set volume."));
    }

    private async Task<MusicCommandResult> SendTransportAsync(
        string action,
        Func<SmtcSessionSnapshot, bool> alreadySatisfied,
        Func<SmtcSessionSnapshot, bool> isEnabled,
        Func<ISmtcSessionSource, string, CancellationToken, Task<bool?>> command,
        CancellationToken cancellationToken)
    {
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

        return matches;
    }

    private string? SelectedId
        => string.IsNullOrWhiteSpace(_options.SourceAppUserModelId)
            ? null
            : _options.SourceAppUserModelId;
}
#endif
