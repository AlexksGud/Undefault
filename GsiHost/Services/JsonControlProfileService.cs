using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Configuration;
using Core.Models;
using Microsoft.Extensions.Logging;

namespace GsiHost.Services;

public sealed class JsonControlProfileService : IControlProfileService
{
    public const string FlowProfileId = "flow";
    public const string FocusProfileId = "focus";

    /// <summary>
    /// Legacy id from pre-preset <c>control-profiles.json</c> files. Resolves to
    /// <see cref="FlowProfileId"/> when that profile is absent so ActiveProfileId
    /// is not rewritten to an unknown value.
    /// </summary>
    public const string ConsoleDefaultAliasId = "console-default";

    private readonly ILogger<JsonControlProfileService> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public JsonControlProfileService(IWebHostEnvironment environment, ILogger<JsonControlProfileService> logger)
    {
        FilePath = Path.Combine(environment.ContentRootPath, "control-profiles.json");
        _logger = logger;
    }

    public string FilePath { get; }

    public async Task<ConsoleControlProfilesConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath))
            {
                var defaultConfig = BuildDefaultConfig();
                await WriteAsync(defaultConfig, cancellationToken);
                return defaultConfig;
            }

            var content = await File.ReadAllTextAsync(FilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                var defaultConfig = BuildDefaultConfig();
                await WriteAsync(defaultConfig, cancellationToken);
                return defaultConfig;
            }

            var config = JsonSerializer.Deserialize<ConsoleControlProfilesConfig>(
                content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return NormalizeAndValidate(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read control profiles config, using default.");
            return BuildDefaultConfig();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(ConsoleControlProfilesConfig config, CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var normalized = NormalizeAndValidate(config);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(normalized, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task WriteAsync(ConsoleControlProfilesConfig config, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(FilePath, json, cancellationToken);
    }

    private static ConsoleControlProfilesConfig BuildDefaultConfig()
    {
        return new ConsoleControlProfilesConfig(
            FlowProfileId,
            new List<ConsoleControlProfile>
            {
                CreateFlowProfile(),
                CreateFocusProfile()
            });
    }

    private static ConsoleControlProfile CreateFlowProfile()
    {
        return new(
            FlowProfileId,
            "Flow",
            new List<EventControlRule>
            {
                new(EventKeys.RoundStart, MusicControlCommands.Resume),
                new(EventKeys.Death, MusicControlCommands.Pause)
            });
    }

    private static ConsoleControlProfile CreateFocusProfile()
    {
        return new(
            FocusProfileId,
            "Focus",
            new List<EventControlRule>
            {
                new(EventKeys.RoundStart, MusicControlCommands.Pause),
                new(EventKeys.Death, MusicControlCommands.Resume)
            });
    }

    private static ConsoleControlProfilesConfig NormalizeAndValidate(ConsoleControlProfilesConfig? config)
    {
        if (config is null || config.Profiles is null || config.Profiles.Count == 0)
        {
            return BuildDefaultConfig();
        }

        var profiles = config.Profiles
            .Select(NormalizeProfile)
            .ToList();

        var activeProfileId = ResolveActiveProfileId(config.ActiveProfileId, profiles);

        var normalized = new ConsoleControlProfilesConfig(activeProfileId, profiles);
        Validate(normalized);
        return normalized;
    }

    private static string ResolveActiveProfileId(string? requestedId, IReadOnlyList<ConsoleControlProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(requestedId))
        {
            return FindProfileId(profiles, FlowProfileId) ?? profiles[0].Id;
        }

        var trimmed = requestedId.Trim();
        if (FindProfileId(profiles, trimmed) is not null)
        {
            return trimmed;
        }

        if (string.Equals(trimmed, ConsoleDefaultAliasId, StringComparison.OrdinalIgnoreCase)
            && FindProfileId(profiles, FlowProfileId) is { } flowId)
        {
            return flowId;
        }

        return profiles[0].Id;
    }

    private static string? FindProfileId(IReadOnlyList<ConsoleControlProfile> profiles, string id)
    {
        return profiles
            .FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static ConsoleControlProfile NormalizeProfile(ConsoleControlProfile profile)
    {
        var id = string.IsNullOrWhiteSpace(profile.Id)
            ? Guid.NewGuid().ToString("N")
            : profile.Id.Trim();
        var name = string.IsNullOrWhiteSpace(profile.Name)
            ? "Untitled Control Profile"
            : profile.Name.Trim();
        var rules = (profile.Rules ?? new List<EventControlRule>())
            .Select(NormalizeRule)
            .ToList();

        return new ConsoleControlProfile(id, name, rules);
    }

    private static EventControlRule NormalizeRule(EventControlRule rule)
    {
        var eventKey = EventKeys.Normalize(rule.EventKey);
        var command = MusicControlCommands.Normalize(rule.Command);
        var volumePercent = command == MusicControlCommands.Duck
            ? rule.VolumePercent
            : null;

        return new EventControlRule(eventKey, command, volumePercent);
    }

    private static void Validate(ConsoleControlProfilesConfig config)
    {
        var seenProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                throw new ArgumentException("Control profile id is required.", nameof(config));
            }

            if (!seenProfileIds.Add(profile.Id))
            {
                throw new ArgumentException($"Duplicate control profile id '{profile.Id}'.", nameof(config));
            }

            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                throw new ArgumentException($"Control profile '{profile.Id}' must have a name.", nameof(config));
            }

            var seenEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in profile.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.EventKey))
                {
                    throw new ArgumentException(
                        $"Control profile '{profile.Name}' contains a rule with an empty event key.",
                        nameof(config));
                }

                if (!seenEventKeys.Add(rule.EventKey))
                {
                    throw new ArgumentException(
                        $"Control profile '{profile.Name}' contains duplicate event key '{rule.EventKey}'.",
                        nameof(config));
                }

                if (!MusicControlCommands.IsSupported(rule.Command))
                {
                    throw new ArgumentException(
                        $"Control profile '{profile.Name}' event '{rule.EventKey}' uses unsupported command '{rule.Command}'.",
                        nameof(config));
                }

                if (rule.Command == MusicControlCommands.Duck)
                {
                    if (rule.VolumePercent is < 0 or > 100)
                    {
                        throw new ArgumentException(
                            $"Control profile '{profile.Name}' event '{rule.EventKey}' duck volume must be between 0 and 100.",
                            nameof(config));
                    }
                }
            }
        }
    }
}
