using Core.Configuration;
using Core.Music;
using FluentAssertions;
using GsiHost.Diagnostics;
using GsiHost.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GsiHost.Tests.Diagnostics;

public sealed class LocalGoNoGoCounterStoreTests
{
    private const string TargetAppId = "Tauon Music Box.exe";

    [Fact]
    public void Record_GameOutcomes_IncrementPerMusicCommandOutcome()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var store = CreateStore(root.Path, clock);

        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Failed("transport"));
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Unavailable("no session"));
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Unsupported("skip"));
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Rejected("refused"));

        var snapshot = store.GetSnapshot();
        snapshot.GameOutcomes[MusicCommandOutcome.Applied].Should().Be(1);
        snapshot.GameOutcomes[MusicCommandOutcome.Failed].Should().Be(1);
        snapshot.GameOutcomes[MusicCommandOutcome.Unavailable].Should().Be(1);
        snapshot.GameOutcomes[MusicCommandOutcome.Unsupported].Should().Be(1);
        snapshot.GameOutcomes[MusicCommandOutcome.Rejected].Should().Be(1);
        snapshot.TestOutcomes.Values.Should().OnlyContain(count => count == 0);
        File.Exists(store.FilePath).Should().BeTrue();
    }

    [Fact]
    public void Record_ReloadsCountersAfterNewStoreInstance()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var first = CreateStore(root.Path, clock);
        first.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        first.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Failed("down"));
        var firstSnapshot = first.GetSnapshot();

        clock.UtcNow = Utc(1);
        var restarted = CreateStore(root.Path, clock);
        var snapshot = restarted.GetSnapshot();

        snapshot.GameOutcomes[MusicCommandOutcome.Applied].Should().Be(1);
        snapshot.GameOutcomes[MusicCommandOutcome.Failed].Should().Be(1);
        snapshot.FirstGameAppliedUtc.Should().Be(firstSnapshot.FirstGameAppliedUtc);
        snapshot.GameSessionsWithApplied.Should().Be(1);
        snapshot.HostStartUtc.Should().Be(Utc(1));
        snapshot.HostStartUtc.Should().NotBe(firstSnapshot.HostStartUtc);
        snapshot.LastCommand!.TargetAppId.Should().Be(TargetAppId);
    }

    [Fact]
    public void Record_FirstGameApplied_SetsTimestampAgainstHostStart()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var store = CreateStore(root.Path, clock);

        clock.UtcNow = Utc(0).AddMinutes(4);
        store.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);

        var snapshot = store.GetSnapshot();
        snapshot.HostStartUtc.Should().Be(Utc(0));
        snapshot.FirstGameAppliedUtc.Should().Be(clock.UtcNow);
        snapshot.HostStartUtcAtFirstGameApplied.Should().Be(Utc(0));
        (snapshot.FirstGameAppliedUtc!.Value - snapshot.HostStartUtc).Should().Be(TimeSpan.FromMinutes(4));

        clock.UtcNow = Utc(0).AddMinutes(8);
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        store.GetSnapshot().FirstGameAppliedUtc.Should().Be(snapshot.FirstGameAppliedUtc);
    }

    [Fact]
    public void Record_CopiesTargetAppIdOntoEveryCommand()
    {
        using var root = new TempDirectory();
        var store = CreateStore(root.Path, new MutableTimeProvider(Utc(0)));

        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Unavailable("missing"));
        store.GetSnapshot().LastCommand.Should().BeEquivalentTo(new LocalGoNoGoRecordedCommand(
            MusicControlCommands.Pause,
            MusicLastCommandStore.GameSource,
            TargetAppId,
            MusicCommandOutcome.Unavailable,
            Utc(0)));

        store.Record(MusicControlCommands.Resume, MusicLastCommandStore.TestSource, "Other.Player", MusicCommandResult.Applied);
        store.GetSnapshot().LastCommand!.TargetAppId.Should().Be("Other.Player");
        store.GetSnapshot().LastCommand!.Source.Should().Be(MusicLastCommandStore.TestSource);
    }

    [Fact]
    public void Record_TestSourceApplied_DoesNotSetFirstAutomaticApplied()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var store = CreateStore(root.Path, clock);

        clock.UtcNow = Utc(0).AddMinutes(2);
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.TestSource, TargetAppId, MusicCommandResult.Applied);

        var afterTest = store.GetSnapshot();
        afterTest.FirstGameAppliedUtc.Should().BeNull();
        afterTest.GameSessionsWithApplied.Should().Be(0);
        afterTest.TestOutcomes[MusicCommandOutcome.Applied].Should().Be(1);
        afterTest.GameOutcomes[MusicCommandOutcome.Applied].Should().Be(0);
        afterTest.LastCommand!.Source.Should().Be(MusicLastCommandStore.TestSource);
        afterTest.LastCommand.TargetAppId.Should().Be(TargetAppId);

        clock.UtcNow = Utc(0).AddMinutes(9);
        store.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);

        var afterGame = store.GetSnapshot();
        afterGame.FirstGameAppliedUtc.Should().Be(clock.UtcNow);
        afterGame.GameSessionsWithApplied.Should().Be(1);
        afterGame.GameOutcomes[MusicCommandOutcome.Applied].Should().Be(1);
    }

    [Fact]
    public void Record_GameAppliedAfterIdleGap_OpensNewSession()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var store = CreateStore(root.Path, clock);

        store.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        store.GetSnapshot().GameSessionsWithApplied.Should().Be(1);

        clock.UtcNow = Utc(0).AddMinutes(29);
        store.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        store.GetSnapshot().GameSessionsWithApplied.Should().Be(1);

        clock.UtcNow = Utc(0).AddMinutes(29) + LocalGoNoGoCounterStore.GameSessionIdleGap;
        store.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        store.GetSnapshot().GameSessionsWithApplied.Should().Be(2);
    }

    [Fact]
    public void Record_FirstGameAppliedAfterRestart_OpensNewSession()
    {
        using var root = new TempDirectory();
        var clock = new MutableTimeProvider(Utc(0));
        var first = CreateStore(root.Path, clock);
        first.Record(MusicControlCommands.Resume, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        first.GetSnapshot().GameSessionsWithApplied.Should().Be(1);

        clock.UtcNow = Utc(0).AddMinutes(1);
        var restarted = CreateStore(root.Path, clock);
        restarted.Record(MusicControlCommands.Pause, MusicLastCommandStore.GameSource, TargetAppId, MusicCommandResult.Applied);
        restarted.GetSnapshot().GameSessionsWithApplied.Should().Be(2);
    }

    private static LocalGoNoGoCounterStore CreateStore(string directory, TimeProvider timeProvider)
        => new(
            Options.Create(new LocalGoNoGoCounterOptions { Directory = directory }),
            new StubWebHostEnvironment("unused-content-root"),
            NullLogger<LocalGoNoGoCounterStore>.Instance,
            timeProvider);

    private static DateTimeOffset Utc(int hours)
        => new(2026, 9, 1, hours, 0, 0, TimeSpan.Zero);

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
                "go-no-go",
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
