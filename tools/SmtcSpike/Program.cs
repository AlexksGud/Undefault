using Windows.Media.Control;

namespace SmtcSpike;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitNoMatchingSession = 2;
    private const int ExitAmbiguousSession = 3;
    private const int ExitControlRejected = 4;
    private const int ExitFailure = 5;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>SMTC applies a command asynchronously, so the post-command status read is best effort.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    private static async Task<int> Main(string[] args)
    {
        try
        {
            return (args.Length == 0 ? string.Empty : args[0]) switch
            {
                "list" when args.Length == 1 => await ListAsync(),
                "pause" when args.Length == 2 => await ControlAsync(args[1], PlaybackCommand.Pause),
                "resume" when args.Length == 2 => await ControlAsync(args[1], PlaybackCommand.Resume),
                _ => PrintUsage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return ExitFailure;
        }
    }

    private static async Task<int> ListAsync()
    {
        var manager = await RequestManagerAsync();
        var sessions = manager.GetSessions();
        var current = manager.GetCurrentSession();

        Console.WriteLine($"session count           : {sessions.Count}");
        Console.WriteLine($"GetCurrentSession() id  : {Quote(current?.SourceAppUserModelId)}");
        Console.WriteLine();

        var anySessionIsReferenceEqualToCurrent = false;

        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;
            anySessionIsReferenceEqualToCurrent |= ReferenceEquals(session, current);
            var isCurrent = current is not null
                && string.Equals(session.SourceAppUserModelId, current.SourceAppUserModelId, StringComparison.Ordinal);

            string title;
            string artist;
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(CallTimeout);
                title = Quote(properties.Title);
                artist = Quote(properties.Artist);
            }
            catch (Exception ex)
            {
                title = artist = $"<unreadable: {ex.GetType().Name}: {ex.Message}>";
            }

            Console.WriteLine($"[{index}] SourceAppUserModelId : {Quote(session.SourceAppUserModelId)}");
            Console.WriteLine($"    PlaybackStatus       : {playbackInfo.PlaybackStatus}");
            Console.WriteLine($"    Title                : {title}");
            Console.WriteLine($"    Artist               : {artist}");
            Console.WriteLine($"    IsPlayEnabled        : {controls.IsPlayEnabled}");
            Console.WriteLine($"    IsPauseEnabled       : {controls.IsPauseEnabled}");
            Console.WriteLine($"    IsNextEnabled        : {controls.IsNextEnabled}");
            Console.WriteLine($"    IsCurrentSession     : {isCurrent}");
            Console.WriteLine();
        }

        if (current is not null && !anySessionIsReferenceEqualToCurrent)
        {
            Console.WriteLine(
                "note: GetCurrentSession() returned an object that is not reference-equal to any entry above, "
                + "so IsCurrentSession above is decided by exact SourceAppUserModelId equality instead.");
        }

        var duplicateIds = sessions
            .GroupBy(session => session.SourceAppUserModelId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => Quote(group.Key))
            .ToList();

        if (duplicateIds.Count > 0)
        {
            Console.WriteLine(
                "warning: these SourceAppUserModelId values are reported by more than one session, "
                + $"so exact-id targeting cannot address a single session: {string.Join(", ", duplicateIds)}");
        }

        return ExitOk;
    }

    private static async Task<int> ControlAsync(string appUserModelId, PlaybackCommand command)
    {
        var manager = await RequestManagerAsync();
        var matches = manager.GetSessions()
            .Where(session => string.Equals(session.SourceAppUserModelId, appUserModelId, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"no matching session for SourceAppUserModelId {Quote(appUserModelId)}");
            Console.Error.WriteLine("no other session is touched. Run 'SmtcSpike list' to read the exact ids.");
            return ExitNoMatchingSession;
        }

        if (matches.Count > 1)
        {
            Console.Error.WriteLine(
                $"ambiguous: {matches.Count} sessions report SourceAppUserModelId {Quote(appUserModelId)}");
            Console.Error.WriteLine("refusing to pick one, because acting on the wrong session is unrecoverable.");
            return ExitAmbiguousSession;
        }

        var target = matches[0];
        var statusBefore = target.GetPlaybackInfo().PlaybackStatus;

        var result = command switch
        {
            PlaybackCommand.Pause => await target.TryPauseAsync().AsTask().WaitAsync(CallTimeout),
            PlaybackCommand.Resume => await target.TryPlayAsync().AsTask().WaitAsync(CallTimeout),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

        await Task.Delay(SettleDelay);

        Console.WriteLine($"target                : {Quote(target.SourceAppUserModelId)}");
        Console.WriteLine($"command               : {(command == PlaybackCommand.Pause ? "TryPauseAsync" : "TryPlayAsync")}");
        Console.WriteLine($"PlaybackStatus before : {statusBefore}");
        Console.WriteLine($"returned bool         : {result}");
        Console.WriteLine($"PlaybackStatus after  : {target.GetPlaybackInfo().PlaybackStatus}");

        return result ? ExitOk : ExitControlRejected;
    }

    private static Task<GlobalSystemMediaTransportControlsSessionManager> RequestManagerAsync() =>
        GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(CallTimeout);

    /// <summary>Quotes and escapes so trailing spaces and newlines in an app id stay visible.</summary>
    private static string Quote(string? value) =>
        value is null
            ? "<null>"
            : $"\"{value.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static int PrintUsage()
    {
        Console.Error.WriteLine("SmtcSpike - UND-88 evidence harness for Windows System Media Transport Controls.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  SmtcSpike list");
        Console.Error.WriteLine("  SmtcSpike pause  <SourceAppUserModelId>");
        Console.Error.WriteLine("  SmtcSpike resume <SourceAppUserModelId>");
        Console.Error.WriteLine();
        Console.Error.WriteLine("The id must match a session exactly. There is no substring or fuzzy matching.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Exit codes: {ExitOk} ok, {ExitUsage} usage, {ExitNoMatchingSession} no matching session, "
            + $"{ExitAmbiguousSession} ambiguous id, {ExitControlRejected} SMTC returned false, {ExitFailure} unexpected error.");
        return ExitUsage;
    }

    private enum PlaybackCommand
    {
        Pause,
        Resume,
    }
}
