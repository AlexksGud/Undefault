namespace GsiHost.Players;

/// <summary>
/// Binds <c>Music:Smtc</c> for the Windows SMTC adapter.
/// </summary>
/// <remarks>
/// Selection is the user's explicit <see cref="SourceAppUserModelId"/> only.
/// HTTP onboarding (UND-95) is out of scope for this slice.
/// </remarks>
public sealed class SmtcOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "Music:Smtc";

    /// <summary>
    /// Gets or sets the exact SMTC <c>SourceAppUserModelId</c> to command.
    /// </summary>
    /// <value>
    /// Copied from an enumerated session list. Empty or missing means no session is selected.
    /// Matching is ordinal; the value is not trimmed, folded, or substring-matched.
    /// </value>
    public string? SourceAppUserModelId { get; set; }
}
