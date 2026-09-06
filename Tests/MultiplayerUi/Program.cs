using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Limelight;
using Limelight.Models;
using Limelight.Views;

internal static class Program
{
    private const string RequestId = "11111111111111111111111111111111";
    private const string SampleInvitation = "LS1-109212290963701761-11111111111111111111111111111111";
    private static int _checks;

    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new App();
            // I fail before splash creation if somebody later adds a dispatcher pump here.
            app.Startup += (_, _) => throw new InvalidOperationException("This test must never start the application.");
            app.InitializeComponent();
            Check(app.Resources.Contains("BackgroundBrush"), "Normal application resources loaded");

            var page = new MultiplayerPage();
            page.AddLog(MultiplayerLogLevel.Warning, "SIMULATED UI TEST: invitation and packet counters are synthetic; no game or connection was started.");
            var routes = Control<ComboBox>(page, "HostRouteComboBox");
            var native = Control<CheckBox>(page, "NativePlayerModeCheckBox");
            var host = Control<Button>(page, "HostButton");
            var join = Control<Button>(page, "JoinButton");
            var steam = Control<ComboBoxItem>(page, "SteamRouteItem");
            SetReadiness(page, true, true);
            Check(host.IsEnabled && join.IsEnabled, "Verified readiness enables host and join");
            Check(steam.Visibility == Visibility.Visible && native.IsEnabled && native.IsChecked == false,
                "Supported Steam is visible and native ownership remains opt-in");

            routes.SelectedIndex = 3;
            native.IsChecked = true;
            Check(Control<TextBlock>(page, "HostRouteNoticeText").Text.Contains("No VPN"), "Steam route explains its VPN-free path");
            Check(Control<ComboBox>(page, "LocalAddressComboBox").Visibility == Visibility.Collapsed &&
                Control<TextBox>(page, "DirectAddressTextBox").Visibility == Visibility.Collapsed,
                "Steam route hides direct-address controls");
            MultiplayerHostOptions? hostOptions = null;
            page.HostRequested += options => hostOptions = options;
            host.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(hostOptions is { Route: MultiplayerHostRoute.Steam, NativePlayerMode: true }, "Host event carries Steam and native choices");
            MultiplayerJoinOptions? joinOptions = null;
            page.JoinRequested += options => joinOptions = options;
            Control<TextBox>(page, "FriendCodeTextBox").Text = SampleInvitation;
            join.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(joinOptions is { NativePlayerMode: true } && joinOptions.FriendCode == SampleInvitation,
                "Join event carries invitation and native choice without starting a service");
            Render(page, "01-ready-steam-native.png");

            page.SetBusy(true, "UI test: preparing session");
            Check(!host.IsEnabled && !join.IsEnabled && !routes.IsEnabled && !native.IsEnabled,
                "Busy state locks launch and configuration controls");
            page.SetBusy(false, "");
            SetReadiness(page, false, false);
            Check(steam.Visibility == Visibility.Collapsed && routes.SelectedIndex == 0,
                "Unsupported Steam is hidden and selection returns to automatic");
            Check(!native.IsEnabled && native.IsChecked == false, "Unsupported native ownership is disabled and unchecked");
            page.ShowReadiness(true, true, true, true, null, null, ["192.0.2.10"], true, true);
            Check(!host.IsEnabled && !join.IsEnabled && Control<TextBlock>(page, "ReadinessTitleText").Text.Contains("CLOSE THE GAME"),
                "A running game blocks role changes");
            SetReadiness(page, true, true);
            routes.SelectedIndex = 3;
            native.IsChecked = true;

            page.ShowSession(new MultiplayerStartResult
            {
                Role = MultiplayerRole.Host,
                UsesSteam = true,
                NativePlayerMode = true,
                SteamRequestId = RequestId,
                GamePort = 7777,
                InputPort = 7778,
                RouteDescription = "Steam transport",
                RouteNotice = "Synthetic UI test. No connection was started."
            });
            Check(Control<Border>(page, "ActiveSessionCard").Visibility == Visibility.Visible &&
                Control<Border>(page, "FriendCodePanel").Visibility == Visibility.Collapsed,
                "Starting host session is visible without inventing an invitation");
            Check(!host.IsEnabled && !join.IsEnabled && !routes.IsEnabled && !native.IsEnabled,
                "Active session locks launch and configuration controls");
            string startingTitle = Control<TextBlock>(page, "SessionTitleText").Text;
            page.ShowSteamStatus(new MultiplayerSteamStatus { RequestId = "stale-request", State = "connected", TransportReady = true });
            Check(Control<TextBlock>(page, "SessionTitleText").Text == startingTitle, "A different request cannot overwrite the current session");

            page.ShowSteamStatus(Status("lobby_ready", false));
            Check(Control<TextBlock>(page, "SessionTitleText").Text.Contains("INVITATION READY") &&
                Control<Border>(page, "FriendCodePanel").Visibility == Visibility.Visible,
                "Host lobby status reveals the supplied invitation");
            Render(page, "02-host-invitation.png", true);
            page.ShowSteamStatus(Status("connected", true));
            Check(Control<TextBlock>(page, "SessionEyebrowText").Text.Contains("GAMEPLAY UNVERIFIED") &&
                Control<TextBlock>(page, "SessionTitleText").Text.Contains("TEST GAMEPLAY NEXT"),
                "Transport readiness retains the gameplay certification boundary");
            Check(Control<TextBlock>(page, "SessionDetailText").Text.Contains("sent 123, received 120"),
                "Steam status renders packet counters");
            Render(page, "03-host-connected.png", true);
            page.ShowSteamStatus(Status("failed", false));
            Check(Control<TextBlock>(page, "SessionTitleText").Text.Contains("STOP, THEN RETRY") &&
                Control<Border>(page, "FriendCodePanel").Visibility == Visibility.Collapsed &&
                Control<TextBlock>(page, "FriendCodeText").Text.Length == 0,
                "Failed transport removes the invitation and shows recovery guidance");
            Render(page, "04-host-failed.png", true);

            page.ShowIdle();
            Check(Control<Border>(page, "ActiveSessionCard").Visibility == Visibility.Collapsed && host.IsEnabled && join.IsEnabled,
                "Stopping returns to idle launch controls");
            page.ShowSession(new MultiplayerStartResult
            {
                Role = MultiplayerRole.Client,
                UsesSteam = true,
                NativePlayerMode = true,
                SteamRequestId = RequestId,
                Address = "127.0.0.1",
                GamePort = 7777,
                RouteDescription = "Steam transport",
                RouteNotice = "Synthetic UI test. No connection was started."
            });
            page.ShowSteamStatus(Status("connected", true));
            Check(Control<Border>(page, "FriendCodePanel").Visibility == Visibility.Collapsed &&
                !Control<TextBlock>(page, "SessionDetailText").Text.Contains("Ctrl+Shift+F5"),
                "Connected client does not get host invitation or host-start instructions");
            Render(page, "05-client-connected.png", true);
            Check(app.Windows.Count == 0 && app.MainWindow is null, "No application window or launcher startup was created");
            Console.WriteLine($"PASS {_checks} UI checks. No game, network, install, clipboard, or desktop actions.");
            Console.WriteLine($"Assembly: {typeof(MultiplayerPage).Assembly.Location}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void SetReadiness(MultiplayerPage page, bool steam, bool native) =>
        page.ShowReadiness(true, false, true, true, null, null, ["192.0.2.10"], steam, native);

    private static MultiplayerSteamStatus Status(string state, bool ready) => new()
    {
        RequestId = RequestId,
        State = state,
        TransportReady = ready,
        NativePlayerMode = true,
        FriendCode = SampleInvitation,
        WorldSent = 123,
        WorldReceived = 120,
        InputSent = 0,
        InputReceived = 0,
        Error = state == "failed" ? "Synthetic peer disconnected for this UI test." : ""
    };

    private static T Control<T>(MultiplayerPage page, string name) where T : FrameworkElement =>
        page.FindName(name) as T ?? throw new InvalidOperationException($"Missing {typeof(T).Name}: {name}");

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine($"PASS {description}");
    }

    private static void Render(MultiplayerPage page, string name, bool session = false)
    {
        const int width = 1100;
        const int height = 900;
        page.Measure(new Size(width, height));
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();
        var scroll = ((Grid)page.Content).Children.OfType<ScrollViewer>().Single();
        if (session)
        {
            var card = Control<Border>(page, "ActiveSessionCard");
            var content = (UIElement)scroll.Content;
            scroll.ScrollToVerticalOffset(card.TranslatePoint(new Point(0, 0), content).Y - 12);
        }
        else
        {
            scroll.ScrollToTop();
        }
        page.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        string directory = Path.Combine(AppContext.BaseDirectory, "renders");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"RENDER {path}");
    }

}
