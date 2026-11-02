# Credits

Stepped Streets is by Tevin Benjamin and is licensed under GPL-3.0.

The build setup is adapted from [Placemaker by jackwrichards](https://github.com/jackwrichards/placemaker), GPL-3.0, commit `e654b32`:

- `src/SteppedStreets/SteppedStreets.csproj`: standalone compiler settings and explicit framework/game DLL references, trimmed to remove the toolchain, UI build, git versioning, and deployment.
- `tests/SteppedStreets.Tests/SteppedStreets.Tests.csproj`: .NET 9/xUnit setup and the copy-local Unity.Mathematics reference, changed to reference the separate Core project.

The default game DLL path is shared through `build/GameReferences.props`. No Placemaker runtime or planner code is included in this scaffold.
