# Multiplayer contracts

Run from the Limelight repository:

```powershell
dotnet run --project Tests/MultiplayerContracts/MultiplayerContracts.csproj -c Release
```

These offline checks compile the production address policy, friend-code service and models directly. They cover route selection, unsafe address rejection, L1 compatibility and token serialization. They do not launch Steam, change a firewall, install game files, or certify that a network connection works.
