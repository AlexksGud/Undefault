using Core.Models;
using Core.Music;
using Core.Spotify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Actions.Spotify;

public sealed class SpotifyVolumeDuckAction : IEventAction
{
    private readonly ISpotifyClient _spotifyClient;
    private readonly VolumeDuckOptions _options;
    private readonly ILogger<SpotifyVolumeDuckAction> _logger;
    private readonly object _sync = new();
    private int? _savedVolume;
    private bool _isMutedByRoundStart;

    public SpotifyVolumeDuckAction(
        ISpotifyClient spotifyClient,
        IOptions<VolumeDuckOptions>? options,
        ILogger<SpotifyVolumeDuckAction> logger)
    {
        _spotifyClient = spotifyClient;
        _options = options?.Value ?? new VolumeDuckOptions();
        _logger = logger;
    }

    public string Key => "spotify.volume_duck";

    public async Task ExecuteAsync(NormalizedEvent normalizedEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _spotifyClient.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning("Spotify is not connected.");
                return;
            }

            switch (normalizedEvent.EventKey)
            {
                case EventKeys.RoundStart:
                    await MuteForRoundStartAsync(normalizedEvent, cancellationToken).ConfigureAwait(false);
                    break;

                case EventKeys.Death:
                    await RestoreAfterDeathAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Volume action failed for {EventKey}", normalizedEvent.EventKey);
        }
    }

    private async Task MuteForRoundStartAsync(NormalizedEvent normalizedEvent, CancellationToken cancellationToken)
    {
        var playback = await _spotifyClient.GetCurrentPlaybackAsync(cancellationToken).ConfigureAwait(false);
        if (playback is null)
        {
            _logger.LogWarning("Round started, but Spotify has no active playback device.");
            return;
        }

        var restoreVolume = playback.VolumePercent ?? _options.FallbackRestoreVolume;
        lock (_sync)
        {
            _savedVolume = restoreVolume;
            _isMutedByRoundStart = true;
        }

        await _spotifyClient.SetVolumeAsync(_options.MuteVolume, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "round_start -> volume={MuteVolume} (saved={SavedVolume}, detail={Detail})",
            _options.MuteVolume,
            restoreVolume,
            normalizedEvent.Detail ?? "n/a");
    }

    private async Task RestoreAfterDeathAsync(CancellationToken cancellationToken)
    {
        int restoreVolume;

        lock (_sync)
        {
            if (!_isMutedByRoundStart)
            {
                return;
            }

            restoreVolume = _savedVolume ?? _options.FallbackRestoreVolume;
            _isMutedByRoundStart = false;
            _savedVolume = null;
        }

        await _spotifyClient.SetVolumeAsync(restoreVolume, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("death -> volume={RestoreVolume}", restoreVolume);
    }
}
