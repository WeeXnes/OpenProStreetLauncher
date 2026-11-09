using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenProStreetLauncher
{
    // A Proton build Steam can run games with.
    class ProtonTool
    {
        public string Name;        // Steam's internal name, used in CompatToolMapping ("proton_experimental")
        public string DisplayName; // "Proton - Experimental"
        public string Dir;         // the folder with the "proton" script
        public int Rank;           // higher is preferred

        public string Script => Path.Combine(Dir, "proton");

        // Proton's own wineserver, for waiting until everything in a prefix has exited.
        public string Wineserver
        {
            get
            {
                foreach (string sub in new[] { "files/bin/wineserver", "dist/bin/wineserver" })
                {
                    string p = Path.Combine(Dir, sub);
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }
    }

    // Steam on Linux: where it is, its accounts and libraries, its Proton builds, and the two files the
    // launcher changes. Steam keeps both in memory and writes them back when it closes, so it has to
    // be closed while they change.
    class Steam
    {
        public const string AppName = "Need for Speed: ProStreet (OpenProStreet)";
        // Lets the ASI loader's dinput8.dll load instead of Wine's own.
        public const string LaunchOptions = "WINEDLLOVERRIDES=\"dinput8=n,b\" %command%";

        public readonly string Root;
        public bool IsFlatpak => Root.Contains("/.var/app/com.valvesoftware.Steam/");

        public Steam(string root) { Root = root; }

        // The native install (~/.steam/steam links to it), then the Flatpak one.
        public static Steam Find(string home = null)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        string path = k == null ? null : k.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            return new Steam(path.Replace('/', '\\'));
                    }
                }
                catch (Exception) { }
                return null;
            }

            home ??= LauncherSettings.Home;
            foreach (string candidate in new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            })
            {
                if (Directory.Exists(Path.Combine(candidate, "steamapps")) || Directory.Exists(Path.Combine(candidate, "userdata")))
                {
                    try { return new Steam(new DirectoryInfo(candidate).ResolveLinkTarget(true)?.FullName ?? candidate); }
                    catch (Exception) { return new Steam(candidate); }
                }
            }
            return null;
        }

        // The accounts that have signed in here, as their userdata/<id>/config folders.
        public List<string> AccountConfigDirs()
        {
            List<string> dirs = new List<string>();
            string userdata = Path.Combine(Root, "userdata");
            if (!Directory.Exists(userdata)) return dirs;
            foreach (string dir in Directory.GetDirectories(userdata))
                if (long.TryParse(Path.GetFileName(dir), out long id) && id != 0) dirs.Add(Path.Combine(dir, "config"));
            return dirs;
        }

        public List<string> Libraries()
        {
            List<string> libraries = new List<string> { Root };
            string file = Path.Combine(Root, "steamapps", "libraryfolders.vdf");
            try
            {
                VdfMap folders = TextVdf.Read(File.ReadAllText(file)).GetMap("libraryfolders");
                if (folders != null)
                    foreach (var item in folders.Items)
                        if (item.Value is VdfMap lib && lib.GetString("path") is string path && Directory.Exists(path)
                            && !libraries.Any(l => string.Equals(Path.GetFullPath(l), Path.GetFullPath(path), StringComparison.Ordinal)))
                            libraries.Add(path);
            }
            catch (Exception) { }
            return libraries;
        }

        // Valve's Proton builds from every library, and custom ones (like GE-Proton) from
        // compatibilitytools.d. The newest Valve build is preferred.
        public List<ProtonTool> ProtonTools()
        {
            List<ProtonTool> tools = new List<ProtonTool>();
            foreach (string library in Libraries())
            {
                string common = Path.Combine(library, "steamapps", "common");
                if (!Directory.Exists(common)) continue;
                foreach (string dir in Directory.GetDirectories(common))
                {
                    if (!File.Exists(Path.Combine(dir, "proton"))) continue;
                    string display = Path.GetFileName(dir);
                    ProtonTool tool = ValveTool(display, dir);
                    if (tool != null) tools.Add(tool);
                }
            }
            foreach (string root in new[] { Path.Combine(Root, "compatibilitytools.d"), "/usr/share/steam/compatibilitytools.d" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string manifest = Path.Combine(dir, "compatibilitytool.vdf");
                    if (!File.Exists(manifest) || !File.Exists(Path.Combine(dir, "proton"))) continue;
                    try
                    {
                        VdfMap compat = TextVdf.Read(File.ReadAllText(manifest)).GetMap("compatibilitytools")?.GetMap("compat_tools");
                        if (compat == null || compat.Items.Count == 0) continue;
                        var first = compat.Items[0];
                        string display = (first.Value as VdfMap)?.GetString("display_name") ?? first.Key;
                        tools.Add(new ProtonTool { Name = first.Key, DisplayName = display, Dir = dir, Rank = 100 });
                    }
                    catch (Exception) { }
                }
            }
            return tools.OrderByDescending(t => t.Rank).ThenBy(t => t.DisplayName, StringComparer.Ordinal).ToList();
        }

        // Steam's internal names for Valve's builds, from their folder names.
        public static ProtonTool ValveTool(string folder, string dir)
        {
            if (folder == "Proton - Experimental") return new ProtonTool { Name = "proton_experimental", DisplayName = folder, Dir = dir, Rank = 1000 };
            if (folder == "Proton Hotfix") return new ProtonTool { Name = "proton_hotfix", DisplayName = folder, Dir = dir, Rank = 50 };
            Match m = Regex.Match(folder, @"^Proton (\d+)\.(\d+)");
            if (!m.Success) return null;
            int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value);
            // Steam names 5.0 and later by their major version, and older ones by both digits.
            string name = major >= 5 ? "proton_" + major : "proton_" + major + minor;
            return new ProtonTool { Name = name, DisplayName = folder, Dir = dir, Rank = 200 + major * 10 + minor };
        }

        public static bool IsRunning()
        {
            try { return Process.GetProcessesByName("steam").Length > 0; }
            catch (Exception) { return false; }
        }

        // Asks Steam to close, and waits up to 30 seconds for it.
        public bool Close()
        {
            try
            {
                if (OperatingSystem.IsWindows()) Process.Start(Path.Combine(Root, "steam.exe"), "-shutdown");
                else if (IsFlatpak) Process.Start("flatpak", "run com.valvesoftware.Steam -shutdown");
                else Process.Start("steam", "-shutdown");
            }
            catch (Exception) { return false; }
            for (int i = 0; i < 60; i++)
            {
                System.Threading.Thread.Sleep(500);
                if (!IsRunning()) return true;
            }
            return false;
        }

        public void Start(string url = null)
        {
            string args = url ?? "";
            if (OperatingSystem.IsWindows())
            {
                if (!string.IsNullOrEmpty(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(Path.Combine(Root, "steam.exe")) { UseShellExecute = true });
            }
            else
            {
                if (IsFlatpak) Process.Start("flatpak", ("run com.valvesoftware.Steam " + args).Trim());
                else Process.Start(new ProcessStartInfo("steam", args) { UseShellExecute = false });
            }
        }

        // Steam's id for a shortcut: CRC32 of the quoted exe and the name, with the top bit set.
        public static uint ShortcutAppId(string exe)
        {
            byte[] data = Encoding.UTF8.GetBytes("\"" + exe + "\"" + AppName);
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc | 0x80000000;
        }

        // steam://rungameid/ takes the shortcut's 64-bit id.
        public static string RunUrl(uint appId) => "steam://rungameid/" + (((ulong)appId << 32) | 0x02000000UL);

        // Where Steam keeps the Proton prefix of a non-Steam game.
        public string CompatDataDir(uint appId) => Path.Combine(Root, "steamapps", "compatdata", appId.ToString());

        // Adds the game to every account's library, or updates it where it's already there, with the
        // launch option the ASI loader needs. Returns how many accounts got it.
        public int AddShortcut(string exe, string icon = "")
        {
            int done = 0;
            uint appId = ShortcutAppId(exe);
            string dir = Path.GetDirectoryName(exe);
            foreach (string config in AccountConfigDirs())
            {
                Directory.CreateDirectory(config);
                string file = Path.Combine(config, "shortcuts.vdf");
                VdfMap root = File.Exists(file) ? BinaryVdf.Read(File.ReadAllBytes(file)) : new VdfMap();
                VdfMap list = root.Path("shortcuts");
                VdfMap entry = list.Items.Select(i => i.Value as VdfMap)
                    .FirstOrDefault(m => m != null && string.Equals(m.GetString("AppName"), AppName, StringComparison.Ordinal));
                if (entry == null)
                {
                    entry = new VdfMap();
                    int next = 0;
                    while (list.Items.Any(i => i.Key == next.ToString())) next++;
                    list.Set(next.ToString(), entry);
                }
                entry.Set("appid", unchecked((int)appId));
                entry.Set("AppName", AppName);
                entry.Set("Exe", "\"" + exe + "\"");
                if (OperatingSystem.IsWindows())
                {
                    string winDir = exe.Substring(0, Math.Max(0, exe.LastIndexOfAny(new[] { '\\', '/' })));
                    entry.Set("StartDir", "\"" + winDir + "\\\"");
                    entry.Set("icon", string.IsNullOrEmpty(icon) ? exe : icon);
                    entry.SetDefault("LaunchOptions", "");
                }
                else
                {
                    entry.Set("StartDir", "\"" + dir + "/\"");
                    entry.Set("icon", icon ?? "");
                    entry.Set("LaunchOptions", LaunchOptions);
                }
                entry.SetDefault("ShortcutPath", "");
                entry.Set("IsHidden", 0);
                entry.SetDefault("AllowDesktopConfig", 1);
                entry.SetDefault("AllowOverlay", 1);
                entry.SetDefault("OpenVR", 0);
                entry.SetDefault("Devkit", 0);
                entry.SetDefault("DevkitGameID", "");
                entry.SetDefault("DevkitOverrideAppID", 0);
                entry.SetDefault("LastPlayTime", 0);
                entry.SetDefault("FlatpakAppID", "");
                if (entry.GetMap("tags") == null) entry.Set("tags", new VdfMap());
                WriteSafely(file, BinaryVdf.Write(root));
                done++;
            }
            return done;
        }

        // Whether some account's library has the game with this exe.
        public bool HasShortcut(string exe)
        {
            foreach (string config in AccountConfigDirs())
            {
                string file = Path.Combine(config, "shortcuts.vdf");
                if (!File.Exists(file)) continue;
                try
                {
                    VdfMap list = BinaryVdf.Read(File.ReadAllBytes(file)).GetMap("shortcuts");
                    if (list != null && list.Items.Any(i => i.Value is VdfMap m && m.GetString("Exe") == "\"" + exe + "\"")) return true;
                }
                catch (Exception) { }
            }
            return false;
        }

        // The Proton build Steam uses for the shortcut, by its internal name, or null.
        public string CompatToolOf(uint appId)
        {
            try
            {
                string file = Path.Combine(Root, "config", "config.vdf");
                if (!File.Exists(file)) return null;
                return TextVdf.Read(File.ReadAllText(file))
                    .GetMap("InstallConfigStore")?.GetMap("Software")?.GetMap("Valve")?.GetMap("Steam")
                    ?.GetMap("CompatToolMapping")?.GetMap(appId.ToString())?.GetString("name");
            }
            catch (Exception) { return null; }
        }

        // Turns on Proton for the shortcut, as ticking "Force the use of a specific Steam Play
        // compatibility tool" does.
        public void SetCompatTool(uint appId, ProtonTool tool)
        {
            string file = Path.Combine(Root, "config", "config.vdf");
            VdfMap root = File.Exists(file) ? TextVdf.Read(File.ReadAllText(file)) : new VdfMap();
            VdfMap mapping = root.Path("InstallConfigStore", "Software", "Valve", "Steam", "CompatToolMapping");
            VdfMap entry = new VdfMap();
            entry.Set("name", tool.Name);
            entry.Set("config", "");
            entry.Set("priority", "250");
            mapping.Set(appId.ToString(), entry);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            WriteSafely(file, Encoding.UTF8.GetBytes(TextVdf.Write(root)));
        }

        // Points the shortcut's prefix at the one the game was installed into, so the game runs with
        // its registry entries and saved settings. A prefix Steam already made is kept as a backup.
        public void LinkPrefix(uint appId, string prefix)
        {
            string link = CompatDataDir(appId);
            Directory.CreateDirectory(Path.GetDirectoryName(link));
            FileSystemInfo existing = Directory.Exists(link) || File.Exists(link) ? new DirectoryInfo(link) : null;
            if (existing != null)
            {
                if (existing.LinkTarget != null)
                {
                    if (Path.GetFullPath(existing.LinkTarget) == Path.GetFullPath(prefix)) return;
                    File.Delete(link);
                }
                else
                {
                    Directory.Move(link, link + ".backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                }
            }
            Directory.CreateSymbolicLink(link, prefix);
        }

        static void WriteSafely(string file, byte[] data)
        {
            if (File.Exists(file)) File.Copy(file, file + ".openprostreet.bak", true);
            string temp = file + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, file, true);
        }
    }
}
