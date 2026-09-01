using Core.Configuration;
using Core.Models;
using Core.Music;
using GsiHost.Players;
using GsiHost.Services;
using Microsoft.Extensions.Options;

namespace GsiHost.Diagnostics;

/// <summary>
/// Host-side wrapper that records game-triggered <see cref="IMusicPlaybackControl"/> outcomes for local go/no-go counters.
/// </summary>
/// <remarks>
/// Onboarding test commands go through <see cref="IMusicPlayer"/> and
/// <see cref="MusicLastCommandStore"/> with source <c>test</c>; they do not pass this decorator.
/// Last-command recording stays on <see cref="RecordingMusicPlaybackControl"/> inside or outside this wrapper.
/// </remarks>
public sealed class CountingMusicPlaybackControl : IMusicPlaybackControl
{
    private readonly IMusicPlaybackControl _inner;
    private readonly LocalGoNoGoCounterStore _store;
    private readonly IOptions<SmtcOptions> _smtcOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountingMusicPlaybackControl"/> class.
    /// </summary>
    /// <param name="inner">The session coordinator or last-command recorder to wrap.</param>
    /// <param name="store">The local go/no-go counter store.</param>
    /// <param name="smtcOptions">The selected SMTC id binding.</param>
    public CountingMusicPlaybackControl(
        IMusicPlaybackControl inner,
        LocalGoNoGoCounterStore store,
        IOptions<SmtcOptions> smtcOptions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(smtcOptions);

        _inner = inner;
        _store = store;
        _smtcOptions = smtcOptions;
    }

    /// <inheritdoc />
    public Task<MusicCommandResult> TryPauseAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Pause, () => _inner.TryPauseAsync(eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryResumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Resume, () => _inner.TryResumeAsync(eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryNextAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Next, () => _inner.TryNextAsync(eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryPreviousAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Previous, () => _inner.TryPreviousAsync(eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryDuckAsync(
        EventControlRule rule,
        NormalizedEvent context,
        CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Duck, () => _inner.TryDuckAsync(rule, context, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryDuckAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default)
        => RecordAsync(MusicControlCommands.Duck, () => _inner.TryDuckAsync(volumePercent, eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TryRestoreVolumeAsync(string? eventKeyForLog, CancellationToken cancellationToken = default)
        => RecordAsync(
            MusicControlCommands.RestoreVolume,
            () => _inner.TryRestoreVolumeAsync(eventKeyForLog, cancellationToken));

    /// <inheritdoc />
    public Task<MusicCommandResult> TrySetManagedVolumeAsync(
        int volumePercent,
        string? eventKeyForLog,
        CancellationToken cancellationToken = default)
        => _inner.TrySetManagedVolumeAsync(volumePercent, eventKeyForLog, cancellationToken);

    private async Task<MusicCommandResult> RecordAsync(
        string command,
        Func<Task<MusicCommandResult>> action)
    {
        var result = await action().ConfigureAwait(false);
        _store.Record(
            command,
            MusicLastCommandStore.GameSource,
            MusicLastCommandStore.ReadSelectedAppId(_smtcOptions),
            result);
        return result;
    }
}
