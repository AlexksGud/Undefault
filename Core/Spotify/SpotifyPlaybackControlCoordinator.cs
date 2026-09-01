using Core.Music;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Spotify;

/// <summary>
/// Compatibility constructor wrapper for leftover Spotify DI and tests. Prefer
/// <see cref="MusicPlaybackControlCoordinator"/>.
/// </summary>
public sealed class SpotifyPlaybackControlCoordinator : MusicPlaybackControlCoordinator, ISpotifyPlaybackControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyPlaybackControlCoordinator"/> class.
    /// </summary>
    /// <param name="player">The player backend used for transport and volume.</param>
    /// <param name="duckOptions">The duck/restore volume defaults.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public SpotifyPlaybackControlCoordinator(
        IMusicPlayer player,
        IOptions<VolumeDuckOptions>? duckOptions,
        ILogger<SpotifyPlaybackControlCoordinator> logger)
        : base(player, duckOptions, recorder: null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyPlaybackControlCoordinator"/> class.
    /// </summary>
    /// <param name="player">The player backend used for transport and volume.</param>
    /// <param name="duckOptions">The duck/restore volume defaults.</param>
    /// <param name="recorder">An unused recorder retained for constructor compatibility.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public SpotifyPlaybackControlCoordinator(
        IMusicPlayer player,
        IOptions<VolumeDuckOptions>? duckOptions,
        IPlaybackEventRecorder? recorder,
        ILogger<SpotifyPlaybackControlCoordinator> logger)
        : base(player, duckOptions, recorder, logger)
    {
    }
}
