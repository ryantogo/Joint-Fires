# Testing

Everything logs to `BepInEx/LogOutput.log` with the prefixes `[Net]`, `[Server]`, `[Client]`, `[Sensors]`, `[Strike]` and `[Jamming]`. Run with only Joint Fires enabled in NOMM (Multi-Crew optional).

## Phase 2: strike missions and jamming (v0.2.0)

### S1. Single player
1. Host a mission with friendly **missile ships** and/or **ground missile launchers** (cruise-missile or anti-ship trucks), and enemy targets in their range. The mission editor helps.
2. Expect in the log: `Joint Fires 0.2.0 loaded`, and on mission start `[Strike] Strike planning allowed for you on this server`.
3. Open the map, select a tracked enemy, press **F9**. The planner shows `Target: <name>`.
4. Press **Find launchers for this target**. Expect a list of launchers with weapon, ready rounds and flight time, plus `[Strike] ... asked for launchers on ...: Ok, N found` in the log. If it says "No friendly ship or launcher can reach that target", note which launchers you expected and send the log.
5. Give two launchers at **different distances** 1-2 rounds each, leave the impact time at the earliest, and press **LAUNCH STRIKE**.
6. Expect:
   - "Strike planned: N rounds, impact in X s" on screen.
   - Launch trucks raise their launchers about 20 s before their first round.
   - The farther launcher fires first.
   - All missiles arrive **close to the impact time** (please note how close: within a few seconds?).
   - The missions list shows Planned, then Launching, In flight and finally Complete or Target destroyed.
7. Plan another strike and press **Abort** before launch. Launchers stow and go back to their normal AI.
8. After a strike that reached its target, the log shows `[Strike] <weapon>: flight Xs vs estimate Ys, factor now Z`. The next strike with that weapon should be closer.

### S2. Two players
1. The client presses F9: "Only the host and players the host promoted can plan strikes".
2. The host opens F9, ticks **Strike planners (host)** and presses **Promote** next to the client. The client can now plan. The file `BepInEx/config/com.ryantogo.jointfires.strike_planners.txt` on the host lists their SteamID.
3. The client plans a strike. Both see the mission in their list. Either the client or the host can abort it.
4. **Demote** the client. They can't plan new strikes.

### J1. Jamming pods
1. Without Multi-Crew: the log shows `[Jamming] Radar Jamming Pod range x1.25 (max range now N m)` when a jamming pod loads.
2. With Multi-Crew 0.5.1+: Joint Fires logs `EW-25 Medusa Multi-Crew applies the jamming pod range boost; Joint Fires leaves it to it`, and the `[Jamming]` range line comes from Multi-Crew only (once per pod type, not twice).
3. In flight, a pod should now jam a radar from about 25% farther than before.

## Phase 1: sensor attribution (v0.1.0)

Joint Fires shows which friendly units detect an enemy target. The host records every detection (radar, visual/IR, missile seekers, laser designators, RWR and missile warning). Your game asks the host about a target only while one of these three places shows it:

| Where | What it shows |
| --- | --- |
| **Map tooltip** (hover an enemy icon) | `Radar: EW-25 Medusa (Ryan), Visual/IR: AFV-8` |
| **Selected-target box** on the map (the SPD/ALT/HDG block) | A compact line under HDG: `RDR EW25 VIS AFV8` |
| **Cockpit HUD**, next to your primary target | The same compact line under the range |

A source counts as **detecting now** for 4 s after its last detection, the same window the game uses for "being tracked". When nobody sees the target any more, the line becomes `Last seen 42s ago by Radar: ...` (compact: `42s RDR ...`). Sources are forgotten after 2 minutes.

Kinds: Radar (RDR), Visual/IR (VIS), Seeker (SKR, a missile's own seeker, named after the shooter), Laser (LSR), RWR, MAW (missile approach warning). A remote player's laser/RWR/missile-warning reports can't be told apart on the host and show as **Reported (RPT)**.

### A1. One PC (host)
1. Host a mission with enemy ground units and a friendly AI or player radar aircraft, e.g. a Medusa.
2. Expect in the log: `Joint Fires 0.1.0 loaded`, `[Net] Server handlers registered`, `[Net] Client handlers registered`, `[Net] Sent hello to server`, `[Server] ... has Joint Fires`, `[Client] Server has Joint Fires`.
3. Open the map and hover a tracked enemy. The tooltip has a sensor line naming the friendly unit(s) that see it.
4. Click it to select. The info block shows the compact line under HDG.
5. Fly an aircraft, lock or select an enemy. The HUD target info shows the compact line.
6. Let the target drop out of sight. Within ~5 s the line changes to "Last seen ...".

### B1. Two players
1. Both run both mods. The client hovers and selects targets as in A1.
2. The client's own aircraft radar should appear as `Radar: <aircraft> (<client name>)`. Their laser/RWR reports show as `Reported`.
3. The client must never see the line for an enemy their faction isn't tracking, and never see the other faction's sensors.

### C1. Mod missing
- **Host without Joint Fires:** the client logs `Sent hello to server` but never `Server has Joint Fires`. No sensor lines appear. Nothing else changes.
- **Client without Joint Fires:** nothing is sent to them.

**Settings:** `Sensors.ShowOnMap` and `Sensors.ShowOnHud` in `BepInEx/config/com.ryantogo.jointfires.cfg` turn each display off.
