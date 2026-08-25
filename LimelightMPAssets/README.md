<h1 align="center">LIMELIGHTMP ASSETS</h1>

<p align="center">
  <strong>The nice-looking bit of my slightly questionable multiplayer experiment.</strong>
</p>

This is the Unreal Engine 5.7 project for LimelightMP's in-game UI. It contains
the loading, reconnect, error, connection and session widgets, plus the little
Limelight logo tying the whole thing together.

The actual networking still lives in `LimelightMP`. This project only handles
presentation, because I would rather a reconnect look intentional than like the
game has quietly exploded.

## Working on it

Open `LimelightMPAssets/LimelightMPAssets.uproject` in Unreal Engine 5.7. The
shipping widgets live under `/Game/LimeLightMP/UI`; contributor-only previews
belong under `/Game/LimeLightMP/Developer` so they do not sneak into the payload
wearing a tiny fake moustache.

Please keep existing asset paths and runtime widget names stable. LimelightMP
finds fields such as `LoadingStageText`, `LoadingDetailText` and `VersionText`
by name, so casual renaming can create extremely professional-looking nothing.

Run `Build.ps1` from this folder to cook the UI and produce the dedicated
`LimelightMPUI.pak`, `.utoc` and `.ucas` files in `Payload`.

## Source control

Commit the `.uproject`, `Config`, `Content` and `.gitignore`. Do not commit
`Saved`, `Intermediate`, `DerivedDataCache` or other generated Unreal crumbs.
The `.uasset` files are binary, so it is best if two people do not wrestle the
same widget at once.

Built for LimelightMP by Henreh <3
