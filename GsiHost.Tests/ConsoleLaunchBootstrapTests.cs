using FluentAssertions;
using GsiHost.Services;
using Microsoft.Extensions.Configuration;
using Core.Spotify;
using Microsoft.Extensions.Logging.Abstractions;

namespace GsiHost.Tests;

public sealed class ConsoleLaunchBootstrapTests
{
    [Fact]
    public void Prepare_DefaultLaunch_UsesTauonMusicProvider_AndEmitsNoSpotifyCredentialOverrides()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, Array.Empty<string>());

        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Tauon");
        settings.ConfigurationOverrides.Keys.Should().NotContain(key =>
            key.StartsWith("Spotify:", StringComparison.OrdinalIgnoreCase));
        settings.ConfigurationOverrides.ContainsKey("UseMockSpotify").Should().BeFalse(
            "UND-84 deleted the real-client switch; the leftover client is always the mock");
    }

    [Fact]
    public void Prepare_NormalizesLoopbackUrls()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://localhost:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, Array.Empty<string>());

        settings.GsiBaseUrl.Should().Be("http://127.0.0.1:5292");
    }

    [Fact]
    public void Prepare_QuickLaunch_UsesMockMusicProvider_AndSkipsOptionalStartup()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, new[] { "--quick" });

        settings.IsQuickLaunch.Should().BeTrue();
        settings.SkipCs2Setup.Should().BeTrue();
        settings.SkipSmartTrackWarmup.Should().BeTrue();
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Mock");
    }

    [Fact]
    public void Prepare_MvpFlag_SetsIntentCaptureAndEnablesAllFeatureFlagsInMemory()
    {
        // UND-66 / UND-78: --mvp is the one-command MVP launch. It implies intent_capture
        // and turns Timeline / PlaybackObserver ON via in-memory overrides, without
        // flipping the git-tracked appsettings default. The user controls playback via
        // the keyboard media play/pause key; Undefault only observes and records.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, new[] { "--mvp" });

        settings.IsMvpLaunch.Should().BeTrue();
        settings.ConfigurationOverrides["Runtime:Mode"].Should().Be("intent_capture");
        settings.ConfigurationOverrides["Timeline:Enabled"].Should().Be("true");
        settings.ConfigurationOverrides["PlaybackObserver:Enabled"].Should().Be("true");
    }

    [Fact]
    public void Prepare_MvpFlag_WithScenarioPlayback_KeepsScenarioPlaybackModeButLeavesFlagsOn()
    {
        // Explicit --scenario-playback wins for the runtime mode; --mvp still turns
        // the feature flags on (they are no-ops outside intent_capture).
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(
            configuration,
            new[] { "--mvp", "--scenario-playback" });

        settings.IsMvpLaunch.Should().BeTrue();
        settings.ConfigurationOverrides["Runtime:Mode"].Should().Be("scenario_playback");
        settings.ConfigurationOverrides["Timeline:Enabled"].Should().Be("true");
        settings.ConfigurationOverrides["PlaybackObserver:Enabled"].Should().Be("true");
    }

    [Fact]
    public void Prepare_MvpFlag_KeepsTauonMusicProvider_AndPreservesIntentCaptureFlagBehavior()
    {
        // --mvp is leftover tester tooling; it must not change the music provider.
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(
            configuration,
            new[] { "--mvp", "--intent-capture" });

        settings.IsMvpLaunch.Should().BeTrue();
        settings.ConfigurationOverrides["Runtime:Mode"].Should().Be("intent_capture");
        settings.ConfigurationOverrides["PlaybackObserver:Enabled"].Should().Be("true");
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Tauon");
    }

    [Fact]
    public async Task MockSpotifyClient_ReportsUnauthenticatedState()
    {
        var client = new MockSpotifyClient(NullLogger<MockSpotifyClient>.Instance);

        (await client.IsAuthenticatedAsync()).Should().BeFalse();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
