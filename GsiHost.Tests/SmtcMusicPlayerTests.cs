using Core.Music;
using FluentAssertions;
using GsiHost.Players;
using GsiHost.Players.Smtc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests;

public sealed class SmtcMusicPlayerTests
{
    private const string TauonId = "Tauon Music Box.exe";
    private const string ChromeId = "Chrome";

    [Fact]
    public async Task PauseAsync_WhenSelectedIdIsMissing_ReturnsUnavailableAndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        var player = CreatePlayer(source, selectedId: "missing-player");

        var result = await player.PauseAsync();

        AssertNonApplied(result, MusicCommandOutcome.Unavailable);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_WhenSelectionIsEmpty_ReturnsUnavailableAndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        var player = CreatePlayer(source, selectedId: "");

        var result = await player.PauseAsync();

        AssertNonApplied(result, MusicCommandOutcome.Unavailable);
        source.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Tauon")]
    [InlineData("Chro")]
    [InlineData("tauon music box.exe")]
    [InlineData("CHROME")]
    [InlineData("Tauon Music Box")]
    public async Task Commands_UseExactOrdinalId_AndDoNotMatchSubstringsOrCaseVariants(string selectedId)
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        source.Sessions.Add(Playing(ChromeId));
        var player = CreatePlayer(source, selectedId);

        var pause = await player.PauseAsync();
        var play = await player.PlayAsync();
        var next = await player.NextAsync();

        AssertNonApplied(pause, MusicCommandOutcome.Unavailable);
        AssertNonApplied(play, MusicCommandOutcome.Unavailable);
        AssertNonApplied(next, MusicCommandOutcome.Unavailable);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_WhenExactIdMatches_CommandsOnlyThatSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        result.Should().Be(MusicCommandResult.Applied);
        source.Commands.Should().Equal(("pause", TauonId));
    }

    [Fact]
    public async Task PauseAsync_WhenAlreadyPaused_ReturnsAppliedWithoutCommand()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused(TauonId, isPauseEnabled: false));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        result.Should().Be(MusicCommandResult.Applied);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PlayAsync_WhenAlreadyPlaying_ReturnsAppliedWithoutCommand()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId, isPlayEnabled: false));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PlayAsync();

        result.Should().Be(MusicCommandResult.Applied);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_WhenDynamicControlIsDisabled_ReturnsUnsupportedAndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId, isPauseEnabled: false));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        AssertNonApplied(result, MusicCommandOutcome.Unsupported);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PlayAsync_WhenDynamicControlIsDisabled_ReturnsUnsupportedAndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Paused(TauonId, isPlayEnabled: false));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PlayAsync();

        AssertNonApplied(result, MusicCommandOutcome.Unsupported);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_WhenTryPauseReturnsFalse_ReturnsRejected()
    {
        var source = new FakeSmtcSessionSource { PauseResult = false };
        source.Sessions.Add(Playing(TauonId));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        AssertNonApplied(result, MusicCommandOutcome.Rejected);
        source.Commands.Should().Equal(("pause", TauonId));
    }

    [Fact]
    public async Task PlayAsync_WhenTryPlayReturnsFalse_ReturnsRejected()
    {
        var source = new FakeSmtcSessionSource { PlayResult = false };
        source.Sessions.Add(Paused(TauonId));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PlayAsync();

        AssertNonApplied(result, MusicCommandOutcome.Rejected);
        source.Commands.Should().Equal(("play", TauonId));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(101)]
    public async Task SetVolumeAsync_ReturnsUnsupportedAndDoesNotThrow(int volumePercent)
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        var player = CreatePlayer(source, TauonId);

        var act = async () => await player.SetVolumeAsync(volumePercent);

        await act.Should().NotThrowAsync();
        var result = await act();
        AssertNonApplied(result, MusicCommandOutcome.Unsupported);
        player.Capabilities.CanSetVolume.Should().BeFalse();
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task IsAvailableAsync_WhenSessionListIsEmpty_ReturnsTrue()
    {
        var source = new FakeSmtcSessionSource();
        var player = CreatePlayer(source, TauonId);

        var available = await player.IsAvailableAsync();
        var state = await player.GetStateAsync();
        var pause = await player.PauseAsync();

        available.Should().BeTrue();
        state.Should().BeNull();
        AssertNonApplied(pause, MusicCommandOutcome.Unavailable);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStateAsync_WhenExactIdMatches_ReturnsThatSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        source.Sessions.Add(Paused(
            TauonId,
            track: new MusicTrack(null, "Song", "Artist", "Album")));
        var player = CreatePlayer(source, TauonId);

        var state = await player.GetStateAsync();

        state.Should().NotBeNull();
        state!.Status.Should().Be(PlaybackStatus.Paused);
        state.VolumePercent.Should().BeNull();
        state.Track.Should().NotBeNull();
        state.Track!.Title.Should().Be("Song");
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseAsync_DoesNotFallBackToCurrentSessionHint()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        AssertNonApplied(result, MusicCommandOutcome.Unavailable);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Capabilities_AllowTransportAndSkip_ButNotVolume()
    {
        var player = CreatePlayer(new FakeSmtcSessionSource(), TauonId);

        player.Capabilities.CanPlay.Should().BeTrue();
        player.Capabilities.CanPause.Should().BeTrue();
        player.Capabilities.CanResume.Should().BeTrue();
        player.Capabilities.CanSkip.Should().BeTrue();
        player.Capabilities.CanSetVolume.Should().BeFalse();
    }

    [Fact]
    public void CoreAssembly_DoesNotReferenceWinRtOrDubya()
    {
        var names = typeof(IMusicPlayer).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .ToArray();

        names.Should().NotContain("WindowsMediaController");
        names.Should().NotContain("Dubya.WindowsMediaController");
        names.Should().NotContain(name => name.StartsWith("Windows.Media", StringComparison.Ordinal));
        names.Should().NotContain(name => name.StartsWith("Microsoft.Windows.SDK.NET", StringComparison.Ordinal));
        names.Should().NotContain(name => name.Contains("WinRT", StringComparison.OrdinalIgnoreCase));
    }

    private static SmtcMusicPlayer CreatePlayer(FakeSmtcSessionSource source, string? selectedId)
    {
        var options = Options.Create(new SmtcOptions { SourceAppUserModelId = selectedId });
        return new SmtcMusicPlayer(source, options, NullLogger<SmtcMusicPlayer>.Instance);
    }

    private static SmtcSessionSnapshot Playing(
        string id,
        bool isPauseEnabled = true,
        bool isPlayEnabled = false,
        bool isCurrent = false)
        => new(
            SourceAppUserModelId: id,
            PlaybackStatus: PlaybackStatus.Playing,
            Track: null,
            IsPlayEnabled: isPlayEnabled,
            IsPauseEnabled: isPauseEnabled,
            IsNextEnabled: true,
            IsPreviousEnabled: true,
            IsCurrentSession: isCurrent);

    private static SmtcSessionSnapshot Paused(
        string id,
        bool isPlayEnabled = true,
        bool isPauseEnabled = false,
        bool isCurrent = false,
        MusicTrack? track = null)
        => new(
            SourceAppUserModelId: id,
            PlaybackStatus: PlaybackStatus.Paused,
            Track: track,
            IsPlayEnabled: isPlayEnabled,
            IsPauseEnabled: isPauseEnabled,
            IsNextEnabled: true,
            IsPreviousEnabled: true,
            IsCurrentSession: isCurrent);

    private static void AssertNonApplied(MusicCommandResult result, MusicCommandOutcome outcome)
    {
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }
}
