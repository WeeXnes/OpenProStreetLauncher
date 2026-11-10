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
            // The password is only kept while automatic login is on.
            ini.SetBool("Account", "AutoLogin", AutoLogin);
            ini.Set("Account", "Username", Username);
            ini.Set("Account", "Password", AutoLogin ? ProtectPassword(Password) : "");
            ini.Save("; OpenProStreet client settings. The launcher can change these for you.");
        }
    }
}
