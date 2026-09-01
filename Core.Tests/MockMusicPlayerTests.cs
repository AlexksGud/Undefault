using Core.Music;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Core.Tests;

public class MockMusicPlayerTests
{
    [Fact]
    public async Task PlayPauseResume_UpdateStateAndCallCounts()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);

        var play = await player.PlayAsync();
        play.Should().Be(MusicCommandResult.Applied);
        var playing = await player.GetStateAsync();
        playing!.Status.Should().Be(PlaybackStatus.Playing);
        player.PlayCalls.Should().Be(1);

        var pause = await player.PauseAsync();
        pause.Should().Be(MusicCommandResult.Applied);
        var paused = await player.GetStateAsync();
        paused!.Status.Should().Be(PlaybackStatus.Paused);
        player.PauseCalls.Should().Be(1);

        var resume = await player.ResumeAsync();
        resume.Should().Be(MusicCommandResult.Applied);
        var resumed = await player.GetStateAsync();
        resumed!.Status.Should().Be(PlaybackStatus.Playing);
        player.ResumeCalls.Should().Be(1);
    }

    [Fact]
    public async Task Resume_WhenAlreadyPlaying_IsIdempotent()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Playing);

        var first = await player.ResumeAsync();
        var second = await player.ResumeAsync();

        first.Should().Be(MusicCommandResult.Applied);
        second.Should().Be(MusicCommandResult.Applied);
        var state = await player.GetStateAsync();
        state!.Status.Should().Be(PlaybackStatus.Playing);
        player.ResumeCalls.Should().Be(2);
    }

    [Fact]
    public async Task Unavailable_ReturnsNoState_AndDoesNotChangeTransport()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);
        player.SeedState(PlaybackStatus.Playing);
        player.Available = false;

        (await player.IsAvailableAsync()).Should().BeFalse();
        (await player.GetStateAsync()).Should().BeNull();

        var pause = await player.PauseAsync();
        pause.Outcome.Should().Be(MusicCommandOutcome.Unavailable);
        pause.Reason.Should().NotBeNullOrWhiteSpace();
        player.Available = true;
        var state = await player.GetStateAsync();
        state!.Status.Should().Be(PlaybackStatus.Playing);
    }

    [Fact]
    public async Task NextPreviousAndVolume_AreRecorded()
    {
        var player = new MockMusicPlayer(NullLogger<MockMusicPlayer>.Instance);

        var next = await player.NextAsync();
        var previous = await player.PreviousAsync();
        var volume = await player.SetVolumeAsync(25);

        next.Should().Be(MusicCommandResult.Applied);
        previous.Should().Be(MusicCommandResult.Applied);
        volume.Should().Be(MusicCommandResult.Applied);
        player.NextCalls.Should().Be(1);
        player.PreviousCalls.Should().Be(1);
        player.VolumeCalls.Should().Equal(25);
        var state = await player.GetStateAsync();
        state!.VolumePercent.Should().Be(25);
    }
}
