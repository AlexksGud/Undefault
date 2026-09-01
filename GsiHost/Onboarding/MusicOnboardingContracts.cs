using System.Text.Json.Serialization;

namespace GsiHost.Onboarding;

/// <summary>
/// Frozen <c>GET /music/sessions</c> payload.
/// </summary>
/// <param name="Provider">Canonical <c>Music:Provider</c> name (<c>Tauon</c>, <c>Smtc</c>, or <c>Mock</c>).</param>
/// <param name="SelectedAppId">The persisted exact <c>SourceAppUserModelId</c>, or <see langword="null"/> when none is selected.</param>
/// <param name="Sessions">Currently present SMTC sessions. An empty array is idle, not an error.</param>
public sealed record MusicSessionsResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("selectedAppId")] string? SelectedAppId,
    [property: JsonPropertyName("sessions")] IReadOnlyList<MusicSessionDto> Sessions);

/// <summary>
/// One row in <see cref="MusicSessionsResponse.Sessions"/>.
/// </summary>
/// <param name="AppId">Exact <c>SourceAppUserModelId</c>.</param>
/// <param name="DisplayName">Friendly name from the vendored snapshot, or the raw app id. Never empty.</param>
/// <param name="PlaybackStatus"><c>Playing</c>, <c>Paused</c>, <c>Stopped</c>, or <c>Unknown</c>.</param>
/// <param name="Track">Track title and artist when readable.</param>
/// <param name="Controls">Transport capability flags from the session snapshot.</param>
/// <param name="IsWindowsCurrent">Whether Windows currently reports this session as focused. Never used as a command target.</param>
/// <param name="IsSelected">Whether <paramref name="AppId"/> equals the persisted selection (ordinal).</param>
public sealed record MusicSessionDto(
    [property: JsonPropertyName("appId")] string AppId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("playbackStatus")] string PlaybackStatus,
    [property: JsonPropertyName("track")] MusicSessionTrackDto Track,
    [property: JsonPropertyName("controls")] MusicSessionControlsDto Controls,
    [property: JsonPropertyName("isWindowsCurrent")] bool IsWindowsCurrent,
    [property: JsonPropertyName("isSelected")] bool IsSelected);

/// <summary>
/// Track fields on a session row. Missing values are JSON <c>null</c>.
/// </summary>
/// <param name="Title">Track title, or <see langword="null"/>.</param>
/// <param name="Artist">Artist name, or <see langword="null"/>.</param>
public sealed record MusicSessionTrackDto(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("artist")] string? Artist);

/// <summary>
/// Per-session transport flags at snapshot time.
/// </summary>
/// <param name="CanPlay">Whether play is enabled.</param>
/// <param name="CanPause">Whether pause is enabled.</param>
/// <param name="CanNext">Whether skip-next is enabled.</param>
public sealed record MusicSessionControlsDto(
    [property: JsonPropertyName("canPlay")] bool CanPlay,
    [property: JsonPropertyName("canPause")] bool CanPause,
    [property: JsonPropertyName("canNext")] bool CanNext);

/// <summary>
/// Body for <c>POST /music/session</c>.
/// </summary>
/// <param name="AppId">Exact <c>SourceAppUserModelId</c> to persist. Missing or empty is HTTP 400.</param>
public sealed record SelectMusicSessionRequest(
    [property: JsonPropertyName("appId")] string? AppId);

/// <summary>
/// Frozen <c>POST /music/session</c> success payload.
/// </summary>
/// <param name="SelectedAppId">The persisted exact id.</param>
public sealed record SelectMusicSessionResponse(
    [property: JsonPropertyName("selectedAppId")] string SelectedAppId);

/// <summary>
/// Frozen test-command payload. HTTP status is 200 even when <paramref name="Outcome"/> is not <c>Applied</c>.
/// </summary>
/// <param name="Outcome"><c>Applied</c>, <c>Unsupported</c>, <c>Unavailable</c>, <c>Rejected</c>, or <c>Failed</c>.</param>
/// <param name="Reason">Explanation when the command was not applied; otherwise <see langword="null"/>.</param>
public sealed record MusicTestCommandResponse(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>
/// Frozen <c>GET /music/last-command</c> payload. All fields are <see langword="null"/> before any command.
/// </summary>
/// <param name="Command"><c>pause</c>, <c>resume</c>, <c>next</c>, <c>previous</c>, <c>duck</c>, <c>restore_volume</c>, or <see langword="null"/>.</param>
/// <param name="Source"><c>test</c>, <c>game</c>, or <see langword="null"/>.</param>
/// <param name="TargetAppId">The selected SMTC id at record time, when one is set.</param>
/// <param name="Outcome">The <c>MusicCommandOutcome</c> name, or <see langword="null"/>.</param>
/// <param name="Reason">Non-applied reason, or <see langword="null"/>.</param>
/// <param name="AtUtc">ISO-8601 timestamp, or <see langword="null"/>.</param>
public sealed record MusicLastCommandResponse(
    [property: JsonPropertyName("command")] string? Command,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("targetAppId")] string? TargetAppId,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("atUtc")] string? AtUtc);

/// <summary>
/// Frozen preset payload for <c>GET</c> and <c>POST /music/preset</c>.
/// </summary>
/// <param name="Preset"><c>Flow</c> or <c>Focus</c>.</param>
public sealed record MusicPresetResponse(
    [property: JsonPropertyName("preset")] string Preset);

/// <summary>
/// Body for <c>POST /music/preset</c>.
/// </summary>
/// <param name="Preset"><c>Flow</c> or <c>Focus</c>. Unknown names are HTTP 400.</param>
public sealed record MusicPresetRequest(
    [property: JsonPropertyName("preset")] string? Preset);
