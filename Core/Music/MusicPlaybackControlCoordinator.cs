using Core.Configuration;
using Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Music;

/// <summary>
/// Applies idempotent pause/resume/skip and duck/restore session state through <see cref="IMusicPlayer"/>.
/// </summary>
public class MusicPlaybackControlCoordinator : IMusicPlaybackControl
{
    private readonly IMusicPlayer _player;
    private readonly VolumeDuckOptions _duckOptions;
    // Retained for DI/constructor compatibility; UND-77 moved pause/resume recording to
    // PlaybackStateObserver, so the coordinator no longer reads this argument.
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private int? _savedVolume;
    private bool _isDuckActive;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicPlaybackControlCoordinator"/> class.
    /// </summary>
    /// <param name="player">The player backend used for transport and volume.</param>
    /// <param name="duckOptions">The duck/restore volume defaults.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public MusicPlaybackControlCoordinator(
        IMusicPlayer player,
        IOptions<VolumeDuckOptions>? duckOptions,
        ILogger<MusicPlaybackControlCoordinator> logger)
        : this(player, duckOptions, recorder: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicPlaybackControlCoordinator"/> class.
    /// </summary>
    /// <param name="player">The player backend used for transport and volume.</param>
    /// <param name="duckOptions">The duck/restore volume defaults.</param>
    /// <param name="recorder">An unused recorder retained for constructor compatibility.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public MusicPlaybackControlCoordinator(
        IMusicPlayer player,
        IOptions<VolumeDuckOptions>? duckOptions,
        IPlaybackEventRecorder? recorder,
        ILogger logger)
    {
        _player = player;
        _duckOptions = duckOptions?.Value ?? new VolumeDuckOptions();
        _ = recorder ?? NullPlaybackEventRecorder.Instance;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task TryPauseAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "pause",
            async () =>
            {
                var state = await GetAvailableStateAsync(eventKeyForLog, "pause", cancellationToken)
                    .ConfigureAwait(false);
                if (state is null)
                {
                    return;
                }

                if (state.Status != PlaybackStatus.Playing)
                {
                    _logger.LogDebug(
                        "Event {EventKey} matched pause, but the player is already {Status}.",
                        eventKeyForLog ?? "(scenario)",
                        state.Status);
                    return;
                }

                await _player.PauseAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Playback pause for {EventKey}", eventKeyForLog ?? "(scenario)");
            });
    }

    /// <inheritdoc />
    public Task TryResumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "resume",
            async () =>
            {
                var state = await GetAvailableStateAsync(eventKeyForLog, "resume", cancellationToken)
                    .ConfigureAwait(false);
                if (state is null)
                {
                    return;
                }

                if (state.Status == PlaybackStatus.Playing)
                {
                    _logger.LogDebug(
                        "Event {EventKey} matched resume, but the player is already playing.",
                        eventKeyForLog ?? "(scenario)");
                    return;
                }

                // PlayAsync, not ResumeAsync: the adapter resume path re-reads status.
                await _player.PlayAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Playback resume for {EventKey}", eventKeyForLog ?? "(scenario)");
            });
    }

    /// <inheritdoc />
    public Task TryNextAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "next",
            async () =>
            {
                if (!await EnsureAvailableAsync(eventKeyForLog, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await _player.NextAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Playback next for {EventKey}", eventKeyForLog ?? "(scenario)");
            });
    }

    /// <inheritdoc />
    public Task TryPreviousAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "previous",
            async () =>
            {
                if (!await EnsureAvailableAsync(eventKeyForLog, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                await _player.PreviousAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Playback previous for {EventKey}", eventKeyForLog ?? "(scenario)");
            });
    }

    /// <inheritdoc />
    public Task TryDuckAsync(
        EventControlRule rule,
        NormalizedEvent context,
        CancellationToken cancellationToken = default)
    {
        var target = rule.VolumePercent ?? _duckOptions.MuteVolume;
        return DuckInternalAsync(target, context.EventKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task TryDuckAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default)
    {
        return DuckInternalAsync(volumePercent, eventKeyForLog, cancellationToken);
    }

    /// <inheritdoc />
    public Task TryRestoreVolumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "restore_volume",
            async () =>
            {
                if (!await EnsureAvailableAsync(eventKeyForLog, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                int restoreVolume;

                lock (_sync)
                {
                    if (!_isDuckActive)
                    {
                        _logger.LogDebug(
                            "Event {EventKey} matched restore_volume, but no managed duck state is active.",
                            eventKeyForLog ?? "(scenario)");
                        return;
                    }

                    restoreVolume = _savedVolume ?? _duckOptions.FallbackRestoreVolume;
                    _savedVolume = null;
                    _isDuckActive = false;
                }

                await _player.SetVolumeAsync(restoreVolume, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Playback restore for {EventKey} -> volume={RestoreVolume}",
                    eventKeyForLog ?? "(scenario)",
                    restoreVolume);
            });
    }

    /// <inheritdoc />
    public Task TrySetManagedVolumeAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default)
    {
        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volumePercent), "Volume must be between 0 and 100.");
        }

        return ExecuteSafelyAsync(
            eventKeyForLog,
            "managed volume",
            async () =>
            {
                var state = await GetAvailableStateAsync(eventKeyForLog, "managed volume", cancellationToken)
                    .ConfigureAwait(false);
                if (state is null)
                {
                    return;
                }

                var restoreVolume = state.VolumePercent ?? _duckOptions.FallbackRestoreVolume;

                lock (_sync)
                {
                    if (!_isDuckActive)
                    {
                        _savedVolume = restoreVolume;
                    }

                    _isDuckActive = true;
                }

                await _player.SetVolumeAsync(volumePercent, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "Managed volume for {EventKey} -> {Volume}% (saved restore={Saved})",
                    eventKeyForLog ?? "(scenario)",
                    volumePercent,
                    restoreVolume);
            });
    }

    private Task DuckInternalAsync(
        int targetVolume,
        string? eventKeyForLog,
        CancellationToken cancellationToken)
    {
        return ExecuteSafelyAsync(
            eventKeyForLog,
            "duck",
            async () =>
            {
                var state = await GetAvailableStateAsync(eventKeyForLog, "duck", cancellationToken)
                    .ConfigureAwait(false);
                if (state is null)
                {
                    return;
                }

                var restoreVolume = state.VolumePercent ?? _duckOptions.FallbackRestoreVolume;

                lock (_sync)
                {
                    if (!_isDuckActive)
                    {
                        _savedVolume = restoreVolume;
                    }

                    _isDuckActive = true;
                }

                await _player.SetVolumeAsync(targetVolume, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Playback duck for {EventKey} -> volume={TargetVolume} (saved={SavedVolume})",
                    eventKeyForLog ?? "(scenario)",
                    targetVolume,
                    restoreVolume);
            });
    }

    private async Task<bool> EnsureAvailableAsync(string? eventKeyForLog, CancellationToken cancellationToken)
    {
        if (await _player.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        _logger.LogWarning(
            "Music player is not available for {EventKey}.",
            eventKeyForLog ?? "(scenario)");
        return false;
    }

    private async Task<MusicPlaybackState?> GetAvailableStateAsync(
        string? eventKeyForLog,
        string operation,
        CancellationToken cancellationToken)
    {
        var state = await _player.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is not null)
        {
            return state;
        }

        _logger.LogWarning(
            "Music player is not available for {EventKey} ({Operation}).",
            eventKeyForLog ?? "(scenario)",
            operation);
        return null;
    }

    private async Task ExecuteSafelyAsync(string? eventKeyForLog, string operation, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Playback {Operation} failed for {EventKey}.",
                operation,
                eventKeyForLog ?? "(scenario)");
        }
    }
}
