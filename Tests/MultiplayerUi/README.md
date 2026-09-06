# Multiplayer UI smoke test

Run on Windows after building the launcher in Release:

```powershell
dotnet build Limelight.csproj --configuration Release
dotnet run --project Tests/MultiplayerUi/MultiplayerUi.csproj --configuration Release
```

The test references the existing `bin/Release/net8.0-windows/Limelight.dll` instead of rebuilding the launcher. Set the `LimelightAssemblyPath` MSBuild property to test a different build.

It loads the normal application resources and multiplayer page on an STA thread, checks readiness/capability controls and simulated host/client Steam states, and renders five 1100×900 PNGs under the test's ignored `bin/.../renders` directory.

There is no application run loop, visible window, game launch, Steam connection, installer action, or clipboard operation. A startup guard fails before the splash can be created if a dispatcher loop is accidentally introduced. Host/join events are captured only by test callbacks. Invitation values and packet counts are synthetic.

This verifies WPF construction, layout and state presentation—not live transport, launcher-to-game integration, or gameplay.
