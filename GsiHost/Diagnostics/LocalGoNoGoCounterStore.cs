using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Music;
using GsiHost.Services;
using Microsoft.Extensions.Options;

namespace GsiHost.Diagnostics;

/// <summary>
/// Persists local MVP+ go/no-go counters next to the host and logs the same facts at Information.
/// </summary>
/// <remarks>
/// <para>
/// A game session is a streak of game-triggered <see cref="MusicCommandOutcome.Applied"/> commands.
/// A new game session starts when this host process records its first game-triggered Applied
/// (process start), or when a later game-triggered Applied occurs after at least
/// <see cref="GameSessionIdleGap"/> with no game-triggered Applied. Session count is persisted
/// across host restarts. CS2 match ids are not read.
/// </para>
/// <para>
/// Test-source commands (<see cref="MusicLastCommandStore.TestSource"/>) increment separate
/// outcome counters and never set the first automatic Applied clock or open a game session.
/// Track names, listening history, machine identifiers, and user identifiers are not stored.
/// Nothing is sent off the machine.
/// </para>
/// </remarks>
public sealed class LocalGoNoGoCounterStore
{
    /// <summary>
    /// The idle gap after which a later game-triggered Applied opens a new game session.
    /// </summary>
    public static readonly TimeSpan GameSessionIdleGap = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ILogger<LocalGoNoGoCounterStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly LocalGoNoGoState _state;
    private bool _recordedGameAppliedThisProcess;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalGoNoGoCounterStore"/> class.
    /// </summary>
    /// <param name="options">The directory and file name for the state file.</param>
    /// <param name="environment">The host environment used when <see cref="LocalGoNoGoCounterOptions.Directory"/> is empty.</param>
    /// <param name="logger">The logger used for Information facts and fail-soft file errors.</param>
    /// <param name="timeProvider">The clock used for timestamps. The default is <see cref="TimeProvider.System"/>.</param>
    public LocalGoNoGoCounterStore(
        IOptions<LocalGoNoGoCounterOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalGoNoGoCounterStore> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var value = options.Value ?? new LocalGoNoGoCounterOptions();
        var directory = string.IsNullOrWhiteSpace(value.Directory)
            ? environment.ContentRootPath
            : value.Directory;
        var fileName = string.IsNullOrWhiteSpace(value.FileName)
            ? LocalGoNoGoCounterOptions.DefaultFileName
            : value.FileName.Trim();
        FilePath = Path.Combine(directory, fileName);

        var hostStartUtc = _timeProvider.GetUtcNow();
        _state = LoadOrCreate(hostStartUtc);
        PersistUnlocked();

        _logger.LogInformation(
            "Local go/no-go counters started at {HostStartUtc}. State file: {StateFilePath}.",
            _state.HostStartUtc,
            FilePath);
    }

    /// <summary>
    /// Gets the host-local JSON path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Records a playback command for local go/no-go counters.
    /// </summary>
    /// <param name="command">One of <c>pause</c>, <c>resume</c>, <c>next</c>, <c>previous</c>, <c>duck</c>, <c>restore_volume</c>.</param>
    /// <param name="source"><see cref="MusicLastCommandStore.GameSource"/> or <see cref="MusicLastCommandStore.TestSource"/>.</param>
    /// <param name="targetAppId">The selected <c>Music:Smtc:SourceAppUserModelId</c>, or <see langword="null"/>.</param>
    /// <param name="result">The player or coordinator result.</param>
    public void Record(string command, string source, string? targetAppId, MusicCommandResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(result);

        var normalized = result.WithRequiredReason();
        var now = _timeProvider.GetUtcNow();
        var isGame = string.Equals(source, MusicLastCommandStore.GameSource, StringComparison.OrdinalIgnoreCase);
        var appId = string.IsNullOrWhiteSpace(targetAppId) ? null : targetAppId;

        lock (_sync)
        {
            IncrementUnlocked(isGame ? _state.GameOutcomes : _state.TestOutcomes, normalized.Outcome);

            _state.LastCommand = new LocalGoNoGoCommandState
            {
                Command = command,
                Source = isGame ? MusicLastCommandStore.GameSource : MusicLastCommandStore.TestSource,
                TargetAppId = appId,
                Outcome = normalized.Outcome.ToString(),
                AtUtc = Format(now)
            };

            if (isGame && normalized.Outcome == MusicCommandOutcome.Applied)
            {
                RecordGameAppliedUnlocked(now);
            }

            PersistUnlocked();
        }

        _logger.LogInformation(
            "Local go/no-go command source={Source} command={Command} outcome={Outcome} targetAppId={TargetAppId}.",
            isGame ? MusicLastCommandStore.GameSource : MusicLastCommandStore.TestSource,
            command,
            normalized.Outcome,
            appId);
    }

    /// <summary>
    /// Gets a copy of the current counters.
    /// </summary>
    /// <returns>The frozen snapshot.</returns>
    public LocalGoNoGoCounterSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new LocalGoNoGoCounterSnapshot(
                FilePath,
                ParseRequired(_state.HostStartUtc),
                ParseOptional(_state.FirstGameAppliedUtc),
                ParseOptional(_state.HostStartUtcAtFirstGameApplied),
                ParseOptional(_state.LastGameAppliedUtc),
                _state.GameSessionsWithApplied,
                CopyOutcomes(_state.GameOutcomes),
                CopyOutcomes(_state.TestOutcomes),
                _state.LastCommand is null
                    ? null
                    : new LocalGoNoGoRecordedCommand(
                        _state.LastCommand.Command ?? string.Empty,
                        _state.LastCommand.Source ?? string.Empty,
                        _state.LastCommand.TargetAppId,
                        ParseOutcome(_state.LastCommand.Outcome),
                        ParseRequired(_state.LastCommand.AtUtc)));
        }
    }

    private void RecordGameAppliedUnlocked(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_state.FirstGameAppliedUtc))
        {
            _state.FirstGameAppliedUtc = Format(now);
            _state.HostStartUtcAtFirstGameApplied = _state.HostStartUtc;
            var hostStart = ParseRequired(_state.HostStartUtc);
            _logger.LogInformation(
                "Local go/no-go first game-triggered Applied at {FirstGameAppliedUtc} ({ElapsedMs} ms after host start {HostStartUtc}).",
                _state.FirstGameAppliedUtc,
                (long)(now - hostStart).TotalMilliseconds,
                _state.HostStartUtc);
        }

        var lastApplied = ParseOptional(_state.LastGameAppliedUtc);
        var newSession = !_recordedGameAppliedThisProcess
            || lastApplied is null
            || now - lastApplied.Value >= GameSessionIdleGap;

        if (newSession)
        {
            _state.GameSessionsWithApplied++;
            _logger.LogInformation(
                "Local go/no-go game session opened; sessionsWithApplied={SessionCount}.",
                _state.GameSessionsWithApplied);
        }

        _recordedGameAppliedThisProcess = true;
        _state.LastGameAppliedUtc = Format(now);
    }

    private LocalGoNoGoState LoadOrCreate(DateTimeOffset hostStartUtc)
    {
        if (!File.Exists(FilePath))
        {
            return CreateFresh(hostStartUtc);
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<LocalGoNoGoState>(json, JsonOptions);
            if (loaded is null)
            {
                return CreateFresh(hostStartUtc);
            }

            Normalize(loaded);
            loaded.HostStartUtc = Format(hostStartUtc);
            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read local go/no-go counters from {StateFilePath}; starting empty.",
                FilePath);
            return CreateFresh(hostStartUtc);
        }
    }

    private void PersistUnlocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_state, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist local go/no-go counters to {StateFilePath}.", FilePath);
        }
    }

    private static LocalGoNoGoState CreateFresh(DateTimeOffset hostStartUtc)
    {
        var state = new LocalGoNoGoState
        {
            HostStartUtc = Format(hostStartUtc)
        };
        Normalize(state);
        return state;
    }

    private static void Normalize(LocalGoNoGoState state)
    {
        EnsureOutcomeKeys(state.GameOutcomes);
        EnsureOutcomeKeys(state.TestOutcomes);
        if (state.GameSessionsWithApplied < 0)
        {
            state.GameSessionsWithApplied = 0;
        }
    }

    private static void EnsureOutcomeKeys(Dictionary<string, int> outcomes)
    {
        foreach (var outcome in Enum.GetValues<MusicCommandOutcome>())
        {
            var key = outcome.ToString();
            if (!outcomes.ContainsKey(key))
            {
                outcomes[key] = 0;
            }
        }
    }

    private static void IncrementUnlocked(Dictionary<string, int> outcomes, MusicCommandOutcome outcome)
    {
        var key = outcome.ToString();
        outcomes.TryGetValue(key, out var current);
        outcomes[key] = current + 1;
    }

    private static IReadOnlyDictionary<MusicCommandOutcome, int> CopyOutcomes(Dictionary<string, int> source)
    {
        var copy = new Dictionary<MusicCommandOutcome, int>();
        foreach (var outcome in Enum.GetValues<MusicCommandOutcome>())
        {
            source.TryGetValue(outcome.ToString(), out var count);
            copy[outcome] = count;
        }

        return copy;
    }

    private static string Format(DateTimeOffset value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private DateTimeOffset ParseRequired(string? value)
        => ParseOptional(value) ?? _timeProvider.GetUtcNow();

    private static DateTimeOffset? ParseOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static MusicCommandOutcome ParseOutcome(string? value)
        => Enum.TryParse(value, ignoreCase: true, out MusicCommandOutcome outcome)
            ? outcome
            : MusicCommandOutcome.Failed;

    private sealed class LocalGoNoGoState
    {
        public int SchemaVersion { get; set; } = 1;

        public string HostStartUtc { get; set; } = string.Empty;

        public string? FirstGameAppliedUtc { get; set; }

        public string? HostStartUtcAtFirstGameApplied { get; set; }

        public string? LastGameAppliedUtc { get; set; }

        public int GameSessionsWithApplied { get; set; }

        public Dictionary<string, int> GameOutcomes { get; set; } = new();

        public Dictionary<string, int> TestOutcomes { get; set; } = new();

        public LocalGoNoGoCommandState? LastCommand { get; set; }
    }

    private sealed class LocalGoNoGoCommandState
    {
        public string? Command { get; set; }

        public string? Source { get; set; }

        public string? TargetAppId { get; set; }

        public string? Outcome { get; set; }

        public string? AtUtc { get; set; }
    }
}
