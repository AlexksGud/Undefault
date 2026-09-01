using Core.Spotify.Models;
using Microsoft.Extensions.Logging;

namespace Core.Spotify;

public sealed class MockSpotifyClient : ISpotifyClient
{
    private readonly ILogger<MockSpotifyClient> _logger;
    private bool _isPlaying;
    private int _volume = 50;
    private string? _currentUri;
    private int? _currentPositionMs;

    public MockSpotifyClient(ILogger<MockSpotifyClient> logger)
    {
        _logger = logger;
    }

    public Task<PlaybackState?> GetCurrentPlaybackAsync(CancellationToken cancellationToken = default)
    {
        Track? track = null;
        if (!string.IsNullOrWhiteSpace(_currentUri))
        {
            track = new Track(
                Id: "mock-track",
                Name: "Mock Track",
                Uri: _currentUri,
                DurationMs: 180_000,
                Artists: new List<Artist> { new("mock-artist", "Mock Artist") },
                Album: null
            );
        }

        return Task.FromResult<PlaybackState?>(new PlaybackState(
            IsPlaying: _isPlaying,
            VolumePercent: _volume,
            Track: track,
            DeviceId: "mock-device",
            DeviceName: "Mock Device"
        ));
    }

    public Task PlayAsync(string? uri = null, int? positionMs = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(uri))
        {
            _currentUri = uri;
        }

        _currentPositionMs = positionMs;
        _isPlaying = true;
        _logger.LogInformation(
            "[MOCK] Would play: {Uri} at {PositionMs}ms",
            _currentUri ?? "(current)",
            _currentPositionMs ?? 0);
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        _isPlaying = false;
        _logger.LogInformation("[MOCK] Would pause playback");
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        _isPlaying = true;
        _logger.LogInformation("[MOCK] Would resume playback");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        if (volume < 0 || volume > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 100");
        }

        _volume = volume;
        _logger.LogInformation("[MOCK] Would set volume to {Volume}%", _volume);
        return Task.CompletedTask;
    }

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
