using FluentAssertions;
using GsiHost.Configuration;
using Microsoft.Extensions.Configuration;

namespace GsiHost.Tests;

public sealed class MusicProviderResolutionTests
{
    [Theory]
    [InlineData(null, MusicProviderOptions.Tauon, true)]
    [InlineData("", MusicProviderOptions.Tauon, true)]
    [InlineData("  ", MusicProviderOptions.Tauon, true)]
    [InlineData("Tauon", MusicProviderOptions.Tauon, true)]
    [InlineData("tauon", MusicProviderOptions.Tauon, true)]
    [InlineData("TAUON", MusicProviderOptions.Tauon, true)]
    [InlineData(" Tauon ", MusicProviderOptions.Tauon, true)]
    [InlineData("Mock", MusicProviderOptions.Mock, true)]
    [InlineData("mock", MusicProviderOptions.Mock, true)]
    [InlineData("Smtc", MusicProviderOptions.Smtc, true)]
    [InlineData("smtc", MusicProviderOptions.Smtc, true)]
    [InlineData("SMTC", MusicProviderOptions.Smtc, true)]
    public void Resolve_AcceptedValues_ReturnCanonicalName(
        string? configured,
        string canonicalName,
        bool playerIsRegistered)
    {
        var resolved = MusicProviderResolver.Resolve(configured);

        resolved.CanonicalName.Should().Be(canonicalName);
        resolved.PlayerIsRegistered.Should().Be(playerIsRegistered);
    }

    [Fact]
    public void Resolve_Smtc_IsAcceptedAndIsNotTauon()
    {
        var resolved = MusicProviderResolver.Resolve("Smtc");

        resolved.CanonicalName.Should().Be(MusicProviderOptions.Smtc);
        resolved.CanonicalName.Should().NotBe(MusicProviderOptions.Tauon);
        resolved.PlayerIsRegistered.Should().BeTrue();
    }

    [Theory]
    [InlineData("Spotify")]
    [InlineData("Unknown")]
    [InlineData("vlc")]
    [InlineData("TauonX")]
    public void Resolve_UnknownValue_ThrowsAndDoesNotBecomeTauon(string configured)
    {
        var act = () => MusicProviderResolver.Resolve(configured);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Music:Provider '{configured}' is unknown. Accepted values: Tauon, Smtc, Mock.");
    }

    [Fact]
    public void Resolve_Configuration_UsesMusicProviderKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Music:Provider"] = "smtc"
            })
            .Build();

        var resolved = MusicProviderResolver.Resolve(configuration);

        resolved.CanonicalName.Should().Be(MusicProviderOptions.Smtc);
        resolved.PlayerIsRegistered.Should().BeTrue();
    }

    [Fact]
    public void EnsurePlayerRegistered_Smtc_DoesNotThrowOnWindowsTfm()
    {
        var resolved = MusicProviderResolver.Resolve("Smtc");

        var act = () => MusicProviderResolver.EnsurePlayerRegistered(resolved);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Tauon")]
    [InlineData("Mock")]
    [InlineData("Smtc")]
    public void EnsurePlayerRegistered_RegisteredProviders_DoNotThrow(string configured)
    {
        var resolved = MusicProviderResolver.Resolve(configured);

        var act = () => MusicProviderResolver.EnsurePlayerRegistered(resolved);

        act.Should().NotThrow();
    }
}
