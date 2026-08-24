# Stagehand package consumed by Limelight

This directory is an integration boundary, not the Stagehand source project.
Limelight embeds and validates only these generated files:

- `LimelightStagehand.zip`
- `stagehand-payload-manifest.json`

The canonical runtime, public scripting API, samples, documentation, tests, and
package recipe (`stagehand-project.json`) live in the neighbouring
`LimelightStagehand` project. The handoff is regenerated from that recipe as
part of Stagehand development; no PowerShell build script is stored in either
application project.

Do not hand-edit the ZIP or manifest; they are an integrity-checked pair.
