using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Limelight.Models
{
    public sealed class InstalledMod
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } =
            "Unnamed mod";

        public string CustomDisplayName { get; set; } =
            string.Empty;

        public string InstallDirectory { get; set; } =
            string.Empty;

        public List<string> PackageFiles { get; set; } =
            new List<string>();

        public string ContentFingerprint { get; set; } =
            string.Empty;

        public List<ModAssetPackage> AssetPackages { get; set; } =
            new List<ModAssetPackage>();

        public int AssetManifestVersion { get; set; }

        public string CharacterSlotName { get; set; } =
            string.Empty;

        public string CharacterSlotInfoFile { get; set; } =
            string.Empty;

        public string CharacterSlotMeshPackagePath { get; set; } =
            string.Empty;

        public string CharacterSlotDefinitionPackagePath { get; set; } =
            string.Empty;

        public string ArenaSlotName { get; set; } =
            string.Empty;

        public string ArenaSlotInfoFile { get; set; } =
            string.Empty;

        public string ArenaSlotId { get; set; } =
            string.Empty;

        public string ArenaSlotDefinitionObjectPath { get; set; } =
            string.Empty;

        public string ArenaSlotMapPackagePath { get; set; } =
            string.Empty;

        public DateTimeOffset InstalledAt { get; set; } =
            DateTimeOffset.Now;

        public long NexusModId { get; set; }

        public int NexusFileId { get; set; }

        [JsonIgnore]
        public string DisplayName =>
            string.IsNullOrWhiteSpace(CustomDisplayName)
                ? CreateDisplayName(Name)
                : CustomDisplayName.Trim();

        public static string CreateDisplayName(
            string originalName)
        {
            // Nexus archives often append a mod ID, version, timestamp,
            // and download token to the readable mod name.
            string cleanedName =
                originalName.Replace('_', ' ').Trim();

            cleanedName = Regex.Replace(
                cleanedName,
                @"\s+\d+\s+[\d.]+\s+\d{4}-\d{2}-\d{2}T\S+(?:\s+\S+)?$",
                string.Empty,
                RegexOptions.IgnoreCase);

            // Collapse accidental repeated spaces so name comparisons remain reliable.
            cleanedName = Regex.Replace(
                cleanedName,
                @"\s+",
                " ");

            return cleanedName.Trim();
        }

        [JsonIgnore]
        public bool IsActive { get; set; }

        [JsonIgnore]
        public bool IsPlayerCharacterMod =>
            IsCharacterSlotMod ||
            AssetPackages.Any(package =>
                package.IsCharlieAsset);

        [JsonIgnore]
        public bool IsCharacterReplacement =>
            IsPlayerCharacterMod ||
            AssetPackages.Any(package =>
                package.IsCharlieAppearanceAsset);

        [JsonIgnore]
        public bool IsConventionalMod =>
            !IsPlayerCharacterMod &&
            !IsArenaSlotMod;

        [JsonIgnore]
        public bool IsEnabledForNextLaunch { get; set; }

        [JsonIgnore]
        public string LibraryCategoryLabel
        {
            get
            {
                if (IsCharacterSlotMod)
                {
                    return "CHARACTER SLOT";
                }

                if (IsArenaSlotMod)
                {
                    return "ARENA SLOT";
                }

                if (IsCharacterReplacement)
                {
                    return "CHARLIE REPLACEMENT";
                }

                string friendlyTarget =
                    GetFriendlyReplacementTarget();

                return string.IsNullOrWhiteSpace(friendlyTarget)
                    ? "OTHER REPLACEMENT"
                    : $"{friendlyTarget} REPLACEMENT";
            }
        }

        [JsonIgnore]
        public string ReplacementTargetLabel
        {
            get
            {
                if (IsCharacterSlotMod)
                {
                    return "LIVE SWITCHING + LOCKER SLOT";
                }

                if (IsArenaSlotMod)
                {
                    return "INFINITE DISCO ARENA CHOICE";
                }

                if (IsPlayerCharacterMod)
                {
                    return "LIVE SWITCHING AVAILABLE";
                }

                if (IsCharacterReplacement)
                {
                    return "TARGET: CHARLIE  |  NEXT LAUNCH";
                }

                string friendlyTarget =
                    GetFriendlyReplacementTarget();

                return string.IsNullOrWhiteSpace(friendlyTarget)
                    ? "ENABLED FOR THE NEXT LAUNCH"
                    : $"TARGET: {friendlyTarget}  |  NEXT LAUNCH";
            }
        }

        private string GetFriendlyReplacementTarget()
        {
            // I only name targets we have confirmed in the game files so a
            // technical asset name never gets mistaken for another character.
            if (AssetPackages.Any(package =>
                    string.Equals(
                        package.AssetName,
                        "SK_AI_Rebel",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "HEMLOCK";
            }

            if (AssetPackages.Any(package =>
                    package.AssetName.StartsWith(
                        "SK_AI_Prophet_Phase",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        package.AssetName,
                        "SM_Prophet_Mic_Hero",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "PROPHET";
            }

            if (AssetPackages.Any(package =>
                    package.AssetName.StartsWith(
                        "SK_AI_Bouncer",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "BOUNCER";
            }

            if (AssetPackages.Any(package =>
                    package.AssetName.StartsWith(
                        "SK_AI_Shred",
                        StringComparison.OrdinalIgnoreCase) ||
                    package.AssetName.StartsWith(
                        "SK_Shred_",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "DEX";
            }

            if (AssetPackages.Any(package =>
                    package.AssetName.StartsWith(
                        "SK_AI_Doll",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "ARORA";
            }

            if (AssetPackages.Any(package =>
                    string.Equals(
                        package.AssetName,
                        "SM_Drumstick",
                        StringComparison.OrdinalIgnoreCase)))
            {
                return "DRUMSTICK";
            }

            return string.Empty;
        }

        [JsonIgnore]
        public bool IsCharacterSlotMod =>
            !string.IsNullOrWhiteSpace(CharacterSlotName) &&
            !string.IsNullOrWhiteSpace(CharacterSlotInfoFile) &&
            !string.IsNullOrWhiteSpace(CharacterSlotMeshPackagePath) &&
            !string.IsNullOrWhiteSpace(CharacterSlotDefinitionPackagePath);

        [JsonIgnore]
        public bool IsArenaSlotMod =>
            !string.IsNullOrWhiteSpace(ArenaSlotName) &&
            !string.IsNullOrWhiteSpace(ArenaSlotInfoFile) &&
            !string.IsNullOrWhiteSpace(ArenaSlotId) &&
            !string.IsNullOrWhiteSpace(ArenaSlotDefinitionObjectPath) &&
            !string.IsNullOrWhiteSpace(ArenaSlotMapPackagePath);

        [JsonIgnore]
        public string CharacterSlotMeshObjectPath =>
            string.IsNullOrWhiteSpace(CharacterSlotMeshPackagePath)
                ? string.Empty
                : CharacterSlotMeshPackagePath +
                  "." +
                  CharacterSlotMeshPackagePath[
                      (CharacterSlotMeshPackagePath.LastIndexOf('/') + 1)..];

        [JsonIgnore]
        public string CharacterSlotDefinitionObjectPath =>
            string.IsNullOrWhiteSpace(CharacterSlotDefinitionPackagePath)
                ? string.Empty
                : CharacterSlotDefinitionPackagePath +
                  "." +
                  CharacterSlotDefinitionPackagePath[
                      (CharacterSlotDefinitionPackagePath.LastIndexOf('/') + 1)..];
    }
}
