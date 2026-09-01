using Microsoft.Extensions.Configuration;

namespace GsiHost.Configuration;

/// <summary>
/// The canonical <c>Music:Provider</c> value after validation.
/// </summary>
/// <param name="CanonicalName">One of <c>Tauon</c>, <c>Smtc</c>, or <c>Mock</c>.</param>
/// <param name="PlayerIsRegistered">
/// <see langword="true"/> when this host build can construct an <c>IMusicPlayer</c> for
/// <paramref name="CanonicalName"/>; <see langword="false"/> when the name is valid but
/// the adapter is not registered (currently <c>Smtc</c>).
/// </param>
public sealed record MusicProviderResolution(string CanonicalName, bool PlayerIsRegistered);

/// <summary>
/// Resolves <c>Music:Provider</c> to a single canonical value.
/// </summary>
/// <remarks>
/// Accepted names are <c>Tauon</c>, <c>Smtc</c>, and <c>Mock</c> (case-insensitive).
/// Missing or blank configuration defaults to <c>Tauon</c>. Unknown values throw;
/// they never become Tauon.
/// </remarks>
public static class MusicProviderResolver
{
    private static readonly HashSet<string> RegisteredPlayers = new(StringComparer.Ordinal)
    {
        MusicProviderOptions.Tauon,
        MusicProviderOptions.Mock
    };

    /// <summary>
    /// Resolves the configured provider name to a canonical value.
    /// </summary>
    /// <param name="configured">The raw <c>Music:Provider</c> value. May be <see langword="null"/>.</param>
    /// <returns>The canonical resolution.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value is not <c>Tauon</c>, <c>Smtc</c>, or <c>Mock</c>.
    /// </exception>
    public static MusicProviderResolution Resolve(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Create(MusicProviderOptions.Tauon);
        }

        var trimmed = configured.Trim();
        if (string.Equals(trimmed, MusicProviderOptions.Tauon, StringComparison.OrdinalIgnoreCase))
        {
            return Create(MusicProviderOptions.Tauon);
        }

        if (string.Equals(trimmed, MusicProviderOptions.Smtc, StringComparison.OrdinalIgnoreCase))
        {
            return Create(MusicProviderOptions.Smtc);
        }

        if (string.Equals(trimmed, MusicProviderOptions.Mock, StringComparison.OrdinalIgnoreCase))
        {
            return Create(MusicProviderOptions.Mock);
        }

        throw new InvalidOperationException(
            $"Music:Provider '{trimmed}' is unknown. Accepted values: Tauon, Smtc, Mock.");
    }

    /// <summary>
    /// Resolves <c>Music:Provider</c> from host configuration.
    /// </summary>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The canonical resolution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The configured value is not <c>Tauon</c>, <c>Smtc</c>, or <c>Mock</c>.
    /// </exception>
    public static MusicProviderResolution Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Resolve(configuration[$"{MusicProviderOptions.SectionName}:Provider"]);
    }

    /// <summary>
    /// Throws when the resolved provider name has no <c>IMusicPlayer</c> registration.
    /// </summary>
    /// <param name="resolution">The value returned by <see cref="Resolve(string?)"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resolution"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="resolution"/> names a valid provider that is not registered in this build.
    /// </exception>
    public static void EnsurePlayerRegistered(MusicProviderResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.PlayerIsRegistered)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Music:Provider '{resolution.CanonicalName}' is not registered.");
    }

    private static MusicProviderResolution Create(string canonicalName)
    {
        return new MusicProviderResolution(canonicalName, RegisteredPlayers.Contains(canonicalName));
    }
}
