using System.Net;
using System.Text.Json;
using Core.Music;

namespace GsiHost.Players;

/// <summary>
/// Tauon Music Box adapter over the verified remote-control HTTP API.
/// </summary>
/// <remarks>
/// Calls loopback <c>GET /api1/*</c> paths only. Each call uses
/// <see cref="IHttpClientFactory.CreateClient(string)"/> so the named client's handler is not
/// cached past the factory lifetime. Transport failures are logged and returned as
/// <see cref="MusicCommandResult"/> except caller cancellation and out-of-range volume.
/// </remarks>
public sealed class TauonMusicPlayer : IMusicPlayer
{
    /// <summary>
    /// The named <see cref="IHttpClientFactory"/> client configured by the host.
    /// </summary>
    public const string HttpClientName = "Tauon";

    private const string PlayPath = "api1/play";
    private const string PausePath = "api1/pause";
    private const string NextPath = "api1/next";
    private const string PreviousPath = "api1/back";
    private const string StatusPath = "api1/status";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TauonMusicPlayer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TauonMusicPlayer"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The factory used to create a Tauon HTTP client per request.</param>
    /// <param name="logger">The logger used for fail-soft diagnostics.</param>
    public TauonMusicPlayer(
        IHttpClientFactory httpClientFactory,
        ILogger<TauonMusicPlayer> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Applies loopback origin and timeout to a named Tauon client.
    /// </summary>
    /// <param name="client">The client created by <see cref="IHttpClientFactory"/>.</param>
    /// <param name="options">The Tauon adapter options.</param>
    public static void ConfigureClient(HttpClient client, TauonOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        client.BaseAddress = NormalizeBaseAddress(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
    }

    /// <inheritdoc />
    public MusicPlayerCapabilities Capabilities => MusicPlayerCapabilities.Mvp;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var status = await TryReadStatusObjectAsync(cancellationToken).ConfigureAwait(false);
        return status is not null;
    }

    /// <inheritdoc />
    public async Task<MusicPlaybackState?> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var status = await TryReadStatusObjectAsync(cancellationToken).ConfigureAwait(false);
        return status is null ? null : MapState(status.Value);
    }

    /// <inheritdoc />
    public Task<MusicCommandResult> PlayAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(PlayPath, "play", cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> PauseAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(PausePath, "pause", cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Tauon has no <c>/resume</c> endpoint. This reads <c>GET api1/status</c> and issues
    /// <c>GET api1/play</c> only when status is not already playing.
    /// </remarks>
    public async Task<MusicCommandResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state?.Status == PlaybackStatus.Playing)
        {
            return MusicCommandResult.Applied;
        }

        return await PlayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<MusicCommandResult> NextAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(NextPath, "next", cancellationToken);

    /// <inheritdoc />
    public Task<MusicCommandResult> PreviousAsync(CancellationToken cancellationToken = default)
        => SendTransportAsync(PreviousPath, "previous", cancellationToken);

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="volumePercent"/> is less than 0 or greater than 100.
    /// </exception>
    public Task<MusicCommandResult> SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
    {
        if (volumePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(volumePercent),
                volumePercent,
                "Volume must be between 0 and 100.");
        }

        return SendTransportAsync($"api1/setvolume/{volumePercent}", "setvolume", cancellationToken);
    }

    private async Task<MusicCommandResult> SendTransportAsync(
        string relativePath,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await CreateClient().GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var malformed = await TryMapMalformedJsonAsync(response, action, cancellationToken)
                    .ConfigureAwait(false);
                return malformed ?? MusicCommandResult.Applied;
            }

            var statusCode = (int)response.StatusCode;
            _logger.LogWarning(
                "Tauon {Action} returned HTTP {StatusCode}.",
                action,
                statusCode);
            return MapHttpFailure(action, response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsSoftFailure(ex))
        {
            _logger.LogWarning(ex, "Tauon {Action} failed.", action);
            return MapSoftFailure(action, ex);
        }
    }

    private async Task<MusicCommandResult?> TryMapMalformedJsonAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using (JsonDocument.Parse(json))
            {
                return null;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Tauon {Action} returned malformed JSON.", action);
            return MusicCommandResult.Rejected($"Tauon {action} returned malformed JSON.");
        }
    }

    private static MusicCommandResult MapHttpFailure(string action, HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        if (statusCode == HttpStatusCode.NotFound)
        {
            return MusicCommandResult.Unsupported($"Tauon {action} returned HTTP 404.");
        }

        if (status is >= 400 and < 500)
        {
            return MusicCommandResult.Rejected($"Tauon {action} returned HTTP {status}.");
        }

        return MusicCommandResult.Failed($"Tauon {action} returned HTTP {status}.");
    }

    private static MusicCommandResult MapSoftFailure(string action, Exception ex)
    {
        if (ex is JsonException)
        {
            return MusicCommandResult.Rejected($"Tauon {action} returned malformed JSON: {ex.Message}");
        }

        if (ex is HttpRequestException)
        {
            return MusicCommandResult.Unavailable($"Tauon {action} could not connect: {ex.Message}");
        }

        return MusicCommandResult.Failed($"Tauon {action} timed out or failed: {ex.Message}");
    }

    private async Task<JsonElement?> TryReadStatusObjectAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await CreateClient().GetAsync(StatusPath, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tauon status returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var element = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
            if (element.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Tauon status returned a non-object body.");
                return null;
            }

            return element;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsSoftFailure(ex))
        {
            _logger.LogWarning(ex, "Tauon status request failed.");
            return null;
        }
    }

    private static MusicPlaybackState MapState(JsonElement root)
    {
        var status = MapStatus(ReadString(root, "status"));
        var track = MapTrack(root);
        var volume = ReadVolume(root);
        return new MusicPlaybackState(status, track, volume);
    }

    private static PlaybackStatus MapStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return PlaybackStatus.Unknown;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "playing" => PlaybackStatus.Playing,
            "paused" => PlaybackStatus.Paused,
            "stopped" => PlaybackStatus.Stopped,
            _ => PlaybackStatus.Unknown
        };
    }

    private static MusicTrack? MapTrack(JsonElement root)
    {
        var id = ReadId(root);
        var title = ReadString(root, "title");
        var artist = ReadString(root, "artist");
        var album = ReadString(root, "album");
        if (id is null && title is null && artist is null && album is null)
        {
            return null;
        }

        return new MusicTrack(id, title, artist, album);
    }

    private static string? ReadId(JsonElement root)
    {
        if (!TryGetProperty(root, "id", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => EmptyToNull(value.GetString()),
            _ => null
        };
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return EmptyToNull(value.GetString());
    }

    private static int? ReadVolume(JsonElement root)
    {
        if (!TryGetProperty(root, "volume", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var volume)
            || volume is < 0 or > 100)
        {
            return null;
        }

        return volume;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private HttpClient CreateClient()
        => _httpClientFactory.CreateClient(HttpClientName);

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsSoftFailure(Exception ex)
        => ex is HttpRequestException or JsonException or OperationCanceledException;

    private static Uri NormalizeBaseAddress(string? baseUrl)
    {
        var origin = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://127.0.0.1:7814"
            : baseUrl.Trim();
        return new Uri(origin.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
