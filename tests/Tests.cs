using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DiscUtils.Iso9660;
using OpenProStreetLauncher;

static class Tests
{
    static int s_failed;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
        if (!ok) s_failed++;
    }

    static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "opsl-linux-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            IniTests(root);
            TexturePackTests(root);
            DxvkTests(root);
            HdReflectionsTests(root);
            if (OperatingSystem.IsWindows()) WindowsInstallTests(root);
            SteamTests(root);
            PrefixTests(root);
            DiscTests(root);
            GameTests(root);
            SelfInstallTests(root);
            DiagnosticsTests(root);
        }
        catch (Exception e)
        {
            Console.WriteLine("  FAIL  " + e);
            s_failed++;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
        Console.WriteLine(s_failed == 0 ? "ALL TESTS PASSED" : s_failed + " TEST(S) FAILED");
        return s_failed == 0 ? 0 : 1;
    }

    // Installing into <game>.launcher and back out. Left alone when the PC already has the Start
    // menu entry, so a real install isn't touched.
    static void WindowsInstallTests(string root)
    {
        Console.WriteLine("Windows install");
        if (File.Exists(WindowsInstall.ShortcutPath))
        {
            Console.WriteLine("  skip  the launcher is installed on this PC");
            return;
        }
        string game = Path.Combine(root, "wingame");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "nfs.exe"), "MZ");
        Check(!WindowsInstall.IsInstalled(game), "a game folder starts without the launcher");
        WindowsInstall.Install(game);
        string exe = WindowsInstall.InstalledExe(game);
        Check(File.Exists(exe) && new FileInfo(exe).Length == new FileInfo(Environment.ProcessPath).Length, "install copies the running launcher into .launcher");
        Check((new DirectoryInfo(WindowsInstall.InstallDir(game)).Attributes & FileAttributes.Hidden) != 0, "the .launcher folder is hidden");
        Check(File.Exists(WindowsInstall.ShortcutPath), "install adds the Start menu entry");
        Check(WindowsInstall.IsInstalled(game) && !WindowsInstall.IsRunningInstalled(game), "it knows it's installed, and that this isn't the installed copy");
        WindowsInstall.Install(game);
        Check(File.Exists(exe), "installing again replaces the copy");
        WindowsInstall.Uninstall(game);
        Check(!Directory.Exists(WindowsInstall.InstallDir(game)) && !File.Exists(WindowsInstall.ShortcutPath), "uninstall removes the folder and the Start menu entry");
        Check(File.Exists(Path.Combine(game, "nfs.exe")), "uninstall leaves the game alone");
    }

    static void IniTests(string root)
    {
        Console.WriteLine("Client settings");
        string file = Path.Combine(root, "game", "scripts", "OpenProStreetClient.ini");
        ClientSettings fresh = ClientSettings.Load(file);
        Check(fresh.Address == "weexnes.dev" && fresh.NoSpeedLimiter && !fresh.Borderless, "a missing file gives the client's defaults");

        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, "; my notes\r\n[server]\r\naddress = play.example.com\r\n\r\n[Display]\r\nBorderless=1\r\nSomethingElse=7\r\n");
        ClientSettings s = ClientSettings.Load(file);
        Check(s.Address == "play.example.com" && s.Borderless, "sections and keys match in any case, around spaces");
        s.Port = 12345;
        s.NoTankSlapper = false;
        s.Save(file);
        string text = File.ReadAllText(file);
        Check(text.StartsWith("; my notes\r\n") && text.Contains("SomethingElse=7"), "comments and unknown keys are kept");
        Check(text.Contains("Port=12345") && text.Contains("NoTankSlapper=0") && !text.Contains("\n\n[Physics]\r\n\r\n"), "new keys are added: " + text.Replace("\r\n", "|"));
        Check(text.IndexOf("Port=12345", StringComparison.Ordinal) < text.IndexOf("[Display]", StringComparison.Ordinal), "a new key goes in its section");
        ClientSettings again = ClientSettings.Load(file);
        Check(again.Port == 12345 && !again.NoTankSlapper && again.Address == "play.example.com", "what was saved reads back");

        string lan = Path.Combine(root, "lan.ini");
        File.WriteAllText(lan, "[Server]\nAddress=\n");
        Check(ClientSettings.Load(lan).Address == "", "an empty Address means search the LAN");

        Console.WriteLine("Overlay");
        Check(fresh.Overlay && fresh.OverlayNotifications && fresh.OverlayScoreboard && fresh.OverlayPing &&
              fresh.OverlayChat && fresh.OverlayLobbyCars && fresh.OverlayPaceBar &&
              fresh.OverlayPosition == "TopRight" && fresh.OverlayScale == 100 &&
              fresh.OverlayToggleKey == "F3" && fresh.OverlayScoreboardKey == "Tab" && fresh.OverlaySettingsKey == "F4",
              "the overlay, notifications, scoreboard, ping, chat, lobby cars, pace bar and hotkeys have the right defaults");
        string overlay = Path.Combine(root, "overlay.ini");
        File.WriteAllText(overlay, "[overlay]\r\nenabled = 0\r\nNotifications=1\r\nScoreboard=0\r\nPing=0\r\nChat=0\r\nLobbyCars=0\r\nPaceBar=0\r\nPosition=TopLeft\r\nScale=150\r\nToggleKey=F5\r\nScoreboardKey=Insert\r\nSettingsKey=F6\r\nTest=1\r\n");
        ClientSettings o = ClientSettings.Load(overlay);
        Check(!o.Overlay && o.OverlayNotifications && !o.OverlayScoreboard && !o.OverlayPing &&
              !o.OverlayChat && !o.OverlayLobbyCars && !o.OverlayPaceBar &&
              o.OverlayPosition == "TopLeft" && o.OverlayScale == 150 &&
              o.OverlayToggleKey == "F5" && o.OverlayScoreboardKey == "Insert" && o.OverlaySettingsKey == "F6",
              "[Overlay] expanded keys are read");
        o.Overlay = true;
        o.OverlayNotifications = false;
        o.OverlayScoreboard = true;
        o.OverlayPing = false;
        o.OverlayChat = true;
        o.OverlayPosition = "BottomRight";
        o.OverlayScale = 125;
        o.Save(overlay);
        string overlayText = File.ReadAllText(overlay);
        Check(overlayText.Contains("Notifications=0") && overlayText.Contains("Scoreboard=1") && overlayText.Contains("Ping=0") &&
              overlayText.Contains("Chat=1") && overlayText.Contains("Position=BottomRight") && overlayText.Contains("Scale=125") &&
              overlayText.Contains("ToggleKey=F5") && overlayText.Contains("ScoreboardKey=Insert") && overlayText.Contains("SettingsKey=F6") &&
              !overlayText.Contains("enabled = 0") && ClientSettings.Load(overlay).Overlay,
              "they're saved under [Overlay]: " + overlayText.Replace("\r\n", "|"));
        Check(overlayText.Contains("Test=1"), "the client's other overlay keys are kept");
        ClientSettings o2 = ClientSettings.Load(overlay);
        Check(o2.Overlay && !o2.OverlayNotifications && o2.OverlayScoreboard && !o2.OverlayPing &&
              o2.OverlayChat && !o2.OverlayLobbyCars && !o2.OverlayPaceBar &&
              o2.OverlayPosition == "BottomRight" && o2.OverlayScale == 125 &&
              o2.OverlayToggleKey == "F5" && o2.OverlayScoreboardKey == "Insert" && o2.OverlaySettingsKey == "F6",
              "what was saved reads back");
        string newOverlay = Path.Combine(root, "overlay-new.ini");
        new ClientSettings().Save(newOverlay);
        string newText = File.ReadAllText(newOverlay);
        Check(newText.Contains("[Overlay]") && newText.Contains("Enabled=1") && newText.Contains("Notifications=1") &&
              newText.Contains("Scoreboard=1") && newText.Contains("Ping=1") && newText.Contains("Chat=1") &&
              newText.Contains("SettingsKey=F4") && newText.Contains("[Atmosphere]") && newText.Contains("[Camera]"),
              "a new file lists the overlay, atmosphere and camera keys with their defaults");

        Console.WriteLine("Atmosphere & Camera orbit");
        Check(fresh.AtmosphereEnabled && fresh.AtmosphereStart == "Day" &&
              !fresh.RightStickOrbit && fresh.OrbitBlockShift && !fresh.OrbitInvertX && !fresh.OrbitInvertY &&
              fresh.OrbitDeadzone == 30 && fresh.OrbitSnapTime == 50 && fresh.OrbitBackTime == 250,
              "atmosphere and camera orbit defaults match the client");
        string atmCam = Path.Combine(root, "atm_cam.ini");
        File.WriteAllText(atmCam, "[Atmosphere]\r\nEnabled=0\r\nStart=night\r\nKey=F8\r\n[Camera]\r\nRightStickOrbit=1\r\nOrbitBlockShift=0\r\nOrbitInvertX=1\r\nOrbitInvertY=1\r\nOrbitDeadzone=999\r\nOrbitSnapTime=120\r\nOrbitBackTime=450\r\n");
        ClientSettings ac = ClientSettings.Load(atmCam);
        Check(!ac.AtmosphereEnabled && ac.AtmosphereStart == "Night" &&
              ac.RightStickOrbit && !ac.OrbitBlockShift && ac.OrbitInvertX && ac.OrbitInvertY &&
              ac.OrbitDeadzone == 90 && ac.OrbitSnapTime == 120 && ac.OrbitBackTime == 450,
              "[Atmosphere] and [Camera] settings read and clamp cleanly");
        ac.AtmosphereEnabled = true;
        ac.AtmosphereStart = "Dusk";
        ac.OrbitDeadzone = 20;
        ac.OrbitInvertY = false;
        ac.Save(atmCam);
        string atmCamText = File.ReadAllText(atmCam);
        Check(atmCamText.Contains("OrbitInvertY=0") && !ClientSettings.Load(atmCam).OrbitInvertY &&
              ClientSettings.Load(atmCam).OrbitInvertX,
              "[Camera] OrbitInvertY round-trips independently of OrbitInvertX");
        Check(atmCamText.Contains("Key=F8") && atmCamText.Contains("Start=Dusk") && atmCamText.Contains("OrbitDeadzone=20"),
              "saving preserves unrelated Atmosphere keys and writes updated values");

        Console.WriteLine("Gearbox");
        Check(!fresh.ReShifted && fresh.ShiftStyle == 2 && fresh.SportMemory && fresh.CoastDownshift && fresh.QuickShift && fresh.SkipGears && fresh.ShiftIndicator &&
              fresh.ShortShift == 28 && fresh.SportShift == 97 && fresh.EcoThrottle == 25 && fresh.SportThrottle == 85 && fresh.QuickShiftTime == 60,
              "ReShifted is off by default, with the client's defaults under it");
        string gearbox = Path.Combine(root, "gearbox.ini");
        File.WriteAllText(gearbox, "[Transmission]\r\nReShifted=1\r\n[ReShifted]\r\nStyle=1\r\nShortShiftFraction=999\r\nKickdownAggression=110\r\nLog=1\r\n");
        ClientSettings g = ClientSettings.Load(gearbox);
        Check(g.ReShifted && g.ShiftStyle == 1 && g.ShortShift == 55, "the master switch and style are read, and values are kept in range");
        g.ShiftStyle = 2;
        g.SportMemory = false;
        g.CoastDownshift = false;
        g.EcoThrottle = 40;
        g.Save(gearbox);
        string gearboxText = File.ReadAllText(gearbox);
        Check(gearboxText.Contains("KickdownAggression=110") && gearboxText.Contains("Log=1"), "the client's other gearbox keys are kept");
        ClientSettings g2 = ClientSettings.Load(gearbox);
        Check(g2.ReShifted && g2.ShiftStyle == 2 && !g2.SportMemory && !g2.CoastDownshift && g2.EcoThrottle == 40 && g2.ShortShift == 55, "what was saved reads back");

        Console.WriteLine("Automatic login");
        string account = Path.Combine(root, "account.ini");
        new ClientSettings { AutoLogin = true, Username = "Alice", Password = "p=ss/word" }.Save(account);
        string saved = File.ReadAllText(account);
        Check(saved.Contains("AutoLogin=1") && saved.Contains("Username=Alice"), "the account is saved under [Account]");
        Check(OperatingSystem.IsWindows() ? saved.Contains("Password=dpapi:") && !saved.Contains("p=ss/word") : saved.Contains("Password=p=ss/word"),
              "the password is encrypted on Windows, as typed elsewhere");
        ClientSettings loaded = ClientSettings.Load(account);
        Check(loaded.AutoLogin && loaded.Username == "Alice" && loaded.Password == "p=ss/word", "the saved login reads back");
        loaded.AutoLogin = false;
        loaded.Save(account);
        ClientSettings off = ClientSettings.Load(account);
        Check(!off.AutoLogin && off.Username == "Alice" && off.Password == "", "turning it off forgets the password");
        File.WriteAllText(account, "[Account]\r\nAutoLogin=1\r\nUsername=Alice\r\nPassword=dpapi:bm90IGEgcmVhbCBibG9i\r\n");
        Check(ClientSettings.Load(account).Password == "", "a password this user can't decrypt reads as empty");

        Console.WriteLine("Console window");
        string game = Path.Combine(root, "consolegame");
        Directory.CreateDirectory(Path.Combine(game, "scripts"));
        Check(ConsoleWindow.Shown(game) == null, "without nfs.exe there's no setting");
        // Like the 1.1 No-CD exe: marked as a console program, subsystem 3 (optional header + 68).
        byte[] exeBytes = SyntheticExe(lastRaw: 0x1000, trailing: 0);
        BitConverter.GetBytes((ushort)3).CopyTo(exeBytes, 0x80 + 24 + 68);
        string exePath = Path.Combine(game, "nfs.exe");
        File.WriteAllBytes(exePath, exeBytes);
        string multifix = "[MultiFix]\r\nPostRaceFix = 1\r\nFramerateUncap = 0\r\nEnablePrints = 1\t\t\t// Enables printing to the console.\r\nResDetect = 1 \t\t\t\t// Generates a resolution table\r\n\r\n[ResDetect]\r\nMonitorIndex = 0\r\n";
        string multifixPath = Path.Combine(game, "scripts", "XNFSPS-MultiFix.ini");
        File.WriteAllText(multifixPath, multifix);
        Check(ConsoleWindow.Shown(game) == true, "a console-subsystem exe shows the console");
        ConsoleWindow.Set(game, false);
        byte[] gui = File.ReadAllBytes(exePath);
        Check(ConsoleWindow.Subsystem(exePath) == 2 && ConsoleWindow.Shown(game) == false, "hiding it marks the exe as a Windows program");
        gui[0x80 + 24 + 68] = 3;
        Check(gui.SequenceEqual(exeBytes), "and changes nothing else in the exe");
        Check(File.ReadAllText(multifixPath) == multifix.Replace("EnablePrints = 1\t", "EnablePrints = 0\t"), "MultiFix's EnablePrints follows, keeping the spacing and comment");
        ConsoleWindow.Set(game, true);
        Check(File.ReadAllBytes(exePath).SequenceEqual(exeBytes) && File.ReadAllText(multifixPath) == multifix, "showing it again restores both files exactly");

        Console.WriteLine("Frame rate");
        Check(Framerate.Uncapped(game) == false, "FramerateUncap = 0 reads as capped");
        Framerate.Set(game, true);
        Check(Framerate.Uncapped(game) == true && File.ReadAllText(multifixPath) == multifix.Replace("FramerateUncap = 0", "FramerateUncap = 1"), "uncapping changes only that line");
        Framerate.Set(game, false);
        Check(File.ReadAllText(multifixPath) == multifix, "capping it again restores the file exactly");
        File.WriteAllText(multifixPath, "[MultiFix]\r\nPostRaceFix = 1\r\n");
        Check(Framerate.Uncapped(game) == true, "a missing key is uncapped, as MultiFix reads it");
        File.WriteAllText(multifixPath, multifix);
        Check(Framerate.Uncapped(Path.Combine(root, "nogame")) == null, "without a MultiFix ini there's no setting");

        string created = Path.Combine(root, "new", "scripts", "OpenProStreetClient.ini");
        new ClientSettings().Save(created);
        Check(File.ReadAllText(created).StartsWith("; OpenProStreet client settings"), "a new file starts with a note");
    }

    static void SteamTests(string root)
    {
        Console.WriteLine("Steam");
        string home = Path.Combine(root, "home");
        string steamRoot = Path.Combine(home, ".local", "share", "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steamRoot, "userdata", "12345", "config"));
        Directory.CreateDirectory(Path.Combine(steamRoot, "userdata", "0"));
        Steam steam = Steam.Find(home);
        Check(steam != null && steam.Root.EndsWith("Steam"), "Steam is found under ~/.local/share/Steam");
        Check(steam.AccountConfigDirs().Count == 1, "userdata/0 isn't an account");

        // A second library, and Proton builds in both.
        string library2 = Path.Combine(root, "games");
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steamRoot + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library2 + "\"\n\t}\n}\n");
        foreach (string dir in new[] { Path.Combine(steamRoot, "steamapps", "common", "Proton 9.0 (Beta)"), Path.Combine(library2, "steamapps", "common", "Proton - Experimental") })
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "proton"), "");
        }
        string ge = Path.Combine(steamRoot, "compatibilitytools.d", "GE-Proton9-20");
        Directory.CreateDirectory(ge);
        File.WriteAllText(Path.Combine(ge, "proton"), "");
        File.WriteAllText(Path.Combine(ge, "compatibilitytool.vdf"),
            "\"compatibilitytools\"\n{\n  \"compat_tools\"\n  {\n    \"GE-Proton9-20\" // the internal name\n    {\n      \"install_path\" \".\"\n      \"display_name\" \"GE-Proton9-20\"\n    }\n  }\n}\n");
        var tools = steam.ProtonTools();
        Check(tools.Count == 3, "three Proton builds are found (" + string.Join(", ", tools.Select(t => t.Name)) + ")");
        Check(tools[0].Name == "proton_experimental", "Proton Experimental is preferred");
        Check(tools.Any(t => t.Name == "proton_9") && tools.Any(t => t.Name == "GE-Proton9-20"), "Valve and custom builds get Steam's names for them");
        Check(Steam.ValveTool("Proton 4.11", "/x").Name == "proton_411", "older builds are named by both digits");

        // Shortcut
        string exe = "/home/me/.local/share/OpenProStreet/prefix/pfx/drive_c/Program Files (x86)/EA GAMES/Need for Speed ProStreet/nfs.exe";
        string shortcuts = Path.Combine(steamRoot, "userdata", "12345", "config", "shortcuts.vdf");
        VdfMap other = new VdfMap();
        other.Set("AppName", "Other Game");
        other.Set("Size", (ulong)42);
        VdfMap start = new VdfMap();
        start.Path("shortcuts").Set("0", other);
        byte[] original = BinaryVdf.Write(start);
        File.WriteAllBytes(shortcuts, original);
        Check(BinaryVdf.Write(BinaryVdf.Read(original)).SequenceEqual(original), "shortcuts.vdf reads and writes back byte for byte");
        steam.AddShortcut(exe, "/icon.png");
        steam.AddShortcut(exe, "/icon.png");
        VdfMap list = BinaryVdf.Read(File.ReadAllBytes(shortcuts)).GetMap("shortcuts");
        VdfMap ours = (VdfMap)list.Items[1].Value;
        Check(list.Items.Count == 2 && ((VdfMap)list.Items[0].Value).Get("Size") is ulong, "adding twice keeps one entry, and the other game is kept");
        Check(ours.GetString("Exe") == "\"" + exe + "\"" && ours.GetString("StartDir") == "\"" + Path.GetDirectoryName(exe) + "/\"", "it starts nfs.exe in the game folder");
        Check(ours.GetString("LaunchOptions") == "WINEDLLOVERRIDES=\"dinput8=n,b\" %command%", "it lets the ASI loader load");
        Check(File.Exists(shortcuts + ".openprostreet.bak"), "the old file is backed up");
        uint appId = Steam.ShortcutAppId(exe);
        Check(unchecked((uint)(int)ours.Get("appid")) == appId && (appId & 0x80000000) != 0, "its appid is Steam's");
        Check(Steam.RunUrl(appId) == "steam://rungameid/" + (((ulong)appId << 32) | 0x02000000UL), "the run link uses the 64-bit game id");

        // Proton for the shortcut, in a config.vdf that already has other settings.
        string config = Path.Combine(steamRoot, "config", "config.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(config));
        File.WriteAllText(config, "\"InstallConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"AutoUpdateWindowEnabled\"\t\t\"0\"\n\t\t\t\t\"CompatToolMapping\"\n\t\t\t\t{\n\t\t\t\t\t\"0\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"name\"\t\t\"proton_9\"\n\t\t\t\t\t\t\"config\"\t\t\"\"\n\t\t\t\t\t\t\"priority\"\t\t\"75\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n\t\"Path\"\t\t\"C:\\\\Games\"\n}\n");
        steam.SetCompatTool(appId, tools[0]);
        VdfMap cfg = TextVdf.Read(File.ReadAllText(config));
        VdfMap mapping = cfg.Path("InstallConfigStore", "Software", "Valve", "Steam", "CompatToolMapping");
        Check(mapping.GetMap(appId.ToString())?.GetString("name") == "proton_experimental", "Proton is turned on for the shortcut");
        Check(mapping.GetMap("0")?.GetString("priority") == "75" && cfg.Path("InstallConfigStore").GetString("Path") == "C:\\Games"
              && cfg.Path("InstallConfigStore", "Software", "Valve", "Steam").GetString("AutoUpdateWindowEnabled") == "0",
              "the rest of config.vdf is kept, backslashes too");

        if (OperatingSystem.IsLinux())
        {
            string prefix = Path.Combine(root, "prefix");
            Directory.CreateDirectory(prefix);
            string stray = steam.CompatDataDir(appId);
            Directory.CreateDirectory(stray);
            steam.LinkPrefix(appId, prefix);
            steam.LinkPrefix(appId, prefix);
            Check(new DirectoryInfo(stray).LinkTarget == prefix, "the shortcut's prefix links to the game's");
            Check(Directory.GetDirectories(Path.GetDirectoryName(stray)).Count(d => d.Contains(".backup-")) == 1, "a prefix Steam made first is kept as a backup");
        }
    }

    static void PrefixTests(string root)
    {
        Console.WriteLine("Wine prefix");
        string prefix = Path.Combine(root, "pfxtest");
        string game = Path.Combine(prefix, "pfx", "drive_c", "Games", "NFS");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "NFS.EXE"), "x");
        File.WriteAllText(Path.Combine(prefix, "pfx", "system.reg"),
            "WINE REGISTRY Version 2\n\n[Software\\\\Wow6432Node\\\\EA GAMES\\\\Need for Speed ProStreet] 1700000000\n#time=1\n\"Install Dir\"=\"C:\\\\Games\\\\NFS\"\n\"Language\"=\"English\"\n\n[Software\\\\Other] 1\n");
        Check(WinePrefix.ReadRegistryValue(File.ReadAllText(Path.Combine(prefix, "pfx", "system.reg")),
              @"Software\\Wow6432Node\\EA GAMES\\Need for Speed ProStreet", "Install Dir") == @"C:\Games\NFS", "the installer's registry entry is read");
        Check(WinePrefix.ToLinux(prefix, @"C:\Games\NFS") == game, "C: is the prefix's drive_c");
        Check(WinePrefix.ToLinux(prefix, @"Z:\home\me\Games") == "/home/me/Games", "Z: is the Linux root");
        Check(WinePrefix.FindGame(prefix) == game, "the game is found where the registry says, whatever case nfs.exe has");
        Check(Game.ExePath(game).EndsWith("NFS.EXE"), "nfs.exe is found in any case");

        string unregistered = Path.Combine(root, "pfx2");
        string elsewhere = Path.Combine(unregistered, "pfx", "drive_c", "Program Files (x86)", "Somewhere", "ProStreet");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "nfs.exe"), "x");
        Check(WinePrefix.FindGame(unregistered) == elsewhere, "without a registry entry, drive_c is searched");

        Console.WriteLine("Uninstalling the game");
        Check(GameUninstall.IsInLauncherPrefix(game, prefix), "a game in the prefix can be uninstalled");
        Check(!GameUninstall.IsInLauncherPrefix(Path.Combine(root, "pfxtest-other", "game"), prefix), "a folder next to the prefix isn't in it");
        Check(!GameUninstall.IsInLauncherPrefix(root, prefix), "the prefix's parent isn't in it");
        if (!OperatingSystem.IsWindows())
        {
            // Like dosdevices/z: pointing at /, a link out of the prefix is removed, not followed.
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep.txt"), "x");
            string dosdevices = Path.Combine(prefix, "pfx", "dosdevices");
            Directory.CreateDirectory(dosdevices);
            Directory.CreateSymbolicLink(Path.Combine(dosdevices, "z:"), outside);
            File.CreateSymbolicLink(Path.Combine(dosdevices, "file-link"), Path.Combine(outside, "keep.txt"));
            GameUninstall.DeleteTree(prefix);
            Check(!Directory.Exists(prefix), "the prefix is deleted");
            Check(File.Exists(Path.Combine(outside, "keep.txt")), "what its links point to is left alone");
        }
    }

    static void DiscTests(string root)
    {
        Console.WriteLine("Disc image");
        string iso = Path.Combine(root, "game.iso");
        CDBuilder builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "NFSPS" };
        builder.AddFile("AUTORUN.INF", Encoding.ASCII.GetBytes("[autorun]\r\nopen=Setup\\EASetup.exe\r\nicon=nfs.ico\r\n"));
        builder.AddFile(@"Setup\EASetup.exe", Encoding.ASCII.GetBytes("MZ installer"));
        builder.AddFile(@"Data\big.dat", new byte[300000]);
        builder.Build(iso);
        string target = Path.Combine(root, "disc");
        long last = 0, total = 0;
        Disc.Extract(iso, target, (done, all) => { last = done; total = all; });
        Check(File.Exists(Path.Combine(target, "Data", "big.dat")) && new FileInfo(Path.Combine(target, "Data", "big.dat")).Length == 300000, "the image's files are copied");
        Check(total == last && total > 300000, "progress reaches the total");
        string installer = Disc.FindInstaller(target);
        Check(installer != null && installer.EndsWith("EASetup.exe", StringComparison.OrdinalIgnoreCase), "autorun.inf names the installer (" + installer + ")");
        bool refused = false;
        File.WriteAllText(Path.Combine(root, "not.iso"), "hello");
        try { Disc.Extract(Path.Combine(root, "not.iso"), Path.Combine(root, "x")); } catch (InvalidDataException) { refused = true; }
        Check(refused, "a file that isn't a disc image is refused");
    }

    static void GameTests(string root)
    {
        Console.WriteLine("Game files");
        byte[] dll = new byte[400];
        // VS_FIXEDFILEINFO: signature, struct version, then file version MS and LS.
        BitConverter.GetBytes(0xFEEF04BDu).CopyTo(dll, 90); // a stray signature without the structure version
        BitConverter.GetBytes(0xFEEF04BDu).CopyTo(dll, 201); // the real one, not 4-byte aligned in the file
        BitConverter.GetBytes(0x00010000u).CopyTo(dll, 205);
        BitConverter.GetBytes((8u << 16) | 7u).CopyTo(dll, 209);
        BitConverter.GetBytes(0u).CopyTo(dll, 213);
        string path = Path.Combine(root, "client.asi");
        File.WriteAllBytes(path, dll);
        Check(Game.FileVersionOf(path) == new Version(8, 7, 0), "a dll's version is read on Linux");
        Check(!Game.IsSupportedExe(path), "a file that isn't nfs.exe 1.1 isn't supported");
        Check(Downloads.ParseVersion("8.10") > Downloads.ParseVersion("8.9"), "8.10 is newer than 8.9");

        Console.WriteLine("Fixing nfs.exe for Proton");
        // Laid out like the 1.1 No-CD exe: the last section's declared size stops at 0x7E2 into a
        // 0x1000 file alignment, and 0x66 bytes of code follow it.
        string exe = Path.Combine(root, "nfs.exe");
        byte[] pe = SyntheticExe(lastRaw: 0x7E2, trailing: 0x66);
        File.WriteAllBytes(exe, pe);
        Check(ExeFix.Needed(exe), "an exe with code past its last section needs the fix");
        Check(ExeFix.Apply(exe), "the fix is made");
        byte[] fixedExe = File.ReadAllBytes(exe);
        Check(fixedExe.Length == 0x3000, "the file is padded to the file alignment (0x" + fixedExe.Length.ToString("X") + ")");
        Check(fixedExe.AsSpan(0x400, pe.Length - 0x400).SequenceEqual(pe.AsSpan(0x400)) && fixedExe.AsSpan(pe.Length).ToArray().All(x => x == 0),
              "the code and data are unchanged, and the padding is zeros");
        int last = 0x80 + 24 + 0xE0 + 40;
        Check(BitConverter.ToUInt32(fixedExe, last + 16) == 0x1000 && BitConverter.ToUInt32(fixedExe, last + 8) == 0x1000,
              "the last section's raw and virtual sizes cover the whole file");
        Check(BitConverter.ToUInt32(fixedExe, 0x80 + 24 + 56) == 0x4000, "SizeOfImage covers the section");
        Check(File.ReadAllBytes(exe + ".before-proton-fix").SequenceEqual(pe), "the original is kept");
        Check(!ExeFix.Needed(exe) && !ExeFix.Apply(exe), "a fixed exe isn't changed again");

        string signed = Path.Combine(root, "signed.exe");
        File.WriteAllBytes(signed, SyntheticExe(lastRaw: 0x1000, trailing: 0x200));
        Check(!ExeFix.Needed(signed), "extra data after an aligned section, like a signature, is left alone");
    }

    static void SelfInstallTests(string root)
    {
        Console.WriteLine("Installing the launcher");
        string binary = Path.Combine(root, "Downloads", "OpenProStreetLauncher");
        Directory.CreateDirectory(Path.GetDirectoryName(binary));
        File.WriteAllBytes(binary, new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 2, 3 });

        InstallTarget user = InstallTarget.ForUser(Path.Combine(root, "home"));
        SelfInstall.Install(user, binary);
        SelfInstall.Install(user, binary);
        Check(File.ReadAllBytes(user.Binary).SequenceEqual(File.ReadAllBytes(binary)) && user.Binary.Replace('\\', '/').EndsWith("/.local/bin/openprostreet-launcher"),
              "the binary goes to ~/.local/bin as openprostreet-launcher");
        if (OperatingSystem.IsLinux())
            Check((File.GetUnixFileMode(user.Binary) & UnixFileMode.UserExecute) != 0, "and can be run");
        string entry = File.ReadAllText(user.DesktopFile);
        Check(entry.Contains("Exec=" + user.Binary + "\n") && entry.Contains("Icon=openprostreet-launcher\n") && entry.Contains("Categories=Game;"),
              "the menu entry starts it, with the icon by its theme name");
        Check(user.IconFile.Contains("hicolor") && File.Exists(user.IconFile) && new FileInfo(user.IconFile).Length > 1000, "the icon is in hicolor, where menus and rofi look");
        Check(!File.Exists(user.Binary + ".new"), "no temporary file is left");

        InstallTarget system = InstallTarget.ForSystem(Path.Combine(root, "sysroot"));
        Check(system.Binary.Replace('\\', '/').EndsWith("/usr/local/bin/openprostreet-launcher") && system.AppsDir.Replace('\\', '/').EndsWith("/usr/local/share/applications"),
              "for everyone, it goes under /usr/local");

        SelfInstall.Uninstall(user);
        Check(!File.Exists(user.Binary) && !File.Exists(user.DesktopFile) && !File.Exists(user.IconFile), "uninstall removes all three");
        Check(SelfInstall.Uninstall(user).StartsWith("Nothing to remove"), "uninstalling again says there's nothing there");
    }

    // A minimal PE32 with two sections, the second ending the file.
    static byte[] SyntheticExe(int lastRaw, int trailing)
    {
        byte[] b = new byte[0x2000 + lastRaw + trailing];
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(b, 0x3C);
        int pe = 0x80, opt = pe + 24, sections = opt + 0xE0;
        BitConverter.GetBytes(0x00004550u).CopyTo(b, pe);
        BitConverter.GetBytes((ushort)0x14C).CopyTo(b, pe + 4);
        BitConverter.GetBytes((ushort)2).CopyTo(b, pe + 6);
        BitConverter.GetBytes((ushort)0xE0).CopyTo(b, pe + 20);
        BitConverter.GetBytes((ushort)0x10B).CopyTo(b, opt);
        BitConverter.GetBytes(0x1000u).CopyTo(b, opt + 32);
        BitConverter.GetBytes(0x1000u).CopyTo(b, opt + 36);
        BitConverter.GetBytes((uint)(0x3000 + lastRaw)).CopyTo(b, opt + 56);
        void Section(int i, uint va, uint vsize, uint raw, uint rsize)
        {
            int s = sections + i * 40;
            BitConverter.GetBytes(vsize).CopyTo(b, s + 8);
            BitConverter.GetBytes(va).CopyTo(b, s + 12);
            BitConverter.GetBytes(rsize).CopyTo(b, s + 16);
            BitConverter.GetBytes(raw).CopyTo(b, s + 20);
        }
        Section(0, 0x1000, 0x2000, 0x1000, 0x1000);
        Section(1, 0x3000, (uint)lastRaw, 0x2000, (uint)lastRaw);
        for (int i = 0x1000; i < b.Length; i++) b[i] = (byte)(i * 7 + 1);
        return b;
    }

    // Texture packs: a small TexMod pack built here, unpacked into a game folder and removed again.
    static void TexturePackTests(string root)
    {
        Console.WriteLine("Texture packs");
        string game = Path.Combine(root, "texgame");
        Directory.CreateDirectory(Path.Combine(game, "scripts"));
        byte[] first = Encoding.ASCII.GetBytes("DDS first texture, a little longer so it has something to check");
        byte[] second = Encoding.ASCII.GetBytes("DDS second texture");
        string def = "0x50EC14EA|NFS.EXE_0x50EC14EA.dds\r\n0x0000BEEF|folder\\Renamed Texture.dds\r\n";
        string tpf = Path.Combine(root, "My Pack v1.tpf");
        File.WriteAllBytes(tpf, BuildTpf(("NFS.EXE_0x50EC14EA.dds", first), ("Renamed Texture.dds", second), ("texmod.def", Encoding.ASCII.GetBytes(def))));

        Check(TexturePacks.Installed(game).Count == 0, "a game starts without texture packs");
        TexturePacks.Pack pack = TexturePacks.Install(game, tpf);
        string dir = Path.Combine(TexturePacks.Root(game), "My Pack v1");
        Check(pack.Name == "My Pack v1" && pack.Textures == 2, "a .tpf unpacks into a folder named after it");
        Check(File.ReadAllBytes(Path.Combine(dir, "50EC14EA.dds")).SequenceEqual(first), "a texture is saved under the id in its name");
        Check(File.ReadAllBytes(Path.Combine(dir, "0000BEEF.dds")).SequenceEqual(second), "texmod.def gives the id when the file name doesn't");
        Check(!File.Exists(Path.Combine(dir, "texmod.def")), "texmod.def itself isn't copied");
        TexturePacks.Install(game, tpf);
        Check(TexturePacks.Installed(game).Count == 1, "installing the same pack again replaces it");

        string broken = Path.Combine(root, "broken.tpf");
        byte[] bytes = File.ReadAllBytes(tpf);
        bytes[70] ^= 0x55; // inside the first texture
        File.WriteAllBytes(broken, bytes);
        bool refused = false;
        try { TexturePacks.Install(game, broken); } catch (InvalidDataException) { refused = true; }
        Check(refused && TexturePacks.Installed(game).Count == 1 && !Directory.Exists(Path.Combine(TexturePacks.Root(game), ".new-broken")),
            "a damaged pack is refused and leaves nothing behind");
        File.WriteAllText(broken, "this is not a pack at all, just some text in a file");
        refused = false;
        try { TexturePacks.Install(game, broken); } catch (InvalidDataException) { refused = true; }
        Check(refused, "a file that isn't a pack is refused");

        TexturePacks.Remove(game, "My Pack v1");
        Check(TexturePacks.Installed(game).Count == 0 && !Directory.Exists(dir), "removing a pack deletes its folder");

        // A real pack, when one is named: OPS_TEST_TPF=<path to a .tpf>.
        string real = Environment.GetEnvironmentVariable("OPS_TEST_TPF");
        if (!string.IsNullOrEmpty(real) && File.Exists(real))
        {
            TexturePacks.Pack unpacked = TexturePacks.Install(game, real);
            Console.WriteLine("  info  " + unpacked.Name + ": " + unpacked.Textures + " textures, " + unpacked.Bytes / (1024 * 1024) + " MB");
            Check(unpacked.Textures > 0, "the real pack unpacks");
        }
    }

    // A TexMod pack: a zip with stored, encrypted entries, then every byte scrambled.
    static byte[] BuildTpf(params (string name, byte[] data)[] files)
    {
        byte[] password =
        {
            0x73, 0x2A, 0x63, 0x7D, 0x5F, 0x0A, 0xA6, 0xBD, 0x7D, 0x65, 0x7E, 0x67, 0x61, 0x2A, 0x7F, 0x7F, 0x74, 0x61, 0x67, 0x5B, 0x60,
            0x70, 0x45, 0x74, 0x5C, 0x22, 0x74, 0x5D, 0x6E, 0x6A, 0x73, 0x41, 0x77, 0x6E, 0x46, 0x47, 0x77, 0x49, 0x0C, 0x4B, 0x46, 0x6F,
        };
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            table[n] = c;
        }
        var zip = new MemoryStream();
        var directory = new MemoryStream();
        var w = new BinaryWriter(zip);
        var d = new BinaryWriter(directory);
        foreach (var file in files)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in file.data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            crc = ~crc;
            byte[] plain = new byte[12 + file.data.Length];
            for (int i = 0; i < 11; i++) plain[i] = (byte)(i * 37 + 5);
            plain[11] = (byte)(crc >> 24);
            file.data.CopyTo(plain, 12);
            uint k0 = 0x12345678, k1 = 0x23456789, k2 = 0x34567890;
            void Update(byte c)
            {
                k0 = table[(k0 ^ c) & 0xFF] ^ (k0 >> 8);
                k1 = unchecked((k1 + (k0 & 0xFF)) * 134775813 + 1);
                k2 = table[(k2 ^ (k1 >> 24)) & 0xFF] ^ (k2 >> 8);
            }
            foreach (byte c in password) Update(c);
            byte[] packed = new byte[plain.Length];
            for (int i = 0; i < plain.Length; i++)
            {
                uint t = (k2 | 2) & 0xFFFF;
                packed[i] = (byte)(plain[i] ^ (byte)((t * (t ^ 1)) >> 8));
                Update(plain[i]);
            }
            byte[] name = Encoding.ASCII.GetBytes(file.name);
            uint offset = (uint)zip.Position;
            w.Write(0x04034B50u); w.Write((ushort)20); w.Write((ushort)1); w.Write((ushort)0); w.Write(0u);
            w.Write(crc); w.Write((uint)packed.Length); w.Write((uint)file.data.Length); w.Write((ushort)name.Length); w.Write((ushort)0);
            w.Write(name); w.Write(packed);
            d.Write(0x02014B50u); d.Write((ushort)20); d.Write((ushort)20); d.Write((ushort)1); d.Write((ushort)0); d.Write(0u);
            d.Write(crc); d.Write((uint)packed.Length); d.Write((uint)file.data.Length); d.Write((ushort)name.Length); d.Write((ushort)0);
            d.Write((ushort)0); d.Write((ushort)0); d.Write((ushort)0); d.Write(0u); d.Write(offset); d.Write(name);
        }
        uint directoryAt = (uint)zip.Position;
        w.Write(directory.ToArray());
        w.Write(0x06054B50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)files.Length); w.Write((ushort)files.Length);
        w.Write((uint)directory.Length); w.Write(directoryAt); w.Write((ushort)0);
        w.Write(new byte[37]); // TexMod leaves bytes after the end
        byte[] result = zip.ToArray();
        byte[] scramble = { 0xA4, 0x3F, 0xA4, 0x3F };
        for (int i = 0; i < result.Length; i++) result[i] ^= scramble[i & 3];
        return result;
    }

    // DXVK: its 32-bit d3d9.dll goes next to nfs.exe, and comes out again.
    static void DxvkTests(string root)
    {
        Console.WriteLine("DXVK");
        string game = Path.Combine(root, "dxvkgame");
        Directory.CreateDirectory(game);
        byte[] dll32 = Encoding.ASCII.GetBytes("MZ the 32-bit one");
        byte[] dll64 = Encoding.ASCII.GetBytes("MZ the 64-bit one, which the game can't load");
        var archive = new MemoryStream();
        using (var zipped = new System.IO.Compression.GZipStream(archive, System.IO.Compression.CompressionMode.Compress, true))
        using (var tar = new System.Formats.Tar.TarWriter(zipped))
        {
            foreach (var file in new[] { ("dxvk-9.9/x64/d3d9.dll", dll64), ("dxvk-9.9/x32/d3d9.dll", dll32), ("dxvk-9.9/x32/d3d11.dll", dll64) })
            {
                var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, file.Item1) { DataStream = new MemoryStream(file.Item2) };
                tar.WriteEntry(entry);
            }
        }

        byte[] packed = archive.ToArray(); // installing closes the stream it reads

        Check(!Dxvk.IsInstalled(game) && !Dxvk.HasOtherDll(game) && Dxvk.InstalledVersion(game) == null, "a game starts without DXVK");
        archive = new MemoryStream(packed);
        Dxvk.InstallFrom(game, archive, "v9.9");
        Check(File.ReadAllBytes(Path.Combine(game, "d3d9.dll")).SequenceEqual(dll32), "the 32-bit d3d9.dll is installed, not the 64-bit one");
        Check(Dxvk.IsInstalled(game) && Dxvk.InstalledVersion(game) == "v9.9", "it knows DXVK is installed, and which version");
        Check(Directory.GetFiles(game).Length == 2, "nothing else from the archive is copied");
        Dxvk.Remove(game);
        Check(Directory.GetFiles(game).Length == 0 && !Dxvk.IsInstalled(game), "turning it off removes the dll");

        File.WriteAllText(Path.Combine(game, "d3d9.dll"), "MZ someone else's, such as ReShade");
        bool refused = false;
        archive = new MemoryStream(packed);
        try { Dxvk.InstallFrom(game, archive, "v9.9"); } catch (InvalidOperationException) { refused = true; }
        Dxvk.Remove(game);
        Check(refused && Dxvk.HasOtherDll(game) && File.ReadAllText(Path.Combine(game, "d3d9.dll")).StartsWith("MZ someone"), "a d3d9.dll from something else is never replaced or removed");
        File.Delete(Path.Combine(game, "d3d9.dll"));

        bool empty = false;
        try { Dxvk.InstallFrom(game, new MemoryStream(new byte[] { 0x1F, 0x8B, 8, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 }), "v9.9"); }
        catch (Exception) { empty = true; }
        Check(empty && Directory.GetFiles(game).Length == 0, "an archive without the dll is refused and leaves nothing behind");
    }

    static void HdReflectionsTests(string root)
    {
        Console.WriteLine("HD reflections");
        Check(Array.IndexOf(Game.HdReflectionsScripts, Game.HdReflectionsAsi) >= 0
            && Array.IndexOf(Game.HdReflectionsScripts, "NFSPSHDReflections.LICENSE.txt") >= 0,
            "HD reflections scripts list includes the plugin and GPL license");

        string game = Path.Combine(root, "hdrefl");
        Directory.CreateDirectory(game);

        Downloads.RemoveHdReflections(game);
        Check(!Directory.Exists(Path.Combine(game, "scripts")), "removing when scripts folder does not exist is a no-op");

        string scripts = Path.Combine(game, "scripts");
        Directory.CreateDirectory(scripts);
        Downloads.RemoveHdReflections(game);
        Check(!File.Exists(Path.Combine(scripts, Game.HdReflectionsAsi)), "removing when the plugin is absent is a no-op");

        File.WriteAllText(Path.Combine(scripts, Game.HdReflectionsAsi), "asi");
        File.WriteAllText(Path.Combine(scripts, Game.HdReflectionsIni), "ini");
        File.WriteAllText(Path.Combine(scripts, "NFSPSHDReflections.LICENSE.txt"), "gplv3");

        Downloads.RemoveHdReflections(game);
        Check(!File.Exists(Path.Combine(scripts, Game.HdReflectionsAsi))
            && File.Exists(Path.Combine(scripts, Game.HdReflectionsIni))
            && File.Exists(Path.Combine(scripts, "NFSPSHDReflections.LICENSE.txt")),
            "removing HD reflections deletes the asi while keeping user ini and license");
    }

    static void DiagnosticsTests(string root)
    {
        Console.WriteLine("Diagnostics bundle");
        string redacted = Diagnostics.RedactClientIni("[Server]\r\nAddress=weexnes.dev\r\n[Account]\r\nAutoLogin=1\r\nUsername=Alice\r\nPassword = dpapi:secretBlob\r\n[Overlay]\r\nPassword=keep_me\r\n");
        Check(redacted.Contains("Username=Alice") && redacted.Contains("Password =<redacted>") && !redacted.Contains("secretBlob") &&
              redacted.Contains("[Overlay]\r\nPassword=keep_me"),
              "RedactClientIni masks [Account] Password without disturbing other sections");

        string game = Path.Combine(root, "diag_game");
        string scripts = Path.Combine(game, "scripts");
        string appData = Path.Combine(root, "diag_appdata");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(appData);
        File.WriteAllBytes(Path.Combine(game, Game.ExeName), new byte[] { 0x4D, 0x5A });
        File.WriteAllText(Path.Combine(scripts, Game.ClientIni), "[Account]\nUsername=Alice\nPassword=super-secret\n[Overlay]\nChat=1\n");
        File.WriteAllText(Path.Combine(scripts, Game.MultiFixIni), "[MultiFix]\nFramerateUncap=1\n");
        File.WriteAllText(Path.Combine(game, "d3d9.log"), "dxvk ok\n");
        File.WriteAllText(Path.Combine(scripts, "OpenProStreetClient.log"), "connected\n");
        File.WriteAllText(Path.Combine(appData, "identity.key"), "PRIVATE_ED25519_KEY");
        File.WriteAllText(Path.Combine(appData, "other.key"), "SECRET_KEY");
        File.WriteAllText(Path.Combine(appData, "race_results.jsonl"), "{\"ok\":true}\n");
        File.WriteAllText(Path.Combine(appData, "client.log"), "appdata log\n");

        string zipPath = Path.Combine(root, "bundle.zip");
        DiagnosticsResult res = Diagnostics.CreateBundle(game, zipPath, new[] { appData });
        Check(res.FilesIncluded >= 6 && File.Exists(zipPath), "bundle archive is written");

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        string[] names = zip.Entries.Select(e => e.FullName).ToArray();
        Check(names.Contains("summary.txt") && names.Contains("config/OpenProStreetClient.ini") &&
              names.Contains("config/XNFSPS-MultiFix.ini") && names.Contains("game/d3d9.log") &&
              names.Contains("scripts/OpenProStreetClient.log") && names.Contains("appdata/race_results.jsonl") &&
              names.Contains("appdata/client.log"),
              "bundle contains summary, configs, logs and race telemetry");
        Check(!names.Any(n => n.EndsWith(".key", StringComparison.OrdinalIgnoreCase)),
              "identity.key and any .key files are never included");

        using StreamReader iniReader = new StreamReader(zip.GetEntry("config/OpenProStreetClient.ini").Open());
        string bundledIni = iniReader.ReadToEnd();
        Check(!bundledIni.Contains("super-secret") && bundledIni.Contains("Password=<redacted>") && bundledIni.Contains("Username=Alice"),
              "bundled OpenProStreetClient.ini redacts the password while keeping other settings");

        Console.WriteLine("Feature branch updates");
        Check(Downloads.IsDefaultBranch(null) && Downloads.IsDefaultBranch("master") && Downloads.IsDefaultBranch("MAIN") && !Downloads.IsDefaultBranch("discord-invite"),
              "default branch detection treats empty/master/main as stable");
        Check(Downloads.BuildClientQuery("master").Contains("\"Job\" is \"Build Release Tag\""),
              "stable branch queries Build Release Tag");
        string featQuery = Downloads.BuildClientQuery("discord-invite");
        Check(featQuery.Contains("\"Job\" is \"Build Feature Branch\"") && featQuery.Contains("\"Branch\" is \"discord-invite\""),
              "feature branch queries Build Feature Branch for that branch");
        string[] parsedBranches = Downloads.ParseFeatureBranches("[{\"refName\":\"refs/heads/discord-invite\"},{\"refName\":\"refs/heads/discord-invite\"},{\"refName\":\"refs/heads/exp-test\"}]");
        Check(parsedBranches.Length == 3 && parsedBranches[0] == "master" && parsedBranches[1] == "discord-invite" && parsedBranches[2] == "exp-test",
              "ParseFeatureBranches lists master first plus unique branch names");

        string pePath = Path.Combine(root, "feature-client.asi");
        using (MemoryStream ms = new MemoryStream())
        {
            ms.Write(Encoding.ASCII.GetBytes("MZdummy"));
            ms.Write(Encoding.Unicode.GetBytes("ProductVersion\0"));
            ms.Write(new byte[] { 0, 0 });
            ms.Write(Encoding.Unicode.GetBytes("10.22-fb-discord-invite\0"));
            File.WriteAllBytes(pePath, ms.ToArray());
        }
        Check(Game.ProductVersionOf(pePath) == "10.22-fb-discord-invite",
              "ProductVersionOf reads UTF-16LE feature build strings cross-platform");
    }
}
