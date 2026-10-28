# Joint Fires 0.2.0: single-player test guide

A step-by-step run of everything in 0.2.0 that one player can test alone: loading, sensor lines, the strike planner, strikes, abort, flight-time calibration and the jamming boost. Allow about 45 minutes. Each step says what you should see. Note anything different in the results table at the end.

> **Single player is a host.** The game runs single player as a server with you as its only player. You're the host, so you can always plan strikes, and every "host" setting is yours.

## 0. Before you start

1. **Mods.** In NOMM, enable **Joint Fires 0.2.0**. For the main run, **disable** EW-25 Medusa Multi-Crew and every other mod. Multi-Crew comes back for one check in step 9.
2. **Launch the game once, then quit.** This creates the config file `<game>\BepInEx\config\com.ryantogo.jointfires.cfg`.
3. **Edit the config** (Notepad is fine):
   - `[Strike]` → `IgnoreEscalation = true`. Ballistic-missile trucks (Truck2-TBM) are *strategic* launchers, which the game (and the planner) holds back until the mission's strategic escalation threshold. Turning this on lets you test them straight away. Set it back to `false` afterwards.
   - Leave `AllowNuclearWeapons = false`, so the **Truck2-TBM-N** (nuclear) trucks won't show up.
   - If **F9** already does something in your controls, change `PlannerKey`.
4. **Clear the log.** The game rewrites `<game>\BepInEx\LogOutput.log` on each launch, so you'll send the one from this session.

## 1. A test mission

You need friendly missile launchers, enemies in their range, and a way for your side to *see* those enemies. Strikes only work on targets your faction tracks. Choose one of these options:

**Option A, quick:** your existing mission **"Escalation EW25 AI Test"** already has Destroyer1, Frigate1 and Corvette1 ships, Truck2-TBM trucks with Truck2-FC fire-control trucks, and Truck2-MLRS. It's big, so finding a good target can take a while.

**Option B, recommended:** build a small mission in the **Mission Editor**:
- **Your side:**
  - One **Destroyer1** and one **Frigate1** at sea.
  - One **Corvette1** a bit farther away.
  - Ashore: two **Truck2-TBM** and one **Truck2-FC** near each other.
  - Optionally one **Truck2-MLRS**.
  - A **radarStation1**, or a friendly **AFV/MBT** placed within a few km of the enemy ground targets, so your side spots them.
- **Enemy side:**
  - One or two enemy **ships** (Corvette1 or PatrolBoat1) 40–120 km from your ships.
  - A few enemy **ground units or buildings** (MBT, Truck2-M, a radarStation1) 40–150 km from your TBM trucks.
- Set the ships to **hold position** so distances stay put.
- Save it as **"JF Strike Test"**.

> Which of these units actually show up as strike launchers is one of the things we're testing. The code looks for any friendly unit whose turret holds missiles that suit the target. If a unit you expected is missing, that's a finding, not a mistake on your part.

## 2. Load check

1. Start the mission in **single player**, pick your faction, and stay in the spectator/map view.
2. Open `LogOutput.log` and look for:
   - `Joint Fires 0.2.0 loaded`
   - `[Net] Server handlers registered`, `[Net] Client handlers registered`, `[Net] Sent hello to server`
   - `[Server] ... has Joint Fires (v2)` and `[Client] Server has Joint Fires (v2)`
   - `[Strike] Strike planning allowed for you on this server`

✅ **Pass:** all six lines. ❌ If any is missing, stop and send the log.

## 3. Sensor lines (from 0.1.0)

1. Open the **map** and **hover** over an enemy unit your side can see.
   - The tooltip has an extra line such as `Radar: Destroyer (…)` or `Visual/IR: MBT`.
2. **Click** the enemy to select it.
   - The info block next to it (SPD / ALT / HDG) has a short extra line under HDG, such as `RDR DD1`.
   - Check that it fits the box cleanly. **Take a screenshot.**
3. Wait until nothing sees the target any more, for example a target hidden behind terrain.
   - The line changes to `Last seen 12s ago by …`.

✅ **Pass:** lines appear and make sense. Note any oddities (wrong names, a line that overflows the box).

## 4. Open the planner

1. With the **map open** (so you have a cursor), press **F9**.
   - A window titled **STRIKE PLANNER** appears.
   - It's draggable by its title bar.
2. With nothing selected it says `Target: select an enemy on the map (or target one in the cockpit)`.
3. Select an enemy on the map.
   - The window shows `Target: <unit name>`.
4. Press **F9** again: it closes. Reopen it.

✅ **Pass:** the window opens and closes, can be clicked, and follows your selection.

## 5. Find launchers

1. Select an **enemy ship** and press **Find launchers for this target**.
   - Expect a list. Each row shows a launcher name with its distance (`Destroyer (63 km)`), the weapon, how many rounds are ready, and `flight ~Ns`.
   - The log shows `[Strike] ... asked for launchers on ...: Ok, N found`.
2. Repeat with an **enemy ground target**. Now the TBM trucks (and maybe the MLRS) should be listed.
3. **Write down which units appear, with which weapons, for each target type.** This is the most useful finding of the session.
4. If the answer is `No friendly ship or launcher can reach that target`:
   - Check the distance: each missile has a maximum range.
   - Check the type: anti-ship missiles only suit ships, land-attack missiles only suit ground targets.
   - Note what you expected to see.

## 6. First strike: one launcher

1. On the ship target, give **one launcher 1 round** with **+**.
   - The impact line shows `Impact in N s (earliest N s)`.
2. Press **LAUNCH STRIKE**.
3. Expect:
   - On screen: `Strike planned: 1 rounds, impact in N s`.
   - The **Missions** list shows `#1 <target>: Planned, 0/1 launched, impact in N s`, counting down.
   - At launch time: `Strike launching`, then `Strike in flight`.
   - Log: `[Strike] Mission 1 by ...: 1 rounds from 1 launchers on ...`.
4. Use the external camera on the target (or the map) and **note the game clock when the missile hits**, compared with the impact time.
5. The mission ends as **Complete** or **Target destroyed**.

✅ **Pass:** the missile launches on its own at the planned time, and the mission completes.

## 7. Time-on-target strike: two launchers, different distances

The main feature. Missiles from a near and a far launcher should arrive together.

1. Pick a target that **two launchers at clearly different distances** can reach (for example the Destroyer at 40 km and the Corvette at 90 km, or a ship plus the TBM trucks on a ground target).
2. Give each launcher **1–2 rounds**. Leave the impact time at the earliest, or add 30 s.
3. **LAUNCH STRIKE**, then watch:
   - The **far** launcher fires **first**, and the near one later.
   - TBM trucks **raise their launchers** about 20 s before they fire, and lower them afterwards.
   - The missiles arrive **close together, near the impact time**.
4. Note:
   - How far apart the impacts were (seconds).
   - How early or late they were against the impact time.

✅ **Pass:** impacts within a few seconds of each other and of the impact time. Over ~10 s off is still useful data; send it.

## 8. Abort

1. Plan a strike with the impact time pushed **+60 s** or more.
2. Before anything launches, press **Abort** in the missions list.
   - Expect: `Strike aborted (Aborted by <you>)`.
   - Trucks that had raised their launchers lower them.
   - Those launchers go back to their normal AI.
3. Plan a strike, then **destroy the target yourself** (or let something else do it) before launch.
   - Expect: `Strike aborted (Target destroyed before launch)`.

## 9. Flight-time calibration

1. After any strike in which a missile **reached** the target, look in the log for:
   `[Strike] <weapon>: flight 42.3s vs estimate 51.0s, factor now 0.91`
2. Run the **same launcher on a similar target** again. The `flight ~Ns` in the planner should now be closer to the real flight time, and the impact closer to the planned time.

Calibration resets when you restart the game.

## 10. Jamming pods (+25%)

1. Still **without Multi-Crew**: when any aircraft carrying a Radar Jamming Pod spawns, the log shows `[Jamming] <pod name> range x1.25 (max range now N m)`. You can fly an EW-25 Medusa with jamming pods yourself to trigger it.
2. Optional: fly a Medusa with pods, jam an enemy radar, and note roughly how far away it starts working.
3. **Enable Multi-Crew 0.5.1**, restart, and spawn a pod again. Joint Fires now logs `EW-25 Medusa Multi-Crew applies the jamming pod range boost; Joint Fires leaves it to it`. The `[Jamming] … x1.25` line appears **once per pod type**, from Multi-Crew, not twice.

## 11. Escalation rule (optional)

1. Set `IgnoreEscalation = false` again and restart.
2. On a ground target, **Find launchers**. The Truck2-TBM trucks should **not** be listed, as long as the mission hasn't reached its strategic threshold.

## Results to send back

Fill this in (copy into a message):

| Step | Result | Notes |
| --- | --- | --- |
| 2 Load check | ✅ / ❌ | |
| 3 Sensor lines | | screenshot of the selected-target box |
| 4 Planner window | | |
| 5 Launchers found | | which units/weapons per target type |
| 6 Single strike | | planned vs actual impact time |
| 7 TOT strike | | spread between impacts; early/late |
| 8 Abort | | |
| 9 Calibration | | the `factor now` lines |
| 10 Jamming | | |
| 11 Escalation | | |

Then send `LogOutput.log`, or just the Joint Fires lines. This PowerShell command copies them to the clipboard:

```powershell
Select-String -Path "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\LogOutput.log" -Pattern "Joint Fires|\[Net\]|\[Server\]|\[Client\]|\[Sensors\]|\[Strike\]|\[Jamming\]|Exception" | ForEach-Object Line | Set-Clipboard
```

**Reset after testing:** set `IgnoreEscalation = false` in the config.
