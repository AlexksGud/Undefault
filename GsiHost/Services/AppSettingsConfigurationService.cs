using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Core.Configuration;
using Microsoft.Extensions.Logging;

namespace GsiHost.Services;

public sealed class AppSettingsConfigurationService : IConfigurationService
{
    private readonly string _filePath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppSettingsConfigurationService> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public AppSettingsConfigurationService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AppSettingsConfigurationService> logger)
    {
        _filePath = Path.Combine(environment.ContentRootPath, "appsettings.json");
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SystemConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadRootAsync(cancellationToken);
            var gsi = ParseGsi(root, _configuration);
            return new SystemConfig(gsi);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(SystemConfig config, CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var root = await ReadRootAsync(cancellationToken);
            root.Remove("UseMockSpotify");
            root.Remove("Spotify");
            root.Remove("SmartTrackStart");
            root["Gsi"] = BuildGsiNode(config.Gsi);

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<JsonObject> ReadRootAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogWarning("Config file not found at {Path}, creating new object", _filePath);
            return new JsonObject();
        }

        var content = await File.ReadAllTextAsync(_filePath, cancellationToken);
        var node = JsonNode.Parse(content);
        return node as JsonObject ?? new JsonObject();
    }

    private static GsiConfig ParseGsi(JsonObject root, IConfiguration configuration)
    {
        var gsiNode = root["Gsi"] as JsonObject;
        var method = gsiNode?["Method"]?.GetValue<string>() ?? "POST";
        var path = gsiNode?["Path"]?.GetValue<string>() ?? "/gsi";
        var configuredUrl = gsiNode?["Url"]?.GetValue<string>();
        var fallbackUrls = configuration["Urls"] ?? configuration["ASPNETCORE_URLS"];
        var url = !string.IsNullOrWhiteSpace(configuredUrl)
            ? configuredUrl
            : SelectPreferredUrl(fallbackUrls);

        return new GsiConfig(
            string.IsNullOrWhiteSpace(method) ? "POST" : method.Trim().ToUpperInvariant(),
            NormalizePath(path),
            NormalizeBaseUrl(url));
    }

    private static JsonObject BuildGsiNode(GsiConfig gsi)
    {
        return new JsonObject
        {
            ["Method"] = string.IsNullOrWhiteSpace(gsi.Method) ? "POST" : gsi.Method.Trim().ToUpperInvariant(),
            ["Path"] = NormalizePath(gsi.Path),
            ["Url"] = NormalizeBaseUrl(gsi.Url)
        };
    }

    private static string NormalizePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/gsi" : path.Trim();
        return value.StartsWith('/') ? value : "/" + value;
    }

    private static string? NormalizeBaseUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url)
            ? null
            : url.Trim().TrimEnd('/');
    }

    private static string? SelectPreferredUrl(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return null;
        }

        return urls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeBaseUrl)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
