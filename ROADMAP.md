# Limelight Architecture and Roadmap

Limelight's immediate priority remains making the features people already use
reliable. Live Loader safety, archive importing, compatibility, diagnostics and
the current experimental multiplayer work come before the ideas below.

Passport and Patchwatch are approved shared-infrastructure directions for the
wider Limelight family. Tag Team is an approved future gameplay experiment
built on X19, Live Loader and Stagehand. None of them should interrupt current
release work or become separate applications.

## The shared architecture

Passport, Patchwatch and Handshake answer three different questions using the
same facts:

| Feature | Question |
|---|---|
| Limelight Passport | What is this mod, what does it replace and what does it need? |
| Limelight Patchwatch | What might a game update have changed or broken? |
| Limelight Handshake | Do both multiplayer peers have a compatible mod setup? |

The intended flow is:

```text
Mod archive or installed mod
          |
          v
Limelight Passport identity and compatibility data
          |
          +--------------------+
          |                    |
          v                    v
Patchwatch update impact   Handshake peer comparison
          |                    |
          v                    v
Human-readable report     Multiplayer readiness result
```

Passport is the common description. Patchwatch evaluates that description
against a changed game build. Handshake compares it between players. None of
the three should invent a second identity for the same mod.

## Limelight Passport

### Direction

Passport will establish one versioned manifest format for mods that choose to
support the Limelight family. It is not a new launcher or a separate app.
Limelight reads and validates it, creators can generate it through a friendly
editor, Handshake uses it for multiplayer comparison, and Patchwatch uses it
for update analysis.

The manifest must include an explicit schema version. A mod's identity must
come from its stable mod ID, never from an archive filename, installation
folder, display name or download name. Optional hashes can strengthen a
comparison, but a hash is evidence about one build of a mod rather than the
mod's permanent identity.

### Proposed Passport data

| Field | Purpose |
|---|---|
| Schema version | Lets Limelight evolve the format predictably |
| Stable mod ID | Permanent identity shared by releases of the same mod |
| Name | Human-readable mod name |
| Author | Creator or maintaining team |
| Mod version | Version of this specific mod release |
| Mod type | Replacement, CSL character, gameplay logic, tool dependency or another confirmed type |
| Supported game versions/builds | Dead as Disco versions or Steam builds the author has tested |
| Dependencies | Other mods or runtimes required for correct behaviour |
| Incompatibilities | Known mods, versions or configurations that should not be combined |
| Replaced assets and targets | Package paths, object paths and friendly targets affected by the mod |
| Multiplayer classification | Required, gameplay, cosmetic or local-only |
| Original source/download URL | Canonical place to find the mod rather than a copied filename |
| Credits and permissions | Attribution, redistribution and modification information supplied by the author |
| Optional content hashes | Evidence for exact files or releases when stronger verification is useful |
| Optional preview metadata | Images, summary text and other presentation information |

### Multiplayer classification

The classification is data for Handshake policy, not a claim that Limelight can
automatically understand every gameplay consequence.

| Classification | Intended meaning |
|---|---|
| Required | Every peer needs a compatible copy for the session to be considered ready |
| Gameplay | Can affect shared behaviour and should receive a strong mismatch warning |
| Cosmetic | Normally allowed to differ when the host's session policy permits it |
| Local-only | Not expected to affect the shared session, but still visible in a detailed comparison |

### Older mods without Passports

Passport support must remain graceful. Older mods should continue to import and
run through the current inspection system.

Limelight can build a provisional local record from inspected assets, package
references, existing metadata and the original source URL when known. That
record must be labelled as inferred rather than author-confirmed. Filenames can
help produce a display name, but must never become the permanent identity used
for dependency, update or multiplayer decisions.

Missing Passport data should normally produce an explanation or warning rather
than rejecting a mod. Hard failure should be reserved for a present Passport
that is invalid in a way that makes its identity or required fields unsafe to
use.

### First Passport milestone

- Publish a small version-one schema and validation rules.
- Read embedded Passports during normal archive inspection.
- Store stable IDs and mod versions in the local library.
- Show author-confirmed data separately from Limelight-inferred data.
- Preserve support for existing mods with no Passport.
- Add a friendly creator editor only after the reader and validator settle.
- Expose one normalized comparison model for both Handshake and Patchwatch.

## Limelight Patchwatch

### Direction

Patchwatch will be an update-impact and mod-survival feature inside Limelight.
When Dead as Disco changes build, it compares a previously recorded structural
snapshot with the new build, checks installed mods and Passports, and explains
which mods have evidence of being safe, possibly affected or broken.

Patchwatch must be conservative. **Safe** means that Patchwatch found no
relevant structural change for the mod. It is not a guarantee that every level
and runtime behaviour still works.

### Snapshot boundaries

The pre-update snapshot records metadata needed for comparison, not game
content for redistribution. It can include build identifiers, asset and package
paths, object types, selected package references, and fingerprints of relevant
runtime or Stagehand hooks.

It must not copy distributable game assets into reports or turn the snapshot
into an alternative game archive.

### Impact classifications

| Result | Meaning |
|---|---|
| Safe | No relevant changed path, reference or hook was detected for the mod |
| Possibly affected | A related package, reference or hook changed, but the evidence is not enough to prove failure |
| Broken | A required asset, target, dependency or hook is confirmed missing or incompatible |

Every result should include human-readable reasons, such as:

- A replaced asset path was removed or renamed.
- A package still exists but its relevant references changed.
- A confirmed target moved to a different gameplay asset.
- A required Stagehand or runtime hook changed.
- The Passport does not list the new game build as supported.
- A dependency or incompatibility rule no longer resolves cleanly.

### First Patchwatch milestone

- Detect and record the current Dead as Disco version and Steam build.
- Capture a pre-update metadata snapshot without redistributing game content.
- Record the installed-mod inventory and available Passport identities.
- Compare changed paths, selected references and relevant runtime hooks.
- Produce Safe, Possibly affected and Broken results with reasons.
- Support Passport-aware classification while retaining inferred checks for older mods.
- Preserve or export a known-good pre-update profile.
- Provide a manual re-scan after the user updates or repairs a mod.
- Generate a report that mod authors can use when preparing an update.

### Repairs and Model Migrator

Patchwatch can recommend an existing tool such as Model Migrator when the
detected change matches a repair that tool understands. A recommendation must
explain the evidence and keep the original mod available for recovery.

Patchwatch must not claim to repair a mod automatically until a specific
transformation has been proven safe. Ambiguous renames, changed references and
runtime hook changes should remain reports for a human rather than guesses
written into somebody's mod.

## Handshake integration

Handshake should compare normalized Passport records rather than filenames.
The comparison can use stable mod ID, mod version, multiplayer classification,
declared dependencies and optional hashes when both peers provide them.

Mods without Passports remain visible as inferred local records. Handshake
should explain that their identity is less certain instead of silently treating
similar filenames as proof that both players have the same mod.

Patchwatch results can also inform Handshake. A required or gameplay mod marked
Broken should prevent a clean readiness result. A Possibly affected mod should
produce a visible warning. Final host policy for cosmetic and local-only
differences can be decided when the Handshake interface is implemented.

## Future gameplay planning boundaries

Tag Team should reuse the Limelight family's existing responsibilities rather
than create a second route for switching characters or controlling the game.

| Component | Responsibility and boundary |
|---|---|
| Limelight Tag Team | Coordinates tag eligibility, safe tag requests, usage tracking and player permissions |
| X19 | Supplies the selected roster, ordering and shuffle rules; it does not activate models itself |
| Live Loader | Remains the only route that may activate a character and may reject any unsafe switch |
| Stagehand | Supplies a trusted beat event, scoring and bounded entrance, HUD or presentation effects |
| Booth | May later request the next eligible tag, but cannot bypass player permissions or safe tag windows |
| Passport | May provide stable roster identities and metadata later, but is not required for the first proof |
| Patchwatch | May warn when a roster mod is affected by a game update after its core comparison work exists |
| Handshake | May compare Tag Team roster identities between peers once multiplayer comparison is ready |

Passport, Patchwatch and Handshake are supporting infrastructure, not blockers
for the first local proof. That proof should depend only on a small X19 roster,
one trusted Stagehand beat event and the existing safe Live Loader path.

## Future gameplay concept: Limelight Tag Team

Tag Team would turn Limelight's existing live character switching into an
optional rhythm-gameplay system rather than treating a switch as a purely
cosmetic action. A player builds a team of two or three installed characters,
earns tag opportunities through play, and receives the best result by changing
character on the beat.

Possible rules include:

- A perfect tag preserves or rewards the current combo.
- An off-beat tag applies no bonus and can optionally reset the tag opportunity.
- Each team member has a separate style or usage meter.
- Repeating one character for too long reduces the variety bonus.
- Tag entrances can trigger bounded Stagehand visual and HUD effects.
- X19 profiles provide the eligible roster and shuffle rules.
- A future Booth role may request the next character without bypassing the
  player's safety settings or the tag window.

The first proof should remain small: define a two-character roster, observe a
safe beat event, request one switch through the existing Live Loader path, and
confirm that the game remains stable across repeated tags and map travel. The
prototype should use scoring and presentation owned by Stagehand; it must not
pretend cosmetic character replacements have different combat abilities.

Tag Team remains a future gameplay experiment. It depends on reliable beat
timing, safe live switching, deduplicated player lifecycle events, and a clear
failure path when a selected character cannot be activated. It must not delay
current Live Loader or Stagehand stability work.

## Delivery order

| Order | Work |
|---|---|
| Current priority | Finish and stabilise existing Limelight, Live Loader, Stagehand and LimelightMP work |
| Foundation | Define Passport version one, validation and legacy fallback |
| Shared model | Let Limelight, Handshake and Patchwatch consume one normalized identity record |
| Creator support | Add the friendly Passport editor and author validation workflow |
| Patchwatch MVP | Add snapshots, manual comparison, classifications and reports |
| Proven assistance | Recommend Model Migrator or another tool only for understood transformations |
| Future experiment | Prototype Tag Team only after beat events and repeated live switching are proven stable |

## Non-goals for the first versions

- Passport will not make old mods stop working just because they lack a manifest.
- Passport permissions will document an author's intent; they are not DRM.
- Patchwatch will not redistribute game content.
- Patchwatch will not call a mod safe when evidence is incomplete.
- Patchwatch will not automatically rewrite mods using guessed repairs.
- Handshake will not use filenames as trusted mod identity.
- Tag Team will not bypass Live Loader safety checks or player permissions.
- Tag Team will not assign combat abilities to cosmetic replacement models.
- These features will not displace current reliability and release work.
