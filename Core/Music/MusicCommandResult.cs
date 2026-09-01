namespace Core.Music;

/// <summary>
/// Neutral outcome of a player transport command.
/// </summary>
public enum MusicCommandOutcome
{
    /// <summary>
    /// The player accepted the command.
    /// </summary>
    Applied,

    /// <summary>
    /// The backend or current session cannot perform this command.
    /// </summary>
    Unsupported,

    /// <summary>
    /// No player is reachable, or no session is selected.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The player refused the command or returned an unusable payload.
    /// </summary>
    Rejected,

    /// <summary>
    /// The command failed because of a transport error.
    /// </summary>
    Failed
}

/// <summary>
/// Result of an <see cref="IMusicPlayer"/> transport command.
/// </summary>
/// <param name="Outcome">One of the enumeration values that specifies how the command ended.</param>
/// <param name="Reason">A human-readable explanation, or <see langword="null"/> when <paramref name="Outcome"/> is <see cref="MusicCommandOutcome.Applied"/>.</param>
/// <remarks>
/// Non-<see cref="MusicCommandOutcome.Applied"/> results always carry a non-empty <see cref="Reason"/>.
/// </remarks>
public sealed record MusicCommandResult(MusicCommandOutcome Outcome, string? Reason = null)
{
    /// <summary>
    /// Gets a successful result with no reason string.
    /// </summary>
    public static MusicCommandResult Applied { get; } = new(MusicCommandOutcome.Applied);

    /// <summary>
    /// Gets a value that indicates whether the player accepted the command.
    /// </summary>
    public bool IsApplied => Outcome == MusicCommandOutcome.Applied;

    /// <summary>
    /// Creates an unsupported-command result.
    /// </summary>
    /// <param name="reason">A human-readable explanation. Cannot be empty.</param>
    /// <returns>A result whose outcome is <see cref="MusicCommandOutcome.Unsupported"/>.</returns>
    public static MusicCommandResult Unsupported(string reason)
        => NonApplied(MusicCommandOutcome.Unsupported, reason);

    /// <summary>
    /// Creates an unavailable-player result.
    /// </summary>
    /// <param name="reason">A human-readable explanation. Cannot be empty.</param>
    /// <returns>A result whose outcome is <see cref="MusicCommandOutcome.Unavailable"/>.</returns>
    public static MusicCommandResult Unavailable(string reason)
        => NonApplied(MusicCommandOutcome.Unavailable, reason);

    /// <summary>
    /// Creates a rejected-command result.
    /// </summary>
    /// <param name="reason">A human-readable explanation. Cannot be empty.</param>
    /// <returns>A result whose outcome is <see cref="MusicCommandOutcome.Rejected"/>.</returns>
    public static MusicCommandResult Rejected(string reason)
        => NonApplied(MusicCommandOutcome.Rejected, reason);

    /// <summary>
    /// Creates a transport-failure result.
    /// </summary>
    /// <param name="reason">A human-readable explanation. Cannot be empty.</param>
    /// <returns>A result whose outcome is <see cref="MusicCommandOutcome.Failed"/>.</returns>
    public static MusicCommandResult Failed(string reason)
        => NonApplied(MusicCommandOutcome.Failed, reason);

    /// <summary>
    /// Returns this result, filling a fallback reason when a non-applied outcome has none.
    /// </summary>
    /// <returns>This instance, or a copy with a non-empty reason.</returns>
    public MusicCommandResult WithRequiredReason()
    {
        if (Outcome == MusicCommandOutcome.Applied || !string.IsNullOrWhiteSpace(Reason))
        {
            return this;
        }

        return this with { Reason = $"Command ended with {Outcome}." };
    }

    private static MusicCommandResult NonApplied(MusicCommandOutcome outcome, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MusicCommandResult(outcome, reason);
    }
}
