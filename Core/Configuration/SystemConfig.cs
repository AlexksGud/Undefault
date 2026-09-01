namespace Core.Configuration;

public sealed record SystemConfig(
    SpotifySystemConfig Spotify,
    GsiConfig Gsi
);

// PKCE flow (UND-47) does not use a client_secret. The shape intentionally omits it so
// the /config endpoint cannot leak a stale value back to a caller.
public sealed record SpotifySystemConfig(
    string ClientId,
    string RedirectUri,
    string[] Scopes
);

public sealed record GsiConfig(
    string Method,
    string Path,
    string? Url
);
