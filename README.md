# Background FPS Limiter

A Mount & Blade II: Bannerlord mod that **caps the framerate while the game window is in the
background** (alt-tabbed or minimized) and restores it the instant you return. Bannerlord
otherwise keeps rendering at full speed when it isn't the active window, needlessly burning
CPU/GPU, battery and fan noise — this fixes that.

## How it works

A single `MBSubModuleBase` with no Harmony patches:

- **Focus detection** polls the real OS foreground window every frame (`GetForegroundWindow`
  + process-id compare). This is deliberately independent of the engine's focus-lost callback,
  which in borderless/windowed-fullscreen frequently never fires on alt-tab — the very reason
  the game keeps running full-speed in the background. If the Win32 API can't be reached (some
  Linux/Proton setups), it falls back once to the engine's `OnConstrainedStateChanged` event.
- **Throttle** sleeps the managed main loop in `OnApplicationTick` down to the target framerate
  while backgrounded (the guaranteed cap, engine-version-independent), and also lowers the
  engine's native `FrameLimiter` so the renderer idles too. Your normal in-focus frame limiter
  is captured and restored on refocus, and never written to disk.

## Configuration (optional)

With [Mod Configuration Menu (MCM)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612)
installed you get a settings page:

- **Enabled** — master toggle (default on)
- **Background FPS** — target framerate while backgrounded (1–60, default **5**)

MCM is a soft dependency: without it the mod runs on those defaults.

## Building

Built against the [BUTR reference assemblies](https://www.nuget.org/packages?q=Bannerlord.ReferenceAssemblies)
via `Bannerlord.BUTRModule.Sdk`, so no local game install is required to compile. The MCM
settings live in a separate `BackgroundFpsLimiter.MCM` project that is built transitively;
build it and it pulls in the core:

```
dotnet build src/BackgroundFpsLimiter.MCM/BackgroundFpsLimiter.MCM.csproj -c Release -p:Platform=x64
```

A local build also deploys into the game's `Modules/BackgroundFpsLimiter` folder via
`scripts\bl-deploy.ps1`. Pass `-p:DeployToGameAfterBuild=false` to skip deployment (as CI does).

## Requirements

- Mount & Blade II: Bannerlord v1.4.6
- [Mod Configuration Menu](https://www.nexusmods.com/mountandblade2bannerlord/mods/612) (optional, for the settings page)
