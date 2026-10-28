# Joint Fires

A BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) that adds depth to the tactical battlespace:

- **Datalink.** Realtime waypoints and retargeting for guided weapons in flight. The EW-25 Medusa acts as a C2 junction that keeps friendly players' and AI strikes' munitions guided even without line of sight.
- **Sensor information.** A target selected on the map (or in the cockpit) shows which friendly sensors detected it or are detecting it.
- **Strike missions.** EW-25 multi-crews plan time-on-target strikes with friendly AI launchers, artillery and (later) aircraft.
- **Server whitelist.** Limits who can crew a Medusa and who can plan strikes.

**Status: in development.** Sensor information is written (phase 1) but not yet tested in-game; the rest is planned. See [docs/plan.md](docs/plan.md) for the design and phases, and [docs/testing.md](docs/testing.md) for test steps.

## Sensor information

Hover or select an enemy on the map, or target it in the cockpit, to see which friendly units detect it: `Radar: EW-25 Medusa (Ryan), Visual/IR: AFV-8`. When nobody sees it any more you get who saw it last and how long ago. The host records detections; your game asks only about targets on your screen, and only for your own faction's tracked targets. Turn the map or HUD line off with `Sensors.ShowOnMap` / `Sensors.ShowOnHud` in `BepInEx/config/com.ryantogo.jointfires.cfg`.

## Requirements

- [EW-25 Medusa Multi-Crew](https://github.com/ryantogo/EW-25-Medusa-MC) 0.5.0 or newer. Joint Fires won't load without it.
- The host and every player who uses the features need both mods installed.

## Building

Requirements: the [.NET 8 SDK](https://dotnet.microsoft.com/download), Nuclear Option with BepInEx 5, and Multi-Crew 0.5.0+ installed in the game (the build references its DLL).

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

Add `-p:DeployToGame=false` to build without copying. Use `-p:MultiCrewDll=<path>` to build against a Multi-Crew DLL elsewhere.

## License

GPL-3.0. See [LICENSE](LICENSE).
