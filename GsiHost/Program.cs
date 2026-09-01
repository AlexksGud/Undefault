using Core.Actions;
using Core.Configuration;
using Core.Diff;
using Core.Models;
using Core.Music;
using Core.Rules;
using Core.Services;
using Core.Stores;
using Core.Adapters;
using GsiHost.Adapters;
using GsiHost.Configuration;
using GsiHost.Dtos;
using GsiHost.Endpoints;
using GsiHost.Mapping;
using GsiHost.Mapping.Modules;
using GsiHost.Players;
#if WINDOWS
using GsiHost.Players.Smtc;
#endif
using GsiHost.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var consoleLaunchSettings = ConsoleLaunchBootstrap.Apply(builder, args);
var resolvedRuntime = RuntimeOptions.From(builder.Configuration);

builder.Services.AddSingleton<GsiSnapshotMapper>();
builder.Services.AddSingleton<IGameAdapter<GsiPayloadDto>, Cs2GameAdapter>();
// Per-title routing registry (UND-40 / docs/multi-adapter-routing.md). Today only CS2 is
// registered; adding a second title is a new registration + a new typed endpoint, no
// change to CS2 wiring.
builder.Services.AddSingleton(new GameAdapterRegistration(
    TitleId: "cs2",
    AppId: 730,
    EndpointPath: "/gsi",
    Description: "Counter-Strike 2 Game State Integration"));
// UND-80: Dota 2 event logging only — no IGameAdapter<T>/rules-engine wiring yet (UND-45).
builder.Services.AddSingleton(new GameAdapterRegistration(
    TitleId: "dota2",
    AppId: 570,
    EndpointPath: "/gsi/dota",
    Description: "Dota 2 Game State Integration (event logging only, no rules engine yet)"));
builder.Services.AddSingleton<IGameAdapterRouter, GameAdapterRouter>();
builder.Services.AddSingleton<SnapshotDiffer>();
builder.Services.AddSingleton<EventDetector>(sp =>
    new EventDetector(sp.GetRequiredService<IOptions<EventDetectorOptions>>().Value));
builder.Services.AddSingleton<ISnapshotStore, InMemorySnapshotStore>();
builder.Services.AddSingleton<IEventAction, LogEventAction>();
builder.Services.AddSingleton<IEventAction, MusicControlProfileAction>();
builder.Services.AddSingleton<IEventAction>(sp =>
    new MusicControlProfileAction(
        sp.GetRequiredService<IMusicPlaybackControl>(),
        sp.GetRequiredService<IControlProfileService>(),
        sp.GetRequiredService<ILogger<MusicControlProfileAction>>(),
        MusicControlProfileAction.LegacySpotifyKey));
builder.Services.AddSingleton<IRulesEngine, RulesEngine>();
builder.Services.AddSingleton<IMusicOrchestrationFacade, ShadowMusicOrchestrationFacade>();
builder.Services.AddSingleton<IShadowMusicSnapshotSink, InMemoryShadowMusicSnapshotSink>();
builder.Services.AddSingleton<GsiProcessingService>();
builder.Services.AddSingleton<TimelineCaptureService>();
builder.Services.AddSingleton<DotaGsiLoggingService>();
builder.Services.AddSingleton<IPlaybackEventRecorder, PlaybackEventRecorder>();
// Always register so /gsi/reset can clear the observer baseline in any runtime mode.
// The background poll loop only runs in intent_capture.
builder.Services.AddSingleton<PlaybackStateObserver>();
if (resolvedRuntime.IsIntentCapture)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PlaybackStateObserver>());
}
builder.Services.AddSingleton<AppStateService>();
builder.Services.AddSingleton<IAppStateService>(sp => sp.GetRequiredService<AppStateService>());
builder.Services.AddSingleton<IGsiResetService, GsiResetService>();
builder.Services.AddSingleton<IConfigurationService, AppSettingsConfigurationService>();
builder.Services.AddSingleton<IControlProfileService, JsonControlProfileService>();
builder.Services.AddSingleton<IProfileService, JsonProfileService>();
builder.Services.AddSingleton<ICs2SetupService, Cs2SetupService>();
builder.Services.AddSingleton<ISnapshotModuleMapper, VitalsModuleMapper>();
builder.Services.AddSingleton<ISnapshotModuleMapper, PositionModuleMapper>();
builder.Services.AddSingleton<ISnapshotModuleMapper, CombatModuleMapper>();
builder.Services.AddSingleton<ISnapshotModuleMapper, RoundModuleMapper>();

var resolvedMusicProvider = MusicProviderResolver.Resolve(builder.Configuration);
MusicProviderResolver.EnsurePlayerRegistered(resolvedMusicProvider);
builder.Services.AddSingleton(resolvedMusicProvider);

BuildMusicPlayer(builder, resolvedMusicProvider.CanonicalName);
builder.Services.AddSingleton<MediaPlayerDisplayNameCatalog>();
builder.Services.AddSingleton<MusicLastCommandStore>();
builder.Services.AddSingleton<MusicOnboardingService>();
builder.Services.AddSingleton<MusicPlaybackControlCoordinator>();
builder.Services.AddSingleton<IMusicPlaybackControl>(sp =>
    new RecordingMusicPlaybackControl(
        sp.GetRequiredService<MusicPlaybackControlCoordinator>(),
        sp.GetRequiredService<MusicLastCommandStore>(),
        sp.GetRequiredService<IOptions<SmtcOptions>>()));

builder.Services.Configure<RulesEngineOptions>(
    builder.Configuration.GetSection("RulesEngine"));
builder.Services.Configure<EventDetectorOptions>(
    builder.Configuration.GetSection("EventDetector"));
builder.Services.Configure<VolumeDuckOptions>(
    builder.Configuration.GetSection("SpotifyVolumeDuck"));
builder.Services.Configure<GsiOptions>(
    builder.Configuration.GetSection(GsiOptions.SectionName));
builder.Services.Configure<RuntimeOptions>(
    builder.Configuration.GetSection(RuntimeOptions.SectionName));
builder.Services.Configure<TimelineOptions>(
    builder.Configuration.GetSection(TimelineOptions.SectionName));
builder.Services.Configure<PlaybackObserverOptions>(
    builder.Configuration.GetSection(PlaybackObserverOptions.SectionName));
builder.Services.Configure<MusicOrchestrationOptions>(
    builder.Configuration.GetSection(MusicOrchestrationOptions.SectionName));
builder.Services.Configure<MusicProviderOptions>(
    builder.Configuration.GetSection(MusicProviderOptions.SectionName));
builder.Services.Configure<TauonOptions>(
    builder.Configuration.GetSection(TauonOptions.SectionName));
builder.Services.Configure<SmtcOptions>(
    builder.Configuration.GetSection(SmtcOptions.SectionName));

var app = builder.Build();
app.Logger.LogInformation("Music provider: {Provider}", resolvedMusicProvider.CanonicalName);

if (!consoleLaunchSettings.SkipCs2Setup)
{
    await EnsureCs2SetupAsync(app);
}

await WriteConsoleStartupChecklistAsync(app, consoleLaunchSettings);

app.UseStaticFiles();
app.MapMusicOnboarding();

app.MapGet("/", () => "UndefaultIt GSI Host");

app.MapPost("/gsi", async (
    GsiPayloadDto payload,
    GsiProcessingService processor,
    CancellationToken cancellationToken) =>
{
    var events = await processor.ProcessAsync(payload, cancellationToken);
    return Results.Ok(new { events = events.Count });
});

app.MapPost("/gsi/dota", (DotaGsiPayloadDto payload, DotaGsiLoggingService dotaLogging) =>
{
    dotaLogging.Process(payload);
    return Results.Ok();
});

app.MapPost("/gsi/reset", (IOptions<GsiOptions> gsiOptions, IGsiResetService resetService) =>
{
    if (!gsiOptions.Value.AllowReset)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    resetService.Reset();
    return Results.NoContent();
});

app.MapGet("/status", async (
    IAppStateService appStateService,
    IMusicPlayer musicPlayer,
    MusicProviderResolution musicProviderResolution,
    CancellationToken cancellationToken) =>
{
    var status = await appStateService.GetCurrentStatusAsync(cancellationToken);
    MusicPlaybackState? musicState = null;
    try
    {
        musicState = await musicPlayer.GetStateAsync(cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception)
    {
        // Fail-soft: GSI fields still return when the player cannot be read.
    }

    return Results.Ok(new
    {
        status.GsiStatus,
        status.LastSnapshotAt,
        status.Game,
        status.LastEvent,
        musicProvider = musicProviderResolution.CanonicalName,
        musicPlayerAvailable = musicState is not null,
        playbackState = musicState?.Status.ToString() ?? "Unavailable",
        currentTrack = musicState?.Track
    });
});

app.MapGet("/events", (AppStateService appStateService) => Results.Ok((object?)appStateService.GetRecentEvents()));

if (resolvedRuntime.IsIntentCapture)
{
    app.MapGet("/timeline", (TimelineCaptureService timeline) => Results.Ok((object?)timeline.GetRecentEntries()));

    app.MapGet("/timeline/episodes", (TimelineCaptureService timeline) => Results.Ok((object?)timeline.GetIntentEpisodes()));
}

app.MapGet("/config", async (IConfigurationService configService, CancellationToken cancellationToken) =>
{
    var config = await configService.GetAsync(cancellationToken);
    return Results.Ok(config);
});

app.MapPut("/config", async (SystemConfig config, IConfigurationService configService, CancellationToken cancellationToken) =>
{
    await configService.SaveAsync(config, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/control-profiles", async (IControlProfileService controlProfileService, CancellationToken cancellationToken) =>
{
    var profiles = await controlProfileService.GetAsync(cancellationToken);
    return Results.Ok(profiles);
});

app.MapPut("/control-profiles", async (
    ConsoleControlProfilesConfig profiles,
    IControlProfileService controlProfileService,
    CancellationToken cancellationToken) =>
{
    await controlProfileService.SaveAsync(profiles, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/setup/cs2/status", async (ICs2SetupService setupService, CancellationToken cancellationToken) =>
{
    var status = await setupService.GetStatusAsync(cancellationToken);
    return Results.Ok(status);
});

app.MapPost("/setup/cs2/install", async (ICs2SetupService setupService, CancellationToken cancellationToken) =>
{
    var result = await setupService.InstallAsync(cancellationToken);
    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/profiles", async (IProfileService profileService, CancellationToken cancellationToken) =>
{
    var profiles = await profileService.GetAsync(cancellationToken);
    return Results.Ok(profiles);
});

app.MapPut("/profiles", async (MusicProfilesConfig profiles, IProfileService profileService, CancellationToken cancellationToken) =>
{
    await profileService.SaveAsync(profiles, cancellationToken);
    return Results.NoContent();
});

// Debug-only surface for the shadow facade; intentionally mapped in both runtime modes.
app.MapGet("/diagnostics/music-shadow", (IShadowMusicSnapshotSink sink) =>
{
    return Results.Ok(new
    {
        latest = sink.Latest,
        recent = sink.Recent()
    });
});

app.MapGet("/diagnostics/adapters", (IGameAdapterRouter router) =>
{
    return Results.Ok(new { adapters = router.Registrations });
});

// AppStateService subscribes to GsiProcessingService.Processed in its ctor. /gsi does not
// resolve it, so without eager creation the recent-events ring would stay empty until some
// other endpoint (or reset) touched the singleton.
_ = app.Services.GetRequiredService<AppStateService>();
_ = app.Services.GetRequiredService<TimelineCaptureService>();

app.Run();

void BuildMusicPlayer(WebApplicationBuilder webApplicationBuilder, string provider)
{
    if (string.Equals(provider, MusicProviderOptions.Mock, StringComparison.OrdinalIgnoreCase))
    {
        webApplicationBuilder.Services.AddSingleton<IMusicPlayer, MockMusicPlayer>();
        return;
    }

    if (string.Equals(provider, MusicProviderOptions.Tauon, StringComparison.OrdinalIgnoreCase))
    {
        webApplicationBuilder.Services.AddHttpClient(TauonMusicPlayer.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<TauonOptions>>().Value ?? new TauonOptions();
            TauonMusicPlayer.ConfigureClient(client, opts);
        });
        webApplicationBuilder.Services.AddSingleton<IMusicPlayer, TauonMusicPlayer>();
        return;
    }

#if WINDOWS
    if (string.Equals(provider, MusicProviderOptions.Smtc, StringComparison.OrdinalIgnoreCase))
    {
        webApplicationBuilder.Services.AddSingleton<ISmtcSessionSource, WindowsMediaControllerSessionSource>();
        webApplicationBuilder.Services.AddSingleton<IMusicPlayer, SmtcMusicPlayer>();
        return;
    }
#endif

    throw new InvalidOperationException(
        $"Music:Provider '{provider}' is not registered.");
}

static async Task EnsureCs2SetupAsync(WebApplication app)
{
    try
    {
        var setupService = app.Services.GetRequiredService<ICs2SetupService>();
        var result = await setupService.EnsureInstalledAsync();

        if (result.Success)
        {
            app.Logger.LogInformation(
                "CS2 GSI setup ready at {CfgPath} (updated={WasUpdated}, uri={GsiUri})",
                result.CfgPath,
                result.WasUpdated,
                result.GsiUri);
            app.Logger.LogInformation("Console control profile mode active. Edit control-profiles.json to change music behavior.");
            return;
        }

        app.Logger.LogWarning("CS2 GSI setup not ready: {Error}", result.Error);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to auto-configure CS2 GSI");
    }
}

static async Task WriteConsoleStartupChecklistAsync(
    WebApplication app,
    ConsoleLaunchSettings consoleLaunchSettings)
{
    var setupService = app.Services.GetRequiredService<ICs2SetupService>();
    var controlProfileService = app.Services.GetRequiredService<IControlProfileService>();

    Cs2SetupStatus? cs2Status = null;
    if (!consoleLaunchSettings.SkipCs2Setup)
    {
        try
        {
            cs2Status = await setupService.GetStatusAsync();
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to read CS2 GSI setup status");
        }
    }

    ConsoleControlProfilesConfig? controlProfiles = null;
    try
    {
        controlProfiles = await controlProfileService.GetAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to read console control profiles");
    }

    var activeControlProfile = controlProfiles is null
        ? null
        : controlProfiles.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, controlProfiles.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? controlProfiles.Profiles.FirstOrDefault();

    var musicProvider = app.Services.GetRequiredService<MusicProviderResolution>().CanonicalName;

    Console.WriteLine();
    Console.WriteLine("UndefaultIt console startup");
    Console.WriteLine($"- Music provider: {musicProvider}");
    Console.WriteLine($"- Quick launch mode: {(consoleLaunchSettings.IsQuickLaunch ? "yes" : "no")}");
    Console.WriteLine($"- MVP launch (--mvp): {(consoleLaunchSettings.IsMvpLaunch ? "yes — intent_capture (observe only; music.control_profile is not executed)" : "no")}");
    Console.WriteLine($"- CS2 setup: {(consoleLaunchSettings.SkipCs2Setup ? "skipped" : "attempted")}");
    Console.WriteLine($"- CS2 GSI target URL: {cs2Status?.GsiUri ?? $"{consoleLaunchSettings.GsiBaseUrl}/gsi"}");
    Console.WriteLine($"- CS2 cfg ready: {(consoleLaunchSettings.SkipCs2Setup ? "skipped" : (cs2Status?.IsReady == true ? "yes" : "no"))}{(consoleLaunchSettings.SkipCs2Setup ? string.Empty : FormatSuffix(cs2Status?.CfgPath))}");
    Console.WriteLine($"- Dota 2 GSI target URL: {consoleLaunchSettings.GsiBaseUrl}/gsi/dota (event logging only — manual cfg setup, see README)");
    Console.WriteLine($"- Control profile file: {controlProfileService.FilePath}");
    Console.WriteLine($"- Active control profile: {activeControlProfile?.Name ?? "none"}{FormatSuffix(activeControlProfile?.Id)}");
    var roundStartCommand = activeControlProfile?.FindRule(EventKeys.RoundStart)?.Command ?? "none";
    var deathCommand = activeControlProfile?.FindRule(EventKeys.Death)?.Command ?? "none";
    Console.WriteLine($"- Active control profile commands: round_start={roundStartCommand}, death={deathCommand}");
    if (!string.Equals(roundStartCommand, MusicControlCommands.Resume, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(deathCommand, MusicControlCommands.Pause, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("- WARNING: existing control-profiles.json is not the product default (round_start=resume, death=pause) and was not auto-migrated.");
        app.Logger.LogWarning(
            "Active control profile '{ProfileId}' uses round_start={RoundStart}, death={Death}; product default is resume/pause. Existing control-profiles.json is not migrated.",
            activeControlProfile?.Id,
            roundStartCommand,
            deathCommand);
    }
    Console.WriteLine("- Edit control-profiles.json for pause/resume/duck behavior.");
    Console.WriteLine("- Open /status for GSI + IMusicPlayer state.");
    Console.WriteLine("- Tauon smoke (PIVOT-9): default launch without --mvp; watch Tauon and Playback pause/resume logs.");
    Console.WriteLine();
}

static string FormatSuffix(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? string.Empty : $" ({value})";
}
