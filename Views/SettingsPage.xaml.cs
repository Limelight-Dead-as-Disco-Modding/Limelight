using Limelight.Models;
using Limelight.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class SettingsPage : UserControl
    {
        private bool _discordPresenceEnabled;
        private bool _resourceOverlayEnabled;
        private bool _isUpdatingResourceOverlay;

        public event Action? RepairRequested;
        public event Action? PurgeAllModsRequested;
        public event Action? CreatePrivateTestReportRequested;
        public event Action? ExportDiagnosticsRequested;
        public event Action? ChangeGameFolderRequested;
        public event Action? NativeTestRequested;
        public event Action<string>? NexusConnectRequested;
        public event Action? NexusDisconnectRequested;
        public event Action<bool>? DiscordPresenceChanged;
        public event Action<bool>? ResourceOverlayChanged;

        public SettingsPage()
        {
            InitializeComponent();

        }

        public void ShowDiscordPresence(
            bool enabled)
        {
            _discordPresenceEnabled =
                enabled;

            DiscordPresenceStatusText.Text =
                enabled
                    ? "SHARING ACTIVITY"
                    : "PRIVATE";

            DiscordPresenceStatusText.Foreground =
                StatusBrush(enabled);

            DiscordPresenceDetailText.Text =
                enabled
                    ? "Limelight will update Discord while the desktop client is open. No Discord login or secret is stored."
                    : "Enable this to share Limelight, game, active-mod, X19, and multiplayer activity on your Discord profile.";

            DiscordPresenceButton.Content =
                enabled
                    ? "DISABLE PRESENCE"
                    : "ENABLE PRESENCE";
        }

        public void ShowResourceOverlay(
            bool enabled)
        {
            _isUpdatingResourceOverlay = true;
            _resourceOverlayEnabled = enabled;

            ResourceOverlayToggleButton.IsChecked =
                enabled;

            ResourceOverlayStatusText.Text =
                enabled
                    ? "VISIBLE"
                    : "DISABLED";

            ResourceOverlayStatusText.Foreground =
                StatusBrush(enabled);

            _isUpdatingResourceOverlay = false;
        }

        private void ResourceOverlayToggleButton_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_isUpdatingResourceOverlay)
            {
                return;
            }

            bool enabled =
                ResourceOverlayToggleButton.IsChecked == true;

            _resourceOverlayEnabled = enabled;

            ResourceOverlayStatusText.Text =
                enabled
                    ? "VISIBLE"
                    : "DISABLED";

            ResourceOverlayStatusText.Foreground =
                StatusBrush(enabled);

            ResourceOverlayChanged?.Invoke(enabled);
        }

        public void ShowStatus(
            string? gameDirectory,
            bool isGameRunning,
            LiveSessionState session,
            LiveSessionCleanupResult stagingSnapshot)
        {
            bool isConnected =
                !string.IsNullOrWhiteSpace(gameDirectory);

            GameConnectionStatusText.Text =
                isConnected
                    ? "CONNECTED"
                    : "NOT CONNECTED";

            GameConnectionStatusText.Foreground =
                StatusBrush(isConnected);

            GameConnectionDetailText.Text =
                isConnected
                    ? gameDirectory
                    : "Choose the Dead as Disco installation folder.";

            GameRunningBadgeText.Text =
                isGameRunning
                    ? "RUNNING"
                    : "NOT RUNNING";

            GameRunningBadgeText.Foreground =
                StatusBrush(isGameRunning);

            SessionStatusText.Text =
                session.Status.ToString().ToUpperInvariant();

            bool sessionHealthy =
                session.Status is LiveSessionStatus.Active or
                    LiveSessionStatus.Initialising or
                    LiveSessionStatus.Switching;

            SessionStatusText.Foreground =
                StatusBrush(sessionHealthy);

            int mountedContainers =
                LiveSessionService.CountMountedContainers(
                    session);

            MountedContainerText.Text =
                $"{mountedContainers} MOUNTED • AUTO-RECYCLE";

            MountedContainerText.Foreground =
                StatusBrush(true);

            SessionDetailText.Text =
                CreateSessionDetail(
                    session,
                    isGameRunning);

            StagingText.Text =
                stagingSnapshot.DeletedFileCount == 0
                    ? "No staged files are waiting for cleanup."
                    : $"{stagingSnapshot.DeletedFileCount} staged file(s), " +
                      $"{FormatBytes(stagingSnapshot.DeletedBytes)}. Limelight cleans these after the game closes.";

            RepairButton.IsEnabled =
                isConnected &&
                !isGameRunning;

            RepairButton.Opacity =
                RepairButton.IsEnabled
                    ? 1
                    : 0.45;

            ChangeGameFolderButton.IsEnabled =
                !isGameRunning;

            ChangeGameFolderButton.Opacity =
                ChangeGameFolderButton.IsEnabled
                    ? 1
                    : 0.45;

            NativeTestButton.IsEnabled =
                isConnected &&
                isGameRunning;

            NativeTestButton.Opacity =
                NativeTestButton.IsEnabled
                    ? 1
                    : 0.45;
        }

        public void ShowCompatibility(
            LocalCompatibilityResult compatibility)
        {
            CompatibilityUpdateTitleText.Text =
                string.IsNullOrWhiteSpace(
                    compatibility.SupportedGameUpdateName)
                    ? "CURRENT DEAD AS DISCO UPDATE"
                    : compatibility.SupportedGameUpdateName;

            CompatibilityReleaseDateText.Text =
                string.IsNullOrWhiteSpace(
                    compatibility.SupportedGameUpdateReleasedLabel)
                    ? "DATE UNAVAILABLE"
                    : compatibility.SupportedGameUpdateReleasedLabel;

            CompatibilityBuildDateText.Text =
                string.IsNullOrWhiteSpace(
                    compatibility.SupportedBuildPublishedLabel)
                    ? "DATE UNAVAILABLE"
                    : compatibility.SupportedBuildPublishedLabel;

            CompatibilityTechnicalText.Text =
                $"INSTALLED GAME  ·  {compatibility.DetectedGameLabel}\n" +
                $"LAST VERIFIED  ·  STEAM BUILD {compatibility.SupportedSteamBuildId}  /  {compatibility.SupportedGameVersion}\n" +
                $"COMPONENTS  ·  LIMELIGHT {compatibility.LimelightVersion}  /  " +
                $"NATIVE BRIDGE {compatibility.NativeBridgeVersion}  /  " +
                $"UE4SS {compatibility.Ue4ssVersion}";

            CompatibilityStatusText.Text =
                compatibility.Status;

            CompatibilityStatusText.Foreground =
                StatusBrush(
                    compatibility.IsLiveLoaderCompatible &&
                    compatibility.GameBuildCompatible);

            CompatibilityDetailText.Text =
                compatibility.Detail;
        }

        public void ShowSupportCategory()
        {
            SettingsCategoryTabs.SelectedIndex = 2;
        }

        private string CreateSessionDetail(
            LiveSessionState session,
            bool isGameRunning)
        {
            if (!string.IsNullOrWhiteSpace(session.LastRecoveryMessage) &&
                !isGameRunning)
            {
                return session.LastRecoveryMessage;
            }

            if (!string.IsNullOrWhiteSpace(session.LastError))
            {
                return $"Last live change: {session.LastError}";
            }

            if (!string.IsNullOrWhiteSpace(session.ActiveModName) &&
                isGameRunning)
            {
                return $"{session.ActiveModName} is active. " +
                       $"{session.SuccessfulSwitches} successful live switch(es) this session.";
            }

            return isGameRunning
                ? "The live session is ready for a model change."
                : "Start the game to begin a fresh live session.";
        }

        private Brush StatusBrush(
            bool isHealthy)
        {
            return (Brush)FindResource(
                isHealthy
                    ? "CyanBrush"
                    : "PinkBrush");
        }

        private static string FormatBytes(
            long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            double kilobytes =
                bytes / 1024d;

            if (kilobytes < 1024)
            {
                return $"{kilobytes:F1} KB";
            }

            return $"{kilobytes / 1024d:F1} MB";
        }

        private void RepairButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RepairRequested?.Invoke();
        }

        private void PurgeAllModsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            PurgeAllModsRequested?.Invoke();
        }

        private void ExportButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ExportDiagnosticsRequested?.Invoke();
        }

        private void CreatePrivateTestReportButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CreatePrivateTestReportRequested?.Invoke();
        }

        private void ChangeGameFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ChangeGameFolderRequested?.Invoke();
        }

        private void DiscordPresenceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DiscordPresenceChanged?.Invoke(
                !_discordPresenceEnabled);
        }

        private void NativeTestButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NativeTestRequested?.Invoke();
        }
    }
}

