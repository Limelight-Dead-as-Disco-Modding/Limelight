using Limelight.Models;
using Limelight.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class MultiplayerPage : UserControl
    {
        private bool _hasActiveSession;
        private bool _isReady;
        private bool _nativePlayerModeSupported;
        private string _steamRequestId = string.Empty;
        private bool _isSteamHost;

        public event Action<MultiplayerHostOptions>? HostRequested;

        public event Action<MultiplayerJoinOptions>? JoinRequested;

        public event Action? StopRequested;

        public event Action? VerifyRequested;

        public event Action? RemoveRequested;

        public MultiplayerPage()
        {
            InitializeComponent();

            VersionText.Text = $"MP v{LimelightMpPayloadService.Version}";
            VersionNoticeText.Text = $"Use the same game build and LimelightMP v{LimelightMpPayloadService.Version} on both PCs. Level transitions, cameras, dialogue, rhythm sync, local menus and effects are still being hardened.";
            UpdateHostRouteControls();

            SessionLogBox.Document =
                new FlowDocument
                {
                    PagePadding = new Thickness(0)
                };

            AddLog(
                MultiplayerLogLevel.Log,
                "LimelightMP is idle. Check setup, then host or join a session.");
        }

        public void ShowReadiness(
            bool gameConnected,
            bool gameRunning,
            bool ue4ssInstalled,
            bool embeddedPayloadValid,
            string? tailscaleAddress,
            MultiplayerInstalledRole? installedRole,
            IReadOnlyCollection<string> localAddresses,
            bool steamTransportSupported,
            bool nativePlayerModeSupported)
        {
            bool ready =
                gameConnected &&
                !gameRunning &&
                ue4ssInstalled &&
                embeddedPayloadValid;

            _isReady = ready;
            _nativePlayerModeSupported = nativePlayerModeSupported;
            SteamRouteItem.Visibility = steamTransportSupported ? Visibility.Visible : Visibility.Collapsed;
            if (!steamTransportSupported && HostRouteComboBox.SelectedIndex == 3)
                HostRouteComboBox.SelectedIndex = 0;
            if (!nativePlayerModeSupported && !_hasActiveSession)
                NativePlayerModeCheckBox.IsChecked = false;
            UpdateHostRouteControls();
            string? previousAddress = LocalAddressComboBox.SelectedItem as string;
            LocalAddressComboBox.ItemsSource = localAddresses;
            LocalAddressComboBox.SelectedItem =
                previousAddress is not null && localAddresses.Contains(previousAddress)
                    ? previousAddress
                    : localAddresses.FirstOrDefault();

            ReadinessTitleText.Text =
                ready
                    ? "READY FOR A MULTIPLAYER TEST"
                    : gameRunning
                        ? "CLOSE THE GAME TO CHANGE ROLES"
                        : !gameConnected
                            ? "CONNECT DEAD AS DISCO FIRST"
                            : !ue4ssInstalled
                                ? "LIVE LOADER SETUP REQUIRED"
                                : "MULTIPLAYER PAYLOAD NEEDS ATTENTION";

            List<string> details =
                new()
                {
                    gameConnected
                        ? "Game connected"
                        : "Game folder missing",
                    ue4ssInstalled
                        ? "UE4SS ready"
                        : "UE4SS not ready",
                    embeddedPayloadValid
                        ? $"v{LimelightMpPayloadService.Version} payload verified"
                        : "payload verification failed",
                    string.IsNullOrWhiteSpace(tailscaleAddress)
                        ? $"Tailscale not detected · {localAddresses.Count} local-network address(es)"
                        : $"Tailscale {tailscaleAddress}"
                };

            if (installedRole is not null)
            {
                details.Add(
                    $"{installedRole.Role} role installed");
            }

            ReadinessDetailText.Text =
                string.Join("  ·  ", details);

            ReadinessTitleText.Foreground =
                ready
                    ? (Brush)FindResource("CyanBrush")
                    : (Brush)FindResource("PinkBrush");

            if (!IsBusy() &&
                !_hasActiveSession)
            {
                HostButton.IsEnabled = ready;
                JoinButton.IsEnabled = ready;
            }
        }

        public void ShowSession(
            MultiplayerStartResult session)
        {
            ActiveSessionCard.Visibility =
                Visibility.Visible;

            _hasActiveSession = true;
            _steamRequestId = session.SteamRequestId;
            _isSteamHost = session.UsesSteam && session.Role == MultiplayerRole.Host;

            bool isHost =
                session.Role == MultiplayerRole.Host;

            SessionEyebrowText.Text =
                session.UsesSteam ? "STEAM TRANSPORT STARTING" : isHost
                    ? "HOST SESSION READY"
                    : "CLIENT SESSION READY";

            SessionTitleText.Text =
                session.UsesSteam ? "WAITING FOR THE GAME'S STEAM CONNECTION" : isHost
                    ? "SHARE THE CODE, THEN ENTER THE DIVE BAR"
                    : "REACHING THE HOST";

            SessionDetailText.Text =
                isHost
                    ? "Your friend runs Join in their own Limelight. In the Dive Bar, press Ctrl+Shift+F5 once to begin hosting."
                    : $"Your local game will connect to {session.Address}:{session.GamePort}. Leave Limelight open while playing.";

            SessionDetailText.Text += $"\n{session.RouteDescription}. {session.RouteNotice}";
            SessionDetailText.Text += session.NativePlayerMode
                ? "\nExperimental remote-player ownership; gameplay unverified."
                : "\nCompatibility player ownership.";

            FriendCodeText.Text =
                session.FriendCode;

            FriendCodePanel.Visibility =
                isHost && !string.IsNullOrEmpty(session.FriendCode)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            HostButton.IsEnabled = false;
            JoinButton.IsEnabled = false;
            UpdateHostRouteControls();
        }

        public void ShowSteamStatus(MultiplayerSteamStatus status)
        {
            if (!_hasActiveSession || string.IsNullOrEmpty(_steamRequestId) || status.RequestId != _steamRequestId)
                return;

            bool failed = status.State is "failed" or "stopped";
            SessionEyebrowText.Text = failed ? "STEAM TRANSPORT STOPPED" :
                status.TransportReady ? "STEAM TRANSPORT READY · GAMEPLAY UNVERIFIED" : "STEAM CONNECTION IN PROGRESS";
            SessionTitleText.Text = failed ? "STOP, THEN RETRY OR CHOOSE A DIRECT ROUTE" :
                status.TransportReady ? "PEER CONNECTED — TEST GAMEPLAY NEXT" :
                _isSteamHost && !string.IsNullOrEmpty(status.FriendCode) ? "STEAM INVITATION READY TO SHARE" : "WAITING FOR STEAM";
            SessionDetailText.Text = LimelightMpSteamContract.DescribeState(status.State) +
                $"\nWorld packets: sent {status.WorldSent}, received {status.WorldReceived}. Input packets: sent {status.InputSent}, received {status.InputReceived}.";
            if (failed)
                SessionDetailText.Text += "\n" + LimelightMpSteamContract.RedactStatusMessage(
                    string.IsNullOrEmpty(status.Error) ? status.Message : status.Error);
            if (_isSteamHost && !failed)
                SessionDetailText.Text += "\nEnter the Dive Bar, then press Ctrl+Shift+F5 once to host the game.";
            FriendCodeText.Text = _isSteamHost && !failed ? status.FriendCode : string.Empty;
            FriendCodePanel.Visibility = string.IsNullOrEmpty(FriendCodeText.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ShowIdle()
        {
            ActiveSessionCard.Visibility =
                Visibility.Collapsed;

            _hasActiveSession = false;
            _steamRequestId = string.Empty;

            FriendCodeText.Text =
                string.Empty;

            SetBusy(
                false,
                string.Empty);
        }

        public void SetBusy(
            bool isBusy,
            string message)
        {
            BusyText.Tag = isBusy;
            BusyText.Text = message;
            HostButton.IsEnabled =
                !isBusy &&
                !_hasActiveSession &&
                _isReady;

            JoinButton.IsEnabled =
                !isBusy &&
                !_hasActiveSession &&
                _isReady;
            FriendCodeTextBox.IsEnabled = !isBusy;
            UpdateHostRouteControls();
        }

        public void AddLog(
            MultiplayerLogLevel level,
            string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                        AddLog(level, message)));
                return;
            }

            Brush foreground =
                level switch
                {
                    MultiplayerLogLevel.Network =>
                        new SolidColorBrush(
                            Color.FromRgb(82, 229, 120)),
                    MultiplayerLogLevel.Gameplay =>
                        (Brush)FindResource("CyanBrush"),
                    MultiplayerLogLevel.Warning =>
                        new SolidColorBrush(
                            Color.FromRgb(255, 212, 119)),
                    MultiplayerLogLevel.Error =>
                        new SolidColorBrush(
                            Color.FromRgb(255, 76, 100)),
                    _ =>
                        new SolidColorBrush(
                            Color.FromRgb(133, 138, 151))
                };

            Paragraph paragraph =
                new()
                {
                    Margin = new Thickness(0, 0, 0, 3)
                };

            paragraph.Inlines.Add(
                new Run(
                    $"[{DateTime.Now:HH:mm:ss}] {message}")
                {
                    Foreground = foreground
                });

            SessionLogBox.Document.Blocks.Add(
                paragraph);

            while (SessionLogBox.Document.Blocks.Count > 250)
            {
                SessionLogBox.Document.Blocks.Remove(
                    SessionLogBox.Document.Blocks.FirstBlock);
            }

            SessionLogBox.ScrollToEnd();
        }

        private bool IsBusy()
        {
            return BusyText.Tag is true;
        }

        private void Host_Click(
            object sender,
            RoutedEventArgs e)
        {
            HostRequested?.Invoke(new MultiplayerHostOptions
            {
                Route = (MultiplayerHostRoute)HostRouteComboBox.SelectedIndex,
                Address = HostRouteComboBox.SelectedIndex == 1
                    ? LocalAddressComboBox.SelectedItem as string ?? string.Empty
                    : DirectAddressTextBox.Text,
                NativePlayerMode = NativePlayerModeCheckBox.IsChecked == true
            });
        }

        private void HostRoute_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateHostRouteControls();
        }

        private void UpdateHostRouteControls()
        {
            if (LocalAddressComboBox is null || DirectAddressTextBox is null ||
                HostRouteNoticeText is null || NativePlayerModeCheckBox is null || BusyText is null)
            {
                return;
            }

            bool enabled = !IsBusy() && !_hasActiveSession;
            HostRouteComboBox.IsEnabled = enabled;
            NativePlayerModeCheckBox.IsEnabled = enabled && _nativePlayerModeSupported;
            LocalAddressComboBox.IsEnabled = enabled;
            DirectAddressTextBox.IsEnabled = enabled;
            LocalAddressComboBox.Visibility = HostRouteComboBox.SelectedIndex == 1
                ? Visibility.Visible : Visibility.Collapsed;
            DirectAddressTextBox.Visibility = HostRouteComboBox.SelectedIndex == 2
                ? Visibility.Visible : Visibility.Collapsed;
            HostRouteNoticeText.Text = HostRouteComboBox.SelectedIndex switch
            {
                1 => "Choose the address on the LAN shared with your friend. No VPN is required on a reachable LAN. The game and relay need UDP 7777 and 7778 allowed through Windows Firewall.",
                2 => "Enter an IPv4 address your friend can reach. Internet play needs existing routing/port forwarding for BOTH UDP 7777 (game) and 7778 (input). Limelight does not change firewall or router settings.",
                3 => "The game will create a private Steam lobby and carry world/input packets over Steam. No VPN is requested. Both players need matching builds and player modes; two-account gameplay is not yet certified.",
                _ => "Uses Tailscale when connected; otherwise the code works only on this PC. For two PCs on the same LAN, choose Local network."
            };
        }

        private void Join_Click(
            object sender,
            RoutedEventArgs e)
        {
            JoinRequested?.Invoke(new MultiplayerJoinOptions
            {
                FriendCode = FriendCodeTextBox.Text,
                NativePlayerMode = NativePlayerModeCheckBox.IsChecked == true
            });
        }

        private void Stop_Click(
            object sender,
            RoutedEventArgs e)
        {
            StopRequested?.Invoke();
        }

        private void Verify_Click(
            object sender,
            RoutedEventArgs e)
        {
            VerifyRequested?.Invoke();
        }

        private void Remove_Click(
            object sender,
            RoutedEventArgs e)
        {
            RemoveRequested?.Invoke();
        }

        private void CopyCode_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                    FriendCodeText.Text))
            {
                return;
            }

            Clipboard.SetText(
                FriendCodeText.Text);

            AddLog(
                MultiplayerLogLevel.Network,
                "Friend code copied to the clipboard.");
        }
    }
}
