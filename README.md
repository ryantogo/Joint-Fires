# Joint Fires

A BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) that adds depth to the tactical battlespace. It's standalone: single player, co-op and multiplayer, no other mod needed.

- **Strike missions.** Plan a time-on-target strike: pick an enemy, pick friendly ships and missile launchers, pick an impact time. Each launcher fires so everything arrives together.
- **Sensor information.** A target selected on the map (or in the cockpit) shows which friendly sensors detect it, or saw it last.
- **Jamming pods.** +25% range for the Radar Jamming Pod.
- **Coming:**
  - **Datalink:** realtime waypoints and retargeting for guided weapons in flight, with the EW-25 Medusa as a C2 relay.
  - **JF Multicrew:** a small add-on that lets [EW-25 Medusa Multi-Crew](https://github.com/ryantogo/EW-25-Medusa-MC) crews plan strikes and adds a crew-seat whitelist.

**Status: in development, not yet tested in-game.** See [docs/plan.md](docs/plan.md) for the design and phases, and [docs/testing.md](docs/testing.md) for test steps.

## Strike missions

Press **F9** (with the map open, so you have a cursor) to open the strike planner.

1. **Pick a target.** Select an enemy on the map, or target one in the cockpit. Your faction must be tracking it.
2. **Find launchers.** The host lists every friendly ship and launcher whose missiles can reach and suit the target, with ready rounds and estimated flight time.
3. **Choose rounds** per launcher and the **impact time**. The earliest possible time is worked out for you.
4. **Launch.** Each launcher waits, then fires so its missiles arrive at the impact time. Launch trucks raise their launchers first. The missions list shows the countdown and has an **Abort** button.

- Launchers in a strike stop picking their own targets until their rounds are away.
- Flight times start from the game's own estimate and get more accurate as strike missiles reach their targets.
- **Who can plan:** the host, plus players the host **promotes** in the planner window ("Strike planners (host)"). Promotions are saved by SteamID in `BepInEx/config/com.ryantogo.jointfires.strike_planners.txt`.
- **Not used:** guns and artillery (later), nuclear missiles (host setting), and strategic launchers before the mission's escalation threshold (host setting), as with the game's own AI.

## Sensor information

Hover or select an enemy on the map, or target it in the cockpit, to see which friendly units detect it, for example `Radar: EW-25 Medusa (Ryan), Visual/IR: AFV-8`. When nobody sees it any more, you get who saw it last and how long ago. The host records detections. Your game asks only about targets on your screen, and only for your own faction's tracked targets.

## Settings (`BepInEx/config/com.ryantogo.jointfires.cfg`)

| Setting | Default | What it does |
| --- | --- | --- |
| `Strike.PlannerKey` | F9 | Open or close the strike planner |
| `Strike.AllowPromotedPlanners` | on | Host: let promoted players plan strikes |
| `Strike.AllowNuclearWeapons` | off | Host: let strikes use nuclear missiles |
| `Strike.IgnoreEscalation` | off | Host: use strategic launchers before the escalation threshold |
| `Sensors.ShowOnMap` / `ShowOnHud` | on | Sensor line on the map / cockpit HUD |
| `Jamming.PodRangeMultiplier` | 1.25 | Host: jamming pod range. Ignored when Multi-Crew 0.5.1+ is installed; set it there instead |

## Requirements

- BepInEx 5. The host and every player who wants the features need Joint Fires. Players without it are unaffected.
- [EW-25 Medusa Multi-Crew](https://github.com/ryantogo/EW-25-Medusa-MC) is optional.

## Building

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download) and Nuclear Option with BepInEx 5.

```
dotnet build -c Release
```

The build deploys `JointFires.dll` plus a `meta.json` to `<game>/BepInEx/plugins/com.ryantogo.jointfires/`, the layout NOMM uses. If the game isn't in the default Steam folder, pass `-p:GameDir="D:\path\to\Nuclear Option"`, or create `GameDir.props.user`:

```xml
<Project>
  <PropertyGroup>
    <GameDir>D:\SteamLibrary\steamapps\common\Nuclear Option</GameDir>
  </PropertyGroup>
</Project>
```

Add `-p:DeployToGame=false` to build without copying.

## License

GPL-3.0. See [LICENSE](LICENSE). The Rewired input helper is adapted from [SimpleWSO](https://github.com/Motmir/SimpleWSO) (CC0).
