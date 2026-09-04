using FluentAssertions;
using GsiHost.Services;
using Microsoft.Extensions.Configuration;

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
            "UND-85 deleted the leftover UseMockSpotify key; Mock is selected via --quick or Music:Provider=Mock");
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
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Mock");
    }

    [Fact]
    public void Prepare_ConfiguredMockProvider_IsPreservedWhenNotQuick()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292",
            ["Music:Provider"] = "Mock"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, Array.Empty<string>());

        settings.IsQuickLaunch.Should().BeFalse();
        settings.SkipCs2Setup.Should().BeFalse();
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Mock");
    }

    [Fact]
    public void Prepare_RemovedUseMockSpotifyFlag_DoesNotSelectMockProvider()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, new[] { "--use-mock-spotify" });

        settings.IsQuickLaunch.Should().BeFalse();
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Tauon");
    }

    [Fact]
    public void Prepare_MvpFlag_Throws()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var act = () => ConsoleLaunchBootstrap.Prepare(configuration, new[] { "--mvp" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(ConsoleLaunchBootstrap.RemovedMvpFlagMessage);
    }

    [Fact]
    public void Prepare_IntentCapture_SetsObserveFlagsInMemory()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(configuration, new[] { "--intent-capture" });

        settings.ConfigurationOverrides["Runtime:Mode"].Should().Be("intent_capture");
        settings.ConfigurationOverrides["Timeline:Enabled"].Should().Be("true");
        settings.ConfigurationOverrides["PlaybackObserver:Enabled"].Should().Be("true");
        settings.ConfigurationOverrides["Music:Provider"].Should().Be("Tauon");
    }

    [Fact]
    public void Prepare_IntentCapture_WithScenarioPlayback_KeepsScenarioPlaybackAndDoesNotEnableObserveFlags()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Gsi:Url"] = "http://127.0.0.1:5292"
        });

        var settings = ConsoleLaunchBootstrap.Prepare(
            configuration,
            new[] { "--intent-capture", "--scenario-playback" });

        settings.ConfigurationOverrides["Runtime:Mode"].Should().Be("scenario_playback");
        settings.ConfigurationOverrides.ContainsKey("Timeline:Enabled").Should().BeFalse();
        settings.ConfigurationOverrides.ContainsKey("PlaybackObserver:Enabled").Should().BeFalse();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
