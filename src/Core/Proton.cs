using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OpenProStreetLauncher
{
    // Runs a Windows program in the launcher's prefix with one of Steam's Proton builds, the way Steam
    // does for a game, but without Steam running.
    static class Proton
    {
        public static ProcessStartInfo StartInfo(Steam steam, ProtonTool tool, string prefix, string exe, IEnumerable<string> args, string workDir)
        {
            Directory.CreateDirectory(prefix);
            ProcessStartInfo info = new ProcessStartInfo(tool.Script)
            {
                UseShellExecute = false,
                WorkingDirectory = workDir ?? Path.GetDirectoryName(exe),
            };
            info.ArgumentList.Add("run");
            info.ArgumentList.Add(exe);
            if (args != null) foreach (string a in args) info.ArgumentList.Add(a);
            info.Environment["STEAM_COMPAT_DATA_PATH"] = prefix;
            info.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steam.Root;
            info.Environment["WINEDLLOVERRIDES"] = "dinput8=n,b";
            return info;
        }

        // Starts the program and waits until it and everything it started have finished. Installers
        // often start a second program and exit straight away, so this waits for the prefix's
        // wineserver, which only stops once nothing is running in the prefix.
        public static int RunAndWait(Steam steam, ProtonTool tool, string prefix, string exe, IEnumerable<string> args = null, string workDir = null)
        {
            int code;
            using (Process p = Process.Start(StartInfo(steam, tool, prefix, exe, args, workDir)))
            {
                p.WaitForExit();
                code = p.ExitCode;
            }
            string wineserver = tool.Wineserver;
            if (wineserver != null)
            {
                ProcessStartInfo wait = new ProcessStartInfo(wineserver, "-w") { UseShellExecute = false };
                wait.Environment["WINEPREFIX"] = Path.Combine(prefix, "pfx");
                try
                {
                    using Process w = Process.Start(wait);
                    w.WaitForExit();
                }
                catch (Exception) { }
            }
            return code;
        }
    }

    // What's inside a Proton prefix: <prefix>/pfx is the Wine prefix, with drive_c and the registry.
    static class WinePrefix
    {
        public static string DriveC(string prefix) => Path.Combine(prefix, "pfx", "drive_c");

        // A Windows path in the prefix as a Linux path. C: is drive_c, and Z: is the Linux root.
        public static string ToLinux(string prefix, string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 2 || windowsPath[1] != ':') return null;
            char drive = char.ToLowerInvariant(windowsPath[0]);
            string rest = windowsPath.Substring(2).Replace('\\', '/').TrimStart('/');
            string root = drive == 'c' ? DriveC(prefix) : drive == 'z' ? "/" : Path.Combine(prefix, "pfx", "dosdevices", drive + ":");
            return Path.Combine(root, rest).TrimEnd('/');
        }

        // The game's folder in the prefix: where its installer's registry entry says, or the usual
        // place, or anywhere in drive_c that has nfs.exe.
        public static string FindGame(string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || !Directory.Exists(prefix)) return null;
            string reg = Path.Combine(prefix, "pfx", "system.reg");
            if (File.Exists(reg))
            {
                string installDir = ReadRegistryValue(File.ReadAllText(reg), @"Software\\Wow6432Node\\EA GAMES\\Need for Speed ProStreet", "Install Dir")
                                    ?? ReadRegistryValue(File.ReadAllText(reg), @"Software\\EA GAMES\\Need for Speed ProStreet", "Install Dir");
                string dir = ToLinux(prefix, installDir);
                if (Game.IsGameDir(dir)) return dir;
            }
            foreach (string guess in new[] { "Program Files (x86)/EA GAMES/Need for Speed ProStreet", "Program Files/EA GAMES/Need for Speed ProStreet" })
            {
                string dir = Path.Combine(DriveC(prefix), guess);
                if (Game.IsGameDir(dir)) return dir;
            }
            return Search(DriveC(prefix), 5);
        }

        static string Search(string dir, int depth)
        {
            if (depth < 0 || !Directory.Exists(dir)) return null;
            if (Game.IsGameDir(dir)) return dir;
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (name == "windows" || name == "users" || name == "ProgramData") continue;
                    string found = Search(sub, depth - 1);
                    if (found != null) return found;
                }
            }
            catch (Exception) { }
            return null;
        }

        // A string value from a Wine .reg file. Keys are written lower case or not, so they're matched
        // in any case; backslashes in key names are doubled, as in the file.
        public static string ReadRegistryValue(string reg, string key, string name)
        {
            string header = "[" + key + "]";
            int at = reg.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int end = reg.IndexOf("\n[", at + header.Length, StringComparison.Ordinal);
            string block = end < 0 ? reg.Substring(at) : reg.Substring(at, end - at);
            foreach (string raw in block.Split('\n'))
            {
                string line = raw.Trim();
                string prefix = "\"" + name + "\"=\"";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !line.EndsWith('"')) continue;
                return line.Substring(prefix.Length, line.Length - prefix.Length - 1).Replace("\\\\", "\\");
            }
            return null;
        }
    }
}
