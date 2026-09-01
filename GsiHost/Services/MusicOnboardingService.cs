using System.Text.Json.Nodes;
using Core.Configuration;
using Core.Music;
using GsiHost.Configuration;
using GsiHost.Onboarding;
using GsiHost.Players;
#if WINDOWS
using GsiHost.Players.Smtc;
#endif
using Microsoft.Extensions.Options;

namespace GsiHost.Services;

/// <summary>
/// Implements the onboarding HTTP surface: sessions, explicit SMTC selection, test commands, and presets.
/// </summary>
public sealed class MusicOnboardingService
{
    public const string FlowPreset = "Flow";
    public const string FocusPreset = "Focus";

    private readonly IServiceProvider _services;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<SmtcOptions> _smtcOptions;
    private readonly MusicProviderResolution _provider;
    private readonly MediaPlayerDisplayNameCatalog _displayNames;
    private readonly IMusicPlayer _musicPlayer;
    private readonly MusicLastCommandStore _lastCommand;
    private readonly IControlProfileService _controlProfiles;
    private readonly ILogger<MusicOnboardingService> _logger;
    private readonly SemaphoreSlim _persistMutex = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicOnboardingService"/> class.
    /// </summary>
    public MusicOnboardingService(
        IServiceProvider services,
        IWebHostEnvironment environment,
        IOptions<SmtcOptions> smtcOptions,
        MusicProviderResolution provider,
        MediaPlayerDisplayNameCatalog displayNames,
        IMusicPlayer musicPlayer,
        MusicLastCommandStore lastCommand,
        IControlProfileService controlProfiles,
        ILogger<MusicOnboardingService> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(smtcOptions);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(displayNames);
        ArgumentNullException.ThrowIfNull(musicPlayer);
        ArgumentNullException.ThrowIfNull(lastCommand);
        ArgumentNullException.ThrowIfNull(controlProfiles);
        ArgumentNullException.ThrowIfNull(logger);

        _services = services;
        _environment = environment;
        _smtcOptions = smtcOptions;
        _provider = provider;
        _displayNames = displayNames;
        _musicPlayer = musicPlayer;
        _lastCommand = lastCommand;
        _controlProfiles = controlProfiles;
        _logger = logger;
    }

    /// <summary>
    /// Builds the frozen sessions payload. An empty list is idle, not an error.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The sessions response.</returns>
    public async Task<MusicSessionsResponse> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var selectedAppId = MusicLastCommandStore.ReadSelectedAppId(_smtcOptions);
        var rows = await ListPresentSessionsAsync(cancellationToken).ConfigureAwait(false);
        var sessions = rows
            .Select(row => new MusicSessionDto(
                AppId: row.AppId,
                DisplayName: _displayNames.Resolve(row.AppId),
                PlaybackStatus: row.PlaybackStatus.ToString(),
                Track: new MusicSessionTrackDto(row.TrackTitle, row.TrackArtist),
                Controls: new MusicSessionControlsDto(row.CanPlay, row.CanPause, row.CanNext),
                IsWindowsCurrent: row.IsWindowsCurrent,
                IsSelected: selectedAppId is not null
                    && string.Equals(row.AppId, selectedAppId, StringComparison.Ordinal)))
            .ToArray();

        return new MusicSessionsResponse(_provider.CanonicalName, selectedAppId, sessions);
    }

    /// <summary>
    /// Persists an exact present session id as <c>Music:Smtc:SourceAppUserModelId</c>.
    /// </summary>
    /// <param name="appId">The requested id. Must not be missing or empty.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The HTTP result: 200, 400, or 409. 409 does not persist or command a session.</returns>
    public async Task<IResult> SelectSessionAsync(string? appId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return Results.BadRequest();
        }

        var rows = await ListPresentSessionsAsync(cancellationToken).ConfigureAwait(false);
        var present = rows.Any(row => string.Equals(row.AppId, appId, StringComparison.Ordinal));
        if (!present)
        {
            return Results.Conflict();
        }

        await PersistSelectedAppIdAsync(appId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new SelectMusicSessionResponse(appId));
    }

    /// <summary>
    /// Issues a test pause through <see cref="IMusicPlayer"/> and records it as a test command.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>HTTP 200 with the command outcome. Never 5xx.</returns>
    public Task<IResult> TestPauseAsync(CancellationToken cancellationToken)
        => ExecuteTestAsync(MusicControlCommands.Pause, player => player.PauseAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Issues a test resume through <see cref="IMusicPlayer"/> and records it as a test command.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>HTTP 200 with the command outcome. Never 5xx.</returns>
    public Task<IResult> TestResumeAsync(CancellationToken cancellationToken)
        => ExecuteTestAsync(MusicControlCommands.Resume, player => player.ResumeAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Returns the last recorded test or game command.
    /// </summary>
    /// <returns>The frozen last-command payload.</returns>
    public MusicLastCommandResponse GetLastCommand()
        => _lastCommand.Get();

    /// <summary>
    /// Returns the active Flow/Focus preset name.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>HTTP 200 with <c>Flow</c> or <c>Focus</c>.</returns>
    public async Task<IResult> GetPresetAsync(CancellationToken cancellationToken)
    {
        var config = await _controlProfiles.GetAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new MusicPresetResponse(ResolvePresetName(config)));
    }

    /// <summary>
    /// Sets the active control profile to Flow or Focus via <see cref="IControlProfileService.SaveAsync"/>.
    /// </summary>
    /// <param name="preset">The requested preset name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>HTTP 200 with the resulting name, or 400 for an unknown name.</returns>
    public async Task<IResult> SetPresetAsync(string? preset, CancellationToken cancellationToken)
    {
        var profileId = TryMapPresetToProfileId(preset);
        if (profileId is null)
        {
            return Results.BadRequest();
        }

        var config = await _controlProfiles.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!config.Profiles.Any(profile =>
                string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest();
        }

        await _controlProfiles.SaveAsync(
            config with { ActiveProfileId = profileId },
            cancellationToken).ConfigureAwait(false);

        var saved = await _controlProfiles.GetAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new MusicPresetResponse(ResolvePresetName(saved)));
    }

    private async Task<IResult> ExecuteTestAsync(
        string command,
        Func<IMusicPlayer, Task<MusicCommandResult>> action,
        CancellationToken cancellationToken)
    {
        MusicCommandResult result;
        try
        {
            result = await action(_musicPlayer).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onboarding test {Command} failed.", command);
            result = MusicCommandResult.Failed($"Test {command} failed: {ex.Message}");
        }

        result = result.WithRequiredReason();
        _lastCommand.Record(
            command,
            MusicLastCommandStore.TestSource,
            MusicLastCommandStore.ReadSelectedAppId(_smtcOptions),
            result);

        return Results.Ok(new MusicTestCommandResponse(
            result.Outcome.ToString(),
            result.Outcome == MusicCommandOutcome.Applied ? null : result.Reason));
    }

    private async Task PersistSelectedAppIdAsync(string appId, CancellationToken cancellationToken)
    {
        await _persistMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_environment.ContentRootPath, "appsettings.json");
            JsonObject root;
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            var music = root["Music"] as JsonObject ?? new JsonObject();
            var smtc = music["Smtc"] as JsonObject ?? new JsonObject();
            smtc["SourceAppUserModelId"] = appId;
            music["Smtc"] = smtc;
            root["Music"] = music;

            var json = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);

            var options = _smtcOptions.Value ?? new SmtcOptions();
            options.SourceAppUserModelId = appId;
        }
        finally
        {
            _persistMutex.Release();
        }
    }

    private async Task<IReadOnlyList<SessionRow>> ListPresentSessionsAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        var source = _services.GetService<ISmtcSessionSource>();
        if (source is null)
        {
            return Array.Empty<SessionRow>();
        }

        try
        {
            var snapshots = await source.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            return snapshots
                .Select(session => new SessionRow(
                    session.SourceAppUserModelId,
                    session.PlaybackStatus,
                    session.Track?.Title,
                    session.Track?.Artist,
                    session.IsPlayEnabled,
                    session.IsPauseEnabled,
                    session.IsNextEnabled,
                    session.IsCurrentSession))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTC session source could not be enumerated; returning an empty sessions list.");
            return Array.Empty<SessionRow>();
        }
#else
        _ = cancellationToken;
        _ = _services;
        return Array.Empty<SessionRow>();
#endif
    }

    private static string ResolvePresetName(ConsoleControlProfilesConfig config)
    {
        var active = config.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, config.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? config.Profiles.FirstOrDefault();

        if (active is not null)
        {
            if (string.Equals(active.Id, JsonControlProfileService.FocusProfileId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.Name, FocusPreset, StringComparison.OrdinalIgnoreCase))
            {
                return FocusPreset;
            }

            if (string.Equals(active.Id, JsonControlProfileService.FlowProfileId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(active.Name, FlowPreset, StringComparison.OrdinalIgnoreCase))
            {
                return FlowPreset;
            }
        }

        return FlowPreset;
    }

    private static string? TryMapPresetToProfileId(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return null;
        }

        var trimmed = preset.Trim();
        if (string.Equals(trimmed, FlowPreset, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, JsonControlProfileService.FlowProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return JsonControlProfileService.FlowProfileId;
        }

        if (string.Equals(trimmed, FocusPreset, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, JsonControlProfileService.FocusProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return JsonControlProfileService.FocusProfileId;
        }

        return null;
    }

    private sealed record SessionRow(
        string AppId,
        PlaybackStatus PlaybackStatus,
        string? TrackTitle,
        string? TrackArtist,
        bool CanPlay,
        bool CanPause,
        bool CanNext,
        bool IsWindowsCurrent);
}
