using Core.Actions;
using Core.Actions.Spotify;
using Core.Configuration;
using Core.Diff;
using Core.Models;
using Core.Music;
using Core.Rules;
using Core.Services;
using Core.Spotify;
using Core.Stores;
using Core.Adapters;
using GsiHost.Adapters;
using GsiHost.Configuration;
using GsiHost.Dtos;
using GsiHost.Mapping;
using GsiHost.Mapping.Modules;
using GsiHost.Players;
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
builder.Services.AddSingleton<IEventAction, SpotifyControlProfileAction>();
builder.Services.AddSingleton<IEventAction, SpotifyProfileAction>();
builder.Services.AddSingleton<IEventAction, SpotifyVolumeDuckAction>();
builder.Services.AddSingleton<IPlaybackPolicy, NoOpPlaybackPolicy>();
builder.Services.AddSingleton<ISmartTrackStartService, JsonSmartTrackStartService>();
builder.Services.AddSingleton<ITrackPlaybackService, TrackPlaybackService>();
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

BuildMusicPlayer(builder);
BuildSpotify(builder);

builder.Services.AddSingleton<SpotifyPlaybackControlCoordinator>();
builder.Services.AddSingleton<IMusicPlaybackControl>(sp =>
    sp.GetRequiredService<SpotifyPlaybackControlCoordinator>());
builder.Services.AddSingleton<ISpotifyPlaybackControl>(sp =>
    sp.GetRequiredService<SpotifyPlaybackControlCoordinator>());

builder.Services.Configure<RulesEngineOptions>(
    builder.Configuration.GetSection("RulesEngine"));
builder.Services.Configure<EventDetectorOptions>(
    builder.Configuration.GetSection("EventDetector"));
builder.Services.Configure<SpotifyClientOptions>(
    builder.Configuration.GetSection("Spotify"));
builder.Services.Configure<VolumeDuckOptions>(
    builder.Configuration.GetSection("SpotifyVolumeDuck"));
builder.Services.Configure<SmartTrackStartOptions>(
    builder.Configuration.GetSection("SmartTrackStart"));
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

var app = builder.Build();

var resolvedMusicProvider = app.Configuration["Music:Provider"] ?? "Tauon";
if (!string.Equals(resolvedMusicProvider, "Tauon", StringComparison.OrdinalIgnoreCase)
    && !string.Equals(resolvedMusicProvider, "Mock", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogWarning(
        "Music:Provider '{Provider}' is unknown; using Tauon.",
        resolvedMusicProvider);
}

if (consoleLaunchSettings.LegacyClientSecretEnvVarPresent)
{
    // UND-47: emit a one-time DEBUG line so testers know the env var is no longer
    // needed. The value itself is never logged.
    app.Logger.LogDebug(
        "CLIENT_SECRET environment variable is set but is ignored — Spotify OAuth uses PKCE and does not require a client secret.");
}

if (!consoleLaunchSettings.SkipCs2Setup)
{
    await EnsureCs2SetupAsync(app);
}

if (!consoleLaunchSettings.SkipSmartTrackWarmup)
{
    await WarmSmartTrackStartAsync(app);
}

var authorizationUrl = consoleLaunchSettings.HasSpotifyCredentials
    ? LogSpotifyAuthorizationUrl(app)
    : null;
await WriteConsoleStartupChecklistAsync(app, consoleLaunchSettings, authorizationUrl);

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
    IConfiguration configuration,
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
        // Fail-soft: GSI fields still return. PlaybackState is leftover Spotify until this overlay.
    }

    return Results.Ok(new
    {
        status.GsiStatus,
        status.LastSnapshotAt,
        status.Game,
        status.LastEvent,
        leftoverSpotifyStatus = status.SpotifyStatus,
        musicProvider = configuration["Music:Provider"] ?? "Tauon",
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

app.MapGet("/spotify/status", async (IServiceProvider services, CancellationToken cancellationToken) =>
{
    var spotifyClient = services.GetRequiredService<ISpotifyClient>();
    var oauthService = services.GetService<SpotifyOAuthService>();
    var tokenStorage = services.GetService<ITokenStorage>();

    bool isAuthenticated;
    try
    {
        isAuthenticated = await spotifyClient.IsAuthenticatedAsync(cancellationToken);
    }
    catch
    {
        isAuthenticated = false;
    }

    var accessToken = tokenStorage is null
        ? null
        : await tokenStorage.GetAccessTokenAsync(cancellationToken);
    var expiresAt = tokenStorage is null
        ? null
        : await tokenStorage.GetExpiresAtAsync(cancellationToken);

    return Results.Ok(new
    {
        UseMockSpotify = oauthService is null || tokenStorage is null,
        HasClientCredentials = oauthService?.HasClientCredentials ?? false,
        RedirectUri = oauthService?.RedirectUri,
        IsAuthenticated = isAuthenticated,
        HasAccessToken = !string.IsNullOrWhiteSpace(accessToken),
        ExpiresAt = expiresAt
    });
});

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

app.MapGet("/spotify/authorize", (IServiceProvider services) =>
{
    var oauthService = services.GetService<SpotifyOAuthService>();
    if (oauthService is null)
    {
        return Results.BadRequest("Spotify OAuth is unavailable in mock mode.");
    }

    var state = SpotifyOAuthService.CreateState();
    var url = oauthService.GetAuthorizationUrl(state);
    return Results.Ok(new { url, state });
});

app.MapGet("/callback", async (
    string? code,
    string? state,
    IServiceProvider services,
    CancellationToken cancellationToken) =>
{
    return await HandleSpotifyCallbackAsync(code, state, services, cancellationToken);
});

app.MapGet("/spotify/callback", async (
    string? code,
    string? state,
    IServiceProvider services,
    CancellationToken cancellationToken) =>
{
    return await HandleSpotifyCallbackAsync(code, state, services, cancellationToken);
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

void BuildMusicPlayer(WebApplicationBuilder webApplicationBuilder)
{
    var provider = webApplicationBuilder.Configuration["Music:Provider"] ?? "Tauon";
    if (string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase))
    {
        webApplicationBuilder.Services.AddSingleton<IMusicPlayer, MockMusicPlayer>();
        return;
    }

    // Unknown values fall through to Tauon (logged after the host is built).
    webApplicationBuilder.Services.AddHttpClient(TauonMusicPlayer.HttpClientName, (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<TauonOptions>>().Value ?? new TauonOptions();
        TauonMusicPlayer.ConfigureClient(client, opts);
    });
    webApplicationBuilder.Services.AddSingleton<IMusicPlayer, TauonMusicPlayer>();
}

void BuildSpotify(WebApplicationBuilder webApplicationBuilder)
{
    var useMock = webApplicationBuilder.Configuration.GetValue<bool>("UseMockSpotify");

    if (useMock)
    {
        webApplicationBuilder.Services.AddSingleton<ISpotifyClient, MockSpotifyClient>();
        return;
    }

    // Spotify services
    webApplicationBuilder.Services.AddHttpClient("SpotifyApi");
    webApplicationBuilder.Services.AddHttpClient("SpotifyOAuth");
    webApplicationBuilder.Services.AddSingleton<ITokenStorage, InMemoryTokenStorage>();
    webApplicationBuilder.Services.AddSingleton<SpotifyOAuthService>();
    webApplicationBuilder.Services.AddSingleton<ISpotifyClient, SpotifyClient>();

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

static string? LogSpotifyAuthorizationUrl(WebApplication app)
{
    var oauthService = app.Services.GetService<SpotifyOAuthService>();
    if (oauthService is null)
    {
        return null;
    }

    if (!oauthService.HasClientCredentials)
    {
        // UND-47: PKCE flow needs only the public client_id. CLIENT_SECRET is no longer
        // read or required.
        app.Logger.LogWarning("Spotify CLIENT_ID not configured. Provide it in the console once, or set the CLIENT_ID environment variable.");
        return null;
    }

    // CSRF state is mandatory even for the console flow: the callback rejects
    // state-less requests, so a drive-by GET /callback cannot consume the pending
    // PKCE verifier or complete a login it did not start.
    var authorizationUrl = oauthService.GetAuthorizationUrl(SpotifyOAuthService.CreateState());
    // The authorization URL embeds the public client_id and a per-attempt PKCE
    // code_challenge. We surface it on the console so a tester can copy/paste, but
    // we deliberately do NOT pass it through the structured logger — that keeps
    // client_id out of any captured log file (UND-47 compliance §"Logs scrub
    // credentials").
    Console.WriteLine($"Spotify authorization URL: {authorizationUrl}");
    app.Logger.LogInformation("Spotify authorization URL ready (printed to console).");
    return authorizationUrl;
}

static async Task WarmSmartTrackStartAsync(WebApplication app)
{
    try
    {
        var smartTrackStartService = app.Services.GetRequiredService<ISmartTrackStartService>();
        await smartTrackStartService.WarmAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to warm Smart Track Start metadata");
    }
}

static async Task WriteConsoleStartupChecklistAsync(
    WebApplication app,
    ConsoleLaunchSettings consoleLaunchSettings,
    string? authorizationUrl)
{
    var setupService = app.Services.GetRequiredService<ICs2SetupService>();
    var controlProfileService = app.Services.GetRequiredService<IControlProfileService>();
    var smartTrackStartService = app.Services.GetRequiredService<ISmartTrackStartService>();
    var spotifyClient = app.Services.GetRequiredService<ISpotifyClient>();

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

    SmartTrackStartOptions smartTrackStartOptions;
    try
    {
        smartTrackStartOptions = app.Services.GetRequiredService<IOptions<SmartTrackStartOptions>>().Value;
    }
    catch
    {
        smartTrackStartOptions = new SmartTrackStartOptions();
    }

    var spotifyAuthenticated = false;
    try
    {
        spotifyAuthenticated = await spotifyClient.IsAuthenticatedAsync();
    }
    catch
    {
        spotifyAuthenticated = false;
    }

    Console.WriteLine();
    Console.WriteLine("UndefaultIt console startup");
    Console.WriteLine($"- Music provider: {app.Configuration["Music:Provider"] ?? "Tauon"}");
    Console.WriteLine($"- Quick launch mode: {(consoleLaunchSettings.IsQuickLaunch ? "yes" : "no")}");
    Console.WriteLine($"- MVP launch (--mvp): {(consoleLaunchSettings.IsMvpLaunch ? "yes — intent_capture (observe only; music.control_profile is not executed)" : "no")}");
    Console.WriteLine($"- Spotify mode: {(consoleLaunchSettings.ConfigurationOverrides.TryGetValue("UseMockSpotify", out var useMock) && string.Equals(useMock, "true", StringComparison.OrdinalIgnoreCase) ? "mock" : "real")}");
    Console.WriteLine($"- Spotify CLIENT_ID: {(consoleLaunchSettings.HasSpotifyCredentials ? "ready" : "missing")} (PKCE flow — no client_secret used)");
    Console.WriteLine($"- Prompted for client id this run: {(consoleLaunchSettings.PromptedForCredentials ? "yes" : "no")}");
    Console.WriteLine($"- Encrypted Spotify secret store: {consoleLaunchSettings.EncryptedStorePath}");
    Console.WriteLine($"- Loaded client id from encrypted store: {(consoleLaunchSettings.LoadedFromEncryptedStore ? "yes" : "no")}");
    Console.WriteLine($"- Saved client id to encrypted store this run: {(consoleLaunchSettings.SavedToEncryptedStore ? "yes" : "no")}");
    Console.WriteLine($"- Cleared encrypted store this run: {(consoleLaunchSettings.ClearedEncryptedStore ? "yes" : "no")}");
    if (consoleLaunchSettings.LegacyClientSecretEnvVarPresent)
    {
        Console.WriteLine("- CLIENT_SECRET environment variable detected: ignored (PKCE flow does not use a client secret).");
    }
    Console.WriteLine($"- Spotify redirect URI to register: {consoleLaunchSettings.RedirectUri}");
    Console.WriteLine($"- Spotify authorization URL: {authorizationUrl ?? "unavailable until credentials are provided"}");
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
    Console.WriteLine($"- Smart Track Start warmup: {(consoleLaunchSettings.SkipSmartTrackWarmup ? "skipped" : "attempted")}");
    Console.WriteLine($"- Smart Track Start: {(smartTrackStartOptions.Enabled ? "enabled" : "disabled")}");
    Console.WriteLine($"- Smart Track Start file: {smartTrackStartService.FilePath}");
    Console.WriteLine($"- Spotify authenticated: {(spotifyAuthenticated ? "yes" : "no")}");
    Console.WriteLine("- Spotify playback control requires Premium and an active playback device.");
    Console.WriteLine("- Use --reset-spotify-secrets to overwrite the saved client id without printing it.");
    Console.WriteLine("- Use --clear-spotify-secrets to wipe the encrypted store. With PKCE there is no client_secret to clear; this only removes the cached client id.");
    Console.WriteLine("- Edit control-profiles.json for pause/resume/duck behavior.");
    Console.WriteLine("- Edit smart-track-starts.json to configure optional non-zero track starts for spotify.profile playback.");
    Console.WriteLine("- Open /status for GSI + IMusicPlayer state. /spotify/status is leftover until PIVOT-10.");
    Console.WriteLine("- Tauon smoke (PIVOT-9): default launch without --mvp; watch Tauon and Playback pause/resume logs, not leftover Spotify fields.");
    Console.WriteLine();
}

static string FormatSuffix(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? string.Empty : $" ({value})";
}

static async Task<IResult> HandleSpotifyCallbackAsync(
    string? code,
    string? state,
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest("Missing authorization code.");
    }

    if (string.IsNullOrWhiteSpace(state))
    {
        // CSRF binding: every authorize URL we issue carries a state, so a
        // state-less callback was not initiated by this host. Reject it before
        // it can consume a pending PKCE verifier or trigger a token exchange.
        return Results.BadRequest("Missing state parameter.");
    }

    var oauthService = services.GetService<SpotifyOAuthService>();
    var tokenStorage = services.GetService<ITokenStorage>();
    if (oauthService is null || tokenStorage is null)
    {
        return Results.BadRequest("Spotify OAuth is unavailable in mock mode.");
    }

    try
    {
        var result = await oauthService.ExchangeCodeForTokenAsync(code, state, cancellationToken);
        await tokenStorage.SaveTokensAsync(result.AccessToken, result.RefreshToken, result.ExpiresAt, cancellationToken);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SpotifyOAuth");
        logger.LogInformation("Spotify connected. Access token stored in memory until the process exits.");
        return Results.Content(
            "<html><body><p>Spotify connected, you can close this window.</p></body></html>",
            "text/html");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SpotifyOAuth");
        logger.LogError(ex, "Failed to complete Spotify callback");
        return Results.Problem("Failed to complete Spotify OAuth callback.");
    }
}
