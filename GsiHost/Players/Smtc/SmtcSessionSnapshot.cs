#if WINDOWS
using Core.Music;

namespace GsiHost.Players.Smtc;

/// <summary>
/// A value-copy of one SMTC session. Identifiers are strings, not WinRT object identity.
/// </summary>
/// <param name="SourceAppUserModelId">
/// The exact session id copied from the enumerated list.
/// </param>
/// <param name="PlaybackStatus">The playback status read at snapshot time.</param>
/// <param name="Track">Track metadata when readable; otherwise <see langword="null"/>.</param>
/// <param name="IsPlayEnabled">Whether play is enabled on this session at snapshot time.</param>
/// <param name="IsPauseEnabled">Whether pause is enabled on this session at snapshot time.</param>
/// <param name="IsNextEnabled">Whether skip-next is enabled on this session at snapshot time.</param>
/// <param name="IsPreviousEnabled">Whether skip-previous is enabled on this session at snapshot time.</param>
/// <param name="IsCurrentSession">
/// <see langword="true"/> when this session's id matches the Windows current-session hint.
/// Never used as a command target.
/// </param>
public sealed record SmtcSessionSnapshot(
    string SourceAppUserModelId,
    PlaybackStatus PlaybackStatus,
    MusicTrack? Track,
    bool IsPlayEnabled,
    bool IsPauseEnabled,
    bool IsNextEnabled,
    bool IsPreviousEnabled,
    bool IsCurrentSession);
#endif
