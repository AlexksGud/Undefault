using Core.Models;
using Core.Music;

namespace Core.Adapters;

public sealed record AdapterObservation(
    GameSnapshot Raw,
    GameClockSnapshot Clock,
    NeutralContext Neutral,
    SafetyFacts Safety);
