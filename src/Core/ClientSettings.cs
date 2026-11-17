using System.IO;

namespace OpenProStreetLauncher
{
    // The 1.1 No-CD nfs.exe is marked as a console program (PE subsystem 3), so Windows, and Wine,
    // open a console window for it before anything else runs. Marking it as a Windows program
    // (subsystem 2) stops that; no code changes. MultiFix's EnablePrints, which turns on the game's
    // own debug output in that console, is set to match. MultiFix and the client print there too.
    static class ConsoleWindow
    {
        const ushort Gui = 2, Console = 3;

        public static string IniPath(string gameDir) => Game.FindFile(Game.ScriptsDir(gameDir), Game.MultiFixIni);

        // The PE header's Subsystem field (optional header + 68), or null if it isn't a PE32 file.
        public static int? Subsystem(string exe)
        {
            try
            {
                using FileStream f = File.OpenRead(exe);
                byte[] b = new byte[0x400];
                int read = f.Read(b, 0, b.Length);
                if (read < 0x40 || b[0] != 'M' || b[1] != 'Z') return null;
                int pe = System.BitConverter.ToInt32(b, 0x3C);
                if (pe < 0 || pe + 24 + 70 > read || System.BitConverter.ToUInt32(b, pe) != 0x00004550) return null;
                return System.BitConverter.ToUInt16(b, pe + 24 + 68);
            }
            catch (System.Exception) { return null; }
        }

        static void SetSubsystem(string exe, ushort value)
        {
            byte[] b = File.ReadAllBytes(exe);
            int pe = System.BitConverter.ToInt32(b, 0x3C);
            System.BitConverter.GetBytes(value).CopyTo(b, pe + 24 + 68);
            string temp = exe + ".tmp";
            File.WriteAllBytes(temp, b);
            File.Move(temp, exe, true);
        }

        // null when there's no nfs.exe to tell from.
        public static bool? Shown(string gameDir)
        {
            string exe = Game.ExePath(gameDir);
            int? subsystem = exe == null ? null : Subsystem(exe);
            return subsystem == null ? null : subsystem == Console;
        }

        public static void Set(string gameDir, bool shown)
        {
            string exe = Game.ExePath(gameDir) ?? throw new FileNotFoundException("There's no nfs.exe in the game folder.");
            int? subsystem = Subsystem(exe);
            if (subsystem != Gui && subsystem != Console) throw new InvalidDataException("nfs.exe isn't a Windows program the launcher knows.");
            if (subsystem != (shown ? Console : Gui)) SetSubsystem(exe, shown ? Console : Gui);

            string path = IniPath(gameDir);
            if (path == null) return;
            IniFile ini = new IniFile(path);
            if (ini.GetBool("MultiFix", "EnablePrints", false) == shown) return;
            ini.SetBool("MultiFix", "EnablePrints", shown);
            ini.Save();
        }
    }

    // MultiFix's FramerateUncap. Without it the game limits races to 30 fps (nfs.exe waits until
    // 33.3 ms have passed since the last frame while racing); menus aren't limited either way.
    // MultiFix treats a missing key as on.
    static class Framerate
    {
        // null when there's no MultiFix ini to tell from.
        public static bool? Uncapped(string gameDir)
        {
            string path = ConsoleWindow.IniPath(gameDir);
            return path == null ? null : new IniFile(path).GetBool("MultiFix", "FramerateUncap", true);
        }

        public static void Set(string gameDir, bool uncapped)
        {
            string path = ConsoleWindow.IniPath(gameDir);
            if (path == null) return;
            IniFile ini = new IniFile(path);
            if (ini.GetBool("MultiFix", "FramerateUncap", true) == uncapped) return;
            ini.SetBool("MultiFix", "FramerateUncap", uncapped);
            ini.Save();
        }

        // The launcher's MultiFix download shipped its ini with the uncap off from 6 to 17 November
        // 2026, so those installs raced at 30 fps. Turns it on once; after that the setting is the
        // player's.
        public static void FixOnce(string gameDir)
        {
            if (LauncherSettings.FramerateUncapFixed || ConsoleWindow.IniPath(gameDir) == null) return;
            try
            {
                Set(gameDir, true);
                LauncherSettings.FramerateUncapFixed = true;
            }
            catch (System.Exception) { }
        }
    }

    // scripts/OpenProStreetClient.ini, with the same defaults the client uses when a key is missing.
    class ClientSettings
    {
        public const string DefaultAddress = "weexnes.dev";
        public const int DefaultPort = 10104;

        public string Address = DefaultAddress; // "" makes the game search the LAN for a server
        public int Port = DefaultPort;
        public bool Borderless = false;
        public bool NoCameraVibration = true;
        public bool NoTankSlapper = true;
        public bool NoSpeedLimiter = true;
        public bool Verbose = false;
        public bool Colour = true;
        public bool DumpEvents = false;
        public bool Overlay = true;              // the standing card and race notifications
        public bool OverlayNotifications = true;

        // ReShifted, the client's automatic gearbox that follows how you drive. ReShifted is the
        // master switch; the rest only matters while it is on.
        public bool ReShifted = false;
        public int ShiftStyle = 2;           // 1 = Classic (throttle only), 2 = Adaptive
        public bool SportMemory = true;      // Adaptive only: remember recent hard driving
        public bool CoastDownshift = true;   // Adaptive only
        public bool QuickShift = true;       // Adaptive only
        public bool SkipGears = true;        // Adaptive only
        public bool ShiftIndicator = true;   // ECO / SPORT marker in the overlay
        public int ShortShift = 28;          // gentle shift point, % of the way from idle to redline (10 to 55)
        public int SportShift = 97;          // highest shift point, % of the redline (88 to 99)
        public int EcoThrottle = 25;         // up to this much throttle it shifts early (0 to 80)
        public int SportThrottle = 85;       // from this much throttle it holds to the sport point (up to 100)
        public int QuickShiftTime = 60;      // gear change time when driven hard, % of stock (30 to 100)

        static int GetInt(IniFile ini, string section, string key, int def, int min, int max)
        {
            return int.TryParse((ini.Get(section, key) ?? "").Trim(), out int v) ? System.Math.Clamp(v, min, max) : def;
        }

        // The client logs in with these as soon as the account screen opens.
        public bool AutoLogin = false;
        public string Username = "";
        public string Password = "";

        // On Windows the password is protected with DPAPI for the Windows user, because the ini sits
        // in the game folder, which other users of the PC can read. The client turns it back with the
        // same entropy. Wine has no DPAPI shared with Linux, so there it's saved as it is.
        const string ProtectedPrefix = "dpapi:";
        static readonly byte[] Entropy = System.Text.Encoding.ASCII.GetBytes("OpenProStreet login");

        static string ProtectPassword(string password)
        {
            if (password.Length == 0 || !System.OperatingSystem.IsWindows()) return password;
            byte[] sealedBytes = System.Security.Cryptography.ProtectedData.Protect(
                System.Text.Encoding.UTF8.GetBytes(password), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return ProtectedPrefix + System.Convert.ToBase64String(sealedBytes);
        }

        // "" when it can't be read here, such as one saved by another Windows user.
        static string UnprotectPassword(string stored)
        {
            if (!stored.StartsWith(ProtectedPrefix, System.StringComparison.Ordinal)) return stored;
            if (!System.OperatingSystem.IsWindows()) return "";
            try
            {
                byte[] plain = System.Security.Cryptography.ProtectedData.Unprotect(
                    System.Convert.FromBase64String(stored.Substring(ProtectedPrefix.Length)), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (System.Exception) { return ""; }
        }

        public static string PathIn(string gameDir)
        {
            string scripts = Game.ScriptsDir(gameDir);
            return Game.FindFile(scripts, Game.ClientIni) ?? Path.Combine(scripts, Game.ClientIni);
        }

        public static ClientSettings Load(string file)
        {
            ClientSettings s = new ClientSettings();
            IniFile ini = new IniFile(file);
            if (!ini.Exists) return s;
            s.Address = (ini.Get("Server", "Address") ?? DefaultAddress).Trim();
            if (int.TryParse(ini.Get("Server", "Port"), out int port) && port >= 1 && port <= 65535) s.Port = port;
            s.Borderless = ini.GetBool("Display", "Borderless", s.Borderless);
            s.NoCameraVibration = ini.GetBool("Display", "NoCameraVibration", s.NoCameraVibration);
            s.NoTankSlapper = ini.GetBool("Physics", "NoTankSlapper", s.NoTankSlapper);
            s.NoSpeedLimiter = ini.GetBool("Physics", "NoSpeedLimiter", s.NoSpeedLimiter);
            s.Verbose = ini.GetBool("Log", "Verbose", s.Verbose);
            s.Colour = ini.GetBool("Log", "Colour", s.Colour);
            s.DumpEvents = ini.GetBool("Debug", "DumpEvents", s.DumpEvents);
            s.Overlay = ini.GetBool("Overlay", "Enabled", s.Overlay);
            s.OverlayNotifications = ini.GetBool("Overlay", "Notifications", s.OverlayNotifications);
            s.ReShifted = ini.GetBool("Transmission", "ReShifted", s.ReShifted);
            s.ShiftStyle = GetInt(ini, "ReShifted", "Style", s.ShiftStyle, 1, 2);
            s.SportMemory = ini.GetBool("ReShifted", "SportMemory", s.SportMemory);
            s.CoastDownshift = ini.GetBool("ReShifted", "CoastDownshift", s.CoastDownshift);
            s.QuickShift = ini.GetBool("ReShifted", "QuickShift", s.QuickShift);
            s.SkipGears = ini.GetBool("ReShifted", "SkipGears", s.SkipGears);
            s.ShiftIndicator = ini.GetBool("ReShifted", "Indicator", s.ShiftIndicator);
            s.ShortShift = GetInt(ini, "ReShifted", "ShortShiftFraction", s.ShortShift, 10, 55);
            s.SportShift = GetInt(ini, "ReShifted", "SportRedlineSafety", s.SportShift, 88, 99);
            s.EcoThrottle = GetInt(ini, "ReShifted", "EcoThrottle", s.EcoThrottle, 0, 80);
            s.SportThrottle = GetInt(ini, "ReShifted", "SportThrottle", s.SportThrottle, 10, 100);
            s.QuickShiftTime = GetInt(ini, "ReShifted", "QuickShiftTime", s.QuickShiftTime, 30, 100);
            s.AutoLogin = ini.GetBool("Account", "AutoLogin", s.AutoLogin);
            s.Username = (ini.Get("Account", "Username") ?? "").Trim();
            s.Password = UnprotectPassword(ini.Get("Account", "Password") ?? "");
            return s;
        }

        // Writes every setting, so a new file lists all of them for anyone editing it by hand.
        public void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            IniFile ini = new IniFile(file);
            ini.Set("Server", "Address", Address);
            ini.Set("Server", "Port", Port.ToString());
            ini.SetBool("Display", "Borderless", Borderless);
            ini.SetBool("Display", "NoCameraVibration", NoCameraVibration);
            ini.SetBool("Physics", "NoTankSlapper", NoTankSlapper);
            ini.SetBool("Physics", "NoSpeedLimiter", NoSpeedLimiter);
            ini.SetBool("Log", "Verbose", Verbose);
            ini.SetBool("Log", "Colour", Colour);
            ini.SetBool("Debug", "DumpEvents", DumpEvents);
            ini.SetBool("Overlay", "Enabled", Overlay);
            ini.SetBool("Overlay", "Notifications", OverlayNotifications);
            ini.SetBool("Transmission", "ReShifted", ReShifted);
            ini.Set("ReShifted", "Style", ShiftStyle.ToString());
            ini.SetBool("ReShifted", "SportMemory", SportMemory);
            ini.SetBool("ReShifted", "CoastDownshift", CoastDownshift);
            ini.SetBool("ReShifted", "QuickShift", QuickShift);
            ini.SetBool("ReShifted", "SkipGears", SkipGears);
            ini.SetBool("ReShifted", "Indicator", ShiftIndicator);
            ini.Set("ReShifted", "ShortShiftFraction", ShortShift.ToString());
            ini.Set("ReShifted", "SportRedlineSafety", SportShift.ToString());
            ini.Set("ReShifted", "EcoThrottle", EcoThrottle.ToString());
            ini.Set("ReShifted", "SportThrottle", SportThrottle.ToString());
            ini.Set("ReShifted", "QuickShiftTime", QuickShiftTime.ToString());
            // The password is only kept while automatic login is on.
            ini.SetBool("Account", "AutoLogin", AutoLogin);
            ini.Set("Account", "Username", Username);
            ini.Set("Account", "Password", AutoLogin ? ProtectPassword(Password) : "");
            ini.Save("; OpenProStreet client settings. The launcher can change these for you.");
        }
    }
}
