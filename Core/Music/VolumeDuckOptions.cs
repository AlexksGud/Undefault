namespace Core.Music;

public sealed class VolumeDuckOptions
{
    public int MuteVolume { get; init; } = 0;
    public int FallbackRestoreVolume { get; init; } = 50;
}
