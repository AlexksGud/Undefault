using Core.Models;
using Core.Music;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

/// <summary>
/// Unit tests for <see cref="MusicPlaybackControlCoordinator"/> pause/resume/skip control behavior.
/// UND-77 moved pause/resume transition recording to <c>PlaybackStateObserver</c>; the coordinator no
/// longer records, so these tests verify playback control (pause/resume applied once on a state change)
/// and that unavailable/missing-state cases fail softly.
/// </summary>
public class MusicPlaybackControlCoordinatorTests
{
    [Fact]
    public async Task Pause_WhenPlaying_PausesOnce()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing()
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryPauseAsync("custom:music_pause");

        player.PauseCalls.Should().Be(1);
        player.GetStateCalls.Should().Be(1);
        player.IsAvailableCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_WhenPaused_ResumesOnce()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Paused()
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryResumeAsync("custom:music_resume");

        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(1);
        player.IsAvailableCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_WhenStopped_ResumesOnce()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = new MusicPlaybackState(PlaybackStatus.Stopped, Track: null, VolumePercent: 70)
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryResumeAsync(EventKeys.RoundStart);

        player.PlayCalls.Should().Be(1);
        player.ResumeCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(1);
        player.IsAvailableCalls.Should().Be(0);
    }

    [Fact]
    public async Task Pause_WhenAlreadyPaused_DoesNotPause()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Paused()
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryPauseAsync("custom:music_pause");

        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_WhenAlreadyPlaying_DoesNotResume()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing()
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryResumeAsync("custom:music_resume");

        player.PlayCalls.Should().Be(0);
        player.ResumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Pause_WhenPlayerUnavailable_DoesNotPause()
    {
        var player = new FakeMusicPlayer
        {
            Available = false,
            State = Playing()
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryPauseAsync("custom:music_pause");

        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task Pause_WhenNoPlaybackState_DoesNotPause()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = null
        };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryPauseAsync("custom:music_pause");

        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task NextAndPrevious_WhenAvailable_RouteToPlayer()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var coordinator = BuildCoordinator(player);

        await coordinator.TryNextAsync("custom:next");
        await coordinator.TryPreviousAsync("custom:previous");

        player.NextCalls.Should().Be(1);
        player.PreviousCalls.Should().Be(1);
    }

    [Fact]
    public async Task Pause_WhenPlayerThrows_DoesNotThrowToCaller()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            ThrowOnPause = true
        };
        var coordinator = BuildCoordinator(player);

        var act = async () => await coordinator.TryPauseAsync(EventKeys.Death);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NullRecorder_DefaultsToNoOp_AndDoesNotThrow()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing()
        };

        var coordinator = new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions()),
            NullLogger<MusicPlaybackControlCoordinator>.Instance);

        var act = async () => await coordinator.TryPauseAsync("custom:music_pause");
        await act.Should().NotThrowAsync();
    }

    private static MusicPlaybackControlCoordinator BuildCoordinator(IMusicPlayer player)
    {
        return new MusicPlaybackControlCoordinator(
            player,
            Options.Create(new VolumeDuckOptions
            {
                MuteVolume = 0,
                FallbackRestoreVolume = 50
            }),
            recorder: null,
            NullLogger<MusicPlaybackControlCoordinator>.Instance);
    }

    private static MusicPlaybackState Playing() => new(
        Status: PlaybackStatus.Playing,
        Track: null,
        VolumePercent: 70);

    private static MusicPlaybackState Paused() => new(
        Status: PlaybackStatus.Paused,
        Track: null,
        VolumePercent: 70);
}

internal sealed class FakeMusicPlayer : IMusicPlayer
{
    public bool Available { get; set; } = true;
    public MusicPlaybackState? State { get; set; }
    public bool ThrowOnPause { get; set; }
    public int PlayCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public int ResumeCalls { get; private set; }
    public int NextCalls { get; private set; }
    public int PreviousCalls { get; private set; }
    public int GetStateCalls { get; private set; }
    public int IsAvailableCalls { get; private set; }
    public List<int> VolumeCalls { get; } = new();

    public MusicPlayerCapabilities Capabilities => MusicPlayerCapabilities.Mvp;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        IsAvailableCalls++;
        return Task.FromResult(Available);
    }

    public Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        GetStateCalls++;
        return Task.FromResult(Available ? State : null);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        PlayCalls++;
        State = State is null ? null : State with { Status = PlaybackStatus.Playing };
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        PauseCalls++;
        if (ThrowOnPause)
        {
            throw new InvalidOperationException("Player unavailable.");
        }

        State = State is null ? null : State with { Status = PlaybackStatus.Paused };
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        ResumeCalls++;
        State = State is null ? null : State with { Status = PlaybackStatus.Playing };
        return Task.CompletedTask;
    }

    public Task NextAsync(CancellationToken cancellationToken = default)
    {
        NextCalls++;
        return Task.CompletedTask;
    }

    public Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        PreviousCalls++;
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        VolumeCalls.Add(volumePercent);
        State = State is null ? null : State with { VolumePercent = volumePercent };
        return Task.CompletedTask;
    }
}
