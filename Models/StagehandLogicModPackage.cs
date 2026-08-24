namespace Limelight.Models
{
    public sealed class StagehandLogicModManifest
    {
        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string ApiVersion { get; init; } = string.Empty;

        public string Entrypoint { get; init; } = string.Empty;

        public string DeclaredTrust { get; init; } = string.Empty;

        public bool NativeCode { get; init; }

        public List<string> Permissions { get; init; } = new();

        public List<string> Capabilities { get; init; } = new();

        public List<StagehandDependency> Dependencies { get; init; } = new();

        public StagehandReviewAttestation? Review { get; init; }
    }

    public sealed class StagehandDependency
    {
        public string Id { get; init; } = string.Empty;
        public string MinimumVersion { get; init; } = "0.0.0";
        public bool Optional { get; init; }
    }

    public sealed class StagehandReviewAttestation
    {
        public int SchemaVersion { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Tool { get; init; } = string.Empty;
        public DateTimeOffset ReviewedUtc { get; init; }
        public string ScriptSha256 { get; init; } = string.Empty;
        public string ManifestSha256 { get; init; } = string.Empty;
    }

    public sealed class StagehandLogicModPackageInspection
    {
        public bool IsStagehandPackage { get; init; }

        public bool IsValid { get; init; }

        public StagehandLogicModManifest? Manifest { get; init; }

        public string Message { get; init; } = string.Empty;

        public bool IsReviewCurrent { get; init; }

        public string ReviewMessage { get; init; } = "Not locally reviewed";
    }

    public sealed record StagehandLogicModInstallResult(
        StagehandLogicModManifest Manifest,
        string InstallDirectory,
        bool Updated);

    public sealed class InstalledStagehandScript
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = "Unknown Stagehand script";
        public string Version { get; init; } = string.Empty;
        public string ApiVersion { get; init; } = string.Empty;
        public string DeclaredTrust { get; init; } = string.Empty;
        public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
        public IReadOnlyList<StagehandDependency> Dependencies { get; init; } = Array.Empty<StagehandDependency>();
        public bool IsEnabled { get; init; }
        public bool IsReviewCurrent { get; init; }
        public bool IsBundled { get; init; }
        public string InstallDirectory { get; init; } = string.Empty;
        public string RecentLog { get; init; } = "No runtime log yet. Launch the game through Limelight to run this script.";
        public string StatusText => IsEnabled ? "ENABLED" : "DISABLED";
        public string ToggleText => IsEnabled ? "DISABLE" : "ENABLE";
        public bool CanRemove => !IsBundled;
        public string RemoveHint => IsBundled
            ? "The bundled proof script is part of the Stagehand runtime. Disable it instead."
            : "Remove this script and its namespaced settings, storage, and log.";
        public string ReviewText => IsReviewCurrent
            ? "EXACT FILES LOCALLY APPROVED"
            : IsBundled
                ? "BUNDLED WITH LIMELIGHT PROTOTYPE"
                : "UNREVIEWED OR CHANGED";
        public string PermissionText => Permissions.Count == 0 ? "No permissions" : string.Join(" · ", Permissions);
        public string CapabilityText => Capabilities.Count == 0
            ? "Capabilities: none declared"
            : "Capabilities: " + string.Join(" · ", Capabilities);
        public string DependencyText => Dependencies.Count == 0
            ? "Dependencies: none"
            : "Dependencies: " + string.Join(
                " · ",
                Dependencies.Select(dependency =>
                    $"{dependency.Id} >= {dependency.MinimumVersion}" +
                    (dependency.Optional ? " (optional)" : string.Empty)));
    }
}
