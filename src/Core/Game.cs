using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OpenProStreetLauncher
{
    // The game folder and what's in it.
    static class Game
    {
        public const string ExeName = "nfs.exe";
        public const string ClientAsi = "OpenProStreetClient.asi";
        public const string ClientIni = "OpenProStreetClient.ini";
        public const string DiscordSdk = "discord_game_sdk.dll";
        public const string MultiFixAsi = "XNFSPS-MultiFix.asi";
        public const string MultiFixIni = "XNFSPS-MultiFix.ini";
        public const string Loader = "dinput8.dll";

        // Linux file names are case sensitive, and Windows installers don't care how they spell them.
        public static string FindFile(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            string exact = Path.Combine(dir, name);
            if (File.Exists(exact)) return exact;
            foreach (string file in Directory.EnumerateFiles(dir))
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase)) return file;
            return null;
        }

        public static string FindDir(string dir, string name)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            foreach (string sub in Directory.EnumerateDirectories(dir))
                if (string.Equals(Path.GetFileName(sub), name, StringComparison.OrdinalIgnoreCase)) return sub;
            return null;
        }

        public static string ExePath(string dir) => FindFile(dir, ExeName);
        public static bool IsGameDir(string dir) => ExePath(dir) != null;

        // The scripts folder, created with the usual spelling when it isn't there.
        public static string ScriptsDir(string gameDir) => FindDir(gameDir, "scripts") ?? Path.Combine(gameDir, "scripts");

        public static string FindGameDir()
        {
            string own = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (IsGameDir(own)) return own;

            // Installed into the game folder, as <game>\.launcher\: the game is right there.
            if (OperatingSystem.IsWindows())
            {
                string installedFor = WindowsInstall.GameDirOfInstalledCopy();
                if (installedFor != null) return installedFor;
            }

            string saved = LauncherSettings.GameDir;
            if (IsGameDir(saved)) return saved;

            if (OperatingSystem.IsWindows())
            {
                foreach (string key in new[] { @"SOFTWARE\WOW6432Node\EA GAMES\Need for Speed ProStreet", @"SOFTWARE\EA GAMES\Need for Speed ProStreet" })
                {
                    try
                    {
                        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key))
                        {
                            string dir = k == null ? null : k.GetValue("Install Dir") as string;
                            if (dir != null && IsGameDir(dir.TrimEnd('\\', '/'))) return dir.TrimEnd('\\', '/');
                        }
                    }
                    catch (Exception) { }
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string installed = WinePrefix.FindGame(LauncherSettings.PrefixDir);
                if (IsGameDir(installed)) return installed;
            }

            return null;
        }

        public static bool IsWritable(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, ".openprostreet-write-test-" + Process.GetCurrentProcess().Id);
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        // The client and MultiFix patch nfs.exe v1.1 at fixed addresses. Its version check pushes 1, 1
        // at 0x6CC3A6 (6A 01 6A 01), which both test. A packed exe, like the "Vitality" 1.1 No-CD,
        // has no code on disk there. The file is only read.
        public static bool IsSupportedExe(string exe)
        {
            try
            {
                byte[] b = File.ReadAllBytes(exe);
                int pe = BitConverter.ToInt32(b, 0x3C);
                int sections = BitConverter.ToUInt16(b, pe + 6);
                int optSize = BitConverter.ToUInt16(b, pe + 20);
                uint imageBase = BitConverter.ToUInt32(b, pe + 24 + 28);
                uint rva = 0x6CC3A6 - imageBase;
                for (int i = 0; i < sections; i++)
                {
                    int s = pe + 24 + optSize + i * 40;
                    uint va = BitConverter.ToUInt32(b, s + 12);
                    uint rawSize = BitConverter.ToUInt32(b, s + 16);
                    uint raw = BitConverter.ToUInt32(b, s + 20);
                    if (rva >= va && rva + 4 <= va + rawSize)
                    {
                        long at = raw + (rva - va);
                        return b[at] == 0x6A && b[at + 1] == 0x01 && b[at + 2] == 0x6A && b[at + 3] == 0x01;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        // The file version from a Windows dll's version resource. .NET only reads these on Windows, so
        // this finds VS_FIXEDFILEINFO by its signature, 0xFEEF04BD.
        public static Version FileVersionOf(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return null;
                byte[] b = File.ReadAllBytes(path);
                ReadOnlySpan<byte> signature = new byte[] { 0xBD, 0x04, 0xEF, 0xFE };
                for (int i = 0; i + 52 <= b.Length; i++)
                {
                    int found = b.AsSpan(i, b.Length - i).IndexOf(signature);
                    if (found < 0) break;
                    i += found;
                    if (i + 52 > b.Length) break;
                    // The structure's version, 1.0, follows the signature, which rules out a chance match.
                    if (BitConverter.ToUInt32(b, i + 4) != 0x00010000) continue;
                    uint ms = BitConverter.ToUInt32(b, i + 8);
                    uint ls = BitConverter.ToUInt32(b, i + 12);
                    return new Version((int)(ms >> 16), (int)(ms & 0xFFFF), (int)(ls >> 16));
                }
            }
            catch (Exception) { }
            return null;
        }

        public static string Show(Version v)
        {
            if (v == null) return "?";
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }
    }

    // The launcher's own settings.
    static class LauncherSettings
    {
        public static string ConfigDir
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenProStreet");
                }
                string xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                string root = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(Home, ".config");
                return Path.Combine(root, "OpenProStreet");
            }
        }

        public static string DataDir
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenProStreet");
                }
                string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                string root = !string.IsNullOrEmpty(xdg) ? xdg : Path.Combine(Home, ".local", "share");
                return Path.Combine(root, "OpenProStreet");
            }
        }

        public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // The Proton prefix the game is installed into (Linux). Its Steam entry's prefix links here.
        public static string PrefixDir => Path.Combine(DataDir, "prefix");

        static string FilePath => Path.Combine(ConfigDir, "launcher.ini");

        public static string GameDir
        {
            get => new IniFile(FilePath).Get("Launcher", "GameDir");
            set
            {
                try
                {
                    Directory.CreateDirectory(ConfigDir);
                    IniFile ini = new IniFile(FilePath);
                    ini.Set("Launcher", "GameDir", value);
                    ini.Save();
                }
                catch (Exception) { }
            }
        }
    }
}
