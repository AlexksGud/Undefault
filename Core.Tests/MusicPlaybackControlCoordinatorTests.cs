using Core.Models;
using Core.Music;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Core.Tests;

/// <summary>
/// Unit tests for <see cref="MusicPlaybackControlCoordinator"/> pause/resume/skip and duck control behavior.
/// UND-77 moved pause/resume transition recording to <c>PlaybackStateObserver</c>; the coordinator no
/// longer records, so these tests verify playback control (pause/resume applied once on a state change),
/// that unavailable/missing-state cases fail softly, and that static capability ceilings block commands
/// before duck-state mutation.
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

        var result = await coordinator.TryPauseAsync("custom:music_pause");

        result.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryResumeAsync("custom:music_resume");

        result.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryResumeAsync(EventKeys.RoundStart);

        result.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryPauseAsync("custom:music_pause");

        result.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryResumeAsync("custom:music_resume");

        result.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryPauseAsync("custom:music_pause");

        AssertNonApplied(result, MusicCommandOutcome.Unavailable);
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

        var result = await coordinator.TryPauseAsync("custom:music_pause");

        AssertNonApplied(result, MusicCommandOutcome.Unavailable);
        player.PauseCalls.Should().Be(0);
    }

    [Fact]
    public async Task NextAndPrevious_WhenAvailable_RouteToPlayer()
    {
        var player = new FakeMusicPlayer { Available = true, State = Playing() };
        var coordinator = BuildCoordinator(player);

        var next = await coordinator.TryNextAsync("custom:next");
        var previous = await coordinator.TryPreviousAsync("custom:previous");

        next.Should().Be(MusicCommandResult.Applied);
        previous.Should().Be(MusicCommandResult.Applied);
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

        var result = await coordinator.TryPauseAsync(EventKeys.Death);

        result.Outcome.Should().Be(MusicCommandOutcome.Failed);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DuckThenRestore_WhenVolumeSupported_LowersAndRestoresVolume()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing()
        };
        var coordinator = BuildCoordinator(player);

        var duck = await coordinator.TryDuckAsync(10, "custom:duck");
        var restore = await coordinator.TryRestoreVolumeAsync("custom:restore");

        duck.Should().Be(MusicCommandResult.Applied);
        restore.Should().Be(MusicCommandResult.Applied);
        player.VolumeCalls.Should().Equal(10, 70);
    }

    [Fact]
    public async Task Duck_WhenVolumeUnsupported_ReturnsUnsupportedAndRestoreDoesNotWriteVolume()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanSetVolume = false }
        };
        var coordinator = BuildCoordinator(player);

        var duck = await coordinator.TryDuckAsync(10, "custom:duck");
        var restore = await coordinator.TryRestoreVolumeAsync("custom:restore");

        AssertNonApplied(duck, MusicCommandOutcome.Unsupported);
        AssertNonApplied(restore, MusicCommandOutcome.Unsupported);
        duck.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanSetVolume));
        restore.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanSetVolume));
        player.VolumeCalls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(0);
        player.IsAvailableCalls.Should().Be(0);
    }

    [Fact]
    public async Task Duck_WhenVolumeUnsupported_DoesNotArmDuckState()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanSetVolume = false }
        };
        var coordinator = BuildCoordinator(player);

        var duck = await coordinator.TryDuckAsync(10, "custom:duck");
        AssertNonApplied(duck, MusicCommandOutcome.Unsupported);
        player.VolumeCalls.Should().BeEmpty();

        player.Capabilities = MusicPlayerCapabilities.Mvp;
        var restore = await coordinator.TryRestoreVolumeAsync("custom:restore");

        restore.Should().Be(MusicCommandResult.Applied);
        player.VolumeCalls.Should().BeEmpty();
        player.GetStateCalls.Should().Be(0);
    }

    [Fact]
    public async Task SetManagedVolume_WhenVolumeUnsupported_DoesNotArmDuckState()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanSetVolume = false }
        };
        var coordinator = BuildCoordinator(player);

        var managed = await coordinator.TrySetManagedVolumeAsync(5, "custom:volume");
        AssertNonApplied(managed, MusicCommandOutcome.Unsupported);
        player.VolumeCalls.Should().BeEmpty();

        player.Capabilities = MusicPlayerCapabilities.Mvp;
        var restore = await coordinator.TryRestoreVolumeAsync("custom:restore");

        restore.Should().Be(MusicCommandResult.Applied);
        player.VolumeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Pause_WhenPauseUnsupported_DoesNotPause()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanPause = false }
        };
        var coordinator = BuildCoordinator(player);

        var result = await coordinator.TryPauseAsync("custom:music_pause");

        AssertNonApplied(result, MusicCommandOutcome.Unsupported);
        result.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanPause));
        player.PauseCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_WhenResumeUnsupported_DoesNotResume()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Paused(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanResume = false }
        };
        var coordinator = BuildCoordinator(player);

        var result = await coordinator.TryResumeAsync(EventKeys.RoundStart);

        AssertNonApplied(result, MusicCommandOutcome.Unsupported);
        result.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanResume));
        player.PlayCalls.Should().Be(0);
        player.ResumeCalls.Should().Be(0);
        player.GetStateCalls.Should().Be(0);
    }

    [Fact]
    public async Task NextAndPrevious_WhenSkipUnsupported_DoNotSkip()
    {
        var player = new FakeMusicPlayer
        {
            Available = true,
            State = Playing(),
            Capabilities = MusicPlayerCapabilities.Mvp with { CanSkip = false }
        };
        var coordinator = BuildCoordinator(player);

        var next = await coordinator.TryNextAsync("custom:next");
        var previous = await coordinator.TryPreviousAsync("custom:previous");

        AssertNonApplied(next, MusicCommandOutcome.Unsupported);
        AssertNonApplied(previous, MusicCommandOutcome.Unsupported);
        next.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanSkip));
        previous.Reason.Should().Contain(nameof(MusicPlayerCapabilities.CanSkip));
        player.NextCalls.Should().Be(0);
        player.PreviousCalls.Should().Be(0);
        player.IsAvailableCalls.Should().Be(0);
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

    private static void AssertNonApplied(MusicCommandResult result, MusicCommandOutcome outcome)
    {
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().NotBeNullOrWhiteSpace();
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

    public MusicPlayerCapabilities Capabilities { get; set; } = MusicPlayerCapabilities.Mvp;

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

    public Task<MusicCommandResult> PlayAsync(CancellationToken cancellationToken = default)
    {
        PlayCalls++;
        State = State is null ? null : State with { Status = PlaybackStatus.Playing };
        return Task.FromResult(MusicCommandResult.Applied);
    }

    public Task<MusicCommandResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        PauseCalls++;
        if (ThrowOnPause)
        {
            throw new InvalidOperationException("Player unavailable.");
        }

        State = State is null ? null : State with { Status = PlaybackStatus.Paused };
        return Task.FromResult(MusicCommandResult.Applied);
    }

    public Task<MusicCommandResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        ResumeCalls++;
        State = State is null ? null : State with { Status = PlaybackStatus.Playing };
        return Task.FromResult(MusicCommandResult.Applied);
    }

    public Task<MusicCommandResult> NextAsync(CancellationToken cancellationToken = default)
    {
        NextCalls++;
        return Task.FromResult(MusicCommandResult.Applied);
    }

    public Task<MusicCommandResult> PreviousAsync(CancellationToken cancellationToken = default)
    {
        PreviousCalls++;
        return Task.FromResult(MusicCommandResult.Applied);
    }

    public Task<MusicCommandResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        VolumeCalls.Add(volumePercent);
        State = State is null ? null : State with { VolumePercent = volumePercent };
        return Task.FromResult(MusicCommandResult.Applied);
    }
}
