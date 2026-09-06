[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$payloadDirectory = Join-Path $PSScriptRoot '..\..\Payloads\Multiplayer'
$fixtureDirectory = Join-Path $PSScriptRoot 'bin\staging-fixture'
$fixtureScript = Join-Path $PSScriptRoot 'Fixtures\invalid-host.lua'
$fixtureManifest = Join-Path $fixtureDirectory 'multiplayer-manifest.json'
$project = Join-Path $PSScriptRoot 'MultiplayerPayload.csproj'

New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $payloadDirectory 'multiplayer-manifest.json') -Raw | ConvertFrom-Json
$manifest.hostScript.size = (Get-Item -LiteralPath $fixtureScript).Length
$manifest.hostScript.sha256 = (Get-FileHash -LiteralPath $fixtureScript -Algorithm SHA256).Hash
# I keep the hash valid so this fixture reaches staging instead of failing the earlier integrity gate.
[System.IO.File]::WriteAllText($fixtureManifest, ($manifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))

& dotnet run --project $project -c Release --no-restore `
    "-p:HostScriptOverride=$fixtureScript" "-p:ManifestOverride=$fixtureManifest" `
    -- --expect-staging-rollback
exit $LASTEXITCODE
