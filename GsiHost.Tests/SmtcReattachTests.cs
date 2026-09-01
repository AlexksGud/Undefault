using Core.Music;
using FluentAssertions;
using GsiHost.Players;
using GsiHost.Players.Smtc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests;

public sealed class SmtcReattachTests
{
    private const string TauonId = "Tauon Music Box.exe";
    private const string ChromeId = "Chrome";

    [Fact]
    public async Task PauseAsync_WhenSelectedSessionIsPresent_ReturnsApplied()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        using var player = CreatePlayer(source, TauonId);

        var result = await player.PauseAsync();

        result.Should().Be(MusicCommandResult.Applied);
        source.Commands.Should().Equal(("pause", TauonId));
    }

    [Fact]
    public async Task PauseAsync_WhenSelectedSessionDisappears_ReturnsUnavailable_AndDoesNotCommandDecoy()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        using var player = CreatePlayer(source, TauonId);
        (await player.PauseAsync()).Should().Be(MusicCommandResult.Applied);

        source.Sessions.Clear();
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        source.Commands.Clear();

        var result = await player.PauseAsync();

        AssertUnavailable(result);
        source.Commands.Should().BeEmpty();
        source.ForceUpdateCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RefreshIfSelectedMissing_WhenSelectedIdIsAbsent_CallsForceUpdate()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(ChromeId));
        using var player = CreatePlayer(source, TauonId);

        await player.RefreshIfSelectedMissingAsync();

        source.ForceUpdateCalls.Should().BeGreaterThan(0);
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task RefreshIfSelectedMissing_WhenSelectedIdIsPresent_DoesNotCallForceUpdate()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        using var player = CreatePlayer(source, TauonId);
        (await player.PauseAsync()).Should().Be(MusicCommandResult.Applied);
        var callsAfterPause = source.ForceUpdateCalls;

        await player.RefreshIfSelectedMissingAsync();

        source.ForceUpdateCalls.Should().Be(callsAfterPause);
    }

    [Fact]
    public async Task PauseAsync_WhenSelectedIdReappears_AppliesAgainOnSameInstance()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(TauonId));
        using var player = CreatePlayer(source, TauonId);

        (await player.PauseAsync()).Should().Be(MusicCommandResult.Applied);

        source.Sessions.Clear();
        source.Sessions.Add(Playing(ChromeId, isCurrent: true));
        source.Commands.Clear();
        AssertUnavailable(await player.PauseAsync());
        source.Commands.Should().BeEmpty();

        await player.RefreshIfSelectedMissingAsync();
        source.ForceUpdateCalls.Should().BeGreaterThan(0);

        source.Sessions.Clear();
        source.Sessions.Add(Playing(TauonId));
        source.Commands.Clear();

        var result = await player.PauseAsync();

        result.Should().Be(MusicCommandResult.Applied);
        source.Commands.Should().Equal(("pause", TauonId));
    }

    [Fact]
    public async Task AppearDisappearCycles_DoNotCommandOtherSessions_OnSameInstance()
    {
        var source = new FakeSmtcSessionSource();
        using var player = CreatePlayer(source, TauonId);

        for (var i = 0; i < 3; i++)
        {
            source.Sessions.Clear();
            source.Sessions.Add(Playing(TauonId));
            source.Commands.Clear();
            (await player.PauseAsync()).Should().Be(MusicCommandResult.Applied);
            source.Commands.Should().Equal(("pause", TauonId));

            source.Sessions.Clear();
            source.Sessions.Add(Playing(ChromeId, isCurrent: true));
            source.Commands.Clear();
            AssertUnavailable(await player.PauseAsync());
            source.Commands.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SetVolumeAsync_WhenSelectedSessionIsAbsent_ReturnsUnsupported_AndCommandsNoSession()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(ChromeId));
        using var player = CreatePlayer(source, TauonId);

        var result = await player.SetVolumeAsync(40);

        result.Outcome.Should().Be(MusicCommandOutcome.Unsupported);
        result.Reason.Should().NotBeNullOrWhiteSpace();
        source.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task ReattachTimer_CallsForceUpdateWhileAbsent_AndStopsAfterDispose()
    {
        var source = new FakeSmtcSessionSource();
        source.Sessions.Add(Playing(ChromeId));
        var player = CreatePlayer(source, TauonId, pollInterval: TimeSpan.FromMilliseconds(30));

        try
        {
            await WaitUntilAsync(() => source.ForceUpdateCalls > 0, TimeSpan.FromSeconds(2));
            source.ForceUpdateCalls.Should().BeGreaterThan(0);
            source.Commands.Should().BeEmpty();
        }
        finally
        {
            player.Dispose();
        }

        var callsAfterDispose = source.ForceUpdateCalls;
        await Task.Delay(150);
        source.ForceUpdateCalls.Should().Be(callsAfterDispose);

        var act = async () => await player.PauseAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task RefreshIfSelectedMissing_AfterDispose_DoesNotCallForceUpdate()
    {
        var source = new FakeSmtcSessionSource();
        using var player = CreatePlayer(source, TauonId);
        player.Dispose();
        var calls = source.ForceUpdateCalls;

        await player.RefreshIfSelectedMissingAsync();

        source.ForceUpdateCalls.Should().Be(calls);
    }

    private static SmtcMusicPlayer CreatePlayer(
        FakeSmtcSessionSource source,
        string? selectedId,
        TimeSpan? pollInterval = null)
    {
        var options = Options.Create(new SmtcOptions { SourceAppUserModelId = selectedId });
        return pollInterval is { } interval
            ? new SmtcMusicPlayer(source, options, NullLogger<SmtcMusicPlayer>.Instance, interval)
            : new SmtcMusicPlayer(source, options, NullLogger<SmtcMusicPlayer>.Instance);
    }

    private static SmtcSessionSnapshot Playing(string id, bool isCurrent = false)
        => new(
            SourceAppUserModelId: id,
            PlaybackStatus: PlaybackStatus.Playing,
            Track: null,
            IsPlayEnabled: false,
            IsPauseEnabled: true,
            IsNextEnabled: true,
            IsPreviousEnabled: true,
            IsCurrentSession: isCurrent);

    private static void AssertUnavailable(MusicCommandResult result)
    {
        result.Outcome.Should().Be(MusicCommandOutcome.Unavailable);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        condition().Should().BeTrue("the reattach timer should call ForceUpdate while the selected id is absent");
    }
}
