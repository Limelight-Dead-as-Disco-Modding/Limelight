# Limelight 0.2.0 Early Access

## The Limelight Family Update

Limelight started as a simple mod manager. Somehow it now has a Live Loader, a
native bridge, several launch modes, experimental multiplayer, and a remote
second player called Chuckles.

This is a proper feature update rather than a small patch. A lot has changed
since 0.1.0, including several things that were absolutely not part of the
original plan.

## Archive importing should behave now

- ZIP, RAR, and 7Z archives can be imported through the file picker or drag and drop.
- Limelight now shows when an archive is being processed instead of appearing to do nothing.
- Fixed RAR imports getting stuck in an infinite processing state.
- Fixed ZIP archives upsetting the archive reader and throwing an exception.
- Improved importing multiple archives together.
- Improved duplicate-mod detection and error messages.

Large archives can still take a moment to inspect. Limelight should now tell you
that it is working rather than staring at you silently.

## My Mods now has a better idea of what your mod actually replaces

My Mods is now split into character replacements and other replacements.

Limelight can identify confirmed replacements for:

- Charlie
- Arora
- Hemlock
- Bouncer
- Prophet
- Dex
- Drumstick
- CSL character slots

This fixes the problem where Limelight assumed that almost every replacement
mod belonged to Charlie.

Non-character replacements can now show a useful target such as **DRUMSTICK
REPLACEMENT** instead of being left as a completely mysterious mod.

## The Live Loader has been hammered into better shape

The game updated and stopped using the old Charlie mesh during gameplay. That
mesh is now mostly used on the main menu, which meant Limelight could report a
successful switch while changing something nobody could actually see.

Limelight now finds and refreshes the active gameplay mesh instead.

Repeated model swaps also exposed a problem where the first model worked and
every model afterwards became completely black. Meshes are only part of an
Unreal replacement, so the Live Loader now does a better job refreshing
materials, textures, cached packages, portraits, localisation assets, and other
replacement state.

Other Live Loader improvements include:

- Prevented overlapping reload commands from racing each other.
- Improved managed UE4SS startup and runtime verification.
- Recycled temporary resources created during repeated switches.
- Added safer level-transition checks.
- Added rollback when a replacement fails to activate.
- Restored string-table state after a failed switch.
- Improved success and failure reporting.

It is still Unreal Engine runtime modding, so I will not pretend it is
indestructible. It should, however, be considerably less likely to catch fire.

## X19 Loader Mode

X19 still builds on top of the regular Live Loader.

Once the normal loader had been hammered into something solid, X19 was created
so replacements could be rotated using a keyboard keybind or controller button
without returning to Limelight every time.

Ordered groups, shuffled groups, keyboard controls, and controller controls are
all still available.

## LimelightMP and the arrival of Chuckles

LimelightMP remains extremely experimental, but the host and client flow has
received several travel and recovery improvements.

Both players run their own copy of *Dead as Disco*. The host owns the real couch
co-op world, while the client sends authenticated input to the host's virtual
second controller. Unreal then replicates the host-owned world back to the
client so both PCs render the game locally.

No Discord gameplay stream is required anymore.

Player 2 is still called Chuckles. This is not an official game name. I made it
up, it stuck, and I refuse to remove the charm.

> "Is it possible? Am I capable? I intended to answer both."

LimelightMP is still rough. Cameras, dialogue, rhythm synchronisation, audio,
player-local menus, replicated effects, and some level transitions remain works
in progress.

## Smaller fixes

- Fixed the guided tour getting stuck when the Nexus Mods page covered the tour controls.
- Diagnostic reports can now collect `UE4SS.log` while the game still has it open.
- Improved multiplayer payload verification and provenance information.
- Improved recovery after interrupted Live Loader and multiplayer sessions.
- Added clearer compatibility and runtime status information.

## Documentation

- Added the highly scientific Limelight family drawing.
- Added the equally scientific Live Loader blueprint.
- Added the LimelightMP host and client diagram.
- Added the development journey to the wiki.
- Added early development videos showing the first hotswaps, X19 trial, local co-op experiment, and first questionable online session.
- Expanded the Native Bridge and LimelightMP READMEs.

## Thank you

Thank you to X19, Taxes I hate em, Bananas, Langerz, Miles, and Bronze_tito for
testing, feedback, ideas, and repeatedly helping me find things that I had
somehow broken.

Bronze_tito also helped track down package references used by the improved
inspection system.

X19 originally came up with the idea that became X19 Loader Mode.

## Early Access reminders

- Nexus Mods browsing and direct downloads are still being prepared.
- Live switching depends on how each mod packages its Unreal assets.
- Some replacements are supported for the next launch but cannot be switched safely at runtime.
- Game updates can move or change internal assets and temporarily break compatibility.
- LimelightMP is an experiment and should be treated like one.
- Back up personal mod work before testing anything particularly suspicious.

## Included downloads

- `Limelight-0.2.0-early-access-win-x64.zip` contains the standalone portable
  application. Extract the ZIP, then open `Limelight.exe`.
- `Limelight-0.2.0-early-access-win-x64.zip.sha256` can be used to verify the
  download.

Limelight carries and manages its matching Live Loader components itself, so
the native bridge and runtime do not need to be downloaded separately.

Read the documentation at
[henreh1.github.io/LimelightWiki](https://henreh1.github.io/LimelightWiki/).

Most of all, have fun and thank you for downloading Limelight!

The family will probably grow again whether I intend it to or not. :D
