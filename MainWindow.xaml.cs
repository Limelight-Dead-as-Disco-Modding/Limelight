using Limelight.Models;
using Limelight.Services;
using Limelight.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Limelight
{
    public partial class MainWindow : Window
    {
        private const int WmGetMinMaxInfo = 0x0024;
        private const int WmDropFiles = 0x0233;
        private const int MonitorDefaultToNearest = 0x00000002;

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(
            IntPtr windowHandle,
            bool acceptFiles);

        [DllImport(
            "shell32.dll",
            CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(
            IntPtr dropHandle,
            uint fileIndex,
            System.Text.StringBuilder? fileName,
            uint fileNameSize);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(
            IntPtr dropHandle);

        private enum NavigationPage
        {
            Dashboard,
            MyMods,
            StagehandScripts,
            Profiles,
            LiveLoaders,
            Multiplayer,
            BrowseNexus,
            Downloads,
            Settings
        }

        private sealed class TutorialStep
        {
            public TutorialStep(
                NavigationPage page,
                FrameworkElement target,
                string eyebrow,
                string title,
                string description,
                string hint)
            {
                Page = page;
                Target = target;
                Eyebrow = eyebrow;
                Title = title;
                Description = description;
                Hint = hint;
            }

            public NavigationPage Page { get; }
            public FrameworkElement Target { get; }
            public string Eyebrow { get; }
            public string Title { get; }
            public string Description { get; }
            public string Hint { get; }
        }

        private const int CurrentTutorialVersion = 1;

        private const int RetainedLiveRollbackGenerations = 3;

        private readonly SettingsService _settingsService;
        private readonly ModLibraryService _modLibraryService;
        private readonly CharacterSlotModService _characterSlotModService;
        private readonly CharacterSlotLoaderService _characterSlotLoaderService;
        private readonly AppSettings _settings;
        private readonly ModDeploymentService _modDeploymentService;
        private readonly ExistingModsMigrationService _existingModsMigrationService;
        private readonly GameProcessService _gameProcessService;
        private readonly GlobalHotkeyService _globalHotkeyService;
        private readonly Ue4ssDetectionService _ue4ssDetectionService;
        private readonly Ue4ssReleaseService _ue4ssReleaseService;
        private readonly Ue4ssInstallerService _ue4ssInstallerService;
        private readonly DeadAsDiscoUe4ssConfigurationService _ue4ssConfigurationService;
        private readonly LiveLoaderBridgeService _liveLoaderBridgeService;
        private readonly LiveLoaderCommandService _liveLoaderCommandService;
        private readonly LiveModStagingService _liveModStagingService;
        private readonly LiveSessionService _liveSessionService;
        private readonly NativeBridgeInstallerService _nativeBridgeInstallerService;
        private readonly StagehandPayloadService _stagehandPayloadService;
        private readonly StagehandLogicModPackageService _stagehandLogicModPackageService;
        private readonly LimelightMpPayloadService _multiplayerPayloadService;
        private readonly LimelightMpRelayService _multiplayerRelayService;
        private readonly LimelightMpFriendCodeService _multiplayerFriendCodeService;
        private readonly LimelightMpSessionService _multiplayerSessionService;
        private readonly CompatibilityService _compatibilityService;
        private readonly DiagnosticReportService _diagnosticReportService;
        private readonly PrivateTestReportService _privateTestReportService;
        private readonly DownloadHistoryService _downloadHistoryService;
        private readonly DiscordPresenceService _discordPresenceService;
        private readonly GitHubReleaseUpdateService _updateService;
        private ResourceUsageOverlayWindow? _resourceUsageOverlayWindow;

        private string _discordPresenceSwitchTarget =
            string.Empty;

        private bool _isNexusBrowseLoading;
        private bool _isNexusDownloadRunning;
        private bool _hasLoadedNexusBrowseMods;
        private readonly DispatcherTimer _gameStatusTimer;
        private bool _hasHandledLiveLoaderPrompt;
        private bool _isLiveLoaderSetupRunning;
        private bool _isLiveModChangeRunning;
        private bool _isX19SwitchRequest;
        private bool _isX19SafetyProbeRunning;
        private bool _isLiveLoaderInitializationRunning;
        private bool _hasInitialisedCurrentGameSession;
        private bool _wasGameRunning;
        private bool _isApplyingPendingDeployment;
        private bool _pendingDeploymentAttempted;
        private int _nextLiveMountOrder = 1000;
        private readonly Dictionary<string, string>
            _characterSlotFingerprintsForSession =
                new(StringComparer.OrdinalIgnoreCase);
        private int _notificationSequence;
        private readonly List<TutorialStep> _tutorialSteps =
            new List<TutorialStep>();
        private int _tutorialStepIndex;
        private LoaderLaunchMode _selectedLoaderMode =
            LoaderLaunchMode.Normal;
        private NavigationPage _selectedNavigationPage =
            NavigationPage.Dashboard;
        private bool _windowTransitionInProgress;
        private bool _animateWindowAfterRestore;
        private bool _isModImportInProgress;
        private string _lastArchiveDropSignature =
            string.Empty;
        private DateTime _lastArchiveDropAt =
            DateTime.MinValue;
        private bool _isManualUpdateCheckRunning;
        private bool _isMultiplayerActionRunning;
        private bool? _isMultiplayerPayloadValid;
        private string? _gameDirectory;

        public MainWindow()
        {
            InitializeComponent();

            GameStatusDescription.Text =
                GetSidebarVersionText();

            _settingsService =
                new SettingsService();

            _modLibraryService =
                new ModLibraryService();

            _characterSlotModService =
                new CharacterSlotModService();

            _characterSlotLoaderService =
                new CharacterSlotLoaderService();

            _modDeploymentService =
                new ModDeploymentService();

            _existingModsMigrationService =
                new ExistingModsMigrationService();

            _gameProcessService =
                new GameProcessService();

            _globalHotkeyService =
                new GlobalHotkeyService();

            _globalHotkeyService.Pressed +=
                X19HotkeyPressed;

            _ue4ssDetectionService =
                new Ue4ssDetectionService();

            _ue4ssReleaseService =
                new Ue4ssReleaseService();

            _ue4ssInstallerService =
                new Ue4ssInstallerService();

            _ue4ssConfigurationService =
                new DeadAsDiscoUe4ssConfigurationService();

            _liveLoaderBridgeService =
                new LiveLoaderBridgeService();

            _nativeBridgeInstallerService =
                new NativeBridgeInstallerService();

            _stagehandPayloadService =
                new StagehandPayloadService();

            _stagehandLogicModPackageService =
                new StagehandLogicModPackageService();

            _multiplayerPayloadService =
                new LimelightMpPayloadService();

            _multiplayerRelayService =
                new LimelightMpRelayService();

            _multiplayerFriendCodeService =
                new LimelightMpFriendCodeService();

            _multiplayerSessionService =
                new LimelightMpSessionService(
                    _multiplayerPayloadService,
                    _multiplayerRelayService,
                    _multiplayerFriendCodeService);

            _multiplayerSessionService.LogEmitted +=
                MultiplayerLogEmitted;

            _compatibilityService =
                new CompatibilityService(
                    _ue4ssDetectionService,
                    _ue4ssConfigurationService,
                    _liveLoaderBridgeService,
                    _nativeBridgeInstallerService,
                    _stagehandPayloadService);

            _liveLoaderCommandService =
                new LiveLoaderCommandService();

            _liveModStagingService =
                new LiveModStagingService();

            _liveSessionService =
                new LiveSessionService();

            _diagnosticReportService =
                new DiagnosticReportService();

            _privateTestReportService =
                new PrivateTestReportService();

            _downloadHistoryService =
                new DownloadHistoryService();
            _updateService =
                new GitHubReleaseUpdateService();

            _settings =
                _settingsService.Load();

            _settings.ModProfiles ??=
                new List<ModProfile>();

            _settings.X19LoaderProfileIds ??=
                new List<string>();

            bool characterSlotMetadataChanged =
                false;

            foreach (InstalledMod installedMod in
                     _settings.InstalledMods)
            {
                characterSlotMetadataChanged |=
                    _characterSlotModService.RefreshMetadata(
                        installedMod);
            }

            InstalledMod? firstCharacterSlotMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    mod.IsCharacterSlotMod &&
                    Directory.Exists(mod.InstallDirectory));

            bool characterSlotCatalogueFlagChanged =
                firstCharacterSlotMod is not null &&
                !_settings.CharacterSlotCatalogueNeedsSynchronization;

            if (firstCharacterSlotMod is not null)
            {
                // I queue one tidy catalogue pass on startup. It also repairs
                // folders left by builds that only invited the active slot.
                _settings.CharacterSlotCatalogueNeedsSynchronization =
                    true;
            }

            if (characterSlotMetadataChanged ||
                characterSlotCatalogueFlagChanged)
            {
                _settingsService.Save(_settings);
            }

            _discordPresenceService =
                new DiscordPresenceService();

            _discordPresenceService.SetEnabled(
                _settings.DiscordRichPresenceEnabled);

            // I keep page event handlers on the main window so settings and
            // the active game directory are available when actions run.
            MyModsPageControl.ToggleModRequested +=
                ToggleModRequested;

            MyModsPageControl.RemoveModRequested +=
                RemoveModRequested;

            MyModsPageControl.RenameModRequested +=
                RenameModRequested;

            StagehandScriptsPageControl.RefreshRequested +=
                RefreshStagehandScriptsPage;

            StagehandScriptsPageControl.UpdateRuntimeRequested +=
                UpdateStagehandRuntimeRequested;

            StagehandScriptsPageControl.SetEnabledRequested +=
                SetStagehandScriptEnabledRequested;

            StagehandScriptsPageControl.RemoveRequested +=
                RemoveStagehandScriptRequested;

            ProfilesPageControl.ProfilesChanged +=
                ProfilesChanged;

            ProfilesPageControl.UseProfileInX19Requested +=
                UseProfileInX19Requested;

            LiveLoadersPageControl.X19GroupChanged +=
                X19GroupChanged;

            LiveLoadersPageControl.X19ProfileGroupsChanged +=
                X19ProfileGroupsChanged;

            LiveLoadersPageControl.X19ShuffleChanged +=
                X19ShuffleChanged;

            LiveLoadersPageControl.X19HotkeyChanged +=
                X19HotkeyChanged;

            MultiplayerPageControl.HostRequested +=
                HostMultiplayerRequested;

            MultiplayerPageControl.JoinRequested +=
                JoinMultiplayerRequested;

            MultiplayerPageControl.StopRequested +=
                StopMultiplayerRequested;

            MultiplayerPageControl.VerifyRequested +=
                VerifyMultiplayerRequested;

            MultiplayerPageControl.RemoveRequested +=
                RemoveMultiplayerRequested;

            SettingsPageControl.RepairRequested +=
                RepairLiveLoaderRequested;

            SettingsPageControl.PurgeAllModsRequested +=
                PurgeAllModsRequested;

            SettingsPageControl.ExportDiagnosticsRequested +=
                ExportDiagnosticsRequested;

            SettingsPageControl.CreatePrivateTestReportRequested +=
                CreatePrivateTestReportRequested;

            SettingsPageControl.ChangeGameFolderRequested +=
                ChangeGameFolderRequested;

            SettingsPageControl.DiscordPresenceChanged +=
                DiscordPresenceChanged;

            SettingsPageControl.ResourceOverlayChanged +=
                ResourceOverlayChanged;

            BrowseNexusPageControl.ArchiveDownloaded +=
                NexusBrowserArchiveDownloaded;
            DownloadsPageControl.ClearFinishedRequested +=
                ClearFinishedDownloadsRequested;


            // Checking every two seconds keeps the display responsive without
            // constantly asking Windows for its process list.
            _gameStatusTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };

            _gameStatusTimer.Tick +=
                GameStatusTimer_Tick;

            RestoreSavedGameDirectory();
            RefreshLibrarySummary();
            RefreshDownloadsPage();

            // Wait until the window is visible before starting timers or
            // showing the existing-mod migration prompt.
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            SizeChanged += MainWindow_SizeChanged;
            SourceInitialized += MainWindow_SourceInitialized;
        }

        private async void MainWindow_Loaded(
    object sender,
    RoutedEventArgs e)
        {
            UpdateGameRunningStatus();

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            if (!isGameRunning &&
                !string.IsNullOrWhiteSpace(_gameDirectory))
            {
                string gameDirectory =
                    _gameDirectory;

                ClearLiveLoaderSessionBypass();
                DeactivateMultiplayerPayloadBestEffort();

                // A previous crash can leave staged containers behind. They
                // are safe to remove once Windows confirms the game is closed.
                await Task.Run(() =>
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory));

                // I prepare the persistent cast before network work or startup
                // prompts can give a direct Steam launch time to overtake it.
                // The later pass still collects mods imported during migration.
                await ApplyPendingDeploymentIfPossible();
            }

            RefreshSettingsPage();
            RefreshMultiplayerPage();
            RefreshDiscordPresence(
                isGameRunning);

            // Finish any existing-mod migration before opening another modal window.
            await CheckForExistingMods();
            await ApplyPendingDeploymentIfPossible();
            await ShowLiveLoaderSetupPromptIfNeeded();

            _wasGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            _gameStatusTimer.Start();

            if (_wasGameRunning)
            {
                _liveSessionService.EnsureSession(
                    _gameDirectory!);

                await InitialiseLiveLoaderForRunningGameAsync(
                    waitForGameProcess: false);
            }

            bool tutorialNeeded =
                _settings.CompletedTutorialVersion <
                CurrentTutorialVersion;

            ShowFirstRunTutorialIfNeeded();

            if (!tutorialNeeded)
            {
                QueueWhatsNewWindow();
            }

            // I keep update checks out of the startup path so a slow network
            // never delays Limelight or stops the rest of the app loading.
            _ =
                CheckForUpdatesAsync();
        }

        private void MainWindow_SourceInitialized(
            object? sender,
            EventArgs e)
        {
            if (PresentationSource.FromVisual(this) is not HwndSource source)
            {
                return;
            }

            DragAcceptFiles(
                source.Handle,
                acceptFiles: true);

            // I keep maximise sizing in WPF native coordinates so the
            // custom chrome continues to respect the monitor work area.
            source.AddHook(WindowProc);
        }

        private IntPtr WindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == WmDropFiles)
            {
                HandleNativeFileDrop(
                    wParam);

                handled = true;
                return IntPtr.Zero;
            }

            if (message != WmGetMinMaxInfo)
            {
                return IntPtr.Zero;
            }

            AdjustWindowMaximizedBounds(hwnd, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        private void AdjustWindowMaximizedBounds(
            IntPtr hwnd,
            IntPtr lParam)
        {
            if (PresentationSource.FromVisual(this) is not HwndSource source)
            {
                return;
            }

            IntPtr monitorHandle =
                MonitorFromWindow(
                    hwnd,
                    MonitorDefaultToNearest);

            if (monitorHandle == IntPtr.Zero)
            {
                return;
            }

            MONITORINFO monitorInfo =
                new MONITORINFO
                {
                    cbSize = Marshal.SizeOf<MONITORINFO>()
                };

            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return;
            }

            MINMAXINFO maxInfo =
                Marshal.PtrToStructure<MINMAXINFO>(lParam);

            int workWidth =
                monitorInfo.rcWork.right -
                monitorInfo.rcWork.left;

            int workHeight =
                monitorInfo.rcWork.bottom -
                monitorInfo.rcWork.top;

            // WM_GETMINMAXINFO expects the maximise position relative to the
            // selected monitor, not absolute virtual-desktop coordinates.
            // Absolute coordinates can move the window off-screen when it is
            // maximised on a monitor positioned beside or above the primary.
            int workOffsetX =
                monitorInfo.rcWork.left -
                monitorInfo.rcMonitor.left;

            int workOffsetY =
                monitorInfo.rcWork.top -
                monitorInfo.rcMonitor.top;

            maxInfo.ptMaxPosition = new POINT(
                workOffsetX,
                workOffsetY);

            maxInfo.ptMaxSize = new POINT(
                workWidth,
                workHeight);

            maxInfo.ptMaxTrackSize = new POINT(
                workWidth,
                workHeight);

            Marshal.StructureToPtr(
                maxInfo,
                lParam,
                fDeleteOld: false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;

            public POINT(
                int x,
                int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(
            IntPtr hMonitor,
            ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr hwnd,
            int dwFlags);

        private void QueueWhatsNewWindow()
        {
            // I let the main window finish its first layout before opening the
            // update card. This keeps the splash and release notes separate.
            Dispatcher.BeginInvoke(
                new Action(ShowWhatsNewWindowIfNeeded),
                DispatcherPriority.ApplicationIdle);
        }

        private void ShowWhatsNewWindowIfNeeded()
        {
            if (_settings.CompletedTutorialVersion <
                    CurrentTutorialVersion ||
                TutorialOverlay.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            string version =
                GetCurrentVersion();

            if (string.Equals(
                    _settings.LastSeenReleaseNotesVersion,
                    version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ShowWhatsNewWindowInternal(version, forceShow: false);
        }

        private void ShowWhatsNewWindowInternal(
            string version,
            bool forceShow)
        {
            if (!forceShow &&
                string.Equals(
                    _settings.LastSeenReleaseNotesVersion,
                    version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReleaseNotesContent content =
                ReleaseNotesContent.CreateCurrent(version);

            WhatsNewWindow window =
                new WhatsNewWindow(content)
                {
                    Owner = this
                };

            window.ShowDialog();

            // Closing the card means the user has acknowledged this release.
            // I save immediately so it stays dismissed after a restart.
            _settings.LastSeenReleaseNotesVersion =
                version;

            _settingsService.Save(_settings);
        }

        private void ShowWhatsNewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowWhatsNewWindowInternal(
                GetCurrentVersion(),
                forceShow: true);
        }

        private static string GetCurrentVersion()
        {
            Assembly assembly =
                typeof(MainWindow).Assembly;

            string? informationalVersion =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                int metadataStart =
                    informationalVersion.IndexOf('+');

                return metadataStart >= 0
                    ? informationalVersion[..metadataStart]
                    : informationalVersion;
            }

            return assembly.GetName().Version?.ToString() ??
                "EARLY ACCESS";
        }

        private static string GetSidebarVersionText()
        {
            string friendlyVersion =
                GetCurrentVersion()
                    .Replace(
                        '-',
                        ' ')
                    .ToUpperInvariant();

            return
                $"LIMELIGHT {friendlyVersion}\nMADE BY HENREH <3";
        }

        private async Task CheckForUpdatesAsync()
        {
            GitHubReleaseUpdate? update =
                await _updateService.CheckForUpdateAsync(
                    GetCurrentVersion());

            if (update == null ||
                !IsLoaded ||
                string.Equals(
                    _settings.LastSeenUpdateVersion,
                    update.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsLoaded)
            {
                await Dispatcher.InvokeAsync(
                    () =>
                        ShowPendingUpdateDialog(update));

                return;
            }

            ShowPendingUpdateDialog(update);
        }

        private void ShowPendingUpdateDialog(
            GitHubReleaseUpdate update)
        {
            ShowUpdateAvailableDialog(update);

            MarkUpdateVersionSeen(update.Version);
        }

        private void ShowUpdateAvailableDialog(
            GitHubReleaseUpdate update)
        {
            string updateTitle =
                string.IsNullOrWhiteSpace(update.Name)
                    ? update.Version
                    : update.Name;

            string releaseNotesPreview =
                BuildUpdateNotesPreview(update.Body);

            string installerStateText =
                string.IsNullOrWhiteSpace(update.InstallerUrl)
                    ? "release page"
                    : "installer package";

            LimelightDialogChoice decision =
                ShowLimelightDialog(
                    "UPDATE AVAILABLE",
                    $"{updateTitle} is available. You are using {GetCurrentVersion()}.",
                    LimelightDialogTone.Information,
                    primaryAction: "UPDATE NOW",
                    secondaryAction: "LATER",
                    details:
                        $"LATEST: {updateTitle} · v{update.Version}\n\n" +
                        "CHANGELOG HIGHLIGHT:\n" +
                        releaseNotesPreview +
                        $"\n\nClick UPDATE NOW to open the GitHub {installerStateText} and begin the install.",
                    eyebrow: "UPGRADE AVAILABLE");

            if (decision !=
                LimelightDialogChoice.Primary)
            {
                return;
            }

            string updateTarget =
                string.IsNullOrWhiteSpace(update.InstallerUrl)
                    ? update.Url
                    : update.InstallerUrl;

            if (!TryOpenUpdateUrl(updateTarget, "installer"))
            {
                if (!string.Equals(
                        updateTarget,
                        update.Url,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = TryOpenUpdateUrl(
                        update.Url,
                        "release page");
                }
            }
        }

        private static string BuildUpdateNotesPreview(
            string? releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
            {
                return "No release notes were included for this build.";
            }

            IEnumerable<string> cleanedLines =
                releaseNotes
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(line => NormalizeReleaseNoteLine(line))
                    .Where(line =>
                        !string.IsNullOrWhiteSpace(line));

            string[] selectedLines =
                cleanedLines
                    .Take(8)
                    .ToArray();

            if (selectedLines.Length == 0)
            {
                return "No readable release notes lines were found.";
            }

            string preview =
                string.Join(
                    Environment.NewLine,
                    selectedLines);

            if (preview.Length > 760)
            {
                preview = preview[..740] + "…";
            }

            return preview;
        }

        private static string NormalizeReleaseNoteLine(
            string line)
        {
            string normalized =
                line.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (normalized.StartsWith("###", StringComparison.Ordinal))
            {
                normalized = normalized[3..].Trim();
            }

            if (normalized.StartsWith("##", StringComparison.Ordinal))
            {
                normalized = normalized[2..].Trim();
            }

            if (normalized.StartsWith("#", StringComparison.Ordinal))
            {
                normalized = normalized[1..].Trim();
            }

            if (normalized.StartsWith("- ", StringComparison.Ordinal) ||
                normalized.StartsWith("* ", StringComparison.Ordinal) ||
                normalized.StartsWith("+ ", StringComparison.Ordinal) ||
                normalized.StartsWith("• ", StringComparison.Ordinal))
            {
                normalized = normalized[2..].Trim();
            }

            return normalized;
        }

        private bool TryOpenUpdateUrl(
            string targetUrl,
            string targetLabel)
        {
            if (IsUpdateLinkSafe(
                    targetUrl,
                    out Uri? targetUri) &&
                targetUri is not null)
            {
                try
                {
                    Process.Start(
                        new ProcessStartInfo(
                            targetUri.AbsoluteUri)
                        {
                            UseShellExecute =
                                true
                        });

                    return true;
                }
                catch (Exception exception)
                {
                    ShowLimelightDialog(
                        "UPDATE LINK BLOCKED",
                        $"Limelight could not open the GitHub {targetLabel} from this device.",
                        LimelightDialogTone.Warning,
                        details: exception.Message,
                        eyebrow: "UPDATE READY");

                    return false;
                }
            }

            ShowLimelightDialog(
                "INVALID UPDATE LINK",
                $"Limelight received a {targetLabel} link that it cannot open safely.",
                LimelightDialogTone.Warning,
                eyebrow: "UPDATE READY");

            return false;
        }

        private async void CheckForUpdates_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_isManualUpdateCheckRunning)
            {
                return;
            }

            _isManualUpdateCheckRunning = true;

            Button? manualUpdateButton =
                sender as Button;

            double previousOpacity =
                1;

            if (manualUpdateButton is not null)
            {
                previousOpacity =
                    manualUpdateButton.Opacity;

                manualUpdateButton.IsEnabled =
                    false;

                manualUpdateButton.Opacity =
                    0.7;
            }

            try
            {
                GitHubReleaseUpdate? update =
                    await _updateService.CheckForUpdateAsync(
                        GetCurrentVersion());

                if (!IsLoaded)
                {
                    return;
                }

                if (update == null)
                {
                    ShowNotification(
                        "UPDATE CHECK COMPLETE",
                        "You are already on the latest Limelight release.",
                        isError: false);

                    return;
                }

                ShowUpdateAvailableDialog(update);

                MarkUpdateVersionSeen(update.Version);
            }
            finally
            {
                if (manualUpdateButton is not null)
                {
                    manualUpdateButton.IsEnabled =
                        true;

                    manualUpdateButton.Opacity =
                        previousOpacity;
                }

                _isManualUpdateCheckRunning =
                    false;
            }
        }

        private void MarkUpdateVersionSeen(
            string updateVersion)
        {
            if (string.Equals(
                    _settings.LastSeenUpdateVersion,
                    updateVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.LastSeenUpdateVersion =
                updateVersion;

            _settingsService.Save(_settings);
        }

        private static bool IsUpdateLinkSafe(
            string updateUrl,
            out Uri? updateUri)
        {
            updateUri =
                null;

            if (!Uri.TryCreate(
                    updateUrl,
                    UriKind.Absolute,
                    out Uri? candidate))
            {
                return false;
            }

            string host =
                candidate.Host;

            bool allowedHost =
                string.Equals(
                    host,
                    "github.com",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    host,
                    "githubusercontent.com",
                    StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".githubusercontent.com",
                    StringComparison.OrdinalIgnoreCase);

            if (!allowedHost)
            {
                return false;
            }

            updateUri =
                candidate;

            return true;
        }
        

        private void ShowFirstRunTutorialIfNeeded()
        {
            if (_settings.CompletedTutorialVersion >=
                CurrentTutorialVersion)
            {
                return;
            }

            _tutorialSteps.Clear();

            _tutorialSteps.AddRange(new[]
            {
                new TutorialStep(
                    NavigationPage.Dashboard,
                    DashboardNavigation,
                    "WELCOME TO LIMELIGHT",
                    "YOUR MODS. YOUR STAGE.",
                    "Limelight manages Dead as Disco character mods, launches the game, and keeps supported replacements available while the game is running.",
                    "This tour opens each real Limelight page. Nothing will be installed or changed while you look around."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    GameConnectionCard,
                    "FIRST CONNECTION",
                    "POINT LIMELIGHT AT THE GAME",
                    "Connect the Dead as Disco installation folder once. Limelight remembers it, checks the game status, and keeps all managed files in the correct locations.",
                    "You can change the connected folder later from Settings."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    ImportModButton,
                    "BUILD YOUR LIBRARY",
                    "IMPORT A MOD ARCHIVE",
                    "Import a ZIP, RAR, or 7Z mod archive and Limelight will validate it, scan its package contents, and add it to your private character library.",
                    "Limelight prevents duplicate imports and never edits the original archive."),
                new TutorialStep(
                    NavigationPage.MyMods,
                    MyModsNavigation,
                    "MY MODS",
                    "CHOOSE WHO TAKES THE SPOTLIGHT",
                    "Your installed characters live here. Activate a supported model, review its status, or remove it from Limelight when you no longer need it.",
                    "When Dead as Disco is running, Activate asks the Live Loader to switch safely."),
                new TutorialStep(
                    NavigationPage.LiveLoaders,
                    LiveLoadersNavigation,
                    "LIVE LOADERS",
                    "NORMAL OR X19 MODE",
                    "Normal mode changes characters from Limelight. X19 LLoader creates an ordered or shuffled group that can rotate from an in-game keyboard or controller shortcut.",
                    "Select the X19 group before launching the game with X19 mode."),
                new TutorialStep(
                    NavigationPage.BrowseNexus,
                    BrowseNexusNavigation,
                    "BROWSE NEXUS",
                    "NEXUS MODS IS LIVE",
                    "Sign in to Nexus Mods inside Limelight, then browse the real Dead as Disco catalogue without connecting an API key.",
                    "Use Manual Download and Limelight will import supported archives when the browser download finishes."),
                new TutorialStep(
                    NavigationPage.Downloads,
                    DownloadsNavigation,
                    "DOWNLOADS",
                    "FOLLOW EVERY TRANSFER",
                    "The Downloads page shows active progress, completed imports, and any failure that needs attention.",
                    "Browser downloads are imported after Nexus finishes the transfer."),
                new TutorialStep(
                    NavigationPage.Settings,
                    SettingsNavigation,
                    "SETTINGS AND SUPPORT",
                    "KEEP THE SHOW RUNNING",
                    "Settings contains game connection, Live Loader controls, Discord activity, optional resource monitoring, repair tools, and private diagnostic reports.",
                    "Nexus sign-in stays inside WebView2's private Limelight browser profile."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    LaunchGameButton,
                    "READY FOR THE SPOTLIGHT",
                    "LAUNCH WHEN YOU ARE READY",
                    "Launch Dead as Disco from here after choosing a character and loader mode. Limelight will prepare the managed bridge automatically and remain available for safe live changes.",
                    "You can replay the important pages at any time from the navigation bar.")
            });

            _tutorialStepIndex = 0;
            TutorialOverlay.Visibility =
                Visibility.Visible;

            ShowTutorialStep();
        }

        private void ShowTutorialStep()
        {
            if (_tutorialSteps.Count == 0)
            {
                return;
            }

            TutorialStep step =
                _tutorialSteps[_tutorialStepIndex];

            BrowseNexusPageControl.SetTutorialOverlayActive(
                step.Page == NavigationPage.BrowseNexus);

            NavigateForTutorial(step.Page);

            TutorialEyebrowText.Text =
                step.Eyebrow;
            TutorialTitleText.Text =
                step.Title;
            TutorialDescriptionText.Text =
                step.Description;
            TutorialHintText.Text =
                step.Hint;
            TutorialStepCounterText.Text =
                $"{_tutorialStepIndex + 1} OF {_tutorialSteps.Count}";

            TutorialPreviousButton.IsEnabled =
                _tutorialStepIndex > 0;
            TutorialPreviousButton.Opacity =
                _tutorialStepIndex > 0
                    ? 1
                    : 0.45;

            TutorialNextButton.Content =
                _tutorialStepIndex ==
                _tutorialSteps.Count - 1
                    ? "FINISH TOUR"
                    : "NEXT";

            Dispatcher.BeginInvoke(
                new Action(() =>
                    PositionTutorialSpotlight(step.Target)),
                DispatcherPriority.Loaded);
        }

        private void NavigateForTutorial(
            NavigationPage page)
        {
            switch (page)
            {
                case NavigationPage.MyMods:
                    ShowMyModsPage();
                    break;

                case NavigationPage.LiveLoaders:
                    ShowLiveLoadersPage();
                    break;

                case NavigationPage.Multiplayer:
                    ShowMultiplayerPage();
                    break;

                case NavigationPage.Profiles:
                    ShowProfilesPage();
                    break;

                case NavigationPage.BrowseNexus:
                    ShowBrowseNexusPage();
                    break;

                case NavigationPage.Downloads:
                    ShowDownloadsPage();
                    break;

                case NavigationPage.Settings:
                    ShowSettingsPage();
                    break;

                default:
                    ShowDashboardPage();
                    break;
            }
        }

        private void PositionTutorialSpotlight(
            FrameworkElement target)
        {
            if (TutorialOverlay.Visibility !=
                    Visibility.Visible ||
                !target.IsVisible ||
                target.ActualWidth <= 0 ||
                target.ActualHeight <= 0)
            {
                return;
            }

            try
            {
                Point targetPosition =
                    target.TransformToAncestor(
                            ApplicationContentRoot)
                        .Transform(new Point(0, 0));

                const double spotlightPadding = 7;

                Canvas.SetLeft(
                    TutorialSpotlight,
                    Math.Max(
                        0,
                        targetPosition.X - spotlightPadding));

                Canvas.SetTop(
                    TutorialSpotlight,
                    Math.Max(
                        0,
                        targetPosition.Y - spotlightPadding));

                TutorialSpotlight.Width =
                    target.ActualWidth +
                    spotlightPadding * 2;

                TutorialSpotlight.Height =
                    target.ActualHeight +
                    spotlightPadding * 2;

                // The card uses the opposite side so the highlighted control
                // remains visible instead of sitting underneath the guide.
                TutorialCard.HorizontalAlignment =
                    targetPosition.X < 300
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left;

                TutorialCard.VerticalAlignment =
                    targetPosition.Y <
                    ApplicationContentRoot.ActualHeight * 0.45
                        ? VerticalAlignment.Bottom
                        : VerticalAlignment.Top;
            }
            catch (InvalidOperationException)
            {
                // A page can still be completing its first layout pass. The
                // next size or tutorial step update will position the outline.
            }
        }

        private void TutorialPrevious_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_tutorialStepIndex <= 0)
            {
                return;
            }

            --_tutorialStepIndex;
            ShowTutorialStep();
        }

        private void TutorialNext_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_tutorialStepIndex <
                _tutorialSteps.Count - 1)
            {
                ++_tutorialStepIndex;
                ShowTutorialStep();
                return;
            }

            CompleteTutorial();
        }

        private void TutorialSkip_Click(
            object sender,
            RoutedEventArgs e)
        {
            CompleteTutorial();
        }

        private void CompleteTutorial()
        {
            BrowseNexusPageControl.SetTutorialOverlayActive(
                false);

            _settings.CompletedTutorialVersion =
                CurrentTutorialVersion;

            _settingsService.Save(_settings);

            TutorialOverlay.Visibility =
                Visibility.Collapsed;

            ShowDashboardPage();

            QueueWhatsNewWindow();
        }

        private void MainWindow_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (TutorialOverlay.Visibility !=
                    Visibility.Visible ||
                _tutorialSteps.Count == 0)
            {
                return;
            }

            PositionTutorialSpotlight(
                _tutorialSteps[_tutorialStepIndex].Target);
        }

        private async void MinimiseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            try
            {
                // A custom title bar does not receive Windows' full native
                // minimise animation. I soften the hand-off so it still
                // feels connected to the taskbar instead of disappearing.
                await AnimateWindowVisualAsync(
                    opacity: 0.35,
                    scale: 0.965,
                    milliseconds: 115);

                _animateWindowAfterRestore =
                    true;

                SystemCommands.MinimizeWindow(
                    this);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private async void ToggleMaximiseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            try
            {
                await AnimateWindowVisualAsync(
                    opacity: 0.72,
                    scale: 0.985,
                    milliseconds: 90);

                if (WindowState == WindowState.Maximized)
                {
                    SystemCommands.RestoreWindow(
                        this);
                }
                else
                {
                    SystemCommands.MaximizeWindow(
                        this);
                }

                // One render pass lets Windows finish changing the outer
                // bounds before Limelight brings its contents back in.
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);

                await AnimateWindowVisualAsync(
                    opacity: 1,
                    scale: 1,
                    milliseconds: 165);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private async void CloseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            await AnimateWindowVisualAsync(
                opacity: 0,
                scale: 0.98,
                milliseconds: 105);

            SystemCommands.CloseWindow(
                this);
        }

        private async void MainWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState == WindowState.Minimized ||
                !_animateWindowAfterRestore)
            {
                return;
            }

            _animateWindowAfterRestore =
                false;

            _windowTransitionInProgress =
                true;

            try
            {
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);

                await AnimateWindowVisualAsync(
                    opacity: 1,
                    scale: 1,
                    milliseconds: 175);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private Task AnimateWindowVisualAsync(
            double opacity,
            double scale,
            int milliseconds)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                // Windows can ask applications to avoid decorative motion.
                // I still apply the final state so every window command works.
                WindowVisualRoot.Opacity =
                    opacity;

                WindowVisualScale.ScaleX =
                    scale;

                WindowVisualScale.ScaleY =
                    scale;

                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> completion =
                new();

            Duration duration =
                TimeSpan.FromMilliseconds(
                    milliseconds);

            CubicEase easing =
                new()
                {
                    EasingMode = EasingMode.EaseOut
                };

            DoubleAnimation opacityAnimation =
                new()
                {
                    To = opacity,
                    Duration = duration,
                    EasingFunction = easing
                };

            DoubleAnimation scaleXAnimation =
                new()
                {
                    To = scale,
                    Duration = duration,
                    EasingFunction = easing
                };

            DoubleAnimation scaleYAnimation =
                new()
                {
                    To = scale,
                    Duration = duration,
                    EasingFunction = easing
                };

            opacityAnimation.Completed +=
                (_, _) =>
                {
                    // Committing the final values releases the animation
                    // clocks instead of leaving them attached to the window.
                    WindowVisualRoot.Opacity =
                        opacity;

                    WindowVisualScale.ScaleX =
                        scale;

                    WindowVisualScale.ScaleY =
                        scale;

                    WindowVisualRoot.BeginAnimation(
                        UIElement.OpacityProperty,
                        null);

                    WindowVisualScale.BeginAnimation(
                        ScaleTransform.ScaleXProperty,
                        null);

                    WindowVisualScale.BeginAnimation(
                        ScaleTransform.ScaleYProperty,
                        null);

                    completion.TrySetResult(
                        true);
                };

            WindowVisualRoot.BeginAnimation(
                UIElement.OpacityProperty,
                opacityAnimation,
                HandoffBehavior.SnapshotAndReplace);

            WindowVisualScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleXAnimation,
                HandoffBehavior.SnapshotAndReplace);

            WindowVisualScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleYAnimation,
                HandoffBehavior.SnapshotAndReplace);

            return completion.Task;
        }
        private void CloseLevelTransitionBlocker_Click(
    object sender,
    RoutedEventArgs e)
        {
            LevelTransitionBlocker.Visibility =
                Visibility.Collapsed;
        }

        private async void GameStatusTimer_Tick(
            object? sender,
            EventArgs e)
        {
            bool isGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            bool gameJustStarted =
                isGameRunning &&
                !_wasGameRunning;

            bool gameJustStopped =
                !isGameRunning &&
                _wasGameRunning;

            // Update this before awaiting cleanup so another timer tick cannot
            // mistake the same shutdown for a second one.
            _wasGameRunning = isGameRunning;

            if (gameJustStopped)
            {
                _multiplayerSessionService.Stop(
                    "The game closed, so Limelight stopped the multiplayer relay.");

                _globalHotkeyService.Unregister();
                ClearLiveLoaderSessionBypass();

                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;

                _hasInitialisedCurrentGameSession = false;
                _nextLiveMountOrder = 1000;
                _characterSlotFingerprintsForSession.Clear();

                string gameDirectory =
                    _gameDirectory!;

                // Give Unreal a moment to release the last file handles before
                // clearing Limelight's private staging folder.
                await Task.Delay(750);

                await Task.Run(() =>
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory));

                DeactivateMultiplayerPayloadBestEffort();
                MultiplayerPageControl.ShowIdle();
            }

            UpdateGameRunningStatus();
            await ApplyPendingDeploymentIfPossible();

            if (gameJustStarted)
            {
                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    _liveSessionService.EnsureSession(
                        _gameDirectory!);

                    await InitialiseLiveLoaderForRunningGameAsync(
                        waitForGameProcess: false);
                }
            }

            RefreshSettingsPage();
            RefreshMultiplayerPage();
            RefreshDiscordPresence(
                isGameRunning);
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            // The timer belongs to this window, so there is no reason to leave
            // it checking processes after Limelight has closed.
            _resourceUsageOverlayWindow?.Close();
            _resourceUsageOverlayWindow = null;
            _gameStatusTimer.Stop();
            _globalHotkeyService.Dispose();
            _discordPresenceService.Dispose();
            _multiplayerSessionService.Dispose();

            // The bridge has already made its startup decision by this point.
            // Clearing the marker here keeps a later direct game launch normal.
            ClearLiveLoaderSessionBypass();
        }

        private void ClearLiveLoaderSessionBypass()
        {
            try
            {
                _liveLoaderBridgeService.SetSessionBypass(
                    isDisabled: false);
            }
            catch
            {
                // The marker expires by itself, so cleanup must never prevent
                // Limelight or the game from closing normally.
            }
        }

        private void MultiplayerLogEmitted(
            MultiplayerLogLevel level,
            string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                        MultiplayerLogEmitted(
                            level,
                            message)));

                return;
            }

            MultiplayerPageControl.AddLog(
                level,
                message);

            // I refresh here because the relay can leave the party without
            // politely pressing Limelight's Stop button first.
            RefreshDiscordPresence();
        }

        private void RefreshMultiplayerPage()
        {
            bool gameConnected =
                !string.IsNullOrWhiteSpace(
                    _gameDirectory) &&
                File.Exists(
                    Path.Combine(
                        _gameDirectory!,
                        "Pagoda.exe"));

            bool gameRunning =
                gameConnected &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            Ue4ssDetectionResult installation =
                _ue4ssDetectionService.Detect(
                    _gameDirectory);

            bool multiplayerRuntimeReady =
                installation.IsInstalled &&
                _ue4ssConfigurationService.IsRuntimeCompatible(
                    installation) &&
                _ue4ssConfigurationService.IsConfigured(
                    installation) &&
                _liveLoaderBridgeService.IsInstalled(
                    installation) &&
                _nativeBridgeInstallerService.IsCurrentVersionInstalled(
                    installation) &&
                _stagehandPayloadService.IsCurrentVersionInstalled(
                    installation);

            if (_isMultiplayerPayloadValid is null)
            {
                try
                {
                    _multiplayerPayloadService.ValidateEmbeddedPayloads();
                    _isMultiplayerPayloadValid = true;
                }
                catch
                {
                    _isMultiplayerPayloadValid = false;
                }
            }

            bool payloadValid =
                _isMultiplayerPayloadValid == true;

            MultiplayerPageControl.ShowReadiness(
                gameConnected,
                gameRunning,
                multiplayerRuntimeReady,
                payloadValid,
                _multiplayerSessionService.FindTailscaleIpv4Address(),
                _multiplayerPayloadService.ReadInstalledRole(
                    installation));
        }

        private async void HostMultiplayerRequested()
        {
            await StartMultiplayerSessionAsync(
                MultiplayerRole.Host,
                friendCode: null);
        }

        private async void JoinMultiplayerRequested(
            string friendCode)
        {
            await StartMultiplayerSessionAsync(
                MultiplayerRole.Client,
                friendCode);
        }

        private async Task StartMultiplayerSessionAsync(
            MultiplayerRole role,
            string? friendCode)
        {
            if (_isMultiplayerActionRunning)
            {
                return;
            }

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory) ||
                !File.Exists(
                    Path.Combine(
                        gameDirectory,
                        "Pagoda.exe")))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to the Dead as Disco folder before starting multiplayer.",
                    LimelightDialogTone.Warning,
                    eyebrow: "MULTIPLAYER BLOCKED");

                return;
            }

            if (_gameProcessService.IsGameRunning(
                    gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE DEAD AS DISCO",
                    "The game must be closed while Limelight installs or changes a multiplayer role.",
                    LimelightDialogTone.Warning,
                    eyebrow: "ROLE CHANGE BLOCKED");

                return;
            }

            Ue4ssDetectionResult installation =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            if (!installation.IsInstalled ||
                !_ue4ssConfigurationService.IsRuntimeCompatible(
                    installation) ||
                !_ue4ssConfigurationService.IsConfigured(
                    installation) ||
                !_liveLoaderBridgeService.IsInstalled(
                    installation) ||
                !_nativeBridgeInstallerService.IsCurrentVersionInstalled(
                    installation) ||
                !_stagehandPayloadService.IsCurrentVersionInstalled(
                    installation))
            {
                ShowLimelightDialog(
                    "LIVE LOADER SETUP REQUIRED",
                    "Install or repair Limelight's Live Loader from Settings before starting multiplayer.",
                    LimelightDialogTone.Warning,
                    primaryAction: "OPEN SETTINGS",
                    eyebrow: "MULTIPLAYER BLOCKED");

                ShowSettingsPage();
                SettingsPageControl.ShowSupportCategory();
                return;
            }

            _isMultiplayerActionRunning = true;

            MultiplayerPageControl.SetBusy(
                true,
                role == MultiplayerRole.Host
                    ? "PREPARING HOST..."
                    : "PREPARING CLIENT...");

            MultiplayerPageControl.AddLog(
                MultiplayerLogLevel.Log,
                role == MultiplayerRole.Host
                    ? "Preparing a new host session."
                    : "Preparing to join the friend session.");

            _globalHotkeyService.Unregister();
            _selectedLoaderMode =
                LoaderLaunchMode.Multiplayer;

            try
            {
                _liveLoaderBridgeService.SetSessionBypass(
                    isDisabled: false);

                MultiplayerStartResult session =
                    await Task.Run(() =>
                        role == MultiplayerRole.Host
                            ? _multiplayerSessionService.StartHost(
                                installation)
                            : _multiplayerSessionService.StartClient(
                                installation,
                                friendCode ?? string.Empty));

                using Process? steamLaunch =
                    Process.Start(
                        CreateSteamLaunchStartInfo());

                if (steamLaunch is null)
                {
                    throw new InvalidOperationException(
                        "Windows did not accept Limelight's Steam launch request.");
                }

                MultiplayerPageControl.ShowSession(
                    session);

                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Network,
                    "Steam accepted the Dead as Disco launch request.");

                ShowNotification(
                    role == MultiplayerRole.Host
                        ? "MULTIPLAYER HOST READY"
                        : "JOIN SESSION READY",
                    role == MultiplayerRole.Host
                        ? "Copy the short code for your friend, then host from the Dive Bar."
                        : "Your game is launching and will connect to the host.",
                    isError: false);
            }
            catch (Exception exception)
            {
                _multiplayerSessionService.Stop(
                    "The multiplayer startup was cancelled.");

                MultiplayerPageControl.ShowIdle();
                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Error,
                    exception.Message);

                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;

                ClearLiveLoaderSessionBypass();
                DeactivateMultiplayerPayloadBestEffort();

                ShowLimelightDialog(
                    "MULTIPLAYER COULD NOT START",
                    "Limelight left the game closed and stopped the controller relay.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "LIMELIGHT MP");
            }
            finally
            {
                _isMultiplayerActionRunning = false;

                MultiplayerPageControl.SetBusy(
                    false,
                    string.Empty);

                RefreshMultiplayerPage();
                RefreshDiscordPresence();
            }
        }

        private void StopMultiplayerRequested()
        {
            _multiplayerSessionService.Stop(
                "The user stopped the multiplayer relay.");

            bool gameRunning =
                !string.IsNullOrWhiteSpace(
                    _gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            if (!gameRunning)
            {
                DeactivateMultiplayerPayloadBestEffort();
                ClearLiveLoaderSessionBypass();
                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;
            }

            MultiplayerPageControl.ShowIdle();
            RefreshMultiplayerPage();
            RefreshDiscordPresence();

            ShowNotification(
                "MULTIPLAYER SESSION STOPPED",
                gameRunning
                    ? "The relay is closed. Dead as Disco is still running and can be closed normally."
                    : "The relay is closed and the multiplayer role is disabled for normal launches.",
                isError: false);
        }

        private void VerifyMultiplayerRequested()
        {
            try
            {
                _multiplayerPayloadService.ValidateEmbeddedPayloads();
                _isMultiplayerPayloadValid = true;
                RefreshMultiplayerPage();

                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Log,
                    "All embedded LimelightMP v0.1.0 files passed their size and SHA-256 checks.");

                ShowNotification(
                    "MULTIPLAYER FILES VERIFIED",
                    "The host, client, native controller and relay payloads are intact.",
                    isError: false);
            }
            catch (Exception exception)
            {
                _isMultiplayerPayloadValid = false;
                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Error,
                    exception.Message);

                ShowLimelightDialog(
                    "MULTIPLAYER FILE CHECK FAILED",
                    "One or more embedded LimelightMP files did not pass verification.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "PAYLOAD CHECK");
            }
        }

        private void RemoveMultiplayerRequested()
        {
            if (string.IsNullOrWhiteSpace(
                    _gameDirectory))
            {
                return;
            }

            if (_gameProcessService.IsGameRunning(
                    _gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE DEAD AS DISCO",
                    "Close the game before removing LimelightMP test files.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REMOVE BLOCKED");

                return;
            }

            LimelightDialogChoice choice =
                ShowLimelightDialog(
                    "REMOVE LIMELIGHTMP TEST FILES?",
                    "This removes only the managed LimelightMP role and native controller folders. Other UE4SS mods and saved session logs are kept.",
                    LimelightDialogTone.Question,
                    primaryAction: "REMOVE TEST FILES",
                    secondaryAction: "KEEP THEM",
                    eyebrow: "EXPERIMENTAL CLEANUP");

            if (choice != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                _multiplayerSessionService.Stop();

                _multiplayerPayloadService.Remove(
                    _ue4ssDetectionService.Detect(
                        _gameDirectory));

                MultiplayerPageControl.ShowIdle();
                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Log,
                    "Managed LimelightMP test files were removed. Session logs were kept.");

                RefreshMultiplayerPage();

                ShowNotification(
                    "MULTIPLAYER TEST FILES REMOVED",
                    "Limelight kept every unrelated mod and multiplayer session log.",
                    isError: false);
            }
            catch (Exception exception)
            {
                MultiplayerPageControl.AddLog(
                    MultiplayerLogLevel.Error,
                    exception.Message);

                ShowLimelightDialog(
                    "MULTIPLAYER CLEANUP FAILED",
                    "Limelight did not remove any unmanaged folder.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "SAFE CLEANUP");
            }
        }

        private void DeactivateMultiplayerPayloadBestEffort()
        {
            try
            {
                _multiplayerPayloadService.Deactivate(
                    _ue4ssDetectionService.Detect(
                        _gameDirectory));
            }
            catch
            {
                // The next multiplayer install repairs its own managed files.
                // Normal Limelight startup must continue if Windows holds one.
            }
        }

        private async Task InitialiseLiveLoaderForRunningGameAsync(
            bool waitForGameProcess)
        {
            if (_isLiveLoaderInitializationRunning ||
                _hasInitialisedCurrentGameSession ||
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled ||
                string.IsNullOrWhiteSpace(_gameDirectory))
            {
                return;
            }

            string gameDirectory =
                _gameDirectory;

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            if (!loader.IsInstalled ||
    !_ue4ssConfigurationService.IsConfigured(loader) ||
    !_liveLoaderBridgeService.IsInstalled(loader) ||
    !_nativeBridgeInstallerService.IsCurrentVersionInstalled(
        loader) ||
    !_stagehandPayloadService.IsCurrentVersionInstalled(
        loader))
            {
                // The optional loader has not been accepted yet. The normal
                // dashboard and setup prompt remain available.
                return;
            }

            _isLiveLoaderInitializationRunning = true;

            LiveLoaderInitializingWindow initialisingWindow =
               new LiveLoaderInitializingWindow();

            bool previousEnabledState =
                IsEnabled;

            Exception? initialisationFailure =
                null;

            try
            {
                IsEnabled = false;

                initialisingWindow.Report(
                    "WAITING FOR DEAD AS DISCO",
                    8,
                    "Limelight is waiting for the game process to start.");

                // I show the waiting card before Steam responds so a failed
                // handoff never looks like an unresponsive Launch button.
                initialisingWindow.Owner =
                    this;

                initialisingWindow.Show();

                DateTime processDeadline =
                    DateTime.UtcNow.AddSeconds(
                        waitForGameProcess
                            ? 75
                            : 10);

                while (!_gameProcessService.IsGameRunning(
                           gameDirectory))
                {
                    if (DateTime.UtcNow >= processDeadline)
                    {
                        throw new TimeoutException(
                            "Dead as Disco did not start before the live-loader check timed out.");
                    }

                    await Task.Delay(250);
                }

                _wasGameRunning = true;
                IntPtr gameWindowHandle =
                    IntPtr.Zero;

                DateTime gameWindowDeadline =
                    DateTime.UtcNow.AddSeconds(30);

                // I wait for Dead as Disco's visible window so the loading card
                // appears over the game instead of over Limelight.
                while (gameWindowHandle == IntPtr.Zero &&
                       DateTime.UtcNow < gameWindowDeadline)
                {
                    gameWindowHandle =
                        _gameProcessService.FindGameWindow(
                            gameDirectory);

                    if (gameWindowHandle == IntPtr.Zero)
                    {
                        await Task.Delay(100);
                    }
                }

                // The first card belongs to Limelight while Steam is opening
                // the game. I replace it here so the next card can belong to
                // Dead as Disco and stay above its loading screen.
                initialisingWindow.Close();

                initialisingWindow =
                    new LiveLoaderInitializingWindow();

                initialisingWindow.Report(
                    "CONNECTING TO UE4SS",
                    18,
                    "The game is running. Waiting for the Limelight runtime bridge and Unreal object system.");

                initialisingWindow.ShowOverGame(
                    gameWindowHandle);

                DateTime bridgeDeadline =
                    DateTime.UtcNow.AddMinutes(2);

                DateTime? gameMissingSince = null;

                while (!_liveLoaderBridgeService.IsOnline())
                {
                    bool gameIsRunning =
                        _gameProcessService.IsGameRunning(
                            gameDirectory);

                    if (gameIsRunning)
                    {
                        gameMissingSince = null;
                    }
                    else
                    {
                        gameMissingSince ??= DateTime.UtcNow;
                    }

                    // Windows can briefly omit a process while the launcher
                    // hands control to the shipping executable. I wait for a
                    // sustained absence before treating the game as closed.
                    if (gameMissingSince.HasValue &&
                        DateTime.UtcNow - gameMissingSince.Value >=
                        TimeSpan.FromSeconds(8))
                    {
                        throw new InvalidOperationException(
                            "Dead as Disco closed before the live loader was ready.");
                    }

                    if (DateTime.UtcNow >= bridgeDeadline)
                    {
                        throw new TimeoutException(
                            "UE4SS did not bring the Limelight bridge online in time.");
                    }

                    await Task.Delay(300);
                }

                initialisingWindow.Report(
                    "VERIFYING NATIVE BRIDGE",
                    27,
                    "Limelight is checking the transition-safe package mounting bridge.");

                LiveLoaderCommandResult nativePing =
                    await _liveLoaderCommandService.PingNativeAsync();

                if (!nativePing.Success)
                {
                    throw new InvalidOperationException(
                        nativePing.Message);
                }

                initialisingWindow.Report(
                    "PREPARING THE MOUNT BRIDGE",
                    29,
                    "Limelight is locating Unreal's live-mount functions. Please remain seated in the Dive Bar until the Live Loader says ready.");

                LiveLoaderCommandResult mountResolver =
                    await _liveLoaderCommandService
                        .ScanMountFunctionsAsync();

                if (!mountResolver.Success)
                {
                    throw new InvalidOperationException(
                        mountResolver.Message);
                }

                InstalledMod? activeMod =
                    _settings.InstalledMods.FirstOrDefault(mod =>
                        string.Equals(
                            mod.Id,
                            _settings.ActiveModId,
                            StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(
                            mod.InstallDirectory));

                LiveLoaderCommandResult startupSafety =
                    await WaitForInitialLiveWorldAsync(
                        gameDirectory,
                        (phase, progress) =>
                            initialisingWindow.Report(
                                phase,
                                progress));

                if (!startupSafety.Success)
                {
                    throw new InvalidOperationException(
                        startupSafety.Message);
                }

                if (activeMod is not null)
                {
                    await ActivateDeployedLiveModAsync(
                        activeMod,
                        gameDirectory,
                        (phase, progress) =>
                            initialisingWindow.Report(
                                phase,
                                progress),
                        allowDeferredCharlieRefresh: true);
                }

                initialisingWindow.Report(
                    "HANDING OVER THE STAGE",
                    98,
                    "Limelight is completing one final bridge check before gameplay is returned.");

                // I only dismiss the in-game panel after the bridge answers again.
                // This keeps a successful scan from being mistaken for a ready session.
                LiveLoaderCommandResult finalNativePing =
                    await _liveLoaderCommandService
                        .PingNativeAsync();

                if (!finalNativePing.Success)
                {
                    throw new InvalidOperationException(
                        finalNativePing.Message);
                }

                initialisingWindow.Report(
                    "LIVE LOADER READY",
                    100,
                    activeMod is null
                        ? "The runtime is online. No active model mod needs to be mounted."
                        : $"{activeMod.Name} is ready and the live loader is online.");

                _hasInitialisedCurrentGameSession = true;

                await Task.Delay(650);
            }
            catch (Exception exception)
            {
                initialisationFailure = exception;

                WriteLaunchTrace(
                    "Live Loader initialisation failed: " +
                    exception.Message);
            }
            finally
            {
                if (initialisingWindow.IsVisible)
                {
                    initialisingWindow.Close();
                }

                IsEnabled = previousEnabledState;
                _isLiveLoaderInitializationRunning = false;
                UpdateGameRunningStatus();
            }

            if (initialisationFailure is not null)
            {
                ShowLimelightDialog(
                    "LIVE LOADER COULD NOT INITIALISE",
                    "Dead as Disco can still be played normally, but live switching will remain locked for this launch.",
                    LimelightDialogTone.Warning,
                    details: initialisationFailure.Message,
                    eyebrow: "LIVE LOADER");
            }
        }

        private void UpdateGameRunningStatus()
        {
            if (_isLiveLoaderSetupRunning)
            {
                return;
            }

            if (_isLiveModChangeRunning)
            {
                return;
            }

            if (_isLiveLoaderInitializationRunning)
            {
                return;
            }

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                GameProcessStatusText.Text =
                    "NOT CONNECTED";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("MutedTextBrush");

                SetLiveLoaderDisplay(
                    "NOT CONNECTED",
                    "Connect Dead as Disco before setting up live character switching.",
                    isHealthy: false);

                return;
            }

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (isGameRunning)
            {
                _pendingDeploymentAttempted = false;

                GameProcessStatusText.Text =
                    "RUNNING";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");
            }
            else
            {
                GameProcessStatusText.Text =
                    "NOT RUNNING";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");
            }

            if (isGameRunning &&
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled)
            {
                SetLiveLoaderDisplay(
                    "DISABLED",
                    "This session is using the deployed mod without live switching, loader scans, or X19 controls.",
                    isHealthy: true);

                return;
            }

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            if (loader.IsPartiallyInstalled)
            {
                SetLiveLoaderDisplay(
                    "REPAIR NEEDED",
                    "The loader installation is incomplete. Close the game and use Repair Live Loader in Settings.",
                    isHealthy: false);

                return;
            }

            if (!loader.IsInstalled)
            {
                SetLiveLoaderDisplay(
                    "NOT INSTALLED",
                    "Set up the Live Loader to switch character mods without restarting the game.",
                    isHealthy: false);

                return;
            }

            if (!_ue4ssConfigurationService.IsConfigured(loader))
            {
                SetLiveLoaderDisplay(
                    "SETUP NEEDED",
                    "Limelight needs to finish configuring the loader for Dead as Disco.",
                    isHealthy: false);

                return;
            }

            if (!_liveLoaderBridgeService.IsInstalled(loader))
            {
                SetLiveLoaderDisplay(
                    "BRIDGE NEEDED",
                    "Limelight's communication bridge is missing and can be restored from Settings.",
                    isHealthy: false);

                return;
            }

            if (!_nativeBridgeInstallerService.IsCurrentVersionInstalled(
        loader))
            {
                SetLiveLoaderDisplay(
                    "NATIVE BRIDGE NEEDED",
                    "Limelight's native companion is missing or does not match this version. Use Repair Live Loader in Settings.",
                    isHealthy: false);

                return;
            }

            if (!_stagehandPayloadService.IsCurrentVersionInstalled(
                    loader))
            {
                SetLiveLoaderDisplay(
                    "LOGIC RUNTIME NEEDED",
                    "Limelight's managed gameplay-logic runtime is missing or out of date. Use Repair Live Loader in Settings.",
                    isHealthy: false);

                return;
            }

            if (!isGameRunning)
            {
                SetLiveLoaderDisplay(
                    "READY",
                    "The Live Loader is installed and will come online when Dead as Disco starts.",
                    isHealthy: true);

                return;
            }

            if (_liveLoaderBridgeService.IsOnline())
            {
                SetLiveLoaderDisplay(
                    "ONLINE",
                    "Live character switching is available for the current game session.",
                    isHealthy: true);

                return;
            }

            // UE4SS is installed and the game exists, but the Lua bridge has not
            // produced a recent heartbeat.
            SetLiveLoaderDisplay(
                "OFFLINE",
                "Dead as Disco is running, but Limelight has not received a loader heartbeat yet.",
                isHealthy: false);
        }

        private void SetLiveLoaderDisplay(
            string status,
            string description,
            bool isHealthy)
        {
            Brush statusBrush =
                (Brush)FindResource(
                    isHealthy
                        ? "CyanBrush"
                        : "PinkBrush");

            LiveLoaderStatusText.Text =
                status;

            LiveLoaderStatusText.Foreground =
                statusBrush;

            LiveLoaderStatusDescriptionText.Text =
                description;

            LiveLoaderStatusDot.Fill =
                statusBrush;

            LiveLoaderStatusRing.Stroke =
                statusBrush;
        }

        private List<InstalledMod> GetCharacterSlotCatalogue(
            string? excludedModId = null)
        {
            return _settings.InstalledMods
                .Where(mod =>
                    mod.IsCharacterSlotMod &&
                    Directory.Exists(mod.InstallDirectory) &&
                    !string.Equals(
                        mod.Id,
                        excludedModId,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private List<InstalledMod> GetEnabledConventionalMods(
            string? excludedModId = null)
        {
            _settings.EnabledConventionalModIds ??=
                new List<string>();

            var enabledIds =
                new HashSet<string>(
                    _settings.EnabledConventionalModIds,
                    StringComparer.OrdinalIgnoreCase);

            return _settings.InstalledMods
                .Where(mod =>
                    mod.IsConventionalMod &&
                    enabledIds.Contains(mod.Id) &&
                    Directory.Exists(mod.InstallDirectory) &&
                    !string.Equals(
                        mod.Id,
                        excludedModId,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void SynchronizeModDeployment(
            InstalledMod? activeMod,
            IReadOnlyCollection<InstalledMod> characterSlotCatalogue,
            string gameDirectory,
            IReadOnlyCollection<InstalledMod>? enabledConventionalMods = null)
        {
            List<InstalledMod> companionMods =
                characterSlotCatalogue
                    .Concat(
                        enabledConventionalMods ??
                        GetEnabledConventionalMods())
                    .ToList();

            if (activeMod is null)
            {
                _modDeploymentService.Deactivate(
                    companionMods,
                    gameDirectory);
            }
            else
            {
                _modDeploymentService.Activate(
                    activeMod,
                    companionMods,
                    gameDirectory);
            }

            _characterSlotLoaderService.SynchronizeRuntimeCatalogue(
                characterSlotCatalogue,
                gameDirectory);
        }

        private async Task ApplyPendingDeploymentIfPossible()
        {
            if (_isApplyingPendingDeployment ||
                _isLiveModChangeRunning ||
                _pendingDeploymentAttempted ||
                (string.IsNullOrWhiteSpace(
                     _settings.PendingDeploymentModId) &&
                 !_settings.CharacterSlotCatalogueNeedsSynchronization &&
                 !_settings.ConventionalModsNeedSynchronization) ||
                string.IsNullOrWhiteSpace(
                    _gameDirectory) ||
                _gameProcessService.IsGameRunning(
                    _gameDirectory))
            {
                return;
            }

            InstalledMod? pendingMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.PendingDeploymentModId,
                        StringComparison.OrdinalIgnoreCase));

            bool pendingModWasRequested =
                !string.IsNullOrWhiteSpace(
                    _settings.PendingDeploymentModId);

            if (pendingModWasRequested &&
                (pendingMod == null ||
                 !Directory.Exists(
                     pendingMod.InstallDirectory)))
            {
                _settings.PendingDeploymentModId =
                    string.Empty;

                _settingsService.Save(_settings);

                if (!_settings.CharacterSlotCatalogueNeedsSynchronization)
                {
                    return;
                }

                pendingMod = null;
            }

            InstalledMod? activeMod =
                pendingMod ??
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(mod.InstallDirectory));

            _isApplyingPendingDeployment = true;
            _pendingDeploymentAttempted = true;

            try
            {
                string gameDirectory =
                    _gameDirectory;

                List<InstalledMod> characterSlotCatalogue =
                    GetCharacterSlotCatalogue();

                await Task.Run(() =>
                    SynchronizeModDeployment(
                        activeMod,
                        characterSlotCatalogue,
                        gameDirectory));

                _settings.PendingDeploymentModId =
                    string.Empty;

                _settings.CharacterSlotCatalogueNeedsSynchronization =
                    false;

                _settings.ConventionalModsNeedSynchronization =
                    false;

                _settingsService.Save(_settings);
            }
            catch
            {
                // I keep both notes. Limelight can try again the next time it
                // opens while the game is fully closed and feeling cooperative.
            }
            finally
            {
                _isApplyingPendingDeployment = false;
            }
        }

        private async Task ShowLiveLoaderSetupPromptIfNeeded()
        {
            if (_hasHandledLiveLoaderPrompt ||
                _isLiveLoaderSetupRunning)
            {
                return;
            }

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return;
            }

            LocalCompatibilityResult compatibility =
                _compatibilityService.Check(
                    gameDirectory);

            WriteLaunchTrace(
                "Compatibility checked: " +
                $"liveLoader={compatibility.IsLiveLoaderCompatible}; " +
                $"gameConnected={compatibility.GameConnected}; " +
                $"buildDetected={compatibility.GameBuildDetected}; " +
                $"buildCompatible={compatibility.GameBuildCompatible}; " +
                $"embeddedPayload={compatibility.EmbeddedPayloadCompatible}; " +
                $"ue4ssInstalled={compatibility.Ue4ssInstalled}; " +
                $"ue4ssCompatible={compatibility.Ue4ssCompatible}; " +
                $"ue4ssConfigured={compatibility.Ue4ssConfigured}; " +
                $"luaBridge={compatibility.LuaBridgeInstalled}; " +
                $"nativeBridge={compatibility.NativeBridgeCurrent}; " +
                $"stagehand={compatibility.StagehandCurrent}; " +
                $"detail={compatibility.Detail}");

            Ue4ssDetectionResult currentInstallation =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (currentInstallation.IsInstalled &&
                _ue4ssConfigurationService.IsRuntimeCompatible(
                    currentInstallation) &&
                _liveLoaderBridgeService.HasBridgeFiles(
                    currentInstallation) &&
                !isGameRunning)
            {
                try
                {
                    // Once the user has accepted setup, repair both our known
                    // game configuration and bridge registration when needed.
                    _ue4ssConfigurationService.Apply(
                        currentInstallation);

                    _liveLoaderBridgeService.EnsureInstalled(
                        currentInstallation);

                    _nativeBridgeInstallerService.EnsureInstalled(
                        currentInstallation);

                    _stagehandPayloadService.EnsureInstalled(
                        currentInstallation);
                }
                catch
                {
                    // The normal setup popup below can explain and retry a
                    // repair if Windows has temporarily locked the file.
                }
            }

            if (currentInstallation.IsInstalled &&
     _ue4ssConfigurationService.IsConfigured(
         currentInstallation) &&
     _liveLoaderBridgeService.IsInstalled(
         currentInstallation) &&
     _nativeBridgeInstallerService.IsCurrentVersionInstalled(
         currentInstallation) &&
     _stagehandPayloadService.IsCurrentVersionInstalled(
         currentInstallation))
            {
                if (!isGameRunning)
                {
                    // Limelight owns this script, so it can safely update the bridge
                    // without modifying the user's other UE4SS mods.
                    _liveLoaderBridgeService.EnsureInstalled(
                        currentInstallation);
                }

                _hasHandledLiveLoaderPrompt = true;
                return;
            }

            _hasHandledLiveLoaderPrompt = true;

            LiveLoaderSetupWindow setupWindow =
                new LiveLoaderSetupWindow
                {
                    Owner = this
                };

            setupWindow.ShowDialog();

            if (setupWindow.PromptDismissed)
            {
                // Store the actual directory rather than one global yes/no value. A
                // different installation should receive its own setup choice.
                _settings.DismissedLiveLoaderPromptForGameDirectory =
                    gameDirectory;

                _settingsService.Save(_settings);
                return;
            }

            if (!setupWindow.SetupRequested)
            {
                return;
            }

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can set up the Live Loader. Limelight will ask again next time it starts.",
                    LimelightDialogTone.Warning,
                    eyebrow: "SETUP PAUSED");

                return;
            }

            _isLiveLoaderSetupRunning = true;

            bool previousEnabledState =
                IsEnabled;

            Ue4ssPackageDownload? downloadedPackage =
                null;

            Ue4ssInstallResult? installResult =
                null;

            Exception? setupFailure =
                null;

            try
            {
                IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                Ue4ssDetectionResult installedLoader =
    _ue4ssDetectionService.Detect(
        gameDirectory);

                if (!installedLoader.IsInstalled ||
                    !_ue4ssConfigurationService.IsRuntimeCompatible(
                        installedLoader))
                {
                    LiveLoaderStatusText.Text =
                        "DOWNLOADING";

                    LiveLoaderStatusText.Foreground =
                        (Brush)FindResource("CyanBrush");

                    downloadedPackage =
                        await _ue4ssReleaseService.DownloadAsync();

                    // The user could start the game through Steam while the download is
                    // running, so check again before changing anything in Win64.
                    if (_gameProcessService.IsGameRunning(gameDirectory))
                    {
                        throw new InvalidOperationException(
                            "Dead as Disco started while the loader was downloading. " +
                            "Close the game and try the setup again.");
                    }

                    LiveLoaderStatusText.Text =
                        "INSTALLING";

                    installResult =
                        await _ue4ssInstallerService.InstallAsync(
                            gameDirectory,
                            downloadedPackage.PackagePath);

                    installedLoader =
                        _ue4ssDetectionService.Detect(
                            gameDirectory);

                    if (!installedLoader.IsInstalled ||
                        !_ue4ssConfigurationService.IsRuntimeCompatible(
                            installedLoader))
                    {
                        throw new InvalidOperationException(
                            "The compatible live-loader files could not be verified after installation.");
                    }
                }

                LiveLoaderStatusText.Text =
                    "CONFIGURING";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // Apply the Dead as Disco signatures and quiet public-facing
                // settings before the bridge is registered.
                _ue4ssConfigurationService.Apply(
                    installedLoader);

                if (!_ue4ssConfigurationService.IsConfigured(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Dead as Disco live-loader configuration could not be verified.");
                }

                LiveLoaderStatusText.Text =
                    "ADDING BRIDGE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // The bridge is Limelight's own Lua mod. Existing UE4SS settings and other
                // installed Lua mods are left in place.
                _liveLoaderBridgeService.EnsureInstalled(
                    installedLoader);

                if (!_liveLoaderBridgeService.IsInstalled(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Limelight runtime bridge could not be verified.");
                }

                LiveLoaderStatusText.Text =
 "ADDING NATIVE BRIDGE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // I install the native companion only after UE4SS and the Lua bridge
                // have both passed their checks.
                _nativeBridgeInstallerService.EnsureInstalled(
                    installedLoader);

                _stagehandPayloadService.EnsureInstalled(
                    installedLoader);

                if (!_nativeBridgeInstallerService.IsCurrentVersionInstalled(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Limelight native bridge could not be verified.");
                }

                if (!_stagehandPayloadService.IsCurrentVersionInstalled(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Limelight Stagehand runtime could not be verified.");
                }

                _settings.DismissedLiveLoaderPromptForGameDirectory =
                    string.Empty;

                _settingsService.Save(_settings);
            }
            catch (Exception exception)
            {
                setupFailure = exception;
            }
            finally
            {
                if (downloadedPackage is not null)
                {
                    try
                    {
                        // The installed files and any rollback backup are elsewhere,
                        // so the downloaded ZIP is no longer needed.
                        File.Delete(
                            downloadedPackage.PackagePath);
                    }
                    catch
                    {
                        // Windows can clear this temporary file later.
                    }
                }

                IsEnabled = previousEnabledState;
                Mouse.OverrideCursor = null;

                _isLiveLoaderSetupRunning = false;
                UpdateGameRunningStatus();
            }

            if (setupFailure is not null)
            {
                ShowLimelightDialog(
                    "LIVE LOADER SETUP FAILED",
                    "No mod-library features were disabled, so Limelight can still manage imported mods normally.",
                    LimelightDialogTone.Error,
                    details: setupFailure.Message,
                    eyebrow: "SETUP MISSED ITS CUE");

                return;
            }

            string backupMessage =
                installResult?.CreatedBackup == true
                    ? "\n\nExisting loader files were backed up before installation."
                    : string.Empty;

            ShowLimelightDialog(
                "LIVE LOADER READY",
                "The Live Loader was set up successfully. It will start the next time Dead as Disco launches." +
                backupMessage,
                LimelightDialogTone.Success,
                eyebrow: "SETUP COMPLETE");
        }

        private async Task CheckForExistingMods()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                return;
            }

            string gameDirectory =
                _gameDirectory;

            int existingModCount =
                _existingModsMigrationService.CountExistingMods(
                    gameDirectory);

            if (existingModCount == 0)
            {
                return;
            }

            string modLabel =
                existingModCount == 1
                    ? "1 existing mod"
                    : $"{existingModCount} existing mods";

            LimelightDialogChoice choice =
                ShowLimelightDialog(
                    "EXISTING MODS FOUND",
                    $"Limelight found {modLabel} inside the game's ~mods folder. Would you like to move them into the Limelight library?",
                    LimelightDialogTone.Question,
                    primaryAction: "MOVE MODS",
                    secondaryAction: "NOT NOW",
                    footerHint: "FILES STAY IN PLACE UNTIL THE LIBRARY IS SAVED");

            if (choice != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                List<InstalledMod> librarySnapshot =
                    _settings.InstalledMods.ToList();

                ExistingModsMigrationPlan plan =
                    await Task.Run(() =>
                        _existingModsMigrationService.PrepareMigration(
                            gameDirectory,
                            librarySnapshot));

                _settings.InstalledMods.AddRange(
                    plan.ImportedMods);

                bool conventionalModsPreserved =
                    false;

                foreach (InstalledMod migratedMod in
                         plan.ImportedMods.Where(mod =>
                             mod.IsConventionalMod))
                {
                    if (_settings.EnabledConventionalModIds.Any(id =>
                            string.Equals(
                                id,
                                migratedMod.Id,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    _settings.EnabledConventionalModIds.Add(
                        migratedMod.Id);

                    conventionalModsPreserved =
                        true;
                }

                if (conventionalModsPreserved)
                {
                    _settings.ConventionalModsNeedSynchronization =
                        true;

                    _pendingDeploymentAttempted =
                        false;
                }

                _settingsService.Save(_settings);

                // Originals are removed only after settings.json contains
                // every successfully prepared library entry.
                await Task.Run(() =>
                    _existingModsMigrationService.CompleteMigration(
                        plan));

                if (conventionalModsPreserved)
                {
                    await ApplyPendingDeploymentIfPossible();
                }

                RefreshLibrarySummary();

                ShowLimelightDialog(
                    "MODS JOINED THE LIBRARY",
                    conventionalModsPreserved
                        ? "The existing mods were moved into Limelight successfully. Other replacements stayed enabled for your next launch."
                        : "The existing mods were moved into Limelight successfully. Choose the character you want and select Activate.",
                    LimelightDialogTone.Success,
                    eyebrow: "MIGRATION COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MIGRATION COULD NOT FINISH",
                    "Limelight left the existing files in place.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "MIGRATION FAILED");
            }
        }

        private async Task<List<ModAssetPackage>>
            GetLivePackagesAsync(
                InstalledMod mod)
        {
            if (mod.AssetPackages.Count == 0 ||
                mod.AssetManifestVersion <
                    ModAssetScannerService.CurrentManifestVersion)
            {
                mod.AssetPackages =
                    await Task.Run(() =>
                        _modLibraryService.ScanAssets(
                            mod));

                mod.AssetManifestVersion =
                    ModAssetScannerService.CurrentManifestVersion;

                _settingsService.Save(_settings);
            }

            return mod.AssetPackages
                .Where(package =>
                    package.IsSafeForLiveReload)
                .OrderBy(package =>
                    package.ReloadPriority)
                .ThenBy(package =>
                    package.PackagePath)
                .ToList();
        }

        private async Task RetireStaleLiveContainersAsync(
            string gameDirectory,
            Action<string, int>? reportProgress)
        {
            List<LiveSessionMountRecord> candidates =
                _liveSessionService.GetRetirableMountedContainers(
                    gameDirectory);

            HashSet<string> protectedGenerations =
                candidates
                    .GroupBy(
                        record =>
                            string.IsNullOrWhiteSpace(record.GenerationId)
                                ? $"pak:{record.PakPath}"
                                : $"generation:{record.GenerationId}",
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group =>
                        group.Max(record =>
                            record.MountedAt ??
                            record.StagedAt))
                    .Take(RetainedLiveRollbackGenerations)
                    .Select(group =>
                        group.Key)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            List<LiveSessionMountRecord> staleContainers =
                candidates
                    .Where(record =>
                        !protectedGenerations.Contains(
                            string.IsNullOrWhiteSpace(record.GenerationId)
                                ? $"pak:{record.PakPath}"
                                : $"generation:{record.GenerationId}"))
                    .ToList();

            if (staleContainers.Count == 0)
            {
                return;
            }

            reportProgress?.Invoke(
                "RECYCLING OLD MODEL CONTAINERS",
                20);

            foreach (LiveSessionMountRecord staleContainer in
                     staleContainers)
            {
                LiveLoaderCommandResult unmountResult =
                    new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message = "Unreal has not accepted the recycle request yet."
                    };

                for (int attempt = 0;
                     attempt < 3 && !unmountResult.Success;
                     attempt++)
                {
                    await EnsureLiveWorldStableAsync();

                    unmountResult =
                        await _liveLoaderCommandService.UnmountPakAsync(
                            staleContainer.PakPath);

                    if (!unmountResult.Success &&
                        attempt < 2)
                    {
                        await Task.Delay(250 * (attempt + 1));
                    }
                }

                if (!unmountResult.Success)
                {
                    _liveSessionService.RecordRetirementFailure(
                        staleContainer.PakPath,
                        unmountResult.Message);

                    // I stop this one swap instead of leaking mounted
                    // containers forever or pretending an arbitrary count is
                    // the real problem. The next attempt retries the recycle.
                    throw new InvalidOperationException(
                        "Limelight could not safely recycle an older model container yet. " +
                        unmountResult.Message +
                        " Wait until the current level is stable, then try the swap again.");
                }

                _liveSessionService.RecordUnmountedContainer(
                    staleContainer.PakPath);

                LiveSessionCleanupResult cleanup =
                    _liveSessionService.DeleteRetiredContainerFiles(
                        staleContainer.PakPath,
                        gameDirectory);

                if (cleanup.Errors.Count > 0)
                {
                    // I can retry a busy file when the game closes; Unreal has
                    // already returned the important mounted-container slot.
                    _liveSessionService.RecordRetirementFailure(
                        staleContainer.PakPath,
                        string.Join(
                            "; ",
                            cleanup.Errors));
                }
            }
        }

        private async Task ActivateDeployedLiveModAsync(
            InstalledMod mod,
            string gameDirectory,
            Action<string, int>? reportProgress = null,
            bool allowDeferredCharlieRefresh = false)
        {
            reportProgress?.Invoke(
                "READING THE DEPLOYED MODEL",
                35);

            if (mod.IsCharacterSlotMod)
            {
                _characterSlotLoaderService.EnsureInstalled(
                    gameDirectory);
            }

            List<ModAssetPackage> livePackages =
                await GetLivePackagesAsync(mod);

            if (livePackages.Count == 0)
            {
                throw new InvalidDataException(
                    "The active mod does not contain any assets Limelight can refresh.");
            }

            bool hasCharlieReplacement =
                livePackages.Any(package =>
                    package.IsCharlieMesh);

            if (!hasCharlieReplacement &&
                !mod.IsCharacterSlotMod)
            {
                throw new InvalidDataException(
                    "The active mod neither replaces SK_Charlie nor contains a complete Character Slot model.");
            }

            string generationId =
                _liveSessionService.BeginActivation(
                    mod,
                    gameDirectory);

            bool activeAssetsPending = false;

            try
            {
                await EnsureLiveWorldStableAsync();

                reportProgress?.Invoke(
                    "LINKING THE DEPLOYED MODEL",
                    68);

                LiveLoaderCommandResult rememberResult =
                    await _liveLoaderCommandService.RememberActiveAssetsAsync(
                        livePackages.Select(package =>
                            package.ObjectPath));

                if (!rememberResult.Success)
                {
                    throw new InvalidOperationException(
                        rememberResult.Message);
                }

                activeAssetsPending = true;

                // The active model was already mounted by Unreal from ~mods at
                // process startup. Mounting a duplicate live container races the
                // initial Asset Registry and caused startup-only verification
                // failures. Prime the existing packages instead.
                LiveLoaderCommandResult preloadResult =
                    await _liveLoaderCommandService.ReloadAssetsAsync(
                        livePackages.Select(package =>
                            package.ObjectPath));

                if (!preloadResult.Success)
                {
                    throw new InvalidOperationException(
                        preloadResult.Message);
                }

                int[] retryDelaysMilliseconds =
                {
                    0,
                    180,
                    320,
                    550,
                    850,
                    1250,
                    1800
                };

                LiveLoaderCommandResult reapplyResult =
                    new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message = "The deployed player model is not ready yet."
                    };

                bool deferredCharlieRefresh = false;

                foreach (int delayMilliseconds in
                         retryDelaysMilliseconds)
                {
                    if (delayMilliseconds > 0)
                    {
                        await Task.Delay(
                            delayMilliseconds);
                    }

                    await EnsureLiveWorldStableAsync();

                    reapplyResult =
                        await ReapplySelectedPlayerMeshAsync(
                            mod);

                    if (reapplyResult.Success)
                    {
                        break;
                    }

                    bool playerHasNotAppeared =
                        reapplyResult.Message.Contains(
                            "No active Charlie pawn",
                            StringComparison.OrdinalIgnoreCase) ||
                        reapplyResult.Message.Contains(
                            "No local cosmetic subsystem is ready",
                            StringComparison.OrdinalIgnoreCase);

                    if (allowDeferredCharlieRefresh &&
                        playerHasNotAppeared)
                    {
                        deferredCharlieRefresh = true;
                        break;
                    }
                }

                if (!reapplyResult.Success &&
                    !deferredCharlieRefresh)
                {
                    throw new InvalidOperationException(
                        reapplyResult.Message);
                }

                LiveLoaderCommandResult commitAssetsResult =
                    await _liveLoaderCommandService
                        .CommitActiveAssetsAsync();

                if (!commitAssetsResult.Success)
                {
                    throw new InvalidOperationException(
                        commitAssetsResult.Message);
                }

                activeAssetsPending = false;

                reportProgress?.Invoke(
                    deferredCharlieRefresh
                        ? "READY: MODEL WILL APPEAR WITH CHARLIE"
                        : "DEPLOYED MODEL READY",
                    96);

                _liveSessionService.CompleteActivation(
                    mod,
                    generationId);

                RememberCharacterSlotForSession(
                    mod);
            }
            catch (Exception exception)
            {
                if (activeAssetsPending)
                {
                    try
                    {
                        await _liveLoaderCommandService
                            .RollbackActiveAssetsAsync();
                    }
                    catch
                    {
                        // I keep the startup failure as the useful error. The
                        // next game process starts with a fresh Lua bridge.
                    }
                }

                _liveSessionService.FailActivation(
                    exception);

                throw;
            }
        }

        private async Task ActivateLiveModAsync(
            InstalledMod mod,
            string gameDirectory,
            Action<string, int>? reportProgress = null,
            bool allowDeferredCharlieRefresh = false)
        {
            if (mod.IsCharacterSlotMod)
            {
                _characterSlotLoaderService.EnsureInstalled(
                    gameDirectory);

                if (TryGetCharacterSlotSessionState(
                        mod,
                        out bool canReuseMountedSlot))
                {
                    if (!canReuseMountedSlot)
                    {
                        throw new InvalidOperationException(
                            "This Character Slot package changed while Dead as Disco was running. " +
                            "Restart the game before loading the updated files so Unreal cannot confuse them with the previous package generation.");
                    }

                    await ReactivateMountedCharacterSlotAsync(
                        mod,
                        gameDirectory,
                        reportProgress);

                    return;
                }
            }

            int upcomingContainerCount =
                _liveModStagingService.CountContainers(
                    mod);

            if (upcomingContainerCount == 0)
            {
                throw new InvalidDataException(
                    $"{mod.DisplayName} does not contain a complete pak, utoc, and ucas set.");
            }

            await EnsureLiveWorldStableAsync();

            // I keep the active model plus three rollback generations, then
            // recycle anything older. That gives render streaming a generous
            // runway without inventing a fixed number of swaps per session.
            reportProgress?.Invoke(
                "PREPARING MODEL RESOURCE WINDOW",
                20);

            await RetireStaleLiveContainersAsync(
                gameDirectory,
                reportProgress);

            string generationId =
                _liveSessionService.BeginActivation(
                    mod,
                    gameDirectory);

            bool packagesWereRetired = false;
            bool registeredMountedAssets = false;
            bool activeAssetsPending = false;
            List<ModAssetPackage> livePackages =
                new();

            try
            {
                reportProgress?.Invoke(
                    "SCANNING MOD CONTENT",
                    35);

                livePackages =
                    await GetLivePackagesAsync(mod);

                if (livePackages.Count == 0)
                {
                    throw new InvalidDataException(
                        "This mod does not contain any assets Limelight can safely refresh live.");
                }

                bool hasCharlieReplacement =
                    livePackages.Any(package =>
                        package.IsCharlieMesh);

                if (!hasCharlieReplacement &&
                    !mod.IsCharacterSlotMod)
                {
                    throw new InvalidDataException(
                        "This mod neither replaces SK_Charlie nor contains a complete Character Slot Loader model, so Limelight cannot live-mount it automatically.");
                }

                reportProgress?.Invoke(
                    "STAGING MOD CONTAINER",
                    48);

                LiveModStageResult stageResult =
                    await Task.Run(() =>
                        _liveModStagingService.Stage(
                            mod,
                            gameDirectory));

                await EnsureLiveWorldStableAsync();

                _liveSessionService.RecordStagedContainers(
                    mod,
                    stageResult.PakPaths,
                    gameDirectory,
                    generationId);

                reportProgress?.Invoke(
                    "MOUNTING MOD CONTENT",
                    60);

                foreach (string pakPath in
                         stageResult.PakPaths)
                {
                    await EnsureLiveWorldStableAsync();

                    int mountOrder =
                        _nextLiveMountOrder++;

                    _liveSessionService.RecordMountAttempt(
                        pakPath,
                        mountOrder);

                    LiveLoaderCommandResult mountResult =
                        await _liveLoaderCommandService.MountPakAsync(
                            pakPath,
                            mountOrder);

                    if (!mountResult.Success)
                    {
                        if (!mountResult.Message.Contains(
                                "did not respond",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            // A definite rejection means Unreal never owned this
                            // container, so failed-stage cleanup may remove it.
                            _liveSessionService.RecordRejectedMount(
                                pakPath);
                        }

                        throw new InvalidOperationException(
                            mountResult.Message);
                    }

                    _liveSessionService.RecordMountedContainer(
                        pakPath,
                        mountOrder);
                }

                reportProgress?.Invoke(
                    "REFRESHING OVERRIDDEN PACKAGES",
                    74);

                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult rememberAssetsResult =
                    await _liveLoaderCommandService.RememberActiveAssetsAsync(
                        livePackages.Select(package =>
                            package.ObjectPath));

                if (!rememberAssetsResult.Success)
                {
                    throw new InvalidOperationException(
                        rememberAssetsResult.Message);
                }

                activeAssetsPending = true;

                if (!mod.IsCharacterSlotMod)
                {
                    // I let the native bridge root and rename the old packages,
                    // so Charlie keeps their clothes on while the next act loads.
                    LiveLoaderCommandResult releaseResult =
                        await _liveLoaderCommandService.ReleasePackagesAsync(
                            livePackages.Select(package =>
                                package.PackagePath));

                    if (!releaseResult.Success)
                    {
                        throw new InvalidOperationException(
                            releaseResult.Message);
                    }

                    packagesWereRetired = true;
                }
                else
                {
                    reportProgress?.Invoke(
                        "REGISTERING CHARACTER SLOT CONTENT",
                        76);

                    List<string> slotObjectPaths =
                        livePackages
                            .Select(package =>
                                package.ObjectPath)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)
                            .ToList();

                    // I register the whole CSM container. Oberon quietly borrows
                    // animation and skeleton packages from another slot folder,
                    // and pretending not to notice was not especially helpful.

                    LiveLoaderCommandResult registrationResult =
                        await _liveLoaderCommandService
                            .RegisterMountedAssetsAsync(
                                slotObjectPaths);

                    registeredMountedAssets = true;

                    if (!registrationResult.Success)
                    {
                        // I let the official loader own the PPCD load. Native
                        // preloading does not get to boo it offstage beforehand.
                        reportProgress?.Invoke(
                            "HANDING CSM CONTENT TO CHARACTER LOADER",
                            78);
                    }
                }

                reportProgress?.Invoke(
                    "LOADING MODELS, PORTRAITS AND TEXT",
                    86);

                await EnsureLiveWorldStableAsync();

                List<ModAssetPackage> dependencyPackages =
                    livePackages
                        .Where(package =>
                            package.Kind !=
                                ModAssetKind.SkeletalMesh)
                        .ToList();

                List<ModAssetPackage> meshPackages =
                    livePackages
                        .Where(package =>
                            package.Kind ==
                                ModAssetKind.SkeletalMesh)
                        .ToList();

                if (meshPackages.Count == 0)
                {
                    throw new InvalidDataException(
                        "The selected mod did not expose a loadable skeletal mesh package.");
                }

                LiveLoaderCommandResult dependencyReloadResult =
                    dependencyPackages.Count == 0 ||
                    mod.IsCharacterSlotMod
                        ? new LiveLoaderCommandResult
                        {
                            Success = true,
                            Message = mod.IsCharacterSlotMod
                                ? "Character Loader owns the PPCD dependencies."
                                : string.Empty
                        }
                        : await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                if (!dependencyReloadResult.Success)
                {
                    throw new InvalidOperationException(
                        dependencyReloadResult.Message);
                }

                int[] meshReloadDelaysMilliseconds =
                {
                    0,
                    180,
                    320,
                    550,
                    850,
                    1250,
                    1800
                };

                LiveLoaderCommandResult meshReloadResult =
                    mod.IsCharacterSlotMod
                        ? new LiveLoaderCommandResult
                        {
                            Success = true,
                            Message =
                                "Character Loader will resolve the PPCD mesh."
                        }
                        : new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message = "The replacement skeletal mesh has not registered yet."
                    };

                if (!mod.IsCharacterSlotMod)
                {
                    foreach (int delayMilliseconds in
                             meshReloadDelaysMilliseconds)
                    {
                        if (delayMilliseconds > 0)
                        {
                            await Task.Delay(
                                delayMilliseconds);
                        }

                        await EnsureLiveWorldStableAsync();

                        // A permissive reload reports success even when Unreal
                        // loads zero objects. Requiring every mesh here prevents a
                        // retired model from remaining bound with black texture
                        // resources while the replacement package is unavailable.
                        meshReloadResult =
                            await _liveLoaderCommandService.VerifyAssetsAsync(
                                meshPackages.Select(package =>
                                    package.ObjectPath));

                        if (meshReloadResult.Success)
                        {
                            break;
                        }
                    }
                }

                if (!meshReloadResult.Success)
                {
                    throw new InvalidOperationException(
                        meshReloadResult.Message);
                }

                if (dependencyPackages.Count > 0 &&
                    !mod.IsCharacterSlotMod)
                {
                    // Some material dependencies do not become loadable until
                    // the replacement mesh has opened its package. A short
                    // second pass fills those references before Charlie is
                    // reapplied, which prevents an otherwise valid model from
                    // appearing black.
                    await Task.Delay(180);
                    await EnsureLiveWorldStableAsync();

                    LiveLoaderCommandResult dependencyRetryResult =
                        await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                    if (!dependencyRetryResult.Success)
                    {
                        throw new InvalidOperationException(
                            dependencyRetryResult.Message);
                    }
                }

                List<ModAssetPackage> renderedDependencies =
                    mod.IsCharacterSlotMod
                        ? new List<ModAssetPackage>()
                        : dependencyPackages
                            .Where(package =>
                                package.Kind == ModAssetKind.Texture ||
                                package.Kind == ModAssetKind.Material)
                            .ToList();

                int[] retryDelaysMilliseconds =
                {
                    150,
                    250,
                    400,
                    650,
                    900,
                    1200
                };

                LiveLoaderCommandResult reapplyResult =
                    new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message = "The replacement model was not verified."
                    };

                LiveLoaderCommandResult dependencyVerificationResult =
                    new LiveLoaderCommandResult
                    {
                        Success = true
                    };

                bool deferredCharlieRefresh = false;

                for (int attempt = 0;
                     attempt < retryDelaysMilliseconds.Length;
                     attempt++)
                {
                    await EnsureLiveWorldStableAsync();

                    dependencyVerificationResult =
                        renderedDependencies.Count == 0
                            ? new LiveLoaderCommandResult
                            {
                                Success = true
                            }
                            : await _liveLoaderCommandService.VerifyAssetsAsync(
                                renderedDependencies.Select(package =>
                                    package.ObjectPath));

                    if (dependencyVerificationResult.Success)
                    {
                        reapplyResult =
                            await ReapplySelectedPlayerMeshAsync(
                                mod);

                        if (reapplyResult.Success)
                        {
                            break;
                        }

                        bool playerHasNotAppeared =
                            reapplyResult.Message.Contains(
                                "No active Charlie pawn",
                                StringComparison.OrdinalIgnoreCase) ||
                            reapplyResult.Message.Contains(
                                "No local cosmetic subsystem is ready",
                                StringComparison.OrdinalIgnoreCase);

                        if (allowDeferredCharlieRefresh &&
                            playerHasNotAppeared)
                        {
                            deferredCharlieRefresh = true;
                            break;
                        }
                    }

                    if (attempt ==
                        retryDelaysMilliseconds.Length - 1)
                    {
                        string failureMessage =
                            dependencyVerificationResult.Success
                                ? reapplyResult.Message
                                : dependencyVerificationResult.Message;

                        throw new InvalidOperationException(
                            failureMessage);
                    }

                    // Cooked dependencies can finish registering just after
                    // SK_Charlie opens. I retry with a small backoff instead of
                    // accepting Unreal's black fallback material as success.
                    await Task.Delay(
                        retryDelaysMilliseconds[attempt]);
                }

                if (reapplyResult.Success &&
                    packagesWereRetired)
                {
                    if (!deferredCharlieRefresh &&
                        renderedDependencies.Count > 0)
                    {
                        int[] stabilizationDelaysMilliseconds =
                        {
                            120,
                            220,
                            400
                        };

                        for (int attempt = 0;
                             attempt < stabilizationDelaysMilliseconds.Length;
                             attempt++)
                        {
                            await Task.Delay(
                                stabilizationDelaysMilliseconds[attempt]);

                            await EnsureLiveWorldStableAsync();

                            LiveLoaderCommandResult stabilizationResult =
                                await _liveLoaderCommandService.ReloadAssetsAsync(
                                    renderedDependencies.Select(package =>
                                        package.ObjectPath));

                            if (!stabilizationResult.Success)
                            {
                                continue;
                            }

                            await EnsureLiveWorldStableAsync();

                            LiveLoaderCommandResult stabilizationReapplyResult =
                                await ReapplySelectedPlayerMeshAsync(
                                    mod);

                            if (stabilizationReapplyResult.Success)
                            {
                                break;
                            }
                        }
                    }

                }

                if (dependencyPackages.Count > 0 &&
                    !mod.IsCharacterSlotMod)
                {
                    // The automatic world refresh needs every non-mesh asset,
                    // not only the strict material verification subset.
                    LiveLoaderCommandResult rememberedAssetsResult =
                        await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                    if (!rememberedAssetsResult.Success)
                    {
                        throw new InvalidOperationException(
                            rememberedAssetsResult.Message);
                    }
                }

                if (reapplyResult.Success &&
                    !deferredCharlieRefresh &&
                    !mod.IsCharacterSlotMod)
                {
                    // Reloading every dependency can update a material's
                    // texture objects after the earlier mesh bind. Give the
                    // streaming manager one final frame window, then bind and
                    // rebuild Charlie again before the native bridge is allowed
                    // to release its temporary retirement roots.
                    await Task.Delay(500);
                    await EnsureLiveWorldStableAsync();

                    LiveLoaderCommandResult finalReapplyResult =
                        await ReapplySelectedPlayerMeshAsync(
                            mod);

                    if (!finalReapplyResult.Success)
                    {
                        throw new InvalidOperationException(
                            finalReapplyResult.Message);
                    }
                }

                if (registeredMountedAssets &&
                    reapplyResult.Success &&
                    !deferredCharlieRefresh)
                {
                    LiveLoaderCommandResult registeredAssetReleaseResult =
                        await _liveLoaderCommandService
                            .ReleaseRegisteredAssetsAsync();

                    if (!registeredAssetReleaseResult.Success)
                    {
                        if (!mod.IsCharacterSlotMod)
                        {
                            throw new InvalidOperationException(
                                registeredAssetReleaseResult.Message);
                        }

                        // I keep partially registered CSM roots for this game
                        // process if cleanup sulks. The verified model matters
                        // more than a dramatic but harmless error card.
                        reportProgress?.Invoke(
                            "CHARACTER SLOT READY; CACHE RETAINED",
                            96);
                    }
                    else
                    {
                        registeredMountedAssets = false;
                    }
                }

                if (reapplyResult.Success &&
                    packagesWereRetired)
                {
                    LiveLoaderCommandResult retirementResult =
                        await _liveLoaderCommandService
                            .ConfirmPackageRetirementAsync();

                    if (!retirementResult.Success)
                    {
                        throw new InvalidOperationException(
                            retirementResult.Message);
                    }

                    LiveLoaderCommandResult settlementResult =
                        await WaitForRetiredAssetsToSettleAsync(
                            reportProgress);

                    if (!settlementResult.Success)
                    {
                        throw new InvalidOperationException(
                            settlementResult.Message);
                    }
                }

                LiveLoaderCommandResult commitAssetsResult =
                    await _liveLoaderCommandService
                        .CommitActiveAssetsAsync();

                if (!commitAssetsResult.Success)
                {
                    throw new InvalidOperationException(
                        commitAssetsResult.Message);
                }

                activeAssetsPending = false;

                reportProgress?.Invoke(
                    deferredCharlieRefresh
                        ? "READY: CHARLIE WILL REFRESH WHEN SHE APPEARS"
                        : "LIVE LOADER READY",
                    100);

                _liveSessionService.CompleteActivation(
                    mod,
                    generationId);

                RememberCharacterSlotForSession(
                    mod);
            }
            catch (Exception exception)
            {
                if (registeredMountedAssets)
                {
                    try
                    {
                        await _liveLoaderCommandService
                            .ReleaseRegisteredAssetsAsync();
                    }
                    catch
                    {
                        // I keep the original activation error in the spotlight.
                        // The native bridge can tidy these temporary roots when
                        // the game closes if this cleanup decides to sulk.
                    }
                }

                try
                {
                    await RollbackFailedLiveActivationAsync(
                        generationId,
                        gameDirectory,
                        livePackages,
                        packagesWereRetired,
                        activeAssetsPending);
                }
                catch
                {
                    // I preserve the activation error the tester actually hit.
                    // A game restart remains the safe fallback if rollback is
                    // interrupted by a level transition.
                }

                // Anything Unreal already mounted stays recorded for the guarded
                // retirement path. Files which never mounted are safe to remove now.
                _liveSessionService.DeleteUncommittedGenerationFiles(
                    generationId,
                    gameDirectory);

                _liveSessionService.FailActivation(
                    exception);

                throw;
            }
        }

        private async Task RollbackFailedLiveActivationAsync(
            string generationId,
            string gameDirectory,
            IReadOnlyCollection<ModAssetPackage> livePackages,
            bool packagesWereRetired,
            bool activeAssetsPending)
        {
            if (activeAssetsPending)
            {
                await EnsureLiveWorldStableAsync();

                await _liveLoaderCommandService
                    .RollbackActiveAssetsAsync();
            }

            bool retirementWindowReady = true;

            if (packagesWereRetired)
            {
                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult retirementResult =
                    await _liveLoaderCommandService
                        .ConfirmPackageRetirementAsync();

                retirementWindowReady =
                    retirementResult.Success;

                if (retirementWindowReady)
                {
                    LiveLoaderCommandResult settlementResult =
                        await WaitForRetiredAssetsToSettleAsync(
                            reportProgress: null);

                    retirementWindowReady =
                        settlementResult.Success;
                }
            }

            List<LiveSessionMountRecord> failedContainers =
                _liveSessionService.Load()
                    .Mounts
                    .Where(record =>
                        record.WasMounted &&
                        !record.WasUnmounted &&
                        string.Equals(
                            record.GenerationId,
                            generationId,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(record =>
                        record.MountOrder)
                    .ToList();

            bool allFailedContainersUnmounted = true;

            foreach (LiveSessionMountRecord failedContainer in
                     failedContainers)
            {
                LiveLoaderCommandResult unmountResult =
                    new()
                    {
                        Success = false,
                        Message = "Unreal has not accepted the failed-generation cleanup yet."
                    };

                for (int attempt = 0;
                     attempt < 3 && !unmountResult.Success;
                     attempt++)
                {
                    await EnsureLiveWorldStableAsync();

                    unmountResult =
                        await _liveLoaderCommandService
                            .UnmountPakAsync(
                                failedContainer.PakPath);

                    if (!unmountResult.Success &&
                        attempt < 2)
                    {
                        await Task.Delay(
                            250 * (attempt + 1));
                    }
                }

                if (!unmountResult.Success)
                {
                    allFailedContainersUnmounted = false;

                    _liveSessionService.RecordRetirementFailure(
                        failedContainer.PakPath,
                        unmountResult.Message);

                    continue;
                }

                _liveSessionService.RecordUnmountedContainer(
                    failedContainer.PakPath);

                LiveSessionCleanupResult cleanup =
                    _liveSessionService.DeleteRetiredContainerFiles(
                        failedContainer.PakPath,
                        gameDirectory);

                if (cleanup.Errors.Count > 0)
                {
                    _liveSessionService.RecordRetirementFailure(
                        failedContainer.PakPath,
                        string.Join(
                            "; ",
                            cleanup.Errors));
                }
            }

            List<ModAssetPackage> failedStringTables =
                livePackages
                    .Where(package =>
                        package.Kind ==
                            ModAssetKind.StringTable)
                    .ToList();

            if (!retirementWindowReady ||
                !allFailedContainersUnmounted ||
                failedStringTables.Count == 0)
            {
                return;
            }

            await EnsureLiveWorldStableAsync();

            LiveLoaderCommandResult releaseResult =
                await _liveLoaderCommandService
                    .ReleasePackagesAsync(
                        failedStringTables.Select(package =>
                            package.PackagePath));

            if (!releaseResult.Success)
            {
                return;
            }

            try
            {
                await EnsureLiveWorldStableAsync();

                // The failed container is gone, so this path now resolves to
                // the previous CRM or Dead as Disco's original string table.
                await _liveLoaderCommandService
                    .ReloadAssetsAsync(
                        failedStringTables.Select(package =>
                            package.ObjectPath));
            }
            finally
            {
                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult retirementResult =
                    await _liveLoaderCommandService
                        .ConfirmPackageRetirementAsync();

                if (retirementResult.Success)
                {
                    await WaitForRetiredAssetsToSettleAsync(
                        reportProgress: null);
                }
            }
        }

        private bool TryGetCharacterSlotSessionState(
            InstalledMod mod,
            out bool canReuseMountedSlot)
        {
            canReuseMountedSlot = false;

            if (!mod.IsCharacterSlotMod ||
                !_characterSlotFingerprintsForSession.TryGetValue(
                    mod.CharacterSlotDefinitionPackagePath,
                    out string? mountedFingerprint))
            {
                return false;
            }

            canReuseMountedSlot =
                string.Equals(
                    mountedFingerprint,
                    GetCharacterSlotSessionFingerprint(mod),
                    StringComparison.OrdinalIgnoreCase);

            return true;
        }

        private void RememberCharacterSlotForSession(
            InstalledMod mod)
        {
            if (!mod.IsCharacterSlotMod)
            {
                return;
            }

            _characterSlotFingerprintsForSession[
                mod.CharacterSlotDefinitionPackagePath] =
                    GetCharacterSlotSessionFingerprint(mod);
        }

        private static string GetCharacterSlotSessionFingerprint(
            InstalledMod mod)
        {
            return string.IsNullOrWhiteSpace(
                       mod.ContentFingerprint)
                ? mod.Id
                : mod.ContentFingerprint.Trim();
        }

        private async Task ReactivateMountedCharacterSlotAsync(
            InstalledMod mod,
            string gameDirectory,
            Action<string, int>? reportProgress)
        {
            string generationId =
                _liveSessionService.BeginActivation(
                    mod,
                    gameDirectory);

            try
            {
                reportProgress?.Invoke(
                    "REUSING REGISTERED CHARACTER SLOT",
                    72);

                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult activationResult =
                    await ReapplySelectedPlayerMeshAsync(
                        mod);

                if (!activationResult.Success)
                {
                    throw new InvalidOperationException(
                        activationResult.Message);
                }

                reportProgress?.Invoke(
                    "CHARACTER SLOT READY",
                    100);

                _liveSessionService.CompleteActivation(
                    mod,
                    generationId);
            }
            catch (Exception exception)
            {
                _liveSessionService.FailActivation(
                    exception);

                throw;
            }
        }

        private async Task EnsureLiveWorldStableAsync()
        {
            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .IsWorldStableAsync();

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.Message);
            }
        }

        private async Task<LiveLoaderCommandResult>
            WaitForRetiredAssetsToSettleAsync(
                Action<string, int>? reportProgress)
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(35);

            int consecutiveReadyChecks = 0;

            // The native bridge releases temporary UObject roots on Unreal's
            // garbage-collection pass. Do not let a rapid next click mount a
            // new generation into that retirement window.
            await Task.Delay(350);

            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .CanSwitchModsAsync();

            while (DateTime.UtcNow < deadline)
            {
                if (result.Success)
                {
                    consecutiveReadyChecks++;

                    if (consecutiveReadyChecks >= 3)
                    {
                        return result;
                    }

                    await Task.Delay(300);
                }
                else
                {
                    consecutiveReadyChecks = 0;

                    if (!IsTemporaryLiveSwitchDelay(
                            result.Message))
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "FINALISING THE PREVIOUS MODEL",
                        98);

                    await Task.Delay(450);
                }

                result =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();
            }

            return new LiveLoaderCommandResult
            {
                Success = false,
                Message =
                    "Unreal did not finish retiring the previous model resources in time. The current model is safe, but restart the game before another live swap."
            };
        }

        private Task<LiveLoaderCommandResult>
            ReapplySelectedPlayerMeshAsync(
                InstalledMod mod)
        {
            return mod.IsCharacterSlotMod
                ? _liveLoaderCommandService.ActivateCharacterSlotAsync(
                    mod.CharacterSlotDefinitionObjectPath,
                    mod.CharacterSlotMeshObjectPath,
                    mod.CharacterSlotName)
                : _liveLoaderCommandService.ReapplyCharlieAsync();
        }

        private List<InstalledMod> GetX19Rotation()
        {
            // I rebuild the rotation from the current library so removed mods
            // can never leave a dead entry behind in the hotkey cycle.
            return _settings.X19LoaderModIds
                .Select(id =>
                    _settings.InstalledMods.FirstOrDefault(mod =>
                        string.Equals(
                            mod.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
                .Where(mod =>
                    mod is not null &&
                    Directory.Exists(mod.InstallDirectory))
                .Cast<InstalledMod>()
                .ToList();
        }

        private int GetNextX19RotationIndex(
            int rotationCount,
            int currentIndex)
        {
            if (rotationCount <= 1)
            {
                return 0;
            }

            if (!_settings.X19ShuffleEnabled)
            {
                return currentIndex < 0
                    ? 0
                    : (currentIndex + 1) % rotationCount;
            }

            if (currentIndex < 0)
            {
                return Random.Shared.Next(rotationCount);
            }

            // The offset starts at one, so shuffle still feels random without
            // choosing the character which is already on stage.
            int offset =
                Random.Shared.Next(
                    1,
                    rotationCount);

            return (currentIndex + offset) % rotationCount;
        }

        private void EnableX19Hotkey()
        {
            _globalHotkeyService.Unregister();

            if (_selectedLoaderMode !=
                LoaderLaunchMode.X19)
            {
                return;
            }

            if (_globalHotkeyService.Register(
                    this,
                    _settings.X19HotkeyGesture,
                    () =>
                        _gameProcessService.IsGameWindowForeground(
                            _gameDirectory),
                    out string errorMessage))
            {
                return;
            }

            _selectedLoaderMode =
                LoaderLaunchMode.Normal;

            ShowNotification(
                "X19 HOTKEY UNAVAILABLE",
                errorMessage +
                " Limelight will use the normal Live Loader for this session.",
                isError: true);
        }

        private async void X19HotkeyPressed()
        {
            if (_selectedLoaderMode != LoaderLaunchMode.X19 ||
                _isLiveModChangeRunning ||
                _isX19SafetyProbeRunning ||
                string.IsNullOrWhiteSpace(_gameDirectory) ||
                !_gameProcessService.IsGameRunning(
                    _gameDirectory) ||
                !_gameProcessService.IsGameWindowForeground(
                    _gameDirectory))
            {
                return;
            }

            _isX19SafetyProbeRunning = true;

            try
            {
                // X19 never queues a key press. If Unreal is loading, streaming,
                // or retiring the previous model, the current character stays put.
                LiveLoaderCommandResult safetyCheck =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();

                if (!safetyCheck.Success ||
                    _selectedLoaderMode != LoaderLaunchMode.X19 ||
                    _isLiveModChangeRunning ||
                    !_gameProcessService.IsGameWindowForeground(
                        _gameDirectory))
                {
                    ShowX19BlockedPulse();
                    return;
                }

                List<InstalledMod> rotation =
                    GetX19Rotation();

                if (rotation.Count == 0)
                {
                    ShowX19BlockedPulse();
                    return;
                }

                int currentIndex =
                    rotation.FindIndex(mod =>
                        string.Equals(
                            mod.Id,
                            _settings.ActiveModId,
                            StringComparison.OrdinalIgnoreCase));

                int nextIndex =
                    GetNextX19RotationIndex(
                        rotation.Count,
                        currentIndex);

                InstalledMod nextMod =
                    rotation[nextIndex];

                if (rotation.Count == 1 &&
                    string.Equals(
                        nextMod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    X19SwitchPulseWindow completePulse =
                        new X19SwitchPulseWindow();

                    completePulse.ShowOverGame(
                        _gameProcessService.FindGameWindow(
                            _gameDirectory));

                    completePulse.ShowSuccess();
                    return;
                }

                _isX19SwitchRequest = true;
                ToggleModRequested(
                    nextMod.Id);
            }
            catch
            {
                ShowX19BlockedPulse();
            }
            finally
            {
                _isX19SafetyProbeRunning = false;
            }
        }

        private void ShowX19BlockedPulse()
        {
            X19SwitchPulseWindow errorPulse =
                new X19SwitchPulseWindow();

            errorPulse.ShowOverGame(
                _gameProcessService.FindGameWindow(
                    _gameDirectory));

            errorPulse.ShowError();
        }

        private List<InstalledMod> FindEnabledConventionalConflicts(
            InstalledMod selectedMod)
        {
            var selectedPackagePaths =
                new HashSet<string>(
                    selectedMod.AssetPackages
                        .Select(package =>
                            package.PackagePath)
                        .Where(packagePath =>
                            !string.IsNullOrWhiteSpace(packagePath)),
                    StringComparer.OrdinalIgnoreCase);

            if (selectedPackagePaths.Count == 0)
            {
                return new List<InstalledMod>();
            }

            // I only block mods that replace the same Unreal package. Different
            // bosses, enemies, and world assets are free to stay enabled together.
            return GetEnabledConventionalMods()
                .Where(mod =>
                    mod.AssetPackages.Any(package =>
                        selectedPackagePaths.Contains(
                            package.PackagePath)))
                .ToList();
        }

        private async Task ToggleConventionalModAsync(
            InstalledMod selectedMod,
            string gameDirectory)
        {
            _settings.EnabledConventionalModIds ??=
                new List<string>();

            List<string> originalEnabledIds =
                _settings.EnabledConventionalModIds.ToList();

            bool originalSynchronizationState =
                _settings.ConventionalModsNeedSynchronization;

            bool isEnabled =
                _settings.EnabledConventionalModIds.Any(id =>
                    string.Equals(
                        id,
                        selectedMod.Id,
                        StringComparison.OrdinalIgnoreCase));

            if (!isEnabled)
            {
                List<InstalledMod> conflicts =
                    FindEnabledConventionalConflicts(
                        selectedMod);

                if (conflicts.Count > 0)
                {
                    string conflictNames =
                        string.Join(
                            ", ",
                            conflicts.Select(mod =>
                                mod.DisplayName));

                    LimelightDialogChoice choice =
                        ShowLimelightDialog(
                            "REPLACEMENT CONFLICT FOUND",
                            $"{selectedMod.DisplayName} replaces the same game asset as {conflictNames}. Disable the conflicting replacement and enable this one instead?",
                            LimelightDialogTone.Question,
                            primaryAction: "SWITCH REPLACEMENT",
                            secondaryAction: "KEEP CURRENT",
                            eyebrow: "ONE MOD PER TARGET");

                    if (choice != LimelightDialogChoice.Primary)
                    {
                        return;
                    }

                    foreach (InstalledMod conflict in conflicts)
                    {
                        _settings.EnabledConventionalModIds.RemoveAll(id =>
                            string.Equals(
                                id,
                                conflict.Id,
                                StringComparison.OrdinalIgnoreCase));
                    }
                }

                _settings.EnabledConventionalModIds.Add(
                    selectedMod.Id);
            }
            else
            {
                _settings.EnabledConventionalModIds.RemoveAll(id =>
                    string.Equals(
                        id,
                        selectedMod.Id,
                        StringComparison.OrdinalIgnoreCase));
            }

            _settings.ConventionalModsNeedSynchronization =
                true;

            _pendingDeploymentAttempted =
                false;

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            try
            {
                if (!isGameRunning)
                {
                    InstalledMod? activeMod =
                        _settings.InstalledMods.FirstOrDefault(mod =>
                            mod.IsPlayerCharacterMod &&
                            string.Equals(
                                mod.Id,
                                _settings.ActiveModId,
                                StringComparison.OrdinalIgnoreCase) &&
                            Directory.Exists(mod.InstallDirectory));

                    List<InstalledMod> characterSlotCatalogue =
                        GetCharacterSlotCatalogue();

                    await Task.Run(() =>
                        SynchronizeModDeployment(
                            activeMod,
                            characterSlotCatalogue,
                            gameDirectory));

                    _settings.ConventionalModsNeedSynchronization =
                        false;
                }

                _settingsService.Save(_settings);
                RefreshLibrarySummary();

                ShowNotification(
                    isEnabled
                        ? "MOD DISABLED"
                        : "MOD ENABLED",
                    isGameRunning
                        ? $"{selectedMod.DisplayName} will be {(isEnabled ? "disabled" : "enabled")} after Dead as Disco closes, ready for the next launch."
                        : $"{selectedMod.DisplayName} is {(isEnabled ? "disabled" : "enabled")} for the next launch.",
                    isError: false);
            }
            catch (Exception exception)
            {
                _settings.EnabledConventionalModIds =
                    originalEnabledIds;

                _settings.ConventionalModsNeedSynchronization =
                    originalSynchronizationState;

                _settingsService.Save(_settings);
                RefreshLibrarySummary();

                ShowNotification(
                    "MOD CHANGE FAILED",
                    exception.Message,
                    isError: true);
            }
        }

        private async void ToggleModRequested(
    string modId)
        {
            if (_isLiveModChangeRunning)
            {
                _isX19SwitchRequest = false;
                return;
            }

            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod == null)
            {
                _isX19SwitchRequest = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowNotification(
                    "GAME NOT CONNECTED",
                    "Connect the Dead as Disco installation before activating a mod.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (selectedMod.IsConventionalMod)
            {
                await ToggleConventionalModAsync(
                    selectedMod,
                    gameDirectory);

                _isX19SwitchRequest = false;
                return;
            }

            bool isCurrentlyActive =
                string.Equals(
                    _settings.ActiveModId,
                    selectedMod.Id,
                    StringComparison.OrdinalIgnoreCase);

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (isGameRunning &&
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled)
            {
                ShowNotification(
                    "LIVE LOADER DISABLED",
                    "This game session was launched without live switching. Close Dead as Disco before changing the deployed mod.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            bool useX19Pulse =
                _isX19SwitchRequest &&
                isGameRunning;

            if (isCurrentlyActive &&
                isGameRunning)
            {
                ShowNotification(
                    "CLOSE THE GAME TO DEACTIVATE",
                    "The active live container cannot be removed safely while Dead as Disco is running.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            _isLiveModChangeRunning = true;
            _discordPresenceSwitchTarget =
                selectedMod.DisplayName;

            RefreshDiscordPresence(
                isGameRunning);

            LiveLoaderStatusText.Text =
                isGameRunning
                    ? "SWITCHING"
                    : LiveLoaderStatusText.Text;

            if (isGameRunning)
            {
                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource(
                        "CyanBrush");
            }

            LiveModSwitchingWindow? switchingWindow =
                null;

            X19SwitchPulseWindow? x19PulseWindow =
                null;

            void CloseSwitchingWindow()
            {
                if (switchingWindow is null)
                {
                    return;
                }

                switchingWindow.CloseWhenFinished();
                switchingWindow = null;
            }

            void CloseX19PulseWindow()
            {
                if (x19PulseWindow is null)
                {
                    return;
                }

                x19PulseWindow.CloseWhenFinished();
                x19PulseWindow = null;
            }

            try
            {
                if (isCurrentlyActive)
                {
                    List<InstalledMod> characterSlotCatalogue =
                        GetCharacterSlotCatalogue();

                    await Task.Run(() =>
                        SynchronizeModDeployment(
                            activeMod: null,
                            characterSlotCatalogue,
                            gameDirectory));

                    _settings.ActiveModId =
                        string.Empty;

                    _settings.PendingDeploymentModId =
                        string.Empty;

                    _settings.CharacterSlotCatalogueNeedsSynchronization =
                        false;
                }
                else if (isGameRunning)
                {
                    bool isFirstLiveSwitch =
                        _nextLiveMountOrder == 1000;

                    IntPtr gameWindowHandle =
                        _gameProcessService.FindGameWindow(
                            gameDirectory);

                    if (useX19Pulse &&
                        !isFirstLiveSwitch)
                    {
                        // X19 is meant to feel instant and unobtrusive, so I only
                        // show Limelight's pulsing mark while the switch is moving.
                        x19PulseWindow =
                            new X19SwitchPulseWindow();

                        x19PulseWindow.ShowOverGame(
                            gameWindowHandle);
                    }
                    else
                    {
                        // The first X19 scan can take long enough to make the
                        // game look frozen. I show the full in-game progress card
                        // once, then return to the quiet pulse for later swaps.
                        switchingWindow =
                            new LiveModSwitchingWindow(
                                selectedMod.DisplayName,
                                isFirstLiveSwitch);

                        switchingWindow.ShowOverGame(
                            gameWindowHandle);
                    }

                    if (!_liveLoaderBridgeService.IsOnline())
                    {
                        throw new InvalidOperationException(
                            "The game is running, but Limelight's Live Loader is not online.");
                    }

                    LiveLoaderCommandResult safetyCheck =
                        await WaitForLiveSwitchWindowAsync(
                            (phase, progress) =>
                            {
                                if (x19PulseWindow is not null)
                                {
                                    x19PulseWindow.Report(
                                        progress);
                                }
                                else
                                {
                                    switchingWindow?.Report(
                                        phase,
                                        progress);
                                }
                            });

                    if (!safetyCheck.Success)
                    {
                        if (IsLevelTransitionBlock(
                                safetyCheck.Message))
                        {
                            // I stop before staging or mounting anything while Unreal
                            // is replacing the current world.
                            LevelTransitionBlockerMessage.Text =
                                safetyCheck.Message +
                                " Wait until the new level is fully visible, then select Activate again.";

                            LevelTransitionBlocker.Visibility =
                                Visibility.Visible;

                            if (x19PulseWindow is not null)
                            {
                                x19PulseWindow.ShowError();
                                x19PulseWindow = null;
                            }
                            else if (switchingWindow is not null)
                            {
                                switchingWindow.ShowError(
                                    safetyCheck.Message);

                                // The overlay now owns its timed closing animation.
                                switchingWindow = null;
                            }

                            return;
                        }

                        throw new InvalidOperationException(
                            safetyCheck.Message);
                    }

                    await ActivateLiveModAsync(
                        selectedMod,
                        gameDirectory,
                        (phase, progress) =>
                        {
                            if (x19PulseWindow is not null)
                            {
                                x19PulseWindow.Report(
                                    progress);
                            }
                            else
                            {
                                switchingWindow?.Report(
                                    phase,
                                    progress);
                            }
                        });

                    _settings.ActiveModId =
                        selectedMod.Id;

                    // The live copy is already active. Once the game closes, Limelight
                    // mirrors the same choice into ~mods for the next launch.
                    _settings.PendingDeploymentModId =
                        selectedMod.Id;
                }
                else
                {
                    List<InstalledMod> characterSlotCatalogue =
                        GetCharacterSlotCatalogue();

                    await Task.Run(() =>
                        SynchronizeModDeployment(
                            selectedMod,
                            characterSlotCatalogue,
                            gameDirectory));

                    _settings.ActiveModId =
                        selectedMod.Id;

                    _settings.PendingDeploymentModId =
                        string.Empty;

                    _settings.CharacterSlotCatalogueNeedsSynchronization =
                        false;
                }

                _settingsService.Save(
                    _settings);

                RefreshLibrarySummary();

                string notificationTitle =
                    isCurrentlyActive
                        ? "MOD DEACTIVATED"
                        : "MOD ACTIVE";

                string notificationMessage =
                    isCurrentlyActive
                        ? $"{selectedMod.DisplayName} is no longer active."
                        : isGameRunning
                            ? _selectedLoaderMode ==
                                LoaderLaunchMode.Multiplayer
                                ? $"{selectedMod.DisplayName} is active in this local MP view. Select the same model on the other PC to keep both views matched."
                                : $"{selectedMod.DisplayName} is now active live."
                            : selectedMod.IsCharacterSlotMod
                                ? $"{selectedMod.DisplayName} is ready for Limelight's Live Loader. Its Character Slot files were also kept together for the in-game Locker."
                                : $"{selectedMod.DisplayName} is active and ready for the next launch.";

                if (isGameRunning &&
                    x19PulseWindow is not null)
                {
                    x19PulseWindow.ShowSuccess();
                    x19PulseWindow = null;
                }
                else if (isGameRunning &&
                         switchingWindow is not null)
                {
                    switchingWindow.ShowSuccess(
                        notificationMessage);

                    // The in-game card remains visible briefly and closes itself.
                    switchingWindow = null;
                }
                else
                {
                    ShowNotification(
                        notificationTitle,
                        notificationMessage,
                        isError: false);
                }
            }
            catch (Exception exception)
            {
                if (isGameRunning &&
                    IsLevelTransitionBlock(
                        exception.Message))
                {
                    // If a level change began after the first check, I stop the
                    // remaining stages and explain why nothing else was touched.
                    LevelTransitionBlockerMessage.Text =
                        exception.Message +
                        " Wait until the new level is fully visible, then select Activate again.";

                    LevelTransitionBlocker.Visibility =
                        Visibility.Visible;

                    if (x19PulseWindow is not null)
                    {
                        x19PulseWindow.ShowError();
                        x19PulseWindow = null;
                    }
                    else
                    {
                        CloseSwitchingWindow();
                    }
                }
                else if (isGameRunning &&
                    x19PulseWindow is not null)
                {
                    x19PulseWindow.ShowError();
                    x19PulseWindow = null;
                }
                else if (isGameRunning &&
                         switchingWindow is not null)
                {
                    switchingWindow.ShowError(
                        exception.Message);

                    // Errors remain visible for slightly longer before closing.
                    switchingWindow = null;
                }
                else
                {
                    ShowNotification(
                        "MOD ACTIVATION FAILED",
                        exception.Message,
                        isError: true);
                }
            }
            finally
            {
                CloseSwitchingWindow();
                CloseX19PulseWindow();

                _isLiveModChangeRunning = false;
                _isX19SwitchRequest = false;
                _discordPresenceSwitchTarget =
                    string.Empty;

                UpdateGameRunningStatus();
                RefreshDiscordPresence(
                    isGameRunning);
            }
        }

        private async Task<LiveLoaderCommandResult> WaitForLiveSwitchWindowAsync(
            Action<string, int>? reportProgress)
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(30);

            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .CanSwitchModsAsync();

            int consecutiveReadyChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (result.Success)
                {
                    consecutiveReadyChecks++;

                    // Two clean samples prevent a brief gap between Unreal world
                    // callbacks from opening the switch gate too early.
                    if (consecutiveReadyChecks >= 2)
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "VERIFYING A STABLE GAME WORLD",
                        9);

                    await Task.Delay(250);
                }
                else
                {
                    consecutiveReadyChecks = 0;

                    if (IsLevelTransitionBlock(
                            result.Message))
                    {
                        // A click made during LoadMap is rejected. I do not hold
                        // it in a queue and surprise the user after the map opens.
                        return result;
                    }

                    if (!IsTemporaryLiveSwitchDelay(result.Message))
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "WAITING FOR LIVE ASSETS TO SETTLE",
                        8);

                    await Task.Delay(400);
                }

                result =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();
            }

            return result;
        }

        private async Task<LiveLoaderCommandResult> WaitForInitialLiveWorldAsync(
            string gameDirectory,
            Action<string, int>? reportProgress)
        {
            DateTime deadline =
                DateTime.UtcNow.AddMinutes(4);

            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .CanSwitchModsAsync();

            int consecutiveReadyChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (!_gameProcessService.IsGameRunning(
                        gameDirectory))
                {
                    return new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message =
                            "Dead as Disco closed before the first game world was ready."
                    };
                }

                if (result.Success)
                {
                    consecutiveReadyChecks++;

                    // Startup crosses several short-lived Unreal worlds. I wait
                    // for a few clean checks before mounting the saved active mod.
                    if (consecutiveReadyChecks >= 3)
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "VERIFYING THE FIRST GAME WORLD",
                        31);

                    await Task.Delay(350);
                }
                else
                {
                    consecutiveReadyChecks = 0;

                    if (!IsLevelTransitionBlock(result.Message) &&
                        !IsTemporaryLiveSwitchDelay(result.Message))
                    {
                        return result;
                    }

                    // A launch-time transition is expected. Unlike a manual
                    // switch, this request is safe to wait because no user action
                    // has been queued and the active mod has not been touched yet.
                    reportProgress?.Invoke(
                        "WAITING FOR THE FIRST LEVEL",
                        30);

                    await Task.Delay(500);
                }

                result =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();
            }

            return new LiveLoaderCommandResult
            {
                Success = false,
                Message =
                    "Dead as Disco did not finish its initial level transition in time."
            };
        }

        private static bool IsTemporaryLiveSwitchDelay(
            string message)
        {
            return ContainsAny(
                message,
                "still settling",
                "still retiring",
                "still processing the previous live-loader command",
                "temporarily locked",
                "level is still loading",
                "world is still loading");
        }

        private static bool IsLevelTransitionBlock(
            string message)
        {
            return ContainsAny(
                message,
                "changing levels",
                "level transition",
                "level is still loading",
                "world is still loading",
                "loadmap");
        }

        private static bool ContainsAny(
            string value,
            params string[] candidates)
        {
            return candidates.Any(candidate =>
                value.Contains(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
        }

        private LimelightDialogChoice ShowLimelightDialog(
            string heading,
            string message,
            LimelightDialogTone tone = LimelightDialogTone.Information,
            string primaryAction = "OK",
            string? secondaryAction = null,
            string? details = null,
            string? eyebrow = null,
            string? footerHint = null,
            bool showCancel = false)
        {
            // Keeping the owner here makes every prompt stay with Limelight,
            // including when the main window is moved to another monitor.
            return LimelightDialog.Open(
                this,
                heading,
                message,
                tone,
                primaryAction,
                secondaryAction,
                details,
                eyebrow,
                footerHint,
                showCancel);
        }

        private async void ShowNotification(
            string title,
            string message,
            bool isError)
        {
            int sequence =
                ++_notificationSequence;

            Brush statusBrush =
                (Brush)FindResource(
                    isError
                        ? "PinkBrush"
                        : "CyanBrush");

            NotificationToastTitle.Text =
                title.ToUpperInvariant();

            NotificationToastMessage.Text =
                message;

            NotificationToastAccent.Background =
                statusBrush;

            NotificationToastTitle.Foreground =
                statusBrush;

            NotificationToastIcon.Foreground =
                statusBrush;

            NotificationToastIcon.Text =
                isError
                    ? "!"
                    : "◆";

            NotificationPopup.Visibility =
                Visibility.Visible;

            // Clear an older animation first so a new message appears at full
            // strength even when the previous toast was fading away.
            NotificationToast.BeginAnimation(
                OpacityProperty,
                null);

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            NotificationToast.Opacity = 0;
            NotificationToastTransform.Y = 10;
            NotificationToast.Visibility =
                Visibility.Visible;

            var entranceEase =
                new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                };

            NotificationToast.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(130)));

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    10,
                    0,
                    TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = entranceEase
                });

            await Task.Delay(
                isError
                    ? 3400
                    : 2300);

            if (sequence != _notificationSequence ||
                !IsLoaded)
            {
                return;
            }

            NotificationToast.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    1,
                    0,
                    TimeSpan.FromMilliseconds(160)));

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    0,
                    -8,
                    TimeSpan.FromMilliseconds(160)));

            await Task.Delay(230);

            if (sequence == _notificationSequence &&
                IsLoaded)
            {
                NotificationPopup.Visibility =
                    Visibility.Collapsed;

                NotificationToast.Visibility =
                    Visibility.Collapsed;
            }
        }

        private async void RemoveModRequested(string modId)
        {
            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod == null)
            {
                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "REMOVE THIS MOD?",
                    $"Remove {selectedMod.DisplayName} from Limelight? This deletes Limelight's stored copy of the mod.",
                    LimelightDialogTone.Question,
                    primaryAction: "REMOVE MOD",
                    secondaryAction: "KEEP MOD",
                    eyebrow: "LIBRARY CHANGE");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            bool isCurrentlyActive =
                string.Equals(
                    _settings.ActiveModId,
                    selectedMod.Id,
                    StringComparison.OrdinalIgnoreCase);

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            bool isEnabledConventional =
                _settings.EnabledConventionalModIds.Any(id =>
                    string.Equals(
                        id,
                        selectedMod.Id,
                        StringComparison.OrdinalIgnoreCase));

            if ((isCurrentlyActive ||
                 selectedMod.IsCharacterSlotMod ||
                 isEnabledConventional) &&
                isGameRunning)
            {
                ShowLimelightDialog(
                    selectedMod.IsCharacterSlotMod
                        ? "CHARACTER SLOT IS IN USE"
                        : isEnabledConventional
                            ? "ENABLED MOD IS IN USE"
                            : "ACTIVE MOD IS IN USE",
                    selectedMod.IsCharacterSlotMod
                        ? "Close Dead as Disco before removing this Character Slot. Unreal loaded its catalogue files when the game started."
                        : isEnabledConventional
                            ? "Close Dead as Disco before removing this enabled replacement. Unreal loaded it when the game started."
                            : "Close Dead as Disco before removing the active mod from Limelight.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REMOVE BLOCKED");

                return;
            }

            try
            {
                InstalledMod? activeModAfterRemoval =
                    isCurrentlyActive
                        ? null
                        : _settings.InstalledMods.FirstOrDefault(mod =>
                            string.Equals(
                                mod.Id,
                                _settings.ActiveModId,
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(
                                mod.Id,
                                selectedMod.Id,
                                StringComparison.OrdinalIgnoreCase) &&
                            Directory.Exists(mod.InstallDirectory));

                List<InstalledMod> characterSlotCatalogue =
                    GetCharacterSlotCatalogue(
                        selectedMod.Id);

                List<InstalledMod> conventionalModsAfterRemoval =
                    GetEnabledConventionalMods(
                        selectedMod.Id);

                if (!string.IsNullOrWhiteSpace(_gameDirectory) &&
                    !isGameRunning)
                {
                    string gameDirectory =
                        _gameDirectory;

                    // I update ~mods before deleting the library copy. That
                    // gives the departing slot a clean exit and keeps its cast.
                    await Task.Run(() =>
                        SynchronizeModDeployment(
                            activeModAfterRemoval,
                            characterSlotCatalogue,
                            gameDirectory,
                            conventionalModsAfterRemoval));

                    _settings.CharacterSlotCatalogueNeedsSynchronization =
                        false;

                    _settings.ConventionalModsNeedSynchronization =
                        false;
                }
                else if (selectedMod.IsCharacterSlotMod)
                {
                    _settings.CharacterSlotCatalogueNeedsSynchronization =
                        true;
                }

                if (isCurrentlyActive ||
                    isEnabledConventional)
                {
                    if (string.IsNullOrWhiteSpace(_gameDirectory))
                    {
                        throw new InvalidOperationException(
                            "Reconnect the game before removing a deployed mod.");
                    }

                    _settings.EnabledConventionalModIds.RemoveAll(id =>
                        string.Equals(
                            id,
                            selectedMod.Id,
                            StringComparison.OrdinalIgnoreCase));

                    if (isCurrentlyActive)
                    {
                        _settings.ActiveModId =
                            string.Empty;
                    }
                }

                await Task.Run(() =>
                {
                    if (Directory.Exists(
                            selectedMod.InstallDirectory))
                    {
                        Directory.Delete(
                            selectedMod.InstallDirectory,
                            recursive: true);
                    }
                });

                _settings.InstalledMods.Remove(
                    selectedMod);

                if (string.Equals(
                        _settings.PendingDeploymentModId,
                        selectedMod.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _settings.PendingDeploymentModId =
                        string.Empty;
                }

                _settingsService.Save(_settings);
                RefreshLibrarySummary();
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD COULD NOT BE REMOVED",
                    "Limelight kept the library entry so nothing is lost.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REMOVE FAILED");
            }
        }

        private void RenameModRequested(
            string modId,
            string displayName)
        {
            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod is null)
            {
                return;
            }

            string cleanedName =
                string.Join(
                    " ",
                    displayName
                        .Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (cleanedName.Length == 0)
            {
                return;
            }

            selectedMod.CustomDisplayName =
                cleanedName;

            _settingsService.Save(_settings);
            RefreshLibrarySummary();

            ShowNotification(
                "MOD RENAMED",
                $"{selectedMod.DisplayName} is now shown with its new name.",
                isError: false);
        }

        private void ShowMyMods_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            ShowMyModsPage();
        }

        private void ShowMyModsPage()
        {
            // Refresh before displaying the page so newly imported
            // mods appear without restarting Limelight.
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: true);
        }

        private void ShowStagehandScripts_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowStagehandScriptsPage();
        }

        private void ShowStagehandScriptsPage()
        {
            DashboardPage.Visibility = Visibility.Collapsed;
            MyModsPageControl.Visibility = Visibility.Collapsed;
            ProfilesPageControl.Visibility = Visibility.Collapsed;
            LiveLoadersPageControl.Visibility = Visibility.Collapsed;
            MultiplayerPageControl.Visibility = Visibility.Collapsed;
            BrowseNexusPageControl.Visibility = Visibility.Collapsed;
            DownloadsPageControl.Visibility = Visibility.Collapsed;
            SettingsPageControl.Visibility = Visibility.Collapsed;

            _selectedNavigationPage = NavigationPage.StagehandScripts;
            RefreshStagehandScriptsPage();
            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void RefreshStagehandScriptsPage()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                StagehandScriptsPageControl.ShowScripts(
                    Array.Empty<InstalledStagehandScript>());
                return;
            }

            Ue4ssDetectionResult loader = _ue4ssDetectionService.Detect(_gameDirectory);
            StagehandScriptsPageControl.ShowScripts(
                _stagehandLogicModPackageService.ListInstalled(loader),
                _stagehandPayloadService.ReadRuntimeHealthSummary(loader));
        }

        private void UpdateStagehandRuntimeRequested()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gameDirectory))
                {
                    throw new InvalidOperationException(
                        "Connect Dead as Disco before updating Stagehand.");
                }
                if (_gameProcessService.IsGameRunning(_gameDirectory))
                {
                    ShowLimelightDialog(
                        "CLOSE THE GAME FIRST",
                        "Dead as Disco must be closed before updating Stagehand's managed runtime files.",
                        LimelightDialogTone.Warning,
                        eyebrow: "STAGEHAND UPDATE PAUSED");
                    return;
                }

                Ue4ssDetectionResult loader =
                    _ue4ssDetectionService.Detect(_gameDirectory);
                if (!loader.IsInstalled)
                {
                    throw new InvalidOperationException(
                        "No existing UE4SS Live Loader was detected. This Stagehand-only action will not install or replace UE4SS.");
                }

                StagehandPayloadManifest installed =
                    _stagehandPayloadService.EnsureInstalled(loader);
                RefreshStagehandScriptsPage();
                ShowLimelightDialog(
                    "STAGEHAND RUNTIME UPDATED",
                    $"Stagehand {installed.StagehandVersion} · API {installed.ApiVersion} is ready.",
                    LimelightDialogTone.Success,
                    details:
                        "Only Limelight's marked Stagehand files and the Stagehand mods.txt entry were managed. " +
                        "UE4SS, signature files, UE4SS-settings.ini, and third-party mods were left untouched.",
                    eyebrow: "STAGEHAND-ONLY UPDATE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "STAGEHAND RUNTIME NOT UPDATED",
                    "Limelight left the existing loader and mod files untouched.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "STAGEHAND UPDATE FAILED");
            }
        }

        private void SetStagehandScriptEnabledRequested(string id, bool enabled)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gameDirectory))
                {
                    throw new InvalidOperationException("Connect Dead as Disco before managing Stagehand scripts.");
                }

                Ue4ssDetectionResult loader = _ue4ssDetectionService.Detect(_gameDirectory);
                _stagehandLogicModPackageService.SetEnabled(loader, id, enabled);
                RefreshStagehandScriptsPage();
                ShowNotification(
                    enabled ? "STAGEHAND SCRIPT ENABLED" : "STAGEHAND SCRIPT DISABLED",
                    "The change will apply on the next Dead as Disco launch.",
                    isError: false);
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "STAGEHAND SCRIPT NOT CHANGED",
                    "Limelight could not update this script's launch state.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "SCRIPT CONTROL FAILED");
            }
        }

        private void RemoveStagehandScriptRequested(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gameDirectory))
                {
                    throw new InvalidOperationException("Connect Dead as Disco before managing Stagehand scripts.");
                }

                Ue4ssDetectionResult loader = _ue4ssDetectionService.Detect(_gameDirectory);
                InstalledStagehandScript? script =
                    _stagehandLogicModPackageService
                        .ListInstalled(loader)
                        .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
                if (script is null)
                {
                    throw new InvalidOperationException("The selected Stagehand script is no longer installed.");
                }
                if (script.IsBundled)
                {
                    ShowLimelightDialog(
                        "BUNDLED SCRIPT",
                        "The Stagehand proof script belongs to Limelight's runtime. Disable it if you do not want it to run.",
                        LimelightDialogTone.Information,
                        eyebrow: "SCRIPT KEPT");
                    return;
                }
                if (_gameProcessService.IsGameRunning(_gameDirectory))
                {
                    ShowLimelightDialog(
                        "CLOSE THE GAME FIRST",
                        "Dead as Disco must be closed before removing a Stagehand script.",
                        LimelightDialogTone.Warning,
                        eyebrow: "SCRIPT REMOVAL PAUSED");
                    return;
                }

                LimelightDialogChoice confirmation = ShowLimelightDialog(
                    "REMOVE STAGEHAND SCRIPT?",
                    $"Remove {script.Name} from Limelight?",
                    LimelightDialogTone.Question,
                    primaryAction: "REMOVE SCRIPT",
                    secondaryAction: "KEEP SCRIPT",
                    details:
                        $"ID: {script.Id}\n" +
                        $"Version: {script.Version}\n\n" +
                        "This permanently deletes the script and its namespaced settings, storage, and runtime log.",
                    eyebrow: "PERMANENT SCRIPT REMOVAL",
                    footerHint: "This cannot be undone. You can reinstall the original .stagehand.zip later.");
                if (confirmation != LimelightDialogChoice.Primary)
                {
                    return;
                }

                _stagehandLogicModPackageService.Remove(loader, script.Id);
                RefreshStagehandScriptsPage();
                ShowNotification(
                    "STAGEHAND SCRIPT REMOVED",
                    $"{script.Name} and its namespaced data were deleted.",
                    isError: false);
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "STAGEHAND SCRIPT NOT REMOVED",
                    "Limelight left the script in place.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "SCRIPT REMOVAL FAILED");
            }
        }

        private void ShowProfiles_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowProfilesPage();
        }

        private void ShowProfilesPage()
        {
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Visible;

            _selectedNavigationPage =
                NavigationPage.Profiles;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ShowLiveLoaders_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowLiveLoadersPage();
        }

        private void ShowLiveLoadersPage()
        {
            // I refresh first so imported or removed mods are immediately
            // reflected in the user's X19 rotation.
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Visible;

            _selectedNavigationPage =
                NavigationPage.LiveLoaders;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ShowMultiplayer_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowMultiplayerPage();
        }

        private void ShowMultiplayerPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            _selectedNavigationPage =
                NavigationPage.Multiplayer;

            ApplyNavigationAppearance();
            RefreshMultiplayerPage();
            RefreshDiscordPresence();
        }

        private void ProfilesChanged(
            IReadOnlyList<ModProfile> profiles)
        {
            HashSet<string> oldGroupedModIds =
                _settings.ModProfiles
                    .Where(profile =>
                        _settings.X19LoaderProfileIds.Contains(
                            profile.Id,
                            StringComparer.OrdinalIgnoreCase))
                    .SelectMany(profile => profile.ModIds)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> standaloneModIds =
                _settings.X19LoaderModIds
                    .Where(modId => !oldGroupedModIds.Contains(modId))
                    .ToList();

            // I replace the saved snapshot in one step so a half-edited
            // profile can never leak into the X19 rotation.
            _settings.ModProfiles =
                profiles
                    .Select(profile =>
                        new ModProfile
                        {
                            Id = profile.Id,
                            Name = profile.Name,
                            ModIds = profile.ModIds
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                            CreatedAt = profile.CreatedAt,
                            UpdatedAt = profile.UpdatedAt
                        })
                    .ToList();

            HashSet<string> availableProfileIds =
                _settings.ModProfiles
                    .Select(profile => profile.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _settings.X19LoaderProfileIds =
                _settings.X19LoaderProfileIds
                    .Where(availableProfileIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            IEnumerable<string> refreshedGroupedModIds =
                _settings.ModProfiles
                    .Where(profile =>
                        _settings.X19LoaderProfileIds.Contains(
                            profile.Id,
                            StringComparer.OrdinalIgnoreCase))
                    .SelectMany(profile => profile.ModIds);

            _settings.X19LoaderModIds =
                refreshedGroupedModIds
                    .Concat(standaloneModIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void UseProfileInX19Requested(
            string profileId)
        {
            ModProfile? profile =
                _settings.ModProfiles.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase));

            if (profile is null)
            {
                return;
            }

            HashSet<string> availableIds =
                _settings.InstalledMods
                    .Where(mod => Directory.Exists(mod.InstallDirectory))
                    .Select(mod => mod.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> rotationIds =
                profile.ModIds
                    .Where(availableIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (rotationIds.Count == 0)
            {
                ShowNotification(
                    "PROFILE NEEDS AVAILABLE MODS",
                    $"None of the characters saved in {profile.Name} are currently available.",
                    isError: true);

                return;
            }

            _settings.X19LoaderModIds =
                rotationIds;

            _settings.X19LoaderProfileIds =
                new List<string>
                {
                    profile.Id
                };

            _settingsService.Save(_settings);
            ShowLiveLoadersPage();

            ShowNotification(
                "X19 PROFILE READY",
                $"{profile.Name} replaced the current X19 rotation.",
                isError: false);
        }

        private void X19GroupChanged(
            IReadOnlyList<string> selectedModIds)
        {
            // I remove duplicates before saving so every hotkey press advances
            // through one predictable copy of each selected character.
            _settings.X19LoaderModIds =
                selectedModIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void X19ProfileGroupsChanged(
            IReadOnlyList<string> selectedProfileIds)
        {
            _settings.X19LoaderProfileIds =
                selectedProfileIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void X19ShuffleChanged(
            bool shuffleEnabled)
        {
            _settings.X19ShuffleEnabled =
                shuffleEnabled;

            _settingsService.Save(_settings);
        }

        private void X19HotkeyChanged(
            string hotkeyGesture)
        {
            _settings.X19HotkeyGesture =
                hotkeyGesture;

            _settingsService.Save(_settings);

            if (_selectedLoaderMode ==
                LoaderLaunchMode.X19)
            {
                EnableX19Hotkey();
            }

            // I refresh the loader page too so its hotkey badge changes
            // immediately instead of waiting for another navigation visit.
            RefreshLibrarySummary();
        }

        private void ResourceOverlayChanged(
    bool enabled)
        {
            _settings.ResourceOverlayEnabled =
                enabled;

            _settingsService.Save(_settings);

            ApplyResourceOverlayPreference();

            SettingsPageControl.ShowResourceOverlay(
                enabled);
        }

        private void ApplyResourceOverlayPreference()
        {
            if (_settings.ResourceOverlayEnabled)
            {
                if (_resourceUsageOverlayWindow != null)
                {
                    return;
                }

                _resourceUsageOverlayWindow =
                    new ResourceUsageOverlayWindow();

                _resourceUsageOverlayWindow.Closed +=
                    ResourceUsageOverlayWindow_Closed;

                _resourceUsageOverlayWindow.Show();

                return;
            }

            _resourceUsageOverlayWindow?.Close();
            _resourceUsageOverlayWindow = null;
        }

        private void ResourceUsageOverlayWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _resourceUsageOverlayWindow = null;
        }

        private void DiscordPresenceChanged(
            bool enabled)
        {
            _settings.DiscordRichPresenceEnabled =
                enabled;

            _settingsService.Save(
                _settings);

            _discordPresenceService.SetEnabled(
                enabled);

            SettingsPageControl.ShowDiscordPresence(
                enabled);

            RefreshDiscordPresence();

            ShowNotification(
                enabled
                    ? "DISCORD PRESENCE ENABLED"
                    : "DISCORD PRESENCE DISABLED",
                enabled
                    ? "Limelight will now share its current activity through the Discord desktop client."
                    : "Limelight cleared its Discord activity and returned to private mode.",
                isError: false);
        }

        private async void TestLiveLoader_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (!_liveLoaderBridgeService.IsOnline())
            {
                ShowLimelightDialog(
                    "LIVE LOADER IS OFFLINE",
                    "Start Dead as Disco and wait for the Live Loader status to show ONLINE.",
                    LimelightDialogTone.Information,
                    eyebrow: "NATIVE TEST");

                return;
            }

            LiveLoaderStatusText.Text =
                "CHECKING";

            LiveLoaderStatusText.Foreground =
                (Brush)FindResource("CyanBrush");

            try
            {
                // Ask the native half of the bridge directly instead of assuming it
                // loaded just because the Lua heartbeat is alive.
                LiveLoaderCommandResult result =
                    await _liveLoaderCommandService.PingNativeAsync();

                LiveLoaderStatusText.Text =
                    result.Success
                        ? "ONLINE"
                        : "NATIVE OFFLINE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource(
                        result.Success
                            ? "CyanBrush"
                            : "PinkBrush");

                ShowLimelightDialog(
                    result.Success
                        ? "NATIVE BRIDGE ONLINE"
                        : "NATIVE BRIDGE UNAVAILABLE",
                    result.Message,
                    result.Success
                        ? LimelightDialogTone.Success
                        : LimelightDialogTone.Warning,
                    eyebrow: "NATIVE TEST");
            }
            catch (Exception exception)
            {
                LiveLoaderStatusText.Text =
                    "TEST FAILED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                ShowLimelightDialog(
                    "NATIVE BRIDGE TEST FAILED",
                    "Limelight could not contact its native bridge.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "NATIVE TEST");
            }
        }

        private void ShowDashboard_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowDashboardPage();
        }

        private void ShowDashboardPage()
        {
            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            DashboardPage.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: false);
        }

        private void ShowSettings_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowSettingsPage();
        }

        private void ShowSettingsPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Visible;

            RefreshSettingsPage();
            SetSelectedNavigation(
                showMyMods: false,
                showSettings: true);
        }

        private void ShowDownloads_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowDownloadsPage();
        }

        private void ShowDownloadsPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Visible;

            RefreshDownloadsPage();

            _selectedNavigationPage =
                NavigationPage.Downloads;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ClearFinishedDownloadsRequested()
        {
            _downloadHistoryService.ClearFinished();
            RefreshDownloadsPage();
        }

        private void RefreshDownloadsPage()
        {
            DownloadsPageControl.ShowDownloads(
                _downloadHistoryService.Records);
        }

        private void SetSelectedNavigation(
    bool showMyMods,
    bool showSettings = false,
    bool showBrowseNexus = false)
        {
            _selectedNavigationPage =
                showSettings
                    ? NavigationPage.Settings
                    : showBrowseNexus
                        ? NavigationPage.BrowseNexus
                        : showMyMods
                            ? NavigationPage.MyMods
                            : NavigationPage.Dashboard;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ApplyNavigationAppearance()
        {
            StagehandScriptsPageControl.Visibility =
                _selectedNavigationPage == NavigationPage.StagehandScripts
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            MultiplayerPageControl.Visibility =
                _selectedNavigationPage ==
                    NavigationPage.Multiplayer
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // The icon is kept separate from the label so the selected page
            // can fill its diamond without moving the text beside it.
            ApplyNavigationItemAppearance(
                DashboardNavigation,
                DashboardNavigationIcon,
                DashboardNavigationText,
                _selectedNavigationPage == NavigationPage.Dashboard);

            ApplyNavigationItemAppearance(
                MyModsNavigation,
                MyModsNavigationIcon,
                MyModsNavigationText,
                _selectedNavigationPage == NavigationPage.MyMods);

            ApplyNavigationItemAppearance(
                StagehandScriptsNavigation,
                StagehandScriptsNavigationIcon,
                StagehandScriptsNavigationText,
                _selectedNavigationPage == NavigationPage.StagehandScripts);

            ApplyNavigationItemAppearance(
                ProfilesNavigation,
                ProfilesNavigationIcon,
                ProfilesNavigationText,
                _selectedNavigationPage == NavigationPage.Profiles);

            ApplyNavigationItemAppearance(
                LiveLoadersNavigation,
                LiveLoadersNavigationIcon,
                LiveLoadersNavigationText,
                _selectedNavigationPage == NavigationPage.LiveLoaders);

            ApplyNavigationItemAppearance(
                MultiplayerNavigation,
                MultiplayerNavigationIcon,
                MultiplayerNavigationText,
                _selectedNavigationPage == NavigationPage.Multiplayer);

            ApplyNavigationItemAppearance(
                BrowseNexusNavigation,
                BrowseNexusNavigationIcon,
                BrowseNexusNavigationText,
                _selectedNavigationPage == NavigationPage.BrowseNexus);

            ApplyNavigationItemAppearance(
                DownloadsNavigation,
                DownloadsNavigationIcon,
                DownloadsNavigationText,
                _selectedNavigationPage == NavigationPage.Downloads);

            ApplyNavigationItemAppearance(
                SettingsNavigation,
                SettingsNavigationIcon,
                SettingsNavigationText,
                _selectedNavigationPage == NavigationPage.Settings);
        }

        private void ApplyNavigationItemAppearance(
            Border navigation,
            TextBlock icon,
            TextBlock label,
            bool isSelected)
        {
            Brush pink =
                (Brush)FindResource("PinkBrush");

            Brush normalText =
                (Brush)FindResource("TextBrush");

            Brush mutedText =
                (Brush)FindResource("MutedTextBrush");

            navigation.Background =
                isSelected
                    ? new SolidColorBrush(
                        Color.FromRgb(37, 32, 59))
                    : Brushes.Transparent;

            navigation.BorderBrush =
                isSelected
                    ? pink
                    : Brushes.Transparent;

            icon.Text =
                isSelected
                    ? "◆"
                    : "◇";

            icon.Foreground =
                isSelected
                    ? pink
                    : mutedText;

            label.Foreground =
                isSelected
                    ? normalText
                    : mutedText;
        }

        private void Navigation_MouseEnter(
            object sender,
            MouseEventArgs e)
        {
            if (sender is not Border navigation ||
                IsSelectedNavigation(navigation))
            {
                return;
            }

            // Hover uses a neutral grey panel and keeps the diamond hollow.
            // The pink filled icon is reserved for the page that is open.
            navigation.Background =
                new SolidColorBrush(
                    Color.FromRgb(27, 30, 43));

            GetNavigationParts(
                navigation,
                out TextBlock? icon,
                out TextBlock? label);

            if (icon is not null)
            {
                icon.Text = "◇";
                icon.Foreground =
                    (Brush)FindResource("MutedTextBrush");
            }

            if (label is not null)
            {
                label.Foreground =
                    (Brush)FindResource("TextBrush");
            }
        }

        private void Navigation_MouseLeave(
            object sender,
            MouseEventArgs e)
        {
            ApplyNavigationAppearance();
        }

        private bool IsSelectedNavigation(
            Border navigation)
        {
            return
                (navigation == DashboardNavigation &&
                 _selectedNavigationPage == NavigationPage.Dashboard) ||
                (navigation == MyModsNavigation &&
                 _selectedNavigationPage == NavigationPage.MyMods) ||
                (navigation == StagehandScriptsNavigation &&
                 _selectedNavigationPage == NavigationPage.StagehandScripts) ||
                (navigation == ProfilesNavigation &&
                 _selectedNavigationPage == NavigationPage.Profiles) ||
                (navigation == LiveLoadersNavigation &&
                 _selectedNavigationPage == NavigationPage.LiveLoaders) ||
                (navigation == MultiplayerNavigation &&
                 _selectedNavigationPage == NavigationPage.Multiplayer) ||
                (navigation == DownloadsNavigation &&
                 _selectedNavigationPage == NavigationPage.Downloads) ||
                (navigation == SettingsNavigation &&
                 _selectedNavigationPage == NavigationPage.Settings) ||
                (navigation == BrowseNexusNavigation &&
                 _selectedNavigationPage == NavigationPage.BrowseNexus);
        }

        private void GetNavigationParts(
            Border navigation,
            out TextBlock? icon,
            out TextBlock? label)
        {
            if (navigation == DashboardNavigation)
            {
                icon = DashboardNavigationIcon;
                label = DashboardNavigationText;
                return;
            }

            if (navigation == MyModsNavigation)
            {
                icon = MyModsNavigationIcon;
                label = MyModsNavigationText;
                return;
            }

            if (navigation == StagehandScriptsNavigation)
            {
                icon = StagehandScriptsNavigationIcon;
                label = StagehandScriptsNavigationText;
                return;
            }

            if (navigation == ProfilesNavigation)
            {
                icon = ProfilesNavigationIcon;
                label = ProfilesNavigationText;
                return;
            }

            if (navigation == LiveLoadersNavigation)
            {
                icon = LiveLoadersNavigationIcon;
                label = LiveLoadersNavigationText;
                return;
            }

            if (navigation == MultiplayerNavigation)
            {
                icon = MultiplayerNavigationIcon;
                label = MultiplayerNavigationText;
                return;
            }

            if (navigation == BrowseNexusNavigation)
            {
                icon = BrowseNexusNavigationIcon;
                label = BrowseNexusNavigationText;
                return;
            }

            if (navigation == DownloadsNavigation)
            {
                icon = DownloadsNavigationIcon;
                label = DownloadsNavigationText;
                return;
            }

            if (navigation == SettingsNavigation)
            {
                icon = SettingsNavigationIcon;
                label = SettingsNavigationText;
                return;
            }

            icon = null;
            label = null;
        }

        private async void NexusBrowserArchiveDownloaded(
            string archivePath)
        {
            // I send browser downloads through the same guarded importer as
            // file-picker and drag-and-drop imports so validation stays equal.
            await ImportModArchiveAsync(
                archivePath);
        }
        private void RefreshSettingsPage()
        {
            string? gameDirectory =
                _gameDirectory;

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            LiveSessionState session =
                _liveSessionService.Load();

            LiveSessionCleanupResult stagingSnapshot =
                string.IsNullOrWhiteSpace(gameDirectory)
                    ? new LiveSessionCleanupResult()
                    : _liveSessionService.GetStagingSnapshot(
                        gameDirectory);

            SettingsPageControl.ShowStatus(
                gameDirectory,
                isGameRunning,
                session,
                stagingSnapshot);

            ShowSettingsCompatibility(
                gameDirectory);

            SettingsPageControl.ShowDiscordPresence(
                _settings.DiscordRichPresenceEnabled);

            SettingsPageControl.ShowResourceOverlay(
                _settings.ResourceOverlayEnabled);
        }

        private void ShowSettingsCompatibility(
            string? gameDirectory)
        {
            SettingsPageControl.ShowCompatibility(
                _compatibilityService.Check(
                    gameDirectory));
        }

        private void RefreshDiscordPresence(
            bool? knownGameRunning = null)
        {
            bool isGameRunning =
                knownGameRunning ??
                (!string.IsNullOrWhiteSpace(_gameDirectory) &&
                 _gameProcessService.IsGameRunning(
                     _gameDirectory));

            InstalledMod? activeMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            string pageLabel =
                _selectedNavigationPage switch
                {
                    NavigationPage.Dashboard =>
                        "Managing the Limelight dashboard",
                    NavigationPage.MyMods =>
                        "Browsing character mods",
                    NavigationPage.StagehandScripts =>
                        "Managing Stagehand scripts",
                    NavigationPage.Profiles =>
                        "Building character profiles",
                    NavigationPage.LiveLoaders =>
                        "Configuring the Live Loader",
                    NavigationPage.Multiplayer =>
                        "Testing Limelight multiplayer",
                    NavigationPage.BrowseNexus =>
                        "Browsing Nexus Mods",
                    NavigationPage.Downloads =>
                        "Checking mod downloads",
                    NavigationPage.Settings =>
                        "Adjusting Limelight settings",
                    _ =>
                        "Managing Dead as Disco mods"
                };

            string loaderMode =
                _selectedLoaderMode switch
                {
                    LoaderLaunchMode.X19 =>
                        "X19 LLoader",
                    LoaderLaunchMode.Multiplayer =>
                        "LimelightMP",
                    LoaderLaunchMode.Disabled =>
                        "No Live Loader",
                    _ =>
                        "Live Loader"
                };

            _discordPresenceService.Update(
                isGameRunning,
                _isLiveModChangeRunning,
                pageLabel,
                activeMod?.DisplayName,
                loaderMode,
                _discordPresenceSwitchTarget,
                _multiplayerSessionService.IsActive
                    ? _multiplayerSessionService.ActiveRole
                    : MultiplayerRole.None);
        }

        private async void RepairLiveLoaderRequested()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to Dead as Disco before repairing the Live Loader.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REPAIR BLOCKED");

                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can repair the Live Loader.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REPAIR BLOCKED");

                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "REPAIR THE LIVE LOADER?",
                    "This clears stale staging files, refreshes the Dead as Disco configuration, and reinstalls Limelight's bridge. Imported mods are not removed.",
                    LimelightDialogTone.Question,
                    primaryAction: "START REPAIR",
                    secondaryAction: "NOT NOW",
                    eyebrow: "RECOVERY TOOLS");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                // I clear the verified resolver cache during a repair so the
                // next launch performs one completely fresh native scan.
                string resolverCachePath =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Limelight",
                        "Cache",
                        "native-resolver-v2.cache");

                if (File.Exists(resolverCachePath))
                {
                    File.Delete(resolverCachePath);
                }

                LiveSessionCleanupResult cleanup =
                    await Task.Run(() =>
                        _liveSessionService.RepairClosedSession(
                            gameDirectory));

                Ue4ssDetectionResult loader =
                    _ue4ssDetectionService.Detect(
                        gameDirectory);

                if (!loader.IsInstalled ||
                    !_ue4ssConfigurationService.IsRuntimeCompatible(loader))
                {
                    // The normal setup flow already knows how to fetch the
                    // verified build, so let it handle a missing runtime too.
                    _hasHandledLiveLoaderPrompt = false;
                    _settings.DismissedLiveLoaderPromptForGameDirectory =
                        string.Empty;

                    _settingsService.Save(_settings);
                    await ShowLiveLoaderSetupPromptIfNeeded();
                    RefreshSettingsPage();
                    return;
                }

                await Task.Run(() =>
                {
                    _ue4ssConfigurationService.Apply(
                        loader);

                    _liveLoaderBridgeService.EnsureInstalled(
                        loader);

                    _nativeBridgeInstallerService.EnsureInstalled(
                        loader);

                    _stagehandPayloadService.EnsureInstalled(
                        loader);
                });

                UpdateGameRunningStatus();
                RefreshSettingsPage();
                ApplyResourceOverlayPreference();

                string warning =
                    cleanup.Errors.Count == 0
                        ? string.Empty
                        : $"\n\n{cleanup.Errors.Count} file(s) could not be removed. The diagnostic report will include the session details.";

                ShowLimelightDialog(
                    cleanup.Errors.Count == 0
                        ? "LIVE LOADER REPAIRED"
                        : "REPAIR FINISHED WITH NOTES",
                    $"Limelight cleared {cleanup.DeletedFileCount} staged file(s) and refreshed its bridge.{warning}",
                    cleanup.Errors.Count == 0
                        ? LimelightDialogTone.Success
                        : LimelightDialogTone.Warning,
                    eyebrow: "REPAIR COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPAIR COULD NOT FINISH",
                    "Limelight did not replace any imported mods.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REPAIR FAILED");
            }
        }

        private async void PurgeAllModsRequested()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to Dead as Disco before clearing its mod folder.",
                    LimelightDialogTone.Warning,
                    eyebrow: "PURGE BLOCKED");

                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can purge its mod folder.",
                    LimelightDialogTone.Warning,
                    eyebrow: "PURGE BLOCKED");

                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "PURGE EVERY DEPLOYED MOD?",
                    "This empties Dead as Disco's ~mods folder, including files that were added outside Limelight. Your imported library, profiles, and X19 groups will stay in Limelight.",
                    LimelightDialogTone.Question,
                    primaryAction: "PURGE ALL MODS",
                    secondaryAction: "KEEP MY FILES",
                    eyebrow: "DESTRUCTIVE RECOVERY",
                    footerHint: "The game must remain closed until the purge finishes.");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    // I close Limelight's session record first so no staged
                    // generation remains associated with files being purged.
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory);

                    _modDeploymentService.PurgeAllMods(
                        gameDirectory);

                    _characterSlotLoaderService.SynchronizeRuntimeCatalogue(
                        Array.Empty<InstalledMod>(),
                        gameDirectory);
                });

                _settings.ActiveModId =
                    string.Empty;

                _settings.PendingDeploymentModId =
                    string.Empty;

                _settings.CharacterSlotCatalogueNeedsSynchronization =
                    false;

                _settings.EnabledConventionalModIds.Clear();

                _settings.ConventionalModsNeedSynchronization =
                    false;

                _settingsService.Save(_settings);

                RefreshLibrarySummary();
                RefreshSettingsPage();
                UpdateGameRunningStatus();

                ShowLimelightDialog(
                    "MOD FOLDER PURGED",
                    "Dead as Disco's ~mods folder is clean. Your imported mods and profiles remain ready inside Limelight.",
                    LimelightDialogTone.Success,
                    eyebrow: "PURGE COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD FOLDER COULD NOT BE PURGED",
                    "Limelight stopped before changing your imported library.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "PURGE FAILED");
            }
        }

        private async void CreatePrivateTestReportRequested()
        {
            var reportWindow =
                new PrivateTestReportWindow
                {
                    Owner = this
                };

            if (reportWindow.ShowDialog() != true ||
                reportWindow.ReportRequest is null)
            {
                return;
            }

            string? reportPath =
                LimelightFilePickerWindow.PickSaveFile(
                    this,
                    "Save Limelight private test report",
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    $"Limelight-Test-Report-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    ".zip",
                    "ZIP ARCHIVES");

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            try
            {
                string automaticDiagnostics =
                    await CreateSanitizedDiagnosticReportAsync();

                string loaderMode =
                    _selectedLoaderMode switch
                    {
                        LoaderLaunchMode.X19 => "X19 LLoader",
                        LoaderLaunchMode.Multiplayer => "LimelightMP",
                        LoaderLaunchMode.Disabled => "No Live Loader",
                        _ => "Live Loader"
                    };

                await _privateTestReportService.CreateArchiveAsync(
                    reportPath,
                    reportWindow.ReportRequest,
                    automaticDiagnostics,
                    loaderMode,
                    _gameDirectory,
                    _nexusApiKey);

                ShowLimelightDialog(
                    "TEST REPORT READY",
                    "The private test report is ready to send. Limelight removed saved paths and private account values from its generated text.",
                    LimelightDialogTone.Success,
                    eyebrow: "PRIVATE TESTING");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPORT COULD NOT BE CREATED",
                    "The selected evidence files were left untouched.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REPORT FAILED");
            }
        }

        private async void ExportDiagnosticsRequested()
        {
            string? reportPath =
                LimelightFilePickerWindow.PickSaveFile(
                    this,
                    "Save Limelight diagnostic report",
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    $"Limelight-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    ".txt",
                    "TEXT FILES");

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            try
            {
                string report =
                    await CreateSanitizedDiagnosticReportAsync();

                await File.WriteAllTextAsync(
                    reportPath,
                    report);

                ShowLimelightDialog(
                    "DIAGNOSTIC REPORT EXPORTED",
                    "The report was saved. Personal and installation paths were replaced with private labels.",
                    LimelightDialogTone.Success,
                    eyebrow: "EXPORT COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPORT COULD NOT BE EXPORTED",
                    "Limelight could not save the diagnostic report.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "EXPORT FAILED");
            }
        }

        private async Task<string> CreateSanitizedDiagnosticReportAsync()
        {
            string? gameDirectory =
                _gameDirectory;

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            LiveSessionState session =
                _liveSessionService.Load();

            LiveSessionCleanupResult stagingSnapshot =
                string.IsNullOrWhiteSpace(gameDirectory)
                    ? new LiveSessionCleanupResult()
                    : _liveSessionService.GetStagingSnapshot(
                        gameDirectory);

            return await Task.Run(() =>
                _diagnosticReportService.CreateReport(
                    _settings,
                    session,
                    gameDirectory,
                    isGameRunning,
                    loader,
                    _compatibilityService.Check(
                        gameDirectory),
                    stagingSnapshot));
        }

        private void ShowBrowseNexus_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            ShowBrowseNexusPage();
        }

        private void BrowseNexus_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowBrowseNexusPage();
        }

        private void DocumentationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            const string documentationUrl =
                "https://henreh1.github.io/LimelightWiki/";

            try
            {
                // I let Windows open the guide in the user's usual browser.
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = documentationUrl,
                        UseShellExecute = true
                    });
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "DOCUMENTATION UNAVAILABLE",
                    "Limelight could not open the documentation in your browser.",
                    LimelightDialogTone.Warning,
                    details: exception.Message,
                    eyebrow: "HELP LINK");
            }
        }

        private void ShowBrowseNexusPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(
                showMyMods: false,
                showBrowseNexus: true);
        }
        private async void ImportMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            string? archivePath =
                LimelightFilePickerWindow.PickFile(
                    this,
                    "Choose a Dead as Disco mod",
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        "Downloads"),
                    ModArchiveSupport.SupportedExtensions,
                    "MOD + STAGEHAND ARCHIVES · ZIP · RAR · 7Z");

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return;
            }

            await ImportModArchiveAsync(
                archivePath);
        }

        private void HandleNativeFileDrop(
            IntPtr dropHandle)
        {
            var droppedPaths =
                new List<string>();

            try
            {
                uint fileCount =
                    DragQueryFile(
                        dropHandle,
                        uint.MaxValue,
                        fileName: null,
                        fileNameSize: 0);

                for (uint fileIndex = 0;
                     fileIndex < fileCount;
                     fileIndex++)
                {
                    uint pathLength =
                        DragQueryFile(
                            dropHandle,
                            fileIndex,
                            fileName: null,
                            fileNameSize: 0);

                    var fileName =
                        new System.Text.StringBuilder(
                            checked((int)pathLength + 1));

                    if (DragQueryFile(
                            dropHandle,
                            fileIndex,
                            fileName,
                            (uint)fileName.Capacity) > 0)
                    {
                        droppedPaths.Add(
                            fileName.ToString());
                    }
                }
            }
            finally
            {
                DragFinish(
                    dropHandle);
            }

            _ = ImportDroppedModArchivesAsync(
                droppedPaths);
        }

        private async Task ImportDroppedModArchivesAsync(
            IEnumerable<string> droppedPaths)
        {
            string[] archivePaths =
                droppedPaths
                    .Where(path =>
                        ModArchiveSupport.IsSupportedArchive(path))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            if (archivePaths.Length == 0)
            {
                ShowLimelightDialog(
                    "MOD ARCHIVE REQUIRED",
                    "Drop one or more Dead as Disco mod archives or Stagehand script packages into Limelight.",
                    LimelightDialogTone.Error,
                    eyebrow: "IMPORT MISSED ITS CUE");

                return;
            }

            string dropSignature =
                string.Join(
                    "|",
                    archivePaths.OrderBy(path =>
                        path,
                        StringComparer.OrdinalIgnoreCase));

            DateTime droppedAt =
                DateTime.UtcNow;

            if (string.Equals(
                    dropSignature,
                    _lastArchiveDropSignature,
                    StringComparison.OrdinalIgnoreCase) &&
                droppedAt - _lastArchiveDropAt <
                    TimeSpan.FromSeconds(2))
            {
                return;
            }

            _lastArchiveDropSignature =
                dropSignature;

            _lastArchiveDropAt =
                droppedAt;

            foreach (string archivePath in archivePaths)
            {
                await ImportModArchiveAsync(
                    archivePath);
            }
        }

        private void MainWindow_PreviewDragEnter(
            object sender,
            DragEventArgs e)
        {
            UpdateModDropFeedback(e);
        }

        private void MainWindow_PreviewDragOver(
            object sender,
            DragEventArgs e)
        {
            UpdateModDropFeedback(e);
        }

        private void MainWindow_PreviewDragLeave(
            object sender,
            DragEventArgs e)
        {
            Point pointerPosition =
                e.GetPosition(this);

            // Routed drag events can also fire while the pointer moves between
            // child controls. I only hide the cue after it leaves the window.
            if (pointerPosition.X <= 0 ||
                pointerPosition.Y <= 0 ||
                pointerPosition.X >= ActualWidth ||
                pointerPosition.Y >= ActualHeight)
            {
                ModDropOverlay.Visibility =
                    Visibility.Collapsed;
            }

            e.Handled = true;
        }

        private async void MainWindow_PreviewDrop(
            object sender,
            DragEventArgs e)
        {
            ModDropOverlay.Visibility =
                Visibility.Collapsed;

            e.Handled = true;

            string[] archivePaths =
                GetDroppedModArchives(
                    e.Data);

            await ImportDroppedModArchivesAsync(
                archivePaths);
        }

        private void UpdateModDropFeedback(
            DragEventArgs e)
        {
            bool canImport =
                !_isModImportInProgress &&
                GetDroppedModArchives(
                        e.Data)
                    .Length > 0;

            e.Effects = canImport
                ? DragDropEffects.Copy
                : DragDropEffects.None;

            ModDropOverlay.Visibility = canImport
                ? Visibility.Visible
                : Visibility.Collapsed;

            e.Handled = true;
        }

        private static string[] GetDroppedModArchives(
            IDataObject data)
        {
            if (!data.GetDataPresent(
                    DataFormats.FileDrop))
            {
                return Array.Empty<string>();
            }

            string[] droppedPaths =
                data.GetData(
                    DataFormats.FileDrop) as string[]
                ?? Array.Empty<string>();

            return droppedPaths
                .Where(path =>
                    ModArchiveSupport.IsSupportedArchive(path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task ImportModArchiveAsync(
            string archivePath)
        {
            if (_isModImportInProgress)
            {
                ShowLimelightDialog(
                    "IMPORT ALREADY IN PROGRESS",
                    "Let Limelight finish adding the current archive before importing another mod.",
                    LimelightDialogTone.Information,
                    eyebrow: "ONE CUE AT A TIME");

                return;
            }

            if (!File.Exists(archivePath) ||
                !ModArchiveSupport.IsSupportedArchive(
                    archivePath))
            {
                ShowLimelightDialog(
                    "MOD ARCHIVE REQUIRED",
                    "Limelight accepts mod archives saved as ZIP, RAR, or 7Z files.",
                    LimelightDialogTone.Error,
                    eyebrow: "IMPORT MISSED ITS CUE");

                return;
            }

            _isModImportInProgress = true;
            ImportModButton.IsEnabled = false;
            ImportModButton.Content = "IMPORTING...";
            ShowModImportProgress(
                "READING AND VALIDATING ARCHIVE...");

            try
            {
                StagehandLogicModPackageInspection stagehandInspection =
                    await Task.Run(() =>
                        _stagehandLogicModPackageService.Inspect(
                            archivePath));

                if (stagehandInspection.IsStagehandPackage)
                {
                    await ImportStagehandLogicModPackageAsync(
                        archivePath,
                        stagehandInspection);

                    return;
                }

                ModArchiveFingerprintResult fingerprintResult =
                    await Task.Run(() =>
                        _modLibraryService.GetArchiveFingerprintResult(
                            archivePath));

                if (!fingerprintResult.IsValid)
                {
                    ShowLimelightDialog(
                        "NOT A MOD ARCHIVE",
                        "Limelight could not find a supported Dead as Disco mod in this archive.",
                        LimelightDialogTone.Error,
                        details: fingerprintResult.Message,
                        eyebrow: "IMPORT SKIPPED");

                    return;
                }

                string incomingFingerprint =
                    fingerprintResult.Fingerprint;

                ShowModImportProgress(
                    "CHECKING FOR DUPLICATES...");

                List<(InstalledMod Mod, string Fingerprint)> libraryFingerprints =
                    await Task.Run(
                        CalculateLibraryFingerprints);

                bool fingerprintsAdded = false;

                foreach ((InstalledMod mod, string fingerprint) in
                         libraryFingerprints)
                {
                    if (string.IsNullOrWhiteSpace(
                            mod.ContentFingerprint))
                    {
                        // Older libraries did not store fingerprints. I fill
                        // them in once so renamed legacy mods are protected too.
                        mod.ContentFingerprint = fingerprint;
                        fingerprintsAdded = true;
                    }
                }

                if (fingerprintsAdded)
                {
                    _settingsService.Save(
                        _settings);
                }

                InstalledMod? existingMod =
                    libraryFingerprints
                        .FirstOrDefault(item =>
                            string.Equals(
                                item.Fingerprint,
                                incomingFingerprint,
                                StringComparison.OrdinalIgnoreCase))
                        .Mod;

                if (existingMod != null)
                {
                    ShowLimelightDialog(
                        "MOD ALREADY INSTALLED",
                        $"{existingMod.DisplayName} already contains the same mod files. Renaming a library entry does not create a separate copy.",
                        LimelightDialogTone.Information,
                        primaryAction: "VIEW MY MODS",
                        eyebrow: "IMPORT SKIPPED");

                    return;
                }

                // Large archives are processed in the background so
                // the interface remains responsive during the import.
                ShowModImportProgress(
                    "EXTRACTING AND SCANNING FILES...");

                InstalledMod installedMod =
                    await Task.Run(() =>
                        _modLibraryService.Import(
                            archivePath,
                            contentFingerprint: incomingFingerprint));

                _settings.InstalledMods.Add(
                    installedMod);

                ShowModImportProgress(
                    "SAVING TO YOUR LIBRARY...");

                if (installedMod.IsCharacterSlotMod)
                {
                    // I add every imported slot to the next catalogue pass so
                    // the Locker sees the whole cast, not only today's lead.
                    _settings.CharacterSlotCatalogueNeedsSynchronization =
                        true;

                    _pendingDeploymentAttempted =
                        false;
                }

                _settingsService.Save(_settings);

                if (installedMod.IsCharacterSlotMod)
                {
                    await ApplyPendingDeploymentIfPossible();
                }

                RefreshLibrarySummary();

                ShowLimelightDialog(
                    "MOD IMPORTED",
                    $"{installedMod.DisplayName} was added to your library.",
                    LimelightDialogTone.Success,
                    details:
                        $"Package files: {installedMod.PackageFiles.Count}\n" +
                        $"Assets detected: {installedMod.AssetPackages.Count}\n" +
                        (installedMod.IsCharacterSlotMod
                            ? $"Format: Character Slot Loader ({installedMod.CharacterSlotName})\n" +
                              $"Live mesh: {installedMod.CharacterSlotMeshPackagePath}\n" +
                              (_settings.CharacterSlotCatalogueNeedsSynchronization
                                  ? "Locker slot: ready on the next game launch"
                                  : "Locker slot: added to the appearance catalogue")
                            : "Live-refreshable: " +
                              $"{installedMod.AssetPackages.Count(package => package.IsSafeForLiveReload)}"),
                    eyebrow: "READY FOR THE SPOTLIGHT");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD IMPORT FAILED",
                    "Limelight could not add this archive to the library.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "IMPORT MISSED ITS CUE");
            }
            finally
            {
                _isModImportInProgress = false;
                ImportModButton.IsEnabled = true;
                ImportModButton.Content = "IMPORT MOD";
                ModImportProgressOverlay.Visibility =
                    Visibility.Collapsed;
            }
        }

        public async Task HandleStartupArgumentsAsync(string[] arguments)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(
                        arguments[index],
                        "--import-stagehand",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await ImportModArchiveAsync(arguments[index + 1]);
                    return;
                }
            }
        }

        private async Task ImportStagehandLogicModPackageAsync(
            string archivePath,
            StagehandLogicModPackageInspection inspection)
        {
            if (!inspection.IsValid ||
                inspection.Manifest is null)
            {
                ShowLimelightDialog(
                    "INVALID STAGEHAND SCRIPT",
                    "Limelight recognized a Stagehand package, but its API or safety contract is invalid.",
                    LimelightDialogTone.Error,
                    details: inspection.Message,
                    eyebrow: "SCRIPT IMPORT BLOCKED");

                return;
            }

            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowLimelightDialog(
                    "CONNECT THE GAME FIRST",
                    "Connect Limelight to Dead as Disco before installing a Stagehand script.",
                    LimelightDialogTone.Warning,
                    eyebrow: "SCRIPT IMPORT PAUSED");

                return;
            }

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    _gameDirectory);

            if (!loader.IsInstalled)
            {
                ShowLimelightDialog(
                    "LIMELIGHT RUNTIME REQUIRED",
                    "Install or repair Limelight's Live Loader before adding a Stagehand script.",
                    LimelightDialogTone.Warning,
                    details:
                        "Stagehand scripts run through Limelight's managed UE4SS runtime. " +
                        "Limelight will not execute native code from this package.",
                    eyebrow: "SCRIPT IMPORT PAUSED");

                return;
            }

            StagehandLogicModManifest manifest =
                inspection.Manifest;

            string permissionReport =
                manifest.Permissions.Count == 0
                    ? "None"
                    : string.Join(
                        Environment.NewLine,
                        manifest.Permissions.Select(permission =>
                            $"• {permission}"));

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "INSTALL STAGEHAND SCRIPT?",
                    $"{manifest.Name} v{manifest.Version} is a Lua logic mod for Stagehand API {manifest.ApiVersion}.",
                    LimelightDialogTone.Question,
                    primaryAction: "INSTALL SCRIPT",
                    secondaryAction: "NOT NOW",
                    details:
                        $"ID: {manifest.Id}\n" +
                        $"Trust label (self-reported): {manifest.DeclaredTrust}\n" +
                        $"Stagehand local review: {(inspection.IsReviewCurrent ? "exact hashes match" : "missing, stale, or changed")}\n" +
                        "Native code: no\n" +
                        "Secure Lua sandbox: no\n\n" +
                        "Requested permissions:\n" +
                        permissionReport,
                    eyebrow: "REVIEW SCRIPT TRUST",
                    footerHint: inspection.IsReviewCurrent
                        ? "Local review matches these exact files. Lua still runs as trusted code inside the game process."
                        : "This is not a secure sandbox and the exact files are not locally approved. Only install scripts you trust.");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            ShowModImportProgress(
                "INSTALLING STAGEHAND SCRIPT...");

            _stagehandPayloadService.EnsureInstalled(
                loader);

            StagehandLogicModInstallResult result =
                await Task.Run(() =>
                    _stagehandLogicModPackageService.Install(
                        archivePath,
                        loader));

            RefreshStagehandScriptsPage();

            bool gameRunning =
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            ShowLimelightDialog(
                result.Updated
                    ? "STAGEHAND SCRIPT UPDATED"
                    : "STAGEHAND SCRIPT INSTALLED",
                $"{result.Manifest.Name} is ready for Stagehand.",
                LimelightDialogTone.Success,
                details:
                    $"Version: {result.Manifest.Version}\n" +
                    $"Permissions: {result.Manifest.Permissions.Count}\n" +
                    $"Installed to: {result.InstallDirectory}\n" +
                    (gameRunning
                        ? "Dead as Disco is already running; the script will load on the next launch."
                        : "Launch Dead as Disco through Limelight to run the script."),
                eyebrow: "SCRIPT READY FOR THE STAGE");
        }

        private void ShowModImportProgress(
            string stage)
        {
            ModImportProgressText.Text =
                stage;

            ModImportProgressOverlay.Visibility =
                Visibility.Visible;
        }

        private List<(InstalledMod Mod, string Fingerprint)>
            CalculateLibraryFingerprints()
        {
            List<(InstalledMod Mod, string Fingerprint)> fingerprints =
                new List<(InstalledMod Mod, string Fingerprint)>();

            foreach (InstalledMod mod in _settings.InstalledMods)
            {
                if (!Directory.Exists(
                        mod.InstallDirectory))
                {
                    continue;
                }

                try
                {
                    string fingerprint =
                        _modLibraryService
                            .CalculateInstalledModFingerprint(mod);

                    fingerprints.Add(
                        (mod, fingerprint));
                }
                catch (IOException)
                {
                    // A damaged legacy entry should not block imports for the
                    // rest of the library. Its normal validation still reports it.
                }
                catch (UnauthorizedAccessException)
                {
                    // Security software can briefly hold an old package file.
                    // I skip only that entry and leave the rest of the scan intact.
                }
            }

            return fingerprints;
        }

        private void RefreshLibrarySummary()
        {
            // Ignore entries whose extracted folder was manually removed.
            List<InstalledMod> availableMods =
                _settings.InstalledMods
                    .Where(mod =>
                        Directory.Exists(
                            mod.InstallDirectory))
                    .ToList();

            _settings.EnabledConventionalModIds ??=
                new List<string>();

            bool settingsChanged =
                false;

            InstalledMod? savedActiveMod =
                availableMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            if (savedActiveMod?.IsConventionalMod == true)
            {
                if (!_settings.EnabledConventionalModIds.Any(id =>
                        string.Equals(
                            id,
                            savedActiveMod.Id,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    _settings.EnabledConventionalModIds.Add(
                        savedActiveMod.Id);
                }

                _settings.ActiveModId =
                    string.Empty;

                _settings.ConventionalModsNeedSynchronization =
                    true;

                settingsChanged =
                    true;
            }

            var availableConventionalIds =
                new HashSet<string>(
                    availableMods
                        .Where(mod =>
                            mod.IsConventionalMod)
                        .Select(mod =>
                            mod.Id),
                    StringComparer.OrdinalIgnoreCase);

            if (_settings.EnabledConventionalModIds.RemoveAll(id =>
                    !availableConventionalIds.Contains(id)) > 0)
            {
                settingsChanged =
                    true;
            }

            InstalledMod? activeMod =
                availableMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            // A missing library folder means the saved active selection
            // is no longer valid.
            if (activeMod == null &&
                !string.IsNullOrWhiteSpace(
                    _settings.ActiveModId))
            {
                _settings.ActiveModId =
                    string.Empty;

                settingsChanged =
                    true;
            }

            List<InstalledMod> playerCharacterMods =
                availableMods
                    .Where(mod =>
                        mod.IsPlayerCharacterMod)
                    .ToList();

            var playerCharacterModIds =
                new HashSet<string>(
                    playerCharacterMods.Select(mod =>
                        mod.Id),
                    StringComparer.OrdinalIgnoreCase);

            if (_settings.X19LoaderModIds.RemoveAll(id =>
                    !playerCharacterModIds.Contains(id)) > 0)
            {
                settingsChanged =
                    true;
            }

            if (settingsChanged)
            {
                _settingsService.Save(_settings);
            }

            int installedCount =
                availableMods.Count;

            MyModsPageControl.ShowMods(
                availableMods,
                _settings.ActiveModId,
                _settings.EnabledConventionalModIds);

            ProfilesPageControl.ShowProfiles(
                _settings.ModProfiles,
                playerCharacterMods);

            LiveLoadersPageControl.ShowConfiguration(
                playerCharacterMods,
                _settings.X19LoaderModIds,
                _settings.X19LoaderProfileIds,
                _settings.ActiveModId,
                _settings.X19HotkeyGesture,
                _settings.X19ShuffleEnabled,
                _settings.ModProfiles);

            InstalledModCountText.Text =
                installedCount.ToString();

            ActiveModelText.Text =
                activeMod?.DisplayName.ToUpperInvariant()
                ?? "NONE";

            InstalledModCountText.Foreground =
    (Brush)FindResource(
        installedCount == 0
            ? "PinkBrush"
            : "CyanBrush");

            ActiveModelText.Foreground =
                (Brush)FindResource(
                    activeMod is null
                        ? "PinkBrush"
                        : "CyanBrush");

            UpdateSpotlightBanner(activeMod);
            RefreshDiscordPresence();

            if (installedCount == 0)
            {
                LibrarySummaryText.Text =
                    "Your mod library is empty. Import or drag in a ZIP, RAR, or 7Z archive to get started.";

                LibraryStatusText.Text =
                    "NO MODS YET";
                LibraryStatusText.Foreground =
    (Brush)FindResource("PinkBrush");

                return;
            }

            LibrarySummaryText.Text =
                installedCount == 1
                    ? "1 mod is installed and ready to activate."
                    : $"{installedCount} mods are installed and ready to activate.";

            LibraryStatusText.Text =
                $"{installedCount} READY";
        }

        private void UpdateSpotlightBanner(
            InstalledMod? activeMod)
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                SpotlightTitleText.Text =
                    "READY FOR THE SPOTLIGHT?";

                SpotlightDescriptionText.Text =
                    "Connect your game directory, install a character mod, and take control of the stage.";

                ConnectGameButton.Content =
                    "CONNECT GAME";

                return;
            }

            if (activeMod is null)
            {
                SpotlightTitleText.Text =
                    "CHOOSE YOUR HEADLINER";

                SpotlightDescriptionText.Text =
                    "Dead as Disco is connected. Choose a character model to take the spotlight.";

                ConnectGameButton.Content =
                    "CHOOSE MODEL";

                return;
            }

            SpotlightTitleText.Text =
                $"{activeMod.DisplayName.ToUpperInvariant()} HAS THE SPOTLIGHT";

            SpotlightDescriptionText.Text =
                "Your selected character is installed and ready for the next performance.";

            ConnectGameButton.Content =
                "SWITCH MODEL";
        }

        private static ProcessStartInfo CreateSteamLaunchStartInfo()
        {
            const string steamAppId =
                "3404260";

            string? steamExecutable =
                FindSteamExecutable();

            if (!string.IsNullOrWhiteSpace(steamExecutable))
            {
                return new ProcessStartInfo
                {
                    // Steam's explicit app-launch command is more dependable
                    // than asking Windows to forward a steam:// link.
                    FileName = steamExecutable,
                    Arguments = $"-applaunch {steamAppId}",
                    WorkingDirectory =
                        Path.GetDirectoryName(steamExecutable) ??
                        string.Empty,
                    UseShellExecute = false
                };
            }

            // Keep the registered protocol as a fallback for unusual Steam
            // installs whose executable path is not available in the registry.
            return new ProcessStartInfo
            {
                FileName =
                    $"steam://rungameid/{steamAppId}",
                UseShellExecute = true
            };
        }

        private static string? FindSteamExecutable()
        {
            string? currentUserSteam =
                Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    "SteamExe",
                    null) as string;

            string? localMachineSteam =
                Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    null) as string;

            string? localMachineSteam64 =
                Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                    "InstallPath",
                    null) as string;

            string? programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string?[] candidates =
            {
                currentUserSteam,
                string.IsNullOrWhiteSpace(localMachineSteam)
                    ? null
                    : Path.Combine(localMachineSteam, "steam.exe"),
                string.IsNullOrWhiteSpace(localMachineSteam64)
                    ? null
                    : Path.Combine(localMachineSteam64, "steam.exe"),
                string.IsNullOrWhiteSpace(programFilesX86)
                    ? null
                    : Path.Combine(programFilesX86, "Steam", "steam.exe")
            };

            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string normalizedCandidate =
                    candidate.Replace('/', Path.DirectorySeparatorChar);

                if (File.Exists(normalizedCandidate))
                {
                    return normalizedCandidate;
                }
            }

            return null;
        }

        private static void WriteLaunchTrace(
            string message)
        {
            try
            {
                string logDirectory =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Limelight",
                        "Logs");

                Directory.CreateDirectory(logDirectory);

                string logPath =
                    Path.Combine(
                        logDirectory,
                        "launch.log");

                // I keep this trace deliberately small. It only records launch
                // stages, but gives us a useful answer if Steam ever stays quiet.
                if (File.Exists(logPath) &&
                    new FileInfo(logPath).Length > 512 * 1024)
                {
                    File.WriteAllText(
                        logPath,
                        string.Empty);
                }

                File.AppendAllText(
                    logPath,
                    $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
            catch
            {
                // A diagnostic trace must never be allowed to block a launch.
            }
        }

        private static bool IsGameVersionCompatibilityWarning(
            LocalCompatibilityResult compatibility)
        {
            return compatibility.GameBuildDetected &&
                   !compatibility.GameBuildCompatible;
        }

        private async void LaunchGame_Click(
            object sender,
            RoutedEventArgs e)
        {
            WriteLaunchTrace(
                "Launch button selected.");

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to your Dead as Disco folder before launching the game.",
                    LimelightDialogTone.Warning,
                    eyebrow: "LAUNCH BLOCKED");

                return;
            }

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                // Starting a second copy can cause Steam or the game to display
                // confusing errors, so keep the already-running instance.
                ShowLimelightDialog(
                    "GAME ALREADY RUNNING",
                    "Limelight found the existing Dead as Disco session and will not start a second copy.",
                    LimelightDialogTone.Information,
                    eyebrow: "LAUNCH SKIPPED");

                return;
            }

            if (_multiplayerSessionService.IsActive)
            {
                ShowMultiplayerPage();

                ShowLimelightDialog(
                    "MULTIPLAYER SESSION ALREADY READY",
                    "Use the active Multiplayer session instead of starting a separate Normal or X19 launch.",
                    LimelightDialogTone.Information,
                    eyebrow: "LAUNCH SKIPPED");

                return;
            }

            string executablePath =
                Path.Combine(
                    gameDirectory,
                    "Pagoda.exe");

            if (!File.Exists(executablePath))
            {
                ShowLimelightDialog(
                    "GAME EXECUTABLE MISSING",
                    "Limelight could not find Pagoda.exe. Reconnect the game folder in Settings and try again.",
                    LimelightDialogTone.Warning,
                    eyebrow: "LAUNCH BLOCKED");

                return;
            }

            // A normal dashboard launch must never inherit an old experimental
            // multiplayer role from a previous test or interrupted Limelight run.
            DeactivateMultiplayerPayloadBestEffort();

            List<InstalledMod> x19Rotation =
                GetX19Rotation();

            LocalCompatibilityResult compatibility =
                _compatibilityService.Check(
                    gameDirectory);

            LoaderModeSelectionWindow modeWindow =
                new LoaderModeSelectionWindow(
                    x19Rotation.Count,
                    _settings.X19HotkeyGesture,
                    compatibility)
                {
                    Owner = this
                };

            bool? modeAccepted =
                modeWindow.ShowDialog();

            WriteLaunchTrace(
                "Loader selector closed: " +
                $"accepted={modeAccepted}; " +
                $"mode={modeWindow.SelectedMode?.ToString() ?? "NONE"}; " +
                $"configureX19={modeWindow.ConfigureX19Requested}; " +
                $"openSupport={modeWindow.OpenSupportRequested}.");

            if (modeAccepted != true ||
                modeWindow.SelectedMode is null)
            {
                if (modeWindow.ConfigureX19Requested)
                {
                    ShowLiveLoadersPage();
                }

                if (modeWindow.OpenSupportRequested)
                {
                    ShowSettingsPage();
                    SettingsPageControl.ShowSupportCategory();

                    ShowNotification(
                        "LIVE LOADER NEEDS ATTENTION",
                        compatibility.Detail,
                        isError: true);
                }

                return;
            }

            _selectedLoaderMode =
                modeWindow.SelectedMode.Value;

            string? launcherCompatibilityWarning =
                _selectedLoaderMode != LoaderLaunchMode.Disabled &&
                !compatibility.IsLiveLoaderCompatible &&
                IsGameVersionCompatibilityWarning(compatibility)
                    ? compatibility.Detail
                    : null;

            WriteLaunchTrace(
                $"Launch mode accepted: {_selectedLoaderMode}.");

            if (launcherCompatibilityWarning is not null)
            {
                // I keep compatibility failures visible while still allowing
                // the game to launch immediately during live updates.
                _selectedLoaderMode =
                    LoaderLaunchMode.Disabled;

                WriteLaunchTrace(
                    "Live loader launch was downgraded for compatibility: " +
                    launcherCompatibilityWarning);
            }

            _globalHotkeyService.Unregister();

            try
            {
                _liveLoaderBridgeService.SetSessionBypass(
                    _selectedLoaderMode ==
                        LoaderLaunchMode.Disabled);

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    WriteLaunchTrace(
                        "Checking Live Loader readiness.");

                    // Recheck immediately before touching the game directory.
                    // Steam may have finished an update while the selector was open.
                    compatibility =
                        _compatibilityService.Check(
                            gameDirectory);

                    if (!compatibility.IsLiveLoaderCompatible)
                    {
                        if (IsGameVersionCompatibilityWarning(compatibility))
                        {
                            launcherCompatibilityWarning =
                                compatibility.Detail;

                            _selectedLoaderMode =
                                LoaderLaunchMode.Disabled;

                            _liveLoaderBridgeService.SetSessionBypass(
                                isDisabled: true);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                compatibility.Detail);
                        }
                    }

                    if (_selectedLoaderMode !=
                        LoaderLaunchMode.Disabled)
                    {
                        Ue4ssDetectionResult loader =
                            _ue4ssDetectionService.Detect(
                                gameDirectory);

                    if (!loader.IsInstalled ||
                        !_ue4ssConfigurationService.IsRuntimeCompatible(loader) ||
                        !_ue4ssConfigurationService.IsConfigured(loader) ||
                        !_liveLoaderBridgeService.IsInstalled(loader) ||
                        !_nativeBridgeInstallerService.IsCurrentVersionInstalled(loader) ||
                        !_stagehandPayloadService.IsCurrentVersionInstalled(loader))
                    {
                        throw new InvalidOperationException(
                            "The Live Loader needs to be repaired before this launch. " +
                            "Open Settings, choose Support, then select Repair Live Loader.");
                    }
                    }

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    // Installation and repair belong to the setup and
                        // Support flows. The launch button only verifies those
                        // files so a locked game folder cannot hold Steam's
                        // request hostage.
                        WriteLaunchTrace(
                            "Live Loader readiness check passed.");
                    }
                }

                if (launcherCompatibilityWarning is not null)
                {
                    ShowNotification(
                        "LIVE LOADER BLOCKED FOR THIS LAUNCH",
                        $"A build update may be in progress. Limelight is launching without Live Loader for this run: {launcherCompatibilityWarning}",
                        isError: true);

                    WriteLaunchTrace(
                        $"Launch downgraded to no live loader: {launcherCompatibilityWarning}");
                }

                ProcessStartInfo startInfo =
                    CreateSteamLaunchStartInfo();

                WriteLaunchTrace(
                    $"Sending Steam launch request with {startInfo.FileName} {startInfo.Arguments}".TrimEnd());

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    // A fresh game launch must produce a fresh heartbeat before the dashboard
                    // is allowed to report the bridge as online.
                    _liveLoaderBridgeService.ClearHeartbeat();
                }

                // Ask Steam to launch its registered Dead as Disco installation.
                using Process? steamLaunch =
                    Process.Start(startInfo);

                if (steamLaunch is null)
                {
                    throw new InvalidOperationException(
                        "Windows did not accept Limelight's Steam launch request.");
                }

                WriteLaunchTrace(
                    "Steam accepted the launch request.");

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    // Keep Limelight locked while the runtime comes online and the
                    // active mod is mounted. This removes the tempting-but-unsafe
                    // window where a user can switch mods during LoadMap.
                    await InitialiseLiveLoaderForRunningGameAsync(
                        waitForGameProcess: true);

                    // The process timer may notice the game a fraction earlier than
                    // this launch path. I wait for that shared setup to finish before
                    // deciding whether X19 can register its hotkey.
                    DateTime initialisationDeadline =
                        DateTime.UtcNow.AddMinutes(6);

                    while (_isLiveLoaderInitializationRunning &&
                           DateTime.UtcNow < initialisationDeadline)
                    {
                        await Task.Delay(100);
                    }
                }

                if (_selectedLoaderMode ==
                    LoaderLaunchMode.X19)
                {
                    if (_liveLoaderBridgeService.IsOnline() &&
                        _hasInitialisedCurrentGameSession &&
                        !_isLiveLoaderInitializationRunning)
                    {
                        EnableX19Hotkey();
                    }
                    else
                    {
                        _selectedLoaderMode =
                            LoaderLaunchMode.Normal;

                        ShowNotification(
                            "X19 COULD NOT START",
                            "The Live Loader did not come online, so the X19 hotkey is unavailable for this session.",
                            isError: true);
                    }
                }
            }
            catch (Exception exception)
            {
                WriteLaunchTrace(
                    $"Launch failed: {exception.GetType().Name}: {exception.Message}");

                _globalHotkeyService.Unregister();
                _liveLoaderBridgeService.SetSessionBypass(
                    isDisabled: false);

                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;

                ShowLimelightDialog(
                    "DEAD AS DISCO COULD NOT START",
                    "Limelight restored its launch state and left the game files unchanged.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "LAUNCH FAILED");
            }
        }
        private async void ConnectGame_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowMyModsPage();
                return;
            }

            await ChooseGameDirectoryAsync();
        }

        private async void ChangeGameFolderRequested()
        {
            await ChooseGameDirectoryAsync();
        }

        private async Task ChooseGameDirectoryAsync()
        {
            // Ask for the main installation folder instead of making
            // the user locate the internal Paks directory.
            string? selectedDirectory =
                LimelightFilePickerWindow.PickFolder(
                    this,
                    "Choose the Dead as Disco installation folder",
                    _gameDirectory);

            // Cancelling leaves the current connection unchanged.
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                return;
            }

            if (!TryConnectToGame(
                    selectedDirectory,
                    showError: true))
            {
                return;
            }

            // Store the directory only after it passes validation.
            _settings.GameDirectory =
                selectedDirectory;

            _settingsService.Save(_settings);

            ShowLimelightDialog(
                "GAME CONNECTED",
                "Dead as Disco was connected successfully.",
                LimelightDialogTone.Success,
                eyebrow: "DIRECTORY READY");

            // A newly selected folder should receive its own optional-loader prompt.
            _hasHandledLiveLoaderPrompt = false;

            await CheckForExistingMods();
            await ShowLiveLoaderSetupPromptIfNeeded();
        }

        private bool TryConnectToGame(
            string selectedDirectory,
            bool showError)
        {
            string gameExecutable = Path.Combine(
                selectedDirectory,
                "Pagoda.exe");

            string pakDirectory = Path.Combine(
                selectedDirectory,
                "Pagoda",
                "Content",
                "Paks");

            // Both paths are checked so an unrelated folder containing
            // a file named Pagoda.exe is not accepted accidentally.
            bool validDirectory =
                File.Exists(gameExecutable) &&
                Directory.Exists(pakDirectory);

            if (!validDirectory)
            {
                if (showError)
                {
                    ShowLimelightDialog(
                        "THAT IS NOT THE GAME FOLDER",
                        "Limelight could not find Pagoda.exe and the game's Paks folder. Select the main Dead as Disco folder, not the Paks folder itself.",
                        LimelightDialogTone.Warning,
                        eyebrow: "INVALID DIRECTORY");
                }

                return false;
            }

            _gameDirectory =
                selectedDirectory;

            // Give the user a clear indication that validation passed.
            GameStatusDot.Fill =
                (Brush)FindResource("CyanBrush");

            GameStatusTitle.Text =
                "GAME CONNECTED";

            GameStatusDescription.Text =
                GetSidebarVersionText();

            RefreshSettingsPage();
            RefreshLibrarySummary();

            return true;
        }

        private void RestoreSavedGameDirectory()
        {
            if (string.IsNullOrWhiteSpace(
                    _settings.GameDirectory))
            {
                return;
            }

            // Steam library moves and game updates can invalidate a
            // previously saved location, so check it on every launch.
            if (TryConnectToGame(
                    _settings.GameDirectory,
                    showError: false))
            {
                return;
            }

            _settings.GameDirectory =
                string.Empty;

            _settingsService.Save(_settings);

            GameStatusDescription.Text =
                GetSidebarVersionText();
        }
    }
}
