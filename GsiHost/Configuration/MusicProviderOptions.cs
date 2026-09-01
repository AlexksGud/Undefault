namespace GsiHost.Configuration;

/// <summary>
/// Binds the <c>Music</c> section that selects the playback backend.
/// </summary>
public sealed class MusicProviderOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Music";

    /// <summary>
    /// Canonical name for the Tauon remote HTTP adapter.
    /// </summary>
    public const string Tauon = "Tauon";

    /// <summary>
    /// Canonical name for the Windows SMTC adapter.
    /// </summary>
    /// <remarks>
    /// The name is accepted by <see cref="MusicProviderResolver"/>. The adapter is not
    /// registered in this host build; startup fails with a not-registered error instead
    /// of falling back to Tauon.
    /// </remarks>
    public const string Smtc = "Smtc";

    /// <summary>
    /// Canonical name for the in-process mock player used by <c>--quick</c>.
    /// </summary>
    public const string Mock = "Mock";

    /// <summary>
    /// Gets or sets the player backend name.
    /// </summary>
    /// <value>
    /// <c>Tauon</c> (default), <c>Smtc</c>, or <c>Mock</c>. Unknown values fail at startup.
    /// </value>
    public string Provider { get; set; } = Tauon;
}
