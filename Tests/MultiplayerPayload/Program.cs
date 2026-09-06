using Limelight.Models;
using Limelight.Services;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

int passed = 0;
List<string> failures = new();
LimelightMpPayloadService service = new();

void Check(string name, Action assertion)
{
    try
    {
        assertion();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures.Add(name);
        Console.WriteLine($"FAIL {name}: {error.GetType().Name}: {error.Message}");
    }
}

void Expect(bool condition, string message = "Contract did not hold.")
{
    if (!condition) throw new InvalidOperationException(message);
}

void Reject(Action action, string expectedMessage)
{
    try { action(); }
    catch (InvalidOperationException error)
    {
        Expect(error.Message.Contains(expectedMessage, StringComparison.OrdinalIgnoreCase), error.Message);
        return;
    }
    throw new InvalidOperationException("The installer accepted invalid input.");
}

if (args.Length == 2 && args[0] == "--expect-invalid-payload")
{
    Check("altered assembly rejects payload before any installation", () =>
        Reject(service.ValidateEmbeddedPayloads, args[1]));
    Console.WriteLine($"{passed} passed; {failures.Count} failed.");
    return failures.Count == 0 ? 0 : 1;
}
bool stagingRollback = args.Length == 1 && args[0] == "--expect-staging-rollback";
if (args.Length != 0 && !stagingRollback)
{
    Console.Error.WriteLine("Usage: MultiplayerPayload [--expect-invalid-payload expected-message | --expect-staging-rollback]");
    return 2;
}

Check("all embedded payload sizes and hashes validate", service.ValidateEmbeddedPayloads);
if (failures.Count != 0)
{
    Console.Error.WriteLine("Rebuild and sync the production payload before running installer checks.");
    return 1;
}

MultiplayerPayloadManifest manifest = service.Manifest;
Check("manifest matches runtime version and protocol", () =>
{
    Expect(manifest.Version == LimelightMpPayloadService.Version);
    Expect(manifest.ProtocolVersion == LimelightMpPayloadService.ProtocolVersion);
    Expect(manifest.SteamTransportSupported && manifest.NativePlayerModeSupported,
        "The final package must advertise the capabilities exercised by these installer checks.");
});

byte[] Resource(MultiplayerPayloadFile entry)
{
    using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(entry.ResourceName)!;
    using MemoryStream bytes = new();
    stream.CopyTo(bytes);
    return bytes.ToArray();
}

void AssertPayloadFile(string path, MultiplayerPayloadFile entry)
{
    byte[] actual = File.ReadAllBytes(path);
    Expect(actual.LongLength == entry.Size, "Installed size mismatch.");
    Expect(Convert.ToHexString(SHA256.HashData(actual)) == entry.Sha256.ToUpperInvariant(), "Installed hash mismatch.");
    Expect(actual.SequenceEqual(Resource(entry)), "Installed resource bytes differ.");
}

void AssertScripts(FakeGame game, MultiplayerRole role, bool steam, string address, int gamePort)
{
    string script = File.ReadAllText(Path.Combine(game.ModDirectory, "Scripts", "main.lua"));
    Expect(script.Contains("local MOD_NAME = \"LimelightMP\""));
    Expect(script.Contains($"local CONNECT_ADDRESS = \"{address}\" -- INSTALL_CONNECT_ADDRESS"));
    Expect(script.Contains("local NETWORK_PLAYER_MODE = \"compatibility\" -- INSTALL_NETWORK_PLAYER_MODE"));
    Expect(script.Contains($"local STEAM_SESSION = {(steam ? "true" : "false")} -- INSTALL_STEAM_SESSION"));
    if (role == MultiplayerRole.Host)
        Expect(script.Contains($"local LISTEN_PORT = {gamePort} -- INSTALL_LISTEN_PORT"));
    AssertPayloadFile(Path.Combine(game.ModDirectory, "Scripts", "native_ownership.lua"), manifest.NativeOwnershipScript);
    AssertPayloadFile(Path.Combine(game.ModDirectory, "Scripts", "steam_gate.lua"), manifest.SteamGateScript);
}

void AssertInstalledFiles(FakeGame game, MultiplayerRole role)
{
    AssertPayloadFile(Path.Combine(game.NativeDirectory, "dlls", "main.dll"), manifest.NativeBridge);
    AssertPayloadFile(Path.Combine(game.LogicModsDirectory, "LimelightMPUI.pak"), manifest.UiPak);
    AssertPayloadFile(Path.Combine(game.LogicModsDirectory, "LimelightMPUI.utoc"), manifest.UiUtoc);
    AssertPayloadFile(Path.Combine(game.LogicModsDirectory, "LimelightMPUI.ucas"), manifest.UiUcas);
    Expect(File.Exists(Path.Combine(game.NativeDirectory, ".limelightmp-native-managed")));
    Expect(File.Exists(Path.Combine(game.NativeDirectory, "enabled.txt")));
    Expect(File.Exists(Path.Combine(game.NativeDirectory, "client-mode.txt")) == (role == MultiplayerRole.Client));
    Expect(!File.Exists(Path.Combine(game.NativeDirectory, "native-player-mode.txt")));
    Expect(File.ReadAllText(Path.Combine(game.LogicModsDirectory, ".limelightmp-ui-managed")) == manifest.Version);
    MultiplayerInstalledRole installed = service.ReadInstalledRole(game.Installation)!;
    Expect(installed.Role == role && installed.Version == manifest.Version && !installed.NativePlayerMode);
}

if (stagingRollback)
{
    Check("early staging failure preserves the previous managed installation", () =>
    {
        using FakeGame game = new();
        service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true);
        var before = game.Snapshot();
        Reject(() => service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777", 7777),
            "missing its installation markers");
        game.AssertSnapshot(before);
        Expect(service.ReadInstalledRole(game.Installation)!.Role == MultiplayerRole.Client);
        AssertInstalledFiles(game, MultiplayerRole.Client);
    });
    Console.WriteLine($"{passed} passed; {failures.Count} failed.");
    return failures.Count == 0 ? 0 : 1;
}

Check("host direct installation preserves baseline and ships both Lua modules", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Host, "192.168.1.20:7777", 7777);
    AssertScripts(game, MultiplayerRole.Host, false, "192.168.1.20:7777", 7777);
    AssertInstalledFiles(game, MultiplayerRole.Host);
    game.AssertBaseline(includeModsFile: false);
    string[] mods = File.ReadAllLines(game.ModsFile);
    Expect(mods.Count(line => line == "LimelightMP : 1") == 1);
    Expect(mods.Contains("KeepThisMod : 1"));
});

Check("Steam host and client installs retain explicit tunnel and role markers", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777", 7777, useSteam: true);
    AssertScripts(game, MultiplayerRole.Host, true, "127.0.0.1:7777", 7777);
    AssertInstalledFiles(game, MultiplayerRole.Host);
    service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true);
    AssertScripts(game, MultiplayerRole.Client, true, "127.0.0.1:7779", 7779);
    AssertInstalledFiles(game, MultiplayerRole.Client);
    Expect(service.ReadInstalledRole(game.Installation)!.Port == 7779);
    game.AssertBaseline(includeModsFile: false);
});

Check("switching from Steam client to direct host clears stale native session files", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true);
    foreach (string file in new[] { "steam-session.ini", "steam-status.json", "steam-ready.txt", "steam-stop.txt" })
        File.WriteAllText(Path.Combine(game.NativeDirectory, file), "old session");
    service.Install(game.Installation, MultiplayerRole.Host, "192.168.1.20:7777", 7777);
    AssertScripts(game, MultiplayerRole.Host, false, "192.168.1.20:7777", 7777);
    AssertInstalledFiles(game, MultiplayerRole.Host);
    foreach (string file in new[] { "steam-session.ini", "steam-status.json", "steam-ready.txt", "steam-stop.txt" })
        Expect(!File.Exists(Path.Combine(game.NativeDirectory, file)));
});

foreach (bool steam in new[] { false, true })
{
    Check($"unknown native-player executable rejected before mutation (Steam={steam})", () =>
    {
        using FakeGame game = new();
        service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777", 7777);
        var before = game.Snapshot();
        Reject(() => service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779,
            nativePlayerMode: true, useSteam: steam), "not been mapped");
        game.AssertSnapshot(before);
    });
}

foreach (string blocked in new[] { "role", "native", "UI" })
{
    Check($"unmanaged {blocked} files are never adopted or overwritten", () =>
    {
        using FakeGame game = new();
        string path = blocked switch
        {
            "role" => Path.Combine(game.ModDirectory, "user-owned.txt"),
            "native" => Path.Combine(game.NativeDirectory, "user-owned.txt"),
            _ => Path.Combine(game.LogicModsDirectory, "LimelightMPUI.pak")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "user-owned fixture");
        var before = game.Snapshot();
        Reject(() => service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777", 7777),
            blocked == "UI" ? "unmanaged" : "non-managed");
        game.AssertSnapshot(before);
    });
}

Check("partial UI backup failure restores moved files without deleting untouched originals", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Host, "192.168.1.20:7777", 7777);
    var before = game.Snapshot();
    bool rejected = false;
    using (FileStream locked = new(Path.Combine(game.LogicModsDirectory, "LimelightMPUI.utoc"),
        FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true); }
        catch (IOException) { rejected = true; }
        catch (UnauthorizedAccessException) { rejected = true; }
    }
    Expect(rejected, "The locked UI table did not stop the partial backup.");
    game.AssertSnapshot(before);
    Expect(service.ReadInstalledRole(game.Installation)!.Role == MultiplayerRole.Host);
});

Check("late mods-file failure rolls role, native bridge and UI back together", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Host, "192.168.1.20:7777", 7777);
    var before = game.Snapshot();
    bool rejected = false;
    using (FileStream locked = new(game.ModsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        try { service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true); }
        catch (IOException) { rejected = true; }
        catch (UnauthorizedAccessException) { rejected = true; }
    }
    Expect(rejected, "The locked mods file did not stop final publication.");
    game.AssertSnapshot(before);
    Expect(service.ReadInstalledRole(game.Installation)!.Role == MultiplayerRole.Host);
});

Check("remove preflight refuses unmanaged native folder before deleting any managed component", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777", 7777);
    File.Delete(Path.Combine(game.NativeDirectory, ".limelightmp-native-managed"));
    File.WriteAllText(Path.Combine(game.NativeDirectory, "user-owned.txt"), "not managed anymore");
    var before = game.Snapshot();
    Reject(() => service.Remove(game.Installation), "unmanaged native");
    game.AssertSnapshot(before);
});

Check("deactivate and managed removal preserve unrelated files and loader state", () =>
{
    using FakeGame game = new();
    service.Install(game.Installation, MultiplayerRole.Client, "127.0.0.1:7779", 7779, useSteam: true);
    service.Deactivate(game.Installation);
    Expect(File.ReadAllLines(game.ModsFile).Contains("LimelightMP : 0"));
    Expect(!File.Exists(Path.Combine(game.NativeDirectory, "enabled.txt")));
    Expect(File.Exists(Path.Combine(game.NativeDirectory, "enabled.disabled-by-limelightmp")));
    service.Remove(game.Installation);
    Expect(!Directory.Exists(game.ModDirectory) && !Directory.Exists(game.NativeDirectory));
    Expect(!File.Exists(Path.Combine(game.LogicModsDirectory, "LimelightMPUI.pak")));
    Expect(!File.Exists(Path.Combine(game.LogicModsDirectory, ".limelightmp-ui-managed")));
    game.AssertBaseline(includeModsFile: true);
    Expect(service.ReadInstalledRole(game.Installation) is null);
});

Check("invalid role or injected address is rejected without touching files", () =>
{
    using FakeGame game = new();
    var before = game.Snapshot();
    Reject(() => service.Install(game.Installation, MultiplayerRole.None, "127.0.0.1:7777", 7777), "choose a host");
    Reject(() => service.Install(game.Installation, MultiplayerRole.Host, "127.0.0.1:7777\n\"", 7777), "address is invalid");
    game.AssertSnapshot(before);
});

Console.WriteLine($"{passed} passed; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;

sealed class FakeGame : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "fake-game-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _baseline;
    public Ue4ssDetectionResult Installation { get; }
    public string ModDirectory => Path.Combine(Installation.ModsDirectory, "LimelightMP");
    public string NativeDirectory => Path.Combine(Installation.ModsDirectory, "LimelightMPNative");
    public string ModsFile => Path.Combine(Installation.ModsDirectory, "mods.txt");
    public string LogicModsDirectory => Path.Combine(_root, "Pagoda", "Content", "Paks", "LogicMods");

    public FakeGame()
    {
        string win64 = Path.Combine(_root, "Pagoda", "Binaries", "Win64");
        string working = Path.Combine(win64, "ue4ss");
        string mods = Path.Combine(working, "Mods");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(LogicModsDirectory);
        Directory.CreateDirectory(Path.Combine(mods, "LimelightNativeBridge", "dlls"));
        Write(Path.Combine(win64, "PagodaSteam-Win64-Shipping.exe"), "deliberately not a game executable");
        Write(Path.Combine(win64, "dwmapi.dll"), "proxy fixture");
        Write(Path.Combine(working, "UE4SS.dll"), "loader fixture");
        Write(Path.Combine(working, "UE4SS-settings.ini"), "[General]\nConsoleEnabled=0\n");
        Write(Path.Combine(mods, "LimelightNativeBridge", "dlls", "main.dll"), "general bridge fixture");
        Write(Path.Combine(LogicModsDirectory, "UnrelatedMod.pak"), "unrelated asset fixture");
        File.WriteAllLines(Path.Combine(mods, "mods.txt"), ["; baseline loader file", "KeepThisMod : 1"], new UTF8Encoding(false));
        Installation = new Ue4ssDetectionService().Detect(_root);
        if (!Installation.IsInstalled) throw new InvalidOperationException("Fake loader layout was not detected.");
        _baseline = Snapshot();
    }

    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    public Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    public void AssertSnapshot(Dictionary<string, string> expected)
    {
        Dictionary<string, string> actual = Snapshot();
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out string? hash) || hash != pair.Value))
            throw new InvalidOperationException("The fake game's file snapshot changed unexpectedly.");
    }

    public void AssertBaseline(bool includeModsFile)
    {
        Dictionary<string, string> current = Snapshot();
        string modsKey = Path.GetRelativePath(_root, ModsFile);
        foreach (var pair in _baseline)
        {
            if (!includeModsFile && pair.Key == modsKey) continue;
            if (!current.TryGetValue(pair.Key, out string? hash) || hash != pair.Value)
                throw new InvalidOperationException("An unrelated baseline file changed: " + pair.Key);
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
