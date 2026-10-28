# Joint Fires — Plan

## Context
Joint Fires (JF) adds depth to Nuclear Option's tactical battlespace. It is a **standalone mod**: it works in single player, co-op and multiplayer without any other mod.
1. **Sensor attribution:** a target selected on the map, or in the cockpit, shows which friendly sensors are detecting it (or last detected it).
2. **Strike missions:** plan a time-on-target (TOT) strike with friendly AI ships and missile launchers.
3. **Datalink:** realtime waypoints and retargeting for in-flight guided weapons. A Medusa acts as a C2 relay, keeping friendly munitions guided without line of sight (LOS).
4. **Jamming pods:** +25% range (host setting).

The user decided:
- **Datalink is additive.** Vanilla guidance is unchanged. The Medusa adds SARH relay, and in-flight retargeting or waypoints need a link.
- **Strike v1 tasks ships and missile launchers.** Guns, artillery and AI aircraft come later.
- **Strike planning is host-only by default.** The host can promote players; promotions are a whitelist file.

## Decision (2026-10-28): standalone Joint Fires + a separate "JF Multicrew" compatibility mod
- **Joint Fires has no Multi-Crew (MC) dependency.** Sensors, datalink and strikes don't need crew seats, so single-player and co-op players get them without MC.
- **JF Multicrew** (a later, small mod; needs both MC and JF) adds the crew-specific pieces:
  - Medusa crews (pilot or WSOs) can plan strikes.
  - A whitelist for MC crew seats, through MC's join-policy hook (`MultiCrewApi.AddJoinPolicy`, MC 0.5.0).
  - Anything else tied to crew seats.
- **Jamming pod boost:** MC 0.5.1+ applies it. JF applies the same patch only when MC 0.5.1+ isn't installed (soft dependency, checked at load), so it never stacks.

## Architecture
- **Repo and build:** `ryantogo/Joint-Fires`, with the same toolchain as MC: net472, BepInEx 5 and Harmony, compiling against the game's Managed folder through `FrameworkPathOverride`. GUID `com.ryantogo.jointfires`.
- **Networking:** MC's pattern (`src/Net/JfNet.cs`): its own hello and ack with a protocol number, a modded-peer set, hand-written `Writer<T>`/`Reader<T>`, and targeted sends only. Unknown message IDs cost unmodded peers `SetError(60)`.
- **The server is the authority** for everything. Missiles, AI and detection all run on the server only. Clients send requests; the server validates them (faction, permission, link, ownership) and applies them.

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

### 2. Strike planner permissions
- The host can always plan. Other players need a promotion from the host (planner window, "Strike planners (host)").
- Promotions are saved to `BepInEx/config/com.ryantogo.jointfires.strike_planners.txt`: one SteamID64 per line, optional `// note`, the game's ban-list format. Hand-editable on a dedicated server.
- `Strike.AllowPromotedPlanners` (host) turns promotions off.
- Identity is `Player.SteamID`, which is 0 off the Steam transport, so promotions only work on Steam.
- The MC crew-seat whitelist moves to JF Multicrew.

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

### 5. Strike missions (v1: ships and missile launchers), 0.2.0
- **Who:** the host, plus promoted players (feature 2). JF Multicrew will add Medusa crews.
- **UI:** `StrikePlannerWindow` (IMGUI, `Strike.PlannerKey` F9). Use it with the map open, where the cursor is free.
  - Target: the selected enemy map icon, else the HUD's primary target. It must be tracked by your faction.
  - "Find launchers" asks the server. It replies with each usable station: name, weapon, ready rounds and estimated flight time.
  - Choose rounds per launcher and the impact time (earliest is computed), then launch.
  - The missions list shows state, launched/planned, a countdown, and an Abort button.
- **Asset discovery (server, `StrikeLaunchers`):**
  - Every `Turret` of the planner's faction whose station holds `MissileLauncher` weapons with ammo.
  - The firing unit is the turret's `FireControl` unit (batteries, ships) or the turret's own unit.
  - Excluded: non-missile weapons, troops, cargo, jammers, energy weapons, nuclear missiles unless `Strike.AllowNuclearWeapons`, and strategic launchers below the escalation threshold unless `Strike.IgnoreEscalation`.
  - Suitability: `targetRequirements` (range, target speed and altitude) plus the game's own `WeaponStation.CalcOpportunityThreat`.
- **Flight time:**
  - Starts from the game's estimate: range / (0.7 x top speed), as in `AimSolver`.
  - Calibrated per weapon from strike missiles that end within 300 m of the target. The factor is clamped and smoothed.
- **Execute (server, `StrikeServer`):**
  - Each round launches at TOT - flight time. A station's rounds are centred on the TOT at its fire interval.
  - Rounds fire with `WeaponStation.Fire(owner, target)`, the same call `FireControl.LaunchSalvo` uses.
  - Launch trucks are raised with `GroundVehicle.RpcDeployFireControl` 20 s before their first round and stowed afterwards.
  - A round that can't fire within 10 s of its time is dropped.
  - Reserved launchers: `Turret.ChooseTarget` / `Turret.FixedUpdate` / `FireControl.HQTargetAssessment` are blocked until the launches finish.
- **Outcomes:** Complete; TargetDestroyed; Aborted (by the planner or the host, or the target destroyed before launch); Failed (nothing could fire).
- **v2:**
  - Guns and artillery (they need aiming and ballistic flight times).
  - AI aircraft: prefix `AIPilotCombatModes.AssessHQTargets` / `CombatAI.ChooseHQTarget` to force the target, and gate `Pilot.Fire` to the release window.
  - Map markers on the target and launchers.

## Phases
0. Done: JF skeleton and the hello/net layer. MC 0.5.0 `MultiCrewApi` (for JF Multicrew).
1. Done: **Sensor attribution** (0.1.0, untested in-game). `src/Sensors/`: capture patches, the `SensorServer` table and query, the `SensorClient` cache and display patches. Remote players' laser/RWR/MAW reports show as "Reported", because the host can't see which of their sensors sent them.
2. Done: **Standalone + strike missions v1 + jamming boost** (0.2.0, untested in-game). The MC dependency is removed; `src/Strike/` and `src/Jamming/` are added. MC 0.5.1 carries the same jamming boost.
3. **Datalink link model**, the map link state, and SARH relay.
4. **Waypoints and retargeting.**
5. **Strike v2:** guns, artillery and AI aircraft.
6. **JF Multicrew** compatibility mod: Medusa crew planning and the crew-seat whitelist. Needs a new repo, which the user creates.
7. **Release and NOMNOM:** MC, then JF, then JF Multicrew.

Each phase ends with a GitHub pre-release with full release notes, as for MC.

## Critical files
- **JF:** `src/Net/` (messages, serializers, `JfNet`), `src/Sensors/`, `src/Strike/`, `src/Jamming/`.
- **MC (for JF Multicrew):** `src/Api/MultiCrewApi.cs`; `src/Patches/JammingPodRangePatch.cs`.
- **Game hook points** (in the game's decompiled `Assembly-CSharp`; never committed here):
  - Sensors: `TargetDetector.cs`, `FactionHQ.cs`, `TrackingInfo.cs`.
  - Map and HUD: `UnitMapIcon.cs`, `TargetMarker.cs`, `DynamicMap.cs`, `CombatHUD.cs`.
  - Missiles: `Missile.cs`, `SARHSeeker.cs`, `ARHSeeker.cs`, `OpticalSeekerCruiseMissile.cs`, `BallisticMissileGuidance.cs`.
  - AI and launchers: `FireControl.cs`, `Turret.cs`, `MobileArtilleryAI.cs`, `AIPilotCombatModes.cs`, `CombatAI.cs`.
  - Flight time: `Kinematics.cs`, `AimSolver.cs`.
  - Server: `NuclearOption.DedicatedServer/AllowBanList.cs`, `ServerRemoteCommands`, `BasePlayer.cs`.
  - Strikes: `WeaponStation.cs`, `MissileLauncher.cs`, `Spawner.cs`, `GroundVehicle.RpcDeployFireControl`, `MissionManager` escalation.
  - Jamming: `JammingPod.cs` (`rangeFalloff`).

## Verification
- **Each phase:**
  - Host and client with both mods; a single-player loopback test first, as MC's test A.
  - Logs prefixed per feature: `[Sensors]`, `[Datalink]`, `[Strike]`, `[Whitelist]`.
  - An unmodded peer gets no unknown messages.
- **Sensors:** a Medusa radar plus a ground spotter on one target; both appear on hover and select; they expire about 4 s after LOS is lost.
- **Planner permission:** a non-promoted client sees "Only the host and players the host promoted can plan strikes"; after the host promotes them, they can plan.
- **Datalink:**
  - Fire a SARH missile, then turn the launcher away while a Medusa tracks the target. The missile keeps guiding.
  - Without the Medusa it goes stale, as in vanilla.
  - Retarget a cruise missile mid-flight, and set a waypoint.
  - An unlinked munition rejects commands.
- **Strike:** plan a TOT with two launchers at different distances; impacts land within ±2 s of the TOT; the strike aborts cleanly if the target is lost.
- **Regression:** with JF installed and unused, vanilla AI and missiles behave as before, apart from the jamming pod range.

## Risks
- **Private-field coupling** (seekers, `FireControl.queuedAttacks`, AI modes) breaks on game updates. Pin the game version, keep patches narrow, and fail closed with a log line.
- **Flight-time error** for missiles that climb or loft: calibrate per weapon in testing, and allow a per-weapon offset table.
- **Balance:** SARH relay and remote retargeting are strong. Keep everything behind host config switches.
- **Promotions depend on the Steam transport** for SteamIDs.
