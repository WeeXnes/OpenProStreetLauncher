using System;
using System.Diagnostics;
using System.IO;

namespace OpenProStreetLauncher
{
    // Removes a game the launcher installed on Linux, so it can be installed again from scratch. The
    // game lives in the launcher's Proton prefix, so the whole prefix goes: the game, its registry,
    // PunkBuster and the settings in its scripts folder. A game folder the player picked themselves
    // is never deleted. The Steam shortcut stays; installing again uses the same one.
    static class GameUninstall
    {
        // True when the game folder is inside the launcher's own prefix, so removing the prefix
        // removes the game and nothing of the player's.
        public static bool IsInLauncherPrefix(string gameDir, string prefix = null)
        {
            prefix ??= LauncherSettings.PrefixDir;
            if (string.IsNullOrEmpty(gameDir) || string.IsNullOrEmpty(prefix)) return false;
            string root = Path.GetFullPath(prefix).TrimEnd('/') + "/";
            return Path.GetFullPath(gameDir).StartsWith(root, StringComparison.Ordinal);
        }

        // Stops everything still running in the prefix, then deletes it. tool may be null, then
        // nothing is stopped first.
        public static void Uninstall(ProtonTool tool, string prefix = null)
        {
            prefix ??= LauncherSettings.PrefixDir;
            if (!Directory.Exists(prefix)) return;
            StopPrefix(tool, prefix);
            DeleteTree(prefix);
            LauncherSettings.GameDir = "";
        }

        // wineserver -k ends every process in the prefix, the game included.
        static void StopPrefix(ProtonTool tool, string prefix)
        {
            string wineserver = tool?.Wineserver;
            if (wineserver == null) return;
            ProcessStartInfo kill = new ProcessStartInfo(wineserver, "-k") { UseShellExecute = false };
            kill.Environment["WINEPREFIX"] = Path.Combine(prefix, "pfx");
            try
            {
                using Process p = Process.Start(kill);
                p.WaitForExit(10000);
            }
            catch (Exception) { }
        }

        // Deletes a folder and everything in it. A Wine prefix links its drives to the rest of the
        // system (dosdevices/z: is /), so a link is removed itself and never followed.
        public static void DeleteTree(string dir)
        {
            DirectoryInfo info = new DirectoryInfo(dir);
            if (info.LinkTarget != null)
            {
                File.Delete(dir);
                return;
            }
            foreach (FileSystemInfo entry in info.EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget != null || entry is FileInfo) File.Delete(entry.FullName);
                else DeleteTree(entry.FullName);
            }
            Directory.Delete(dir, false);
        }
    }
}
