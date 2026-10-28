# Joint Fires — Plan

## Context
Joint Fires is a larger mod built around the EW-25 Medusa Multi-Crew mod (MC, 0.4.2, repo `ryantogo/EW-25-Medusa-MC`). It adds:
1. **Datalink:** realtime waypoints and retargeting for in-flight guided weapons. A Medusa acts as a C2 relay, keeping friendly players' and AI's munitions guided without line of sight (LOS).
2. **Sensor attribution:** a target selected on the map, or in the cockpit, shows which friendly sensors are detecting it (or last detected it).
3. **Strike mission generation:** a crewed Medusa plans a time-on-target (TOT) strike using friendly AI assets.
4. **Server whitelist:** controls who can take MC seats and who can plan strikes.

The user decided:
- **Datalink is additive.** Vanilla guidance is unchanged. The Medusa adds SARH relay, and in-flight retargeting or waypoints need a link.
- **Strike v1 tasks launchers and artillery first.** AI aircraft come later.

## Decision: separate mod, with MC as a hard dependency (not one merged mod)
- **MC stays small and shippable.** It is close to playable and will be submitted to NOMNOM first. Joint Fires is several times bigger and touches different game systems (missiles, sensors, AI, map UI). Merging would hold MC's release and stability hostage to unfinished JF work.
- **The overlap is narrow.** JF needs only MC's crew roster (who is in which Medusa seat) and a join-policy hook for the whitelist. Everything else is new.
- **Some JF features don't need a crew.** Sensor attribution, datalink and the solo Medusa relay work for anyone. Strike planning is the only part that needs MC's crew roster.
- **Installation stays simple.** NOMM `meta.json` supports `dependencies`, so installing Joint Fires pulls in MC. The BepInEx `[BepInDependency("com.ryantogo.medusamulticrew")]` attribute (hard) makes the load order and a missing MC explicit.
- **Rejected options:**
  - A merged mod: it couples the release cycles.
  - A soft dependency: it adds an extra code path for little gain.
  - A shared core library: it means three packages to version.

## Architecture
- **Repo and build:** new repo `ryantogo/Joint-Fires`, with the same toolchain as MC: net472, BepInEx 5 and Harmony, compiling against the game's Managed folder through `FrameworkPathOverride`. The build references `MedusaMultiCrew.dll` from the game's plugins folder. GUID `com.ryantogo.jointfires`.
- **Networking:** JF copies MC's pattern (`src/Net/CrewNet.cs`): its own hello and ack with a protocol number, a modded-peer set, hand-written `Writer<T>`/`Reader<T>`, and targeted sends only. Unknown message IDs cost unmodded peers `SetError(60)`.
- **The server is the authority** for everything. Missiles, AI and detection all run on the server only. Clients send requests; the server validates them (faction, whitelist, link, ownership or crew) and applies them.

### MC changes needed (MC 0.5.0, small)
- **A public `MedusaMultiCrew.Api` static class:**
  - `TryGetCrew(Aircraft, out players per seat)`.
  - `CrewChanged` event (server and client).
  - `LocalSeat` / `LocalCrewAircraft`.
  - `IsMultiCrewAircraft`.
- **A join-policy hook:** `CrewServer` calls registered `Func<INetworkPlayer, Aircraft, byte seat, bool>` validators in `CheckJoin`.
  - Adds a new `JoinResult.NotAllowed` value, which bumps MC's protocol.
- **No behaviour changes otherwise.**

## Features

### 1. Sensor attribution (first: low risk, read-only, tests JF's networking)
- **Capture (server):** a Harmony prefix on `TargetDetector.DetectTarget(Unit)`, the single place every sensor reports through.
  - `__instance is Radar` tells radar from visual/IRST.
  - `GetAttachedUnit()` gives the platform.
  - Also record callers of `FactionHQ.CmdUpdateTrackingInfo`: laser designator, RWR/`RadarLocator`, `MissileWarning`.
  - Store per faction: `PersistentID target → list of {detector unit id, sensor kind, last time}`. A source counts as live for 4 s, matching the game's `LAST_SPOTTED_EXTRA_TIME`. Keep the last source for "last seen by".
- **Query on demand.** The client sends `SensorQuery(targetId)` when a target is selected or hovered. The server checks the faction and replies `SensorReport`. There is no broadcast.
- **Display:**
  - Postfix on `UnitMapIcon.GetInfoText`, for the map tooltip.
  - An extra line on `TargetMarker`, for the map selection.
  - Postfix on `CombatHUD.ShowTargetInfo`, for the cockpit.
  - Example lines: "Radar: EW-25 Medusa (PlayerX) · Visual: AFV Y".
- **Note:** the game never stores who detected a target. `TrackingInfo` holds only position and time, so this capture is new data.

### 2. Server whitelist
- **List file:** `BepInEx/config/jointfires_whitelist.txt`, read with the game's `NuclearOption.DedicatedServer.AllowBanList.Load` format (one SteamID64 per line, optional `// note`).
- **Config switches:**
  - `Whitelist.Enabled`.
  - `Whitelist.RestrictCrewSeats` (through the MC join hook).
  - `Whitelist.RestrictStrikePlanning`.
  - `Whitelist.RestrictDatalinkCommands`.
  - The host always passes.
- **Identity:** the server reads `Player.SteamID` (`BasePlayer`, set from auth data). On a non-Steam transport the ID is 0: deny when the whitelist is on, and log a warning.
- **Reload:**
  - A `ServerRemoteCommands` command `jointfires-whitelist-reload` on dedicated servers.
  - A hotkey for listen-server hosts.

### 3. Datalink network and Medusa relay (additive)
- **Link model:**
  - A munition is **linked** if its launcher is alive and within `DatalinkRange` with LOS, **or** a friendly Medusa node is (with or without crew).
  - The server computes links every 0.5 s with `TargetCalc.LineOfSight` and caches them.
- **SARH relay:** when the launcher's radar loses the target or LOS, a linked Medusa whose radar tracks the target takes over. Achieved by a postfix on `SARHSeeker.GetTrackingStrength` that swaps the private `radarSource` / `radarSourcePoint` / `detectorUnit` fields to the Medusa's radar.
- **Medusa C2 for ARH midcourse:** when a linked Medusa tracks the target itself, relax `ARHSeeker.DatalinkMode`'s 2000 m accuracy drop to the Medusa's own track. This is optional, controlled by config.
- **The map shows a link state** on friendly missile icons: linked direct, via Medusa, or unlinked.
- **Vanilla guidance is otherwise untouched**, as the user decided.

### 4. Realtime waypoints and retargeting
- **Command:** select one or more friendly in-flight munitions on the map, then right-click a point (waypoint) or a tracked unit (new target). The client sends `MunitionCommand {missileIds, waypoint | targetId}`.
- **Server validation:**
  - The faction matches and the whitelist allows it.
  - The munition is linked (feature 3).
  - The sender is the launcher's pilot or crew, or crew of the linking Medusa.
  - The new target is in the faction's `trackingDatabase`.
- **Apply per seeker type,** because seekers don't re-read `Missile.targetID`. Their private fields are set with Traverse, and `Missile.SetTarget(Unit)` is called so clients' SyncVars update:

  | Seeker | Waypoint | Retarget |
  | --- | --- | --- |
  | `OpticalSeekerCruiseMissile` | Set `aimPos`. It already reverts to the target within 1000 m. Its `ProcessSetDestination` handler exists but is never subscribed. | `targetUnit`, `knownPos` |
  | `BallisticMissileGuidance`, `InertialSeekerShell`, `OpticalSeekerHighDrag` | — | `targetUnit`, `knownPos` |
  | `ARHSeeker` (midcourse only) | — | `targetUnit`, `knownPos` |
  | `OpticalSeeker` with `useDatalink` | — | Retarget |

  Other seekers are not supported: IR and ARM are fire-and-forget, laser is faction-wide already, and bombs.
- **Multi-waypoint routes:** a JF-side queue per missile that feeds `aimPos` the next point when the current one is reached. This is v2.

### 5. Strike mission generation (v1: launchers and artillery)
- **Who:** the crew of a Medusa (MC roster, at least one WSO seated, or the pilot), if whitelisted.
- **UI:** an IMGUI planner on the map, opened with a key while crewing.
  - Pick a tracked target and a TOT, either an absolute time or "now + N s".
  - The server returns the available assets with their flight time; the crew ticks the ones to use and confirms.
- **Asset discovery (server):** friendly `FireControl` batteries (ships, strategic and SAM launchers in strike mode), `Turret`-based artillery and launchers, and `MobileArtilleryAI`. Each must have a weapon that can reach the target (`WeaponInfo.targetRequirements.maxRange`, ammo, `Ready()`).
- **Flight time:**
  - Shells: `Kinematics.TrajectorySim` / `TargetCalc.TargetLeadTime`.
  - Missiles: `range / (0.7 * maxSpeed)`, the game's own estimate from `AimSolver`, refined with `Missile.CalcRange`.
  - Ballistic: `Kinematics.FallTime`.
  - `ArtilleryCalc.GetElevation` is broken in the game, so it's not used.
- **Execute (server):** launch time = TOT − flight time.
  - `FireControl`: fill the private `queuedAttacks` and call `LaunchSalvo` at the launch time.
  - `Turret`: `SetTarget(targetId, station)`, with a `FixedUpdate` fire gate (Harmony prefix) until the launch time.
  - While a unit is tasked, suppress its own retargeting (prefix `ChooseTarget` / `HQTargetAssessment`). Release it afterwards or on abort.
  - The target must stay in the tracking database, which the Medusa provides.
- **Feedback:** mission status goes to the crew: assets launching, time to impact, and the result from `Unit.onDisableUnit`.
  - Map markers on the target and launchers.
  - Abort and retask buttons.
- **Rules:** respect `MissionManager.currentEscalation` against `strategicThreshold` for strategic weapons (config-overridable by the host).
- **v2: AI aircraft.**
  - Prefix `AIPilotCombatModes.AssessHQTargets` / `CombatAI.ChooseHQTarget` to force the target.
  - Gate `Pilot.Fire` to the release window.
  - Route timing from the distance and the aircraft's cruise speed.

## Phases
0. ✅ **MC 0.5.0 API and join hook** (`MultiCrewApi`), then this repo's skeleton and the JF hello/net layer (`src/Net/JfNet.cs`).
1. ✅ **Sensor attribution** (code done in 0.1.0, untested in-game). `src/Sensors/`: capture patches, `SensorServer` table and query, `SensorClient` cache and display patches. Remote players' laser/RWR/MAW reports show as "Reported", since the host can't see which of their sensors sent them.
2. **Whitelist.** Covers MC seats, plus switches for later features.
3. **Datalink link model,** the map link state, and SARH relay.
4. **Waypoints and retargeting.**
5. **Strike planner v1:** launchers and artillery.
6. **Strike planner v2:** AI aircraft.
7. **Release and NOMNOM:** MC first, then JF with `dependencies: [com.ryantogo.medusamulticrew]`.

Each phase ends with a GitHub pre-release with full release notes, as for MC.

## Critical files
- **MC:**
  - `src/Crew/CrewServer.cs`: the join validators, `CheckJoin`.
  - `src/Crew/CrewClient.cs`.
  - `src/Net/CrewMessages.cs`: `JoinResult.NotAllowed`.
  - New `src/Api/MultiCrewApi.cs`.
  - `src/Net/CrewNet.cs`: the template for the JF net layer.
- **Game hook points** (in the game's decompiled `Assembly-CSharp`; never committed here):
  - Sensors: `TargetDetector.cs`, `FactionHQ.cs`, `TrackingInfo.cs`.
  - Map and HUD: `UnitMapIcon.cs`, `TargetMarker.cs`, `DynamicMap.cs`, `CombatHUD.cs`.
  - Missiles: `Missile.cs`, `SARHSeeker.cs`, `ARHSeeker.cs`, `OpticalSeekerCruiseMissile.cs`, `BallisticMissileGuidance.cs`.
  - AI and launchers: `FireControl.cs`, `Turret.cs`, `MobileArtilleryAI.cs`, `AIPilotCombatModes.cs`, `CombatAI.cs`.
  - Flight time: `Kinematics.cs`, `AimSolver.cs`.
  - Server: `NuclearOption.DedicatedServer/AllowBanList.cs`, `ServerRemoteCommands`, `BasePlayer.cs`.

## Verification
- **Each phase:**
  - Host and client with both mods; a single-player loopback test first, as MC's test A.
  - Logs prefixed per feature: `[Sensors]`, `[Datalink]`, `[Strike]`, `[Whitelist]`.
  - An unmodded peer gets no unknown messages.
- **Sensors:** a Medusa radar plus a ground spotter on one target; both appear on hover and select; they expire about 4 s after LOS is lost.
- **Whitelist:** a non-listed client gets "not allowed" on F6 and in the strike planner; after adding them and reloading, they're allowed.
- **Datalink:**
  - Fire a SARH missile, then turn the launcher away while a Medusa tracks the target. The missile keeps guiding.
  - Without the Medusa it goes stale, as in vanilla.
  - Retarget a cruise missile mid-flight, and set a waypoint.
  - An unlinked munition rejects commands.
- **Strike:** plan a TOT with two launchers at different distances; impacts land within ±2 s of the TOT; the strike aborts cleanly if the target is lost.
- **Regression:** with JF installed and unused, vanilla AI, missiles and MC behave as before.

## Risks
- **Private-field coupling** (seekers, `FireControl.queuedAttacks`, AI modes) breaks on game updates. Pin the game version, keep patches narrow, and fail closed with a log line.
- **Flight-time error** for missiles that climb or loft: calibrate per weapon in testing, and allow a per-weapon offset table.
- **Balance:** SARH relay and remote retargeting are strong. Keep everything behind host config switches.
- **The whitelist depends on the Steam transport** for SteamIDs.
