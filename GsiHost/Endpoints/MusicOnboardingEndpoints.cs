using GsiHost.Onboarding;
using GsiHost.Services;

namespace GsiHost.Endpoints;

/// <summary>
/// Maps the onboarding HTTP surface. Registered from <c>Program.cs</c> with a single call.
/// </summary>
public static class MusicOnboardingEndpoints
{
    /// <summary>
    /// Maps <c>/music/sessions</c>, <c>/music/session</c>, test commands, last-command, and preset routes.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static WebApplication MapMusicOnboarding(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var music = app.MapGroup("/music");

        music.MapGet("/sessions", (
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) => onboarding.GetSessionsAsync(cancellationToken));

        music.MapPost("/session", (
            SelectMusicSessionRequest? request,
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) =>
            onboarding.SelectSessionAsync(request?.AppId, cancellationToken));

        music.MapPost("/test/pause", (
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) => onboarding.TestPauseAsync(cancellationToken));

        music.MapPost("/test/resume", (
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) => onboarding.TestResumeAsync(cancellationToken));

        music.MapGet("/last-command", (MusicOnboardingService onboarding) =>
            Results.Ok(onboarding.GetLastCommand()));

        music.MapGet("/preset", (
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) => onboarding.GetPresetAsync(cancellationToken));

        music.MapPost("/preset", (
            MusicPresetRequest? request,
            MusicOnboardingService onboarding,
            CancellationToken cancellationToken) =>
            onboarding.SetPresetAsync(request?.Preset, cancellationToken));

        return app;
    }
}
