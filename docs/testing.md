# Testing

Everything logs to `BepInEx/LogOutput.log` with the prefixes `[Net]`, `[Server]`, `[Client]` and `[Sensors]`. Run with only Joint Fires and EW-25 Medusa Multi-Crew enabled in NOMM.

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
