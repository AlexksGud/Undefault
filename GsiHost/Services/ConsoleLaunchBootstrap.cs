using Microsoft.AspNetCore.Builder;

namespace GsiHost.Services;

public sealed record ConsoleLaunchSettings(
    string GsiBaseUrl,
    bool IsQuickLaunch,
    bool IsMvpLaunch,
    bool SkipCs2Setup,
    bool SkipSmartTrackWarmup,
    IReadOnlyDictionary<string, string?> ConfigurationOverrides
);

public static class ConsoleLaunchBootstrap
{
    public const string DefaultGsiBaseUrl = "http://127.0.0.1:5292";
    private const string QuickLaunchArg = "--quick";
    private const string SkipCs2SetupArg = "--skip-cs2-setup";
    private const string SkipSmartTrackWarmupArg = "--skip-smart-track-warmup";
    private const string UseMockSpotifyArg = "--use-mock-spotify";
    private const string IntentCaptureArg = "--intent-capture";
    private const string ScenarioPlaybackArg = "--scenario-playback";
    private const string MvpArg = "--mvp";

    public static ConsoleLaunchSettings Apply(WebApplicationBuilder builder, string[] args)
    {
        var settings = Prepare(builder.Configuration, args);

        builder.Configuration.AddInMemoryCollection(settings.ConfigurationOverrides);
        builder.WebHost.UseUrls(settings.GsiBaseUrl);
        return settings;
    }

    public static ConsoleLaunchSettings Prepare(
        IConfiguration configuration,
        IReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);

        var gsiBaseUrl = NormalizeBaseUrl(configuration["Gsi:Url"]);

        var requestedUseMockSpotify = HasArg(args, UseMockSpotifyArg);
        var requestedIntentCapture = HasArg(args, IntentCaptureArg);
        var requestedScenarioPlayback = HasArg(args, ScenarioPlaybackArg);
        var requestedMvp = HasArg(args, MvpArg);
        var isQuickLaunch = HasArg(args, QuickLaunchArg);
        var skipCs2Setup = isQuickLaunch || HasArg(args, SkipCs2SetupArg);
        var skipSmartTrackWarmup = isQuickLaunch || HasArg(args, SkipSmartTrackWarmupArg);

        var overrides = new Dictionary<string, string?>
        {
            ["Gsi:Url"] = gsiBaseUrl,
            ["Music:Provider"] = ResolveMusicProvider(configuration, isQuickLaunch, requestedUseMockSpotify)
        };

        if ((requestedIntentCapture || requestedMvp) && !requestedScenarioPlayback)
        {
            overrides["Runtime:Mode"] = "intent_capture";
        }
        else if (requestedScenarioPlayback)
        {
            overrides["Runtime:Mode"] = "scenario_playback";
        }

        // --mvp is the one-command MVP launch: it implies intent_capture and turns the
        // tester feature flags ON in memory so a single flag yields a host with timeline
        // + playback observer active. The user controls playback with the keyboard media
        // play/pause key (Spotify handles it natively); Undefault only observes and records.
        // The git-tracked appsettings.json defaults (scenario_playback, flags false) stay
        // intact; these overrides win at runtime via the in-memory configuration collection
        // added in Apply.
        if (requestedMvp)
        {
            overrides["Timeline:Enabled"] = "true";
            overrides["PlaybackObserver:Enabled"] = "true";
        }

        return new ConsoleLaunchSettings(
            GsiBaseUrl: gsiBaseUrl,
            IsQuickLaunch: isQuickLaunch,
            IsMvpLaunch: requestedMvp,
            SkipCs2Setup: skipCs2Setup,
            SkipSmartTrackWarmup: skipSmartTrackWarmup,
            ConfigurationOverrides: overrides);
    }

    private static string ResolveMusicProvider(
        IConfiguration configuration,
        bool isQuickLaunch,
        bool requestedUseMockSpotify)
    {
        if (isQuickLaunch || requestedUseMockSpotify)
        {
            return "Mock";
        }

        var configured = configuration["Music:Provider"];
        return string.IsNullOrWhiteSpace(configured) ? "Tauon" : configured.Trim();
    }

    private static bool HasArg(IEnumerable<string> args, string expectedArg)
    {
        return args.Any(arg => string.Equals(arg, expectedArg, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBaseUrl(string? configuredBaseUrl)
    {
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var uri))
        {
            return DefaultGsiBaseUrl;
        }

        var builder = new UriBuilder(uri)
        {
            Host = NormalizeLoopbackHost(uri.Host),
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (builder.Port <= 0)
        {
            builder.Port = 5292;
        }

        if (!builder.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = Uri.UriSchemeHttp;
        }

        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string NormalizeLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;
    }
}
