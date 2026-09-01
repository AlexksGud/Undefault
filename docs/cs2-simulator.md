# CS2 GSI Simulator

A standalone console + class library that posts realistic CS2-shaped JSON to the
existing `POST /gsi` endpoint on `GsiHost`. It exists so the rest of the stack
(snapshot mapping, diffing, `EventDetector`, `RulesEngine`, Spotify control)
can be exercised without launching CS2.

## Projects

| Project | What it is |
|---------|------------|
| `Cs2Simulator.Scenarios/` | Pure class library: payload models, `SimulationState`, `IScenario`, `ScenarioBase`, `ScenarioCatalog`. No I/O. |
| `Cs2Simulator.Runtime/` | Class library: `IGsiTransport`, `HttpGsiTransport`, `ScenarioRunner`, `Speed`, options. Referenced by the console app and tests (avoids test projects referencing the Exe). |
| `Cs2Simulator/` | Console app: DI bootstrap, interactive CLI, `ConsoleStepGate`. |
| `Cs2Simulator.Tests/` | xUnit tests: catalog discovery, JSON contract via `GsiSnapshotMapper`, per-scenario invariants, runner behavior, end-to-end `EventDetector` fidelity. |

Simulator projects stay `net8.0`. `GsiHost` multi-targets `net8.0-windows10.0.19041.0` and `net8.0` (the latter only so `Cs2Simulator.Tests` can keep referencing host DTOs). `GsiHost.Tests` is Windows-only.

## Quick start

In one terminal:

```powershell
dotnet run --project .\GsiHost -f net8.0-windows10.0.19041.0
```

In a second terminal:

```powershell
dotnet run --project .\Cs2Simulator
```

The interactive menu offers `list`, `run`, `restart`, `speed`, `step`,
`reset host`, and `quit`. Pick a scenario id from the list to run it.

For scripted / CI use, pass `--scenario` (runs that scenario once, then exits):

```powershell
dotnet run --project .\Cs2Simulator -- --scenario t-side-round --speed max
```

`--once` is still accepted for backward compatibility but is redundant with `--scenario`.

## CLI reference

| Flag | Meaning |
|------|---------|
| `--scenario <id>` (`-s`) | Run this scenario once, then exit (see exit codes below). |
| `--speed <1\|2\|5\|max>` | Wall-clock multiplier; `max` = no delay between sends but still cancellable. The simulated `provider.timestamp` baked into each payload is **not** affected by speed. |
| `--step` | Pause between ticks; press ENTER to advance. |
| `--once` | Optional legacy flag; `--scenario` already implies a single run. |
| `--reset` / `--no-reset` | Toggle calling `POST /gsi/reset` before each run. Default is on. |
| `--endpoint <url>` (`-e`) | `GsiHost` base URL (trailing slash optional). Relative paths `gsi` / `gsi/reset` merge correctly when the host is behind a path prefix. Default `http://127.0.0.1:5292`. |
| `--help` (`-h`) | Show usage. |

With no `--scenario`, only the interactive menu runs (CLI defaults still apply to the first run you start from the menu).

### Exit codes (`--scenario` / non-interactive)

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Scenario or transport error |
| 2 | Unknown scenario id |
| 64 | Invalid CLI arguments |
| 130 | Cancelled (Ctrl+C) |

## Scenario catalog

| Id | Name | What it drives |
|----|------|----------------|
| `t-side-round` | T-side round (plant) | seed → warmup→live (`round_start`) → freezetime → live → bomb plant → post-plant → T win |
| `ct-defense` | CT-side defense (defuse) | seed → warmup→live (`round_start`) → contact → bomb planted → retake → defuse → CT win |
| `ct-defense-fail` | CT-side defense (bomb explodes) | same as `ct-defense` but the player dies (`death`) and the bomb explodes |
| `clutch-1v3` | Clutch 1v3 over multiple rounds | seed → 3 rounds (`round_start` per round increment) → 1v3 swing → ace → T win |
| `death-spectator` | Death and spectate | seed → warmup→live (`round_start`) → live → player dies (`death`) → payload switches to spectated teammate → round end |
| `tactical-pause` | Tactical pause and resume | seed → warmup→live (`round_start`) → live → `intermission` → `warmup` → `live` again (`round_start` again) → end |

## Event mapping (what the host actually sees)

`GsiHost` only binds three top-level fields from the GSI JSON: `provider`,
`map`, and `player`. The simulator emits `round`, `weapons`, and `match_stats`
for shape fidelity, but the host's current detectors don't read them. So the
fields that matter to `EventDetector` today are:

| Detector event | Fires when |
|----------------|------------|
| `round_start` | `map.phase` transitions from `warmup`/`intermission` to `live`, OR `map.round` increments. |
| `death` | `player.state.health` goes from > 0 to 0 (subject to a 1 s cooldown). |

`combat` and `idle` are off by default (`appsettings.json`) and aren't reachable
from these payloads anyway, so the simulator marks them payload-shape-only.

### Per-scenario event sequence

| Scenario | Expected normalized events |
|----------|----------------------------|
| `t-side-round` | `round_start` |
| `ct-defense` | `round_start` |
| `ct-defense-fail` | `round_start`, `death` |
| `clutch-1v3` | `round_start` × 3 |
| `death-spectator` | `round_start`, `death` |
| `tactical-pause` | `round_start` × 2 |

Each is asserted by `EventDetectionFidelityTests` end-to-end through
`GsiSnapshotMapper` + `SnapshotDiffer` + `EventDetector`.

`GsiHost.Tests` also runs a smoke test: real `HttpGsiTransport` against
`WebApplicationFactory`, driving `TSideRoundScenario`, and asserts `GET /events`
contains a `round_start`.

## Important runtime details

### Seed-tick rule

`EventDetector.Detect` short-circuits the very first snapshot it sees. Every
scenario's first tick is therefore a non-firing "seed" (e.g.
`map.phase="warmup"`, `map.round=0`). Tick 2 is the first one the detector
will actually evaluate against. `ScenarioBase.Seed(...)` produces this seed
for you and every built-in scenario starts with it.

### Simulated clock

`provider.timestamp` is read by `GsiSnapshotMapper.ResolveTimestamp` and used
as the snapshot's authoritative `Timestamp`, which `EventDetector` uses for
`DeathCooldown` and friends. To keep detector behavior identical at 1×, 2×, 5×,
and `max`, `SimulationState` owns a `SimulatedClock` that advances by each
tick's *nominal* `Delay`. The runner's speed multiplier only affects
`await Task.Delay(...)`, never the clock.

### Host reset

The simulator calls `POST /gsi/reset` before each run by default. The handler
delegates to `IGsiResetService`: it clears the rules engine / snapshot store
**and** the recent-events ring used by `GET /events`, so clients do not see
stale events after a reset.

Set `Gsi:AllowReset` to `false` in `GsiHost` `appsettings.json` to disable the
endpoint (returns 403). Default is `true` for local development.

Use `--no-reset` on the simulator if you want stateful continuation across runs
against a host that still allows reset for other clients.

## Authoring a new scenario

1. Add a new file under `Cs2Simulator.Scenarios/Scenarios/` (one type per
   file by convention). Inherit `ScenarioBase` and implement `IScenario`:

   ```csharp
   public sealed class MyNewScenario : ScenarioBase, IScenario
   {
       public string Id => "my-new-scenario";
       public string Name => "My new scenario";
       public string Description => "...";

       public async IAsyncEnumerable<SimulatedTick> Run(
           SimulationState state,
           [EnumeratorCancellation] CancellationToken ct)
       {
           await Task.Yield();
           yield return Seed(state, TimeSpan.Zero);
           yield return GoLive(state, TimeSpan.FromSeconds(2));
           // ...
       }
   }
   ```

2. Rules:
   - The first yielded tick MUST be `Seed(...)` (or any non-live tick) so the
     detector's first-snapshot suppression doesn't eat your first event.
   - Use the `ScenarioBase` helpers (`GoLive`, `NextRound`, `Freezetime`,
     `RoundLive`, `BombPlant`, `BombDefuse`, `BombExplode`, `Move`, `Die`,
     `RoundOver`, `Custom`). They mutate `SimulationState` and advance the
     simulated clock for you.
   - Public parameterless constructor required — the catalog activates
     scenarios by reflection without DI.
   - Mark expected normalized events using `ScenarioEventKeys.RoundStart`
     or `ScenarioEventKeys.Death`. The runner's logger and the offline
     fidelity tests use this hint.

3. The reflective `ScenarioCatalog` will pick it up automatically. Add a
   matching test under `Cs2Simulator.Tests/` if it asserts a specific event
   order.

`Cs2Simulator/appsettings.json`: `Simulator:LogVerbose` controls whether
per-tick runner logs use Information (true) or Debug (false). Start/finish
lines stay at Information.

## Out of scope

- Multi-player teammate physics, grenades, kill-feed objects beyond what the
  bundled scenarios need.
- Replaying captured GSI traces (could be added later as a `JsonTraceScenario`
  reading a file).
- Mocking Spotify or any host-side behavior — the simulator only feeds the
  host; what happens after `EventDetector` is the real production code.
- Any change to `EventDetector` semantics or to `GsiPayloadDto`'s bound fields.
