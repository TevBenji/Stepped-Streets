# Stepped Streets

Stepped Streets is a Cities: Skylines II code mod by Tevin Benjamin for turning steep street frontages into level lots that step down the hill, like San Francisco or Lisbon. The planned mod adds zoning to each pad and a wall or planted bank at each step, working on one street at a time alongside Placemaker.

**Status: scaffold, not functional.** The mod currently only logs "Stepped Streets loading" on load. The Core planner project is empty; the single smoke test checks that the game's Unity.Mathematics assembly loads under .NET 9.

Build prerequisites: Windows, the installed game's Managed DLLs, the .NET Framework 4.8 targeting pack, the .NET 10 SDK, and the .NET 9 runtime for tests. NuGet restore needs network access.

Run from the repository root:

```powershell
dotnet build SteppedStreets.sln
dotnet test SteppedStreets.sln
```

The default `ManagedPath` is `D:\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed`. Override it for another installation:

```powershell
dotnet build SteppedStreets.sln -p:ManagedPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
dotnet test SteppedStreets.sln -p:ManagedPath="E:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed"
```

Builds use the game DLLs as read-only references. Project output stays in `bin/` and `obj/` under the repository; no deployment targets are included. The mod output is `src/SteppedStreets/bin/Debug/SteppedStreets.dll`, with `SteppedStreets.Core.dll` beside it.

Licensed under [GPL-3.0](LICENSE). See [CREDITS.md](CREDITS.md) for the build setup adapted from Placemaker and [the PRD](docs/Stepped_Streets_PRD.md) for the planned functionality.
