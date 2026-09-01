namespace GsiHost.Configuration;

/// <summary>
/// Configuration for <see cref="GsiHost.Services.PlaybackStateObserver"/>, the background service
/// that polls <c>IMusicPlayer</c> and records pause/resume transitions to the timeline.
/// </summary>
/// <remarks>
/// <para>
/// Leftover <c>intent_capture</c> tooling observes player transport (via <c>GetStateAsync</c> polling)
/// instead of binding a global hotkey, because physical media play/pause keys cannot be registered
/// with <c>RegisterHotKey</c>.
/// </para>
/// <para>
/// Recording is additionally gated on the <c>intent_capture</c> runtime: the hosted service is only
/// registered in <c>intent_capture</c>, and the poll loop checks both <see cref="Enabled"/> and
/// <c>RuntimeOptions.IsIntentCapture</c> before polling. No transitions are recorded in
/// <c>scenario_playback</c>.
/// </para>
/// </remarks>
public sealed class PlaybackObserverOptions
{
    public const string SectionName = "PlaybackObserver";

    /// <summary>Gets or sets a value indicating whether playback-state polling is active.</summary>
    /// <remarks>
    /// Git default is <see langword="false"/> so the observer is opt-in; <c>--mvp</c> turns it on
    /// via an in-memory configuration override.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the delay, in seconds, between playback-state polls.</summary>
    /// <remarks>
    /// Defaults to 2 seconds. Values below 1 are clamped up to 1 by the observer to avoid hot-looping.
    /// </remarks>
    public int PollIntervalSeconds { get; set; } = 2;
}
