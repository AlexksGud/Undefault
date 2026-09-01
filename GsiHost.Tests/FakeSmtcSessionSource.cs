using Core.Music;
using GsiHost.Players.Smtc;

namespace GsiHost.Tests;

internal sealed class FakeSmtcSessionSource : ISmtcSessionSource
{
    public List<SmtcSessionSnapshot> Sessions { get; } = new();

    public List<(string Action, string Id)> Commands { get; } = new();

    public bool PlayResult { get; set; } = true;

    public bool PauseResult { get; set; } = true;

    public bool NextResult { get; set; } = true;

    public bool PreviousResult { get; set; } = true;

    private int _forceUpdateCalls;

    public int ForceUpdateCalls => Volatile.Read(ref _forceUpdateCalls);

    public Exception? GetSessionsException { get; set; }

    public Task<IReadOnlyList<SmtcSessionSnapshot>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetSessionsException is not null)
        {
            throw GetSessionsException;
        }

        return Task.FromResult<IReadOnlyList<SmtcSessionSnapshot>>(Sessions.ToArray());
    }

    public Task<bool?> TryPlayAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => CompleteCommand("play", sourceAppUserModelId, PlayResult, cancellationToken);

    public Task<bool?> TryPauseAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => CompleteCommand("pause", sourceAppUserModelId, PauseResult, cancellationToken);

    public Task<bool?> TrySkipNextAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => CompleteCommand("next", sourceAppUserModelId, NextResult, cancellationToken);

    public Task<bool?> TrySkipPreviousAsync(string sourceAppUserModelId, CancellationToken cancellationToken = default)
        => CompleteCommand("previous", sourceAppUserModelId, PreviousResult, cancellationToken);

    public void ForceUpdate()
        => Interlocked.Increment(ref _forceUpdateCalls);

    private Task<bool?> CompleteCommand(
        string action,
        string sourceAppUserModelId,
        bool result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add((action, sourceAppUserModelId));
        if (!Sessions.Exists(session =>
                string.Equals(session.SourceAppUserModelId, sourceAppUserModelId, StringComparison.Ordinal)))
        {
            return Task.FromResult<bool?>(null);
        }

        return Task.FromResult<bool?>(result);
    }
}
