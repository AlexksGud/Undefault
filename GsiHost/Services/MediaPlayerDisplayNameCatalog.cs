using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace GsiHost.Services;

/// <summary>
/// Resolves SMTC <c>SourceAppUserModelId</c> values to friendly names from a vendored
/// music-presence <c>players.win.json</c> snapshot. Never HTTP-fetches at runtime.
/// </summary>
public sealed class MediaPlayerDisplayNameCatalog
{
    private const string SnapshotFileName = "players.win.json";
    private const string EmbeddedResourceName = "GsiHost.Data.players.win.json";

    private readonly IReadOnlyDictionary<string, string> _exact;
    private readonly IReadOnlyDictionary<string, string> _ignoreCase;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaPlayerDisplayNameCatalog"/> class.
    /// </summary>
    /// <param name="environment">The host environment used to locate <c>Data/players.win.json</c>.</param>
    /// <param name="logger">The logger used when the snapshot cannot be read.</param>
    public MediaPlayerDisplayNameCatalog(
        IWebHostEnvironment environment,
        ILogger<MediaPlayerDisplayNameCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var maps = Load(environment, logger);
        _exact = maps.Exact;
        _ignoreCase = maps.IgnoreCase;
    }

    /// <summary>
    /// Returns a non-empty display name for <paramref name="sourceAppUserModelId"/>.
    /// </summary>
    /// <param name="sourceAppUserModelId">The exact session id.</param>
    /// <returns>
    /// The snapshot <c>name</c> when a <c>win_winrt</c> row matches; otherwise the raw id.
    /// </returns>
    public string Resolve(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return "unknown";
        }

        if (_exact.TryGetValue(sourceAppUserModelId, out var exactName)
            && !string.IsNullOrWhiteSpace(exactName))
        {
            return exactName;
        }

        if (_ignoreCase.TryGetValue(sourceAppUserModelId, out var foldedName)
            && !string.IsNullOrWhiteSpace(foldedName))
        {
            return foldedName;
        }

        return sourceAppUserModelId;
    }

    private static (Dictionary<string, string> Exact, Dictionary<string, string> IgnoreCase) Load(
        IWebHostEnvironment environment,
        ILogger logger)
    {
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var ignoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream = OpenSnapshot(environment);
            if (stream is null)
            {
                logger.LogWarning("Media player display-name snapshot was not found; displayName will be the raw app id.");
                return (exact, ignoreCase);
            }

            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("players", out var players)
                || players.ValueKind != JsonValueKind.Array)
            {
                return (exact, ignoreCase);
            }

            foreach (var player in players.EnumerateArray())
            {
                var name = player.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!player.TryGetProperty("sources", out var sources)
                    || sources.ValueKind != JsonValueKind.Object
                    || !sources.TryGetProperty("win_winrt", out var winRt)
                    || winRt.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var idElement in winRt.EnumerateArray())
                {
                    var id = idElement.GetString();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    exact.TryAdd(id, name);
                    ignoreCase.TryAdd(id, name);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load media player display-name snapshot; displayName will be the raw app id.");
        }

        return (exact, ignoreCase);
    }

    private static Stream? OpenSnapshot(IWebHostEnvironment environment)
    {
        foreach (var path in EnumerateFileCandidates(environment))
        {
            if (File.Exists(path))
            {
                return File.OpenRead(path);
            }
        }

        var assembly = typeof(MediaPlayerDisplayNameCatalog).Assembly;
        var resource = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(SnapshotFileName, StringComparison.OrdinalIgnoreCase))
                .Select(assembly.GetManifestResourceStream)
                .FirstOrDefault();

        return resource;
    }

    private static IEnumerable<string> EnumerateFileCandidates(IWebHostEnvironment environment)
    {
        yield return Path.Combine(environment.ContentRootPath, "Data", SnapshotFileName);
        yield return Path.Combine(AppContext.BaseDirectory, "Data", SnapshotFileName);
    }
}
