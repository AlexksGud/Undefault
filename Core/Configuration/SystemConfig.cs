namespace Core.Configuration;

public sealed record SystemConfig(GsiConfig Gsi);

public sealed record GsiConfig(
    string Method,
    string Path,
    string? Url
);
