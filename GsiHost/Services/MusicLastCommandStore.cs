using Core.Music;
using GsiHost.Onboarding;
using GsiHost.Players;
using Microsoft.Extensions.Options;

namespace GsiHost.Services;

/// <summary>
/// In-memory last music command for the onboarding HTTP surface.
/// </summary>
public sealed class MusicLastCommandStore
{
    public const string TestSource = "test";
    public const string GameSource = "game";

    private readonly object _sync = new();
    private MusicLastCommandResponse _latest = Empty();

    /// <summary>
    /// Records a command result for later <c>GET /music/last-command</c>.
    /// </summary>
    /// <param name="command">One of <c>pause</c>, <c>resume</c>, <c>next</c>, <c>previous</c>, <c>duck</c>, <c>restore_volume</c>.</param>
    /// <param name="source"><see cref="TestSource"/> or <see cref="GameSource"/>.</param>
    /// <param name="targetAppId">The selected SMTC id, or <see langword="null"/>.</param>
    /// <param name="result">The player or coordinator result.</param>
    public void Record(string command, string source, string? targetAppId, MusicCommandResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(result);

        var normalized = result.WithRequiredReason();
        var snapshot = new MusicLastCommandResponse(
            Command: command,
            Source: source,
            TargetAppId: string.IsNullOrWhiteSpace(targetAppId) ? null : targetAppId,
            Outcome: normalized.Outcome.ToString(),
            Reason: normalized.Outcome == MusicCommandOutcome.Applied ? null : normalized.Reason,
            AtUtc: DateTimeOffset.UtcNow.ToString("o"));

        lock (_sync)
        {
            _latest = snapshot;
        }
    }

    /// <summary>
    /// Gets the most recent recorded command, or an all-null payload when none exists.
    /// </summary>
    /// <returns>The frozen last-command shape.</returns>
    public MusicLastCommandResponse Get()
    {
        lock (_sync)
        {
            return _latest;
        }
    }

    internal static string? ReadSelectedAppId(IOptions<SmtcOptions> options)
    {
        var selected = options.Value?.SourceAppUserModelId;
        return string.IsNullOrWhiteSpace(selected) ? null : selected;
    }

    private static MusicLastCommandResponse Empty()
        => new(null, null, null, null, null, null);
}
