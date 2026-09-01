using Core.Configuration;
using Core.Models;
using Core.Music;
using FluentAssertions;
using GsiHost.Diagnostics;
using GsiHost.Players;
using GsiHost.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests.Diagnostics;

public sealed class CountingMusicPlaybackControlTests
{
    private const string TargetAppId = "Exact.SourceAppUserModelId";

    [Fact]
    public async Task TryPauseAsync_GamePath_IncrementsCountersAndKeepsLastCommand()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var store = CreateStore(root.Path, clock);
        var lastCommand = new MusicLastCommandStore();
        var inner = new StubPlaybackControl { Result = MusicCommandResult.Applied };
        var smtc = Options.Create(new SmtcOptions { SourceAppUserModelId = TargetAppId });
        var counting = new CountingMusicPlaybackControl(
            new RecordingMusicPlaybackControl(inner, lastCommand, smtc),
            store,
            smtc);

        var result = await counting.TryPauseAsync("death");

        result.IsApplied.Should().BeTrue();
        inner.PauseCalls.Should().Be(1);

        var snapshot = store.GetSnapshot();
        snapshot.GameOutcomes[MusicCommandOutcome.Applied].Should().Be(1);
        snapshot.FirstGameAppliedUtc.Should().Be(clock.UtcNow);
        snapshot.LastCommand!.TargetAppId.Should().Be(TargetAppId);
        snapshot.LastCommand.Source.Should().Be(MusicLastCommandStore.GameSource);

        var recorded = lastCommand.Get();
        recorded.Command.Should().Be(MusicControlCommands.Pause);
        recorded.Source.Should().Be(MusicLastCommandStore.GameSource);
        recorded.TargetAppId.Should().Be(TargetAppId);
        recorded.Outcome.Should().Be(nameof(MusicCommandOutcome.Applied));
    }

    [Fact]
    public async Task TrySetManagedVolumeAsync_DoesNotRecordACounter()
    {
        using var root = new TempDirectory();
        var store = CreateStore(root.Path, TimeProvider.System);
        var inner = new StubPlaybackControl { Result = MusicCommandResult.Applied };
        var counting = new CountingMusicPlaybackControl(
            inner,
            store,
            Options.Create(new SmtcOptions { SourceAppUserModelId = TargetAppId }));

        var result = await counting.TrySetManagedVolumeAsync(40, "volume");

        result.IsApplied.Should().BeTrue();
        inner.ManagedVolumeCalls.Should().Be(1);
        store.GetSnapshot().GameOutcomes.Values.Should().OnlyContain(count => count == 0);
        store.GetSnapshot().LastCommand.Should().BeNull();
    }

    [Fact]
    public async Task TryResumeAsync_NonAppliedOutcome_StillCopiesTargetAppId()
    {
        using var root = new TempDirectory();
        var store = CreateStore(root.Path, TimeProvider.System);
        var inner = new StubPlaybackControl { Result = MusicCommandResult.Unavailable("no player") };
        var counting = new CountingMusicPlaybackControl(
            inner,
            store,
            Options.Create(new SmtcOptions { SourceAppUserModelId = TargetAppId }));

        var result = await counting.TryResumeAsync("round_start");

        result.Outcome.Should().Be(MusicCommandOutcome.Unavailable);
        var snapshot = store.GetSnapshot();
        snapshot.FirstGameAppliedUtc.Should().BeNull();
        snapshot.GameOutcomes[MusicCommandOutcome.Unavailable].Should().Be(1);
        snapshot.LastCommand!.TargetAppId.Should().Be(TargetAppId);
        snapshot.LastCommand.Outcome.Should().Be(MusicCommandOutcome.Unavailable);
    }

    private static LocalGoNoGoCounterStore CreateStore(string directory, TimeProvider timeProvider)
        => new(
            Options.Create(new LocalGoNoGoCounterOptions { Directory = directory }),
            new StubWebHostEnvironment("unused-content-root"),
            NullLogger<LocalGoNoGoCounterStore>.Instance,
            timeProvider);

    private sealed class StubPlaybackControl : IMusicPlaybackControl
    {
        public MusicCommandResult Result { get; set; } = MusicCommandResult.Applied;

        public int PauseCalls { get; private set; }

        public int ManagedVolumeCalls { get; private set; }

        public Task<MusicCommandResult> TryPauseAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            return Task.FromResult(Result);
        }

        public Task<MusicCommandResult> TryResumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TryNextAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TryPreviousAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TryDuckAsync(
            EventControlRule rule,
            NormalizedEvent context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TryDuckAsync(
            int volumePercent,
            string? eventKeyForLog,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TryRestoreVolumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<MusicCommandResult> TrySetManagedVolumeAsync(
            int volumePercent,
            string? eventKeyForLog,
            CancellationToken cancellationToken = default)
        {
            ManagedVolumeCalls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "GsiHost.Tests";

        public string WebRootPath { get; set; } = string.Empty;

        public string ContentRootPath { get; set; }

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "UndefaultIt.Tests",
                "go-no-go-counting",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
