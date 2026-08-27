using System.Collections.Generic;

namespace Limelight.Models
{
    // Add future user preferences here so they can all share one settings file.
    public sealed class AppSettings
    {
        public string GameDirectory { get; set; } =
            string.Empty;

        public string ActiveModId { get; set; } =
            string.Empty;

        // I keep the X19 group separate from the main library so users
        // can choose exactly which characters appear in the rotation.
        public List<string> X19LoaderModIds { get; set; } =
            new List<string>();

        // I remember selected profile groups separately so their characters
        // do not also appear in the individual X19 selector.
        public List<string> X19LoaderProfileIds { get; set; } =
            new List<string>();

        // Sequential keeps the hand-picked order. Shuffle chooses a different
        // selected character each time without immediately repeating one.
        public bool X19ShuffleEnabled { get; set; }

        // C is unlikely to conflict with normal gameplay, but the user
        // can replace it from Limelight's Settings page.
        public string X19HotkeyGesture { get; set; } =
            "C";

        // Discord presence is public, so I wait for the user to opt in
        // before Limelight shares any activity with the desktop client.
        public bool DiscordRichPresenceEnabled { get; set; }

        // The resource overlay is optional, so I leave it hidden
        // until the user chooses to monitor Limelight.
        public bool ResourceOverlayEnabled { get; set; }

        // A version number lets a future Limelight update introduce a new
        // tour without repeatedly showing the same guide on every launch.
        public int CompletedTutorialVersion { get; set; }

        // I remember which update card the user has already seen so release
        // notes appear once for each Limelight version, not on every launch.
        public string LastSeenReleaseNotesVersion { get; set; } =
            string.Empty;

        // I remember which update notice has already been acknowledged
        // so users are not prompted for the same update repeatedly.
        public string LastSeenUpdateVersion { get; set; } =
            string.Empty;

        public string PendingDeploymentModId { get; set; } =
            string.Empty;

        public List<string> EnabledConventionalModIds { get; set; } =
            new List<string>();

        public bool ConventionalModsNeedSynchronization { get; set; }

        // I keep a separate catalogue flag because removing the final slot
        // leaves no mod ID to carry the cleanup note home.
        public bool CharacterSlotCatalogueNeedsSynchronization { get; set; }

        public string DismissedLiveLoaderPromptForGameDirectory { get; set; } =
            string.Empty;

        // Profiles are reusable casts. They stay separate from the current
        // X19 rotation until the user chooses to add or apply one.
        public List<ModProfile> ModProfiles { get; set; } =
            new List<ModProfile>();

        public List<InstalledMod> InstalledMods { get; set; } =
            new List<InstalledMod>();
    }
}
