using System.Collections.Generic;

namespace Limelight.Models
{
    public sealed class ReleaseNoteItem
    {
        public required string Eyebrow { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Accent { get; init; }
        public required IReadOnlyList<string> Changes { get; init; }
    }

    public sealed class ReleaseNotesContent
    {
        public required string Version { get; init; }
        public required string Eyebrow { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string PublishedLabel { get; init; }
        public required string RangeLabel { get; init; }
        public required IReadOnlyList<ReleaseNoteItem> Items { get; init; }
        public required IReadOnlyList<string> KnownIssues { get; init; }

        // I keep the current release copy together so future updates only
        // need one small file changed before a new Early Access build is packaged.
        public static ReleaseNotesContent CreateCurrent(
            string version)
        {
            return new ReleaseNotesContent
            {
                Version = version,
                Eyebrow = "LIMELIGHT 0.2.0 EARLY ACCESS",
                Title = "THE LIMELIGHT FAMILY UPDATE",
                Summary = "A proper feature update with safer live switching, clearer mod management, built-in slot loaders, a simpler Nexus browser, and another questionable leap forward for Chuckles.",
                PublishedLabel = "27 AUGUST 2026",
                RangeLabel = "CHANGES SINCE 18 AUGUST 2026",
                Items = new List<ReleaseNoteItem>
                {
                    new ReleaseNoteItem
                    {
                        Eyebrow = "NEW",
                        Title = "ARENA SLOT LOADER",
                        Description = "Custom arenas can now join Infinite Disco's arena list instead of replacing the default arena.",
                        Accent = "#35E7FF",
                        Changes = new[]
                        {
                            "Detects complete arena manifests and verifies their definition and map assets.",
                            "Deploys every supported arena together so duplicate IDs cannot depend on import order.",
                            "Installs and repairs Limelight's shared Arena Slot Loader automatically."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "IMPROVED",
                        Title = "CHARACTER SLOT SUPPORT",
                        Description = "Character Slot mods no longer depend on users finding and wiring in a separate loader themselves.",
                        Accent = "#FF3CAC",
                        Changes = new[]
                        {
                            "Bundles Limelight's matching Character Loader Logic Mod.",
                            "Refreshes consumed Character Slot metadata when Limelight opens.",
                            "Only uses the original legacy fallback when its complete actor payload is present."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "CHANGED",
                        Title = "NEXUS WITHOUT API SETUP",
                        Description = "The old Nexus API connection has been retired in favour of a normal embedded browser session.",
                        Accent = "#885CFF",
                        Changes = new[]
                        {
                            "Sign in to Nexus normally inside Limelight's browser.",
                            "Completed ZIP, RAR, and 7Z browser downloads are sent into Limelight's importer.",
                            "No Nexus API key or account token is stored by Limelight."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "IMPROVED",
                        Title = "LIVE LOADER RELIABILITY",
                        Description = "Live switching now follows the gameplay mesh used by the updated game and restores more of Unreal's replacement state.",
                        Accent = "#35E7FF",
                        Changes = new[]
                        {
                            "Refreshes materials, textures, cached packages, portraits, localisation, and string tables.",
                            "Serialises reload commands so overlapping changes cannot race each other.",
                            "Rolls back failed activations and verifies the managed UE4SS runtime before use."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "IMPROVED",
                        Title = "MOD IMPORTS AND CATEGORIES",
                        Description = "My Mods has a better idea of what each package replaces, and archive imports now stay visible while they work.",
                        Accent = "#FF3CAC",
                        Changes = new[]
                        {
                            "Shows progress for ZIP, RAR, and 7Z imports, including multiple archives and duplicate handling.",
                            "Separates character replacements from other mods instead of assuming everything belongs to Charlie.",
                            "Recognises Charlie, Arora, Hemlock, Bouncer, Prophet, Dex, Drumstick, Character Slots, and Arena Slots."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "EXPERIMENTAL",
                        Title = "LIMELIGHTMP 0.1.4",
                        Description = "Chuckles has received another round of local-render multiplayer travel, recovery, and presentation work.",
                        Accent = "#885CFF",
                        Changes = new[]
                        {
                            "Bundles the new LimelightMP interface containers with the host and client payload.",
                            "Improves client travel and recovery after interrupted sessions.",
                            "Strengthens payload verification while keeping both players on their own game copies."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "PREVIEW",
                        Title = "STAGEHAND IS BUILDING THE SET",
                        Description = "The Stagehand runtime and packaging groundwork is present, but its page is deliberately covered while the experience is still being built.",
                        Accent = "#35E7FF",
                        Changes = new[]
                        {
                            "Adds the first managed Stagehand payload and Logic Mod package services.",
                            "Keeps unfinished controls out of normal use until the feature is ready for testing."
                        }
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "POLISH",
                        Title = "CLEANER SETTINGS AND SUPPORT",
                        Description = "Settings now reads like a small control panel instead of one long collection of technical options.",
                        Accent = "#FF3CAC",
                        Changes = new[]
                        {
                            "Adds a permanent General, Live Loader, and Support category rail.",
                            "Separates reports, repairs, compatibility details, and destructive actions.",
                            "Fixes the Nexus tour blocker and lets reports collect UE4SS.log while the game still has it open."
                        }
                    }
                },
                KnownIssues = new[]
                {
                    "Stagehand is under construction and is not ready for normal use.",
                    "LimelightMP remains experimental, especially around cameras, dialogue, rhythm timing, audio, and some level transitions.",
                    "Nexus Mod Manager links are not consumed by Limelight; use Manual Download in the embedded browser.",
                    "Some mods remain next-launch only, and a Dead as Disco update can temporarily move or break internal targets."
                }
            };
        }
    }
}
