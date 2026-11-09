using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenProStreetLauncher
{
    // Where the launcher installs itself: for one user under ~/.local, or for everyone under /usr/local.
    // Both are on the default XDG paths, so desktop menus and rofi's drun mode find the entry and icon.
    class InstallTarget
    {
        public const string Command = "openprostreet-launcher";

        public string BinDir, AppsDir, IconDir;
        public bool System;

        public string Binary => Path.Combine(BinDir, Command);
        public string DesktopFile => Path.Combine(AppsDir, Command + ".desktop");
        public string IconFile => Path.Combine(IconDir, Command + ".png");

        public static InstallTarget ForUser(string home) => new InstallTarget
        {
            BinDir = Path.Combine(home, ".local", "bin"),
            AppsDir = Path.Combine(home, ".local", "share", "applications"),
            IconDir = Path.Combine(home, ".local", "share", "icons", "hicolor", "256x256", "apps"),
        };

        public static InstallTarget ForSystem(string root = "/") => new InstallTarget
        {
            BinDir = Path.Combine(root, "usr", "local", "bin"),
            AppsDir = Path.Combine(root, "usr", "local", "share", "applications"),
            IconDir = Path.Combine(root, "usr", "local", "share", "icons", "hicolor", "256x256", "apps"),
            System = true,
        };
    }

    static class SelfInstall
    {
        [DllImport("libc", EntryPoint = "geteuid")]
        static extern uint GetEuid();

        public static bool IsRoot
        {
            get
            {
                try { return OperatingSystem.IsLinux() && GetEuid() == 0; }
                catch (Exception) { return false; }
            }
        }

        // The menu entry. Icon is a theme name, found in hicolor, so every launcher and rofi shows it.
        public static string DesktopEntry(string binary) =>
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=OpenProStreet Launcher\n" +
            "GenericName=Need for Speed: ProStreet launcher\n" +
            "Comment=Install and play Need for Speed: ProStreet online with OpenProStreet\n" +
            "Exec=" + binary + "\n" +
            "TryExec=" + binary + "\n" +
            "Icon=" + InstallTarget.Command + "\n" +
            "Terminal=false\n" +
            "Categories=Game;\n" +
            "Keywords=nfs;need for speed;prostreet;racing;openprostreet;\n" +
            "StartupWMClass=OpenProStreetLauncher\n";

        static byte[] Logo()
        {
            using Stream s = typeof(SelfInstall).Assembly.GetManifestResourceStream("logo.png");
            if (s == null) return null;
            using MemoryStream m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }

        // Copies this binary into place, with its menu entry and icon. Returns what it did, line by line.
        public static string Install(InstallTarget t, string source)
        {
            StringBuilder log = new StringBuilder();
            Directory.CreateDirectory(t.BinDir);
            Directory.CreateDirectory(t.AppsDir);
            Directory.CreateDirectory(t.IconDir);

            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(t.Binary), StringComparison.Ordinal))
            {
                ReplaceFile(source, t.Binary);
                log.Append("Installed ").Append(t.Binary).Append('\n');
            }
            byte[] logo = Logo();
            if (logo != null)
            {
                File.WriteAllBytes(t.IconFile, logo);
                log.Append("Installed the icon ").Append(t.IconFile).Append('\n');
            }
            File.WriteAllText(t.DesktopFile, DesktopEntry(t.Binary));
            MakeExecutable(t.DesktopFile, false);
            log.Append("Added the menu entry ").Append(t.DesktopFile).Append('\n');

            RefreshCaches(t);
            return log.ToString();
        }

        public static string Uninstall(InstallTarget t)
        {
            StringBuilder log = new StringBuilder();
            foreach (string file in new[] { t.DesktopFile, t.IconFile, t.Binary })
            {
                if (!File.Exists(file)) continue;
                File.Delete(file);
                log.Append("Removed ").Append(file).Append('\n');
            }
            RefreshCaches(t);
            return log.Length > 0 ? log.ToString() : "Nothing to remove: it isn't installed there.\n";
        }

        // Copies next to the target first and renames over it, so a running copy keeps working and a
        // failed copy never leaves half a binary.
        public static void ReplaceFile(string source, string target)
        {
            string temp = target + ".new";
            File.Copy(source, temp, true);
            MakeExecutable(temp, true);
            File.Move(temp, target, true);
        }

        public static void MakeExecutable(string path, bool executable)
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
            UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            if (executable) mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }

        static void RefreshCaches(InstallTarget t)
        {
            Run("update-desktop-database", t.AppsDir);
            string hicolor = Path.GetFullPath(Path.Combine(t.IconDir, "..", ".."));
            Run("gtk-update-icon-cache", "-q -t -f " + Quote(hicolor));
        }

        static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

        static void Run(string program, string args)
        {
            try
            {
                using Process p = Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                p?.WaitForExit(10000);
            }
            catch (Exception) { } // optional tools: menus refresh on their own without them
        }

        public static bool OnPath(string dir)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string part in path.Split(':'))
                if (part.Length > 0 && string.Equals(Path.GetFullPath(part).TrimEnd('/'), Path.GetFullPath(dir).TrimEnd('/'), StringComparison.Ordinal)) return true;
            return false;
        }

        // The running binary. A single-file .NET app is its own file, so this is what update replaces.
        public static string CurrentBinary => Environment.ProcessPath;

        // Checks the newest release and replaces the running binary with it. Returns a message for the
        // console; throws when it can't.
        public static string Update(bool force)
        {
            Build build = Downloads.LatestLauncher();
            Version latest = Downloads.ParseVersion(build.Version);
            Version current = Downloads.ParseVersion(AppInfo.Version);
            if (!force && latest != null && current != null && latest <= current)
                return "OpenProStreet Launcher " + AppInfo.Version + " is the newest.\n";

            string target = CurrentBinary ?? throw new Exception("Can't tell where this launcher's binary is.");
            string dir = Path.GetDirectoryName(target);
            if (!CanWrite(dir))
                throw new Exception("Can't write to " + dir + ". Run it with sudo: sudo " + InstallTarget.Command + " update");

            string download = target + ".download";
            try
            {
                Downloads.DownloadArtifact(build, Downloads.LauncherArtifact, download);
                if (OperatingSystem.IsWindows())
                {
                    byte[] head = new byte[2];
                    using (FileStream f = File.OpenRead(download)) f.ReadExactly(head);
                    if (head[0] != 'M' || head[1] != 'Z')
                        throw new Exception("The download isn't a Windows program, so nothing was replaced.");
                    string old = target + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(target, old);
                    File.Move(download, target);
                }
                else
                {
                    byte[] head = new byte[4];
                    using (FileStream f = File.OpenRead(download)) f.ReadExactly(head);
                    if (head[0] != 0x7F || head[1] != 'E' || head[2] != 'L' || head[3] != 'F')
                        throw new Exception("The download isn't a Linux program, so nothing was replaced.");
                    MakeExecutable(download, true);
                    File.Move(download, target, true);
                }
            }
            finally
            {
                if (File.Exists(download)) File.Delete(download);
            }
            return "Updated OpenProStreet Launcher from " + AppInfo.Version + " to " + (build.Version ?? "the newest build") + " (" + target + ").\n";
        }

        static bool CanWrite(string dir)
        {
            string probe = Path.Combine(dir, ".openprostreet-write-test-" + Environment.ProcessId);
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
