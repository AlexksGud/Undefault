using Core.Music;

namespace GsiHost.Diagnostics;

/// <summary>
/// Immutable view of the local go/no-go counters.
/// </summary>
/// <param name="FilePath">The host-local JSON path.</param>
/// <param name="HostStartUtc">The UTC start time of this host process.</param>
/// <param name="FirstGameAppliedUtc">The first game-triggered <see cref="MusicCommandOutcome.Applied"/>, or <see langword="null"/>.</param>
/// <param name="HostStartUtcAtFirstGameApplied">
/// The host-process start that was current when <paramref name="FirstGameAppliedUtc"/> was recorded, or <see langword="null"/>.
/// </param>
/// <param name="LastGameAppliedUtc">The most recent game-triggered Applied, or <see langword="null"/>.</param>
/// <param name="GameSessionsWithApplied">The persisted count of game sessions that recorded at least one Applied.</param>
/// <param name="GameOutcomes">Per-<see cref="MusicCommandOutcome"/> totals for game-triggered commands.</param>
/// <param name="TestOutcomes">Per-<see cref="MusicCommandOutcome"/> totals for test-source commands.</param>
/// <param name="LastCommand">The most recently recorded command, or <see langword="null"/>.</param>
public sealed record LocalGoNoGoCounterSnapshot(
    string FilePath,
    DateTimeOffset HostStartUtc,
    DateTimeOffset? FirstGameAppliedUtc,
    DateTimeOffset? HostStartUtcAtFirstGameApplied,
    DateTimeOffset? LastGameAppliedUtc,
    int GameSessionsWithApplied,
    IReadOnlyDictionary<MusicCommandOutcome, int> GameOutcomes,
    IReadOnlyDictionary<MusicCommandOutcome, int> TestOutcomes,
    LocalGoNoGoRecordedCommand? LastCommand);

/// <summary>
/// One recorded command on the local go/no-go counter stream.
/// </summary>
/// <param name="Command">One of <c>pause</c>, <c>resume</c>, <c>next</c>, <c>previous</c>, <c>duck</c>, <c>restore_volume</c>.</param>
/// <param name="Source"><c>game</c> or <c>test</c>.</param>
/// <param name="TargetAppId">The selected <c>Music:Smtc:SourceAppUserModelId</c> at record time, or <see langword="null"/>.</param>
/// <param name="Outcome">One of the enumeration values that specifies how the command ended.</param>
/// <param name="AtUtc">The UTC timestamp of the record.</param>
/// <remarks>
/// Track names and listening history are not stored. <paramref name="TargetAppId"/> is copied from the last-command store field.
/// </remarks>
public sealed record LocalGoNoGoRecordedCommand(
    string Command,
    string Source,
    string? TargetAppId,
    MusicCommandOutcome Outcome,
    DateTimeOffset AtUtc);
