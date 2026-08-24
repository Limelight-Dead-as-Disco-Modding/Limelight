namespace Limelight.Models
{
    public sealed class LocalCompatibilityResult
    {
        public string LimelightVersion { get; init; } =
            "UNKNOWN";

        public string SupportedSteamBuildId { get; init; } =
            string.Empty;

        public string SupportedGameVersion { get; init; } =
            string.Empty;

        public string SupportedGameUpdateName { get; init; } =
            string.Empty;

        public string SupportedGameUpdateReleasedLabel { get; init; } =
            string.Empty;

        public string SupportedBuildPublishedLabel { get; init; } =
            string.Empty;

        public string DetectedSteamBuildId { get; init; } =
            string.Empty;

        public string DetectedGameVersion { get; init; } =
            string.Empty;

        public string NativeBridgeVersion { get; init; } =
            "UNKNOWN";

        public string Ue4ssVersion { get; init; } =
            "UNKNOWN";

        public bool GameConnected { get; init; }

        public bool GameBuildDetected { get; init; }

        public bool GameBuildCompatible { get; init; }

        public bool EmbeddedPayloadCompatible { get; init; }

        public bool Ue4ssInstalled { get; init; }

        public bool Ue4ssCompatible { get; init; }

        public bool Ue4ssConfigured { get; init; }

        public bool LuaBridgeInstalled { get; init; }

        public bool NativeBridgeCurrent { get; init; }

        public bool StagehandCurrent { get; init; }

        public bool IsLiveLoaderCompatible =>
            GameConnected &&
            EmbeddedPayloadCompatible &&
            Ue4ssInstalled &&
            Ue4ssCompatible &&
            Ue4ssConfigured &&
            LuaBridgeInstalled &&
            NativeBridgeCurrent &&
            StagehandCurrent;

        public string DetectedGameLabel
        {
            get
            {
                if (!GameBuildDetected)
                {
                    return "UNKNOWN BUILD";
                }

                string steamBuild =
                    string.IsNullOrWhiteSpace(DetectedSteamBuildId)
                        ? "STEAM BUILD UNKNOWN"
                        : $"STEAM BUILD {DetectedSteamBuildId}";

                string gameVersion =
                    string.IsNullOrWhiteSpace(DetectedGameVersion)
                        ? string.Empty
                        : $" / {DetectedGameVersion}";

                return steamBuild + gameVersion;
            }
        }

        public string Status
        {
            get
            {
                if (!GameConnected)
                {
                    return "NOT CHECKED";
                }

                if (!GameBuildDetected)
                {
                    return "BUILD UNKNOWN";
                }

                if (!GameBuildCompatible)
                {
                    return "GAME UPDATE DETECTED";
                }

                return IsLiveLoaderCompatible
                    ? "READY TO USE"
                    : "REPAIR NEEDED";
            }
        }

        public string Detail
        {
            get
            {
                if (!GameConnected)
                {
                    return "Connect Dead as Disco to check the game and managed Live Loader files.";
                }

                if (!GameBuildDetected)
                {
                    return "Limelight could not identify the installed Dead as Disco update. You can still launch the game, but Live Loader support has not been confirmed for this installation.";
                }

                if (!GameBuildCompatible)
                {
                    return "Dead as Disco has updated since Limelight's last verified build. Launch and repair remain available, but Live Loader behavior may need to be checked again.";
                }

                if (!EmbeddedPayloadCompatible)
                {
                    return "This Limelight build contains an invalid or incompatible managed runtime payload.";
                }

                if (!Ue4ssInstalled)
                {
                    return "The compatible UE4SS runtime is not installed. Use Repair Live Loader below.";
                }

                if (!Ue4ssCompatible)
                {
                    return "The installed UE4SS runtime does not match Limelight's supported build.";
                }

                if (!Ue4ssConfigured)
                {
                    return "The Dead as Disco signatures or loader settings need to be refreshed.";
                }

                if (!LuaBridgeInstalled)
                {
                    return "Limelight's Lua bridge is missing or out of date.";
                }

                if (!NativeBridgeCurrent)
                {
                    return "The installed native bridge is missing or does not match this Limelight build.";
                }

                if (!StagehandCurrent)
                {
                    return "Limelight's managed gameplay-logic runtime is missing or out of date.";
                }

                return "Limelight's Live Loader is ready for this Dead as Disco update.";
            }
        }
    }
}
