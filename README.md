<p align="center">
  <img src="Assets/README/limelight-logo.png" alt="Limelight logo" width="190">
</p>

<h1 align="center">LIMELIGHT</h1>

<p align="center">
  <strong>Your mods. Your stage.</strong><br>
  A standalone mod manager and live character loader for <em>Dead as Disco</em>.
</p>

<p align="center">
  <a href="https://henreh1.github.io/LimelightWiki/">Documentation</a>
  &nbsp;&bull;&nbsp;
  <a href="https://github.com/Henreh1/Limelight/releases">Releases</a>
  &nbsp;&bull;&nbsp;
  <a href="https://github.com/Henreh1/Limelight/issues">Report an issue</a>
</p>

> [!IMPORTANT]
> Limelight is currently in Early Access. Nexus Mods browsing and direct downloads are temporarily marked as under construction while application registration is reviewed.

## About Limelight

Limelight keeps character model mods organised and lets supported assets switch while *Dead as Disco* is still running. Point it at the game once, import a mod archive, and manage the rest from one themed desktop application.

The Live Loader installs and configures its managed runtime components automatically. Users do not need to manually copy UE4SS, bridge, or staging files into the game.

## The Limelight family

Limelight started as a mod manager and somehow grew into an entire family of
increasingly questionable ideas. The main app keeps everything together while
each mode handles its own part of the show.

<p align="center">
  <img src="Assets/README/LimelightFamily.png" alt="Hand-drawn overview of the Limelight family and its different modes" width="100%">
  <br>
  <em>One launcher, several increasingly questionable ideas.</em>
</p>

## Highlights

| Feature                       | What it does                                                                                                                                       |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| Character library             | Import ZIP, RAR, and 7Z archives, drag and drop mods, rename entries, reject duplicates, and remove mods from one library.                         |
| Normal Live Loader            | Activate a supported character replacement without restarting the game.                                                                            |
| Character Slot Loader support | Detect a slot mod's data asset and unique mesh, keep its original Locker layout, and switch it through Normal Live Loader or X19.                  |
| Arena Slot Loader support     | Detect additive Infinite Disco arenas, deploy their manifests, and install or repair Limelight's standalone shared arena loader automatically.     |
| X19 LLoader                   | Rotate through a chosen cast by keyboard or controller, in order or shuffled.                                                                      |
| LimelightMP (experimental)    | Host or join a private two-PC co-op test where each player renders their own game and the client controls Chuckles through an authenticated relay. |
| Profiles                      | Save reusable groups of characters and assign an entire profile to X19 rotation.                                                                   |
| Asset-aware switching         | Scan each imported Unreal container and refresh the model, materials, textures, portraits, and supported localisation assets it replaces.          |
| Safe switching                | Detect game transitions, block unsafe requests, reuse mounted containers, and show a subtle Limelight pulse while X19 changes character.           |
| Compatibility checks          | Verify the game build, UE4SS runtime, Lua bridge, and native bridge before enabling live switching.                                                |
| Recovery and reports          | Repair managed loader files and create privacy-conscious diagnostic or private test reports.                                                       |
| Windows integration           | Themed dialogs, a themed file explorer, Discord Rich Presence, optional resource monitoring, and a guided first-run tour.                          |

## Screenshots

### Dashboard

<p align="center">
  <img src="Assets/README/screenshots/dashboard.png" alt="Limelight dashboard" width="100%">
</p>

### Character library

<p align="center">
  <img src="Assets/README/screenshots/my-mods.png" alt="Limelight character library" width="100%">
</p>

### Profiles

<p align="center">
  <img src="Assets/README/screenshots/profiles.png" alt="Limelight profiles" width="100%">
</p>

### Live Loaders

<p align="center">
  <img src="Assets/README/screenshots/live-loaders.png" alt="Limelight Live Loaders" width="100%">
</p>

### Subtle X19 feedback

X19 keeps the game view clean. During a character switch, this translucent Limelight mark briefly pulses in the corner instead of covering gameplay with a full status panel.

<p align="center">
  <img src="Assets/README/x19-pulse.png" alt="Limelight X19 switching pulse" width="120">
</p>

## Requirements

- Windows 10 or Windows 11, 64-bit
- A Steam installation of *Dead as Disco*
- Enough free space for Limelight, managed loader files, imported mods, and temporary staging
- The game must be closed while Limelight installs or repairs the Live Loader

The normal installer includes the .NET runtime required by Limelight.

## Getting started

1. Download the latest installer from [Releases](https://github.com/Henreh1/Limelight/releases).
2. Install and open Limelight.
3. Choose the folder containing `Dead as Disco` when prompted.
4. Import a supported mod ZIP, RAR, or 7Z archive, or drag it onto the Limelight window.
5. Open **My Mods** and activate the character you want to use.
6. Select **Launch Game**, then choose Normal Live Loader, X19 LLoader, or launch without live switching.

Limelight keeps its mod library, profiles, settings, reports, and temporary runtime data inside the current Windows account. Uninstalling the application does not silently delete the user's library.

## Live Loader modes

### Normal Live Loader

Use the character library to choose each active mod manually. This is the simplest mode for players who want one character at a time with full switching feedback.

### X19 LLoader

Build a rotation from individual characters or a saved profile, then advance through it with a configurable keyboard or controller button. X19 supports sequential and shuffled rotation, prevents overlapping requests, and limits input handling to *Dead as Disco*.

### No Live Loader

Launch the game without starting live switching. This reduces startup time and resource use when Limelight's runtime features are not needed.

### How the Live Loader works

Limelight detects the selected replacement, identifies its target and sends a
reload request into the running game. UE4SS and the native bridge perform the
Unreal-specific work: mounting the replacement containers, releasing stale
package state, swapping the active mesh and refreshing its materials and
textures. Success or a useful error then returns to Limelight.

<p align="center">
  <img src="Assets/README/LiveLoaderSimple.png" alt="Hand-drawn Live Loader flow from updated replacement files to the running game" width="100%">
  <br>
  <em>The highly advanced Live Loader technical blueprint.</em>
</p>

The drawing uses Charlie as the example on stage, but the same flow applies to
the active replacement target. Limelight can identify supported Charlie, Arora,
Hemlock, Bouncer, Prophet, Dex and other replacement packages rather than
assuming every mod belongs to Charlie.

## Experimental multiplayer

The **Multiplayer** page contains the current LimelightMP v0.1.0 test harness. The host and client each run their own copy of *Dead as Disco*; Limelight installs the appropriate managed role, starts the controller relay without a separate terminal, launches through Steam, and displays a short friend code plus colour-coded session events.

Both players should use the same game build and LimelightMP version. Tailscale currently provides the private route between PCs. Multiplayer remains experimental while level transitions, cameras, dialogue, rhythm sync, player-local menus, audio and replicated effects are hardened. Ordinary dashboard launches disable a leftover multiplayer role before starting Normal or X19 mode.

## Mod compatibility

Limelight is primarily designed for Unreal Engine IoStore character replacements containing matching `.pak`, `.ucas`, and `.utoc` files. Imported archives are validated before entering the library.

Character Slot Loader packages are detected when `info.json` names a character whose matching `PPCD_<CharacterName>` data asset and skeletal mesh are present under `/Game/Pagoda/Characters/Player/ModdedCharacters/<CharacterName>`. Limelight preserves the contained folder needed by the in-game Locker, live-mounts its PPCD definition, and applies that definition through the game's own body-type cosmetic pipeline in Normal Live Loader or X19 instead of requiring an `SK_Charlie` replacement. When Character Slot mods are present, Limelight installs and verifies its own small Character Loader Logic Mod in `Pagoda\Content\Paks\LogicMods`; restart Dead as Disco after its first installation. Existing setups using the original `CharacterLoader.pak`, `.ucas`, and `.utoc` files remain supported as a legacy fallback.

Arena Slot packages keep the familiar `ArenaName` property but also declare a
unique `ArenaId` gameplay tag, a full `ArenaDefinition` object path, and an
`ArenaMap` package path. Limelight verifies that both assets are present,
deploys every arena manifest in its own managed folder, and installs or repairs
the shared `LimelightArenaSlotLoader` Logic Mod and UE4SS script. The loader
adds choices beside Infinite Disco's stock arena without replacing
`DA_Arenas`, `LI_Arenas`, or `Default/LI_Arena_Default`. ArenaName-only and
default-map replacement packages continue to behave as legacy replacements
until their authors give them unique packages and the complete arena-slot
manifest.

Live switching depends on the contents and structure of each mod. A mod that works after restarting the game may still contain assets that Unreal cannot safely replace at runtime. See the [compatibility guide](https://henreh1.github.io/LimelightWiki/mod-compatibility.html) for current details.

## Nexus Mods status

The Nexus catalogue interface, mod detail pages, image carousel, download history, and credential protection have been implemented and privately tested. Public access is paused until Nexus Mods completes Limelight's application registration review.

Limelight does not expose a personal API key in diagnostic reports. Public builds will follow the registered authentication flow required by Nexus Mods.

## Documentation and support

- Read the [Limelight documentation](https://henreh1.github.io/LimelightWiki/)
- Review [troubleshooting and recovery](https://henreh1.github.io/LimelightWiki/troubleshooting.html)
- Create a themed diagnostic or private test report from **Settings > Support**
- Report reproducible problems through [GitHub Issues](https://github.com/Henreh1/Limelight/issues)

Please do not include personal API credentials, private files, or unrelated crash data in a public report.

## Roadmap

Future Limelight architecture is being designed around three connected ideas:
**Passport** describes a mod, **Patchwatch** evaluates it after game updates,
and **Handshake** compares it between multiplayer peers. These are planned as
shared infrastructure behind current reliability and release work, not as
separate apps or promises of unsafe automatic repair.

**Tag Team** is a later rhythm-gameplay experiment built on X19 rosters, the
safe Live Loader activation path and bounded Stagehand scoring and effects. It
will remain behind current Live Loader and Stagehand reliability work.

Read the full [Limelight architecture and roadmap](ROADMAP.md).

## Credits

Created by **Henreh**.

A massive thank you to the people at **Brain Jar Games** for making *Dead as Disco* exist.

Special thanks to the Limelight testers:

- **X19** - Idea of the Live "model refresh" concept and X19 loader mode, and for Early Access testing and feedback.
- **Taxes I Hate Em** - Early Access testing and feedback.
- **Bananas** - First Multiplayer tester and for Early Access testing and feedback.
- **Miles** - Early Access testing and feedback.
- **Bronze_tito** - Early Access testing and feedback, and for helping track down package references used by the improved inspection system.

Limelight also builds on the work of the [RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) and [CUE4Parse](https://github.com/FabianFG/CUE4Parse) projects.

<p align="center">
  <strong>Henreh &lt;3</strong>
</p>
