# Multiplayer payload installation checks

Run after building and synchronizing the final multiplayer payload. From the Limelight repository:

```powershell
dotnet run --project Tests/MultiplayerPayload/MultiplayerPayload.csproj -c Release
```

This console harness links the production installer, detector and models, and embeds the actual multiplayer payload. It creates only disposable fake game directories below its build output. It does not copy the retail executable, launch the game, load a DLL, extract the relay to AppData, or modify an installed game.

Checks cover resource hashes, both runtime modules, direct/Steam compatibility-role installation, client tunnel markers, stale-session removal on reinstall, unknown-build native-mode rejection, unmanaged-file protection, partial UI-backup rollback, late transaction rollback, deactivation, and preservation of unrelated loader/mod files.

Positive native-player admission deliberately is not tested: the fake executable must fail the production fingerprint gate. This is installer evidence, not Steam connectivity or gameplay certification.

To verify rejection of altered assembly resources, build and run each fixture separately:

```powershell
dotnet run --project Tests/MultiplayerPayload/MultiplayerPayload.csproj -c Release -p:HostScriptOverride=Fixtures/invalid-host.lua -- --expect-invalid-payload "host script failed its integrity check"
dotnet run --project Tests/MultiplayerPayload/MultiplayerPayload.csproj -c Release -p:ManifestOverride=Fixtures/invalid-manifest.json -- --expect-invalid-payload "manifest is invalid or incompatible"
```

These properties alter only the test assembly's embedded resource, not the production payload files. Run the ordinary command again afterward to rebuild the unmodified fixture. The locked-file rollback case requires Windows file-sharing semantics.

The early-staging regression uses an altered host script with a matching fixture manifest, stored under ignored `bin/staging-fixture`. It first installs the untouched client role through the production installer, then proves a host staging failure preserves that installation:

```powershell
pwsh -File Tests/MultiplayerPayload/Run-StagingRollbackTest.ps1
```

Run the ordinary command again afterward to rebuild with unmodified resources. No production payload source is changed.
