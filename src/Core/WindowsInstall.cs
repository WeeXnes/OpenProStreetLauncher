using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace OpenProStreetLauncher
{
    // On Windows the launcher can install itself into the game folder, as <game>\.launcher\OpenProStreetLauncher.exe,
    // with a Start menu entry. Living next to the game, it always finds it, whatever the saved settings
    // say, and its own updates replace that copy. Uninstalling removes both again.
    static class WindowsInstall
    {
        public const string DirName = ".launcher";
        public const string ExeName = "OpenProStreetLauncher.exe";
        const string ShortcutName = "OpenProStreet Launcher.lnk";

        public static string InstallDir(string gameDir) => Path.Combine(gameDir, DirName);
        public static string InstalledExe(string gameDir) => Path.Combine(InstallDir(gameDir), ExeName);

        // The current user's Start menu, which needs no admin rights.
        public static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);

        public static bool IsInstalled(string gameDir) =>
            !string.IsNullOrEmpty(gameDir) && File.Exists(InstalledExe(gameDir));

        public static bool IsRunningInstalled(string gameDir) =>
            IsInstalled(gameDir) && string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe(gameDir)),
                                                  StringComparison.OrdinalIgnoreCase);

        // The game folder when this launcher is an installed copy, else null.
        public static string GameDirOfInstalledCopy()
        {
            string own = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (string.IsNullOrEmpty(own) || !string.Equals(Path.GetFileName(own), DirName, StringComparison.OrdinalIgnoreCase)) return null;
            string parent = Path.GetDirectoryName(own);
            return Game.IsGameDir(parent) ? parent : null;
        }

        // Copies the running launcher in, unless it's already the installed copy, and adds the Start
        // menu entry. Needs write access to the game folder.
        public static void Install(string gameDir)
        {
            string dir = InstallDir(gameDir);
            DirectoryInfo info = Directory.CreateDirectory(dir);
            info.Attributes |= FileAttributes.Hidden; // like .git: it's the launcher's, not the game's
            string target = InstalledExe(gameDir);
            if (!IsRunningInstalled(gameDir))
            {
                string temp = target + ".new";
                File.Copy(Environment.ProcessPath ?? throw new Exception("Can't tell where this launcher is."), temp, true);
                File.Move(temp, target, true);
            }
            if (OperatingSystem.IsWindows()) CreateShortcut(ShortcutPath, target, dir, "Installs, configures and starts Need for Speed: ProStreet with OpenProStreet");
        }

        // Removes the Start menu entry and the installed copy. When that copy is the one running, it's
        // deleted by a short helper once this launcher has closed; the caller closes it.
        public static void Uninstall(string gameDir)
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            string dir = InstallDir(gameDir);
            if (!Directory.Exists(dir)) return;
            if (IsRunningInstalled(gameDir))
            {
                var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("/c");
                info.ArgumentList.Add("timeout /t 3 /nobreak >nul & rmdir /s /q \"" + dir + "\"");
                Process.Start(info);
                return;
            }
            Directory.Delete(dir, true);
        }

        // ---- Start menu shortcut, through the shell's own COM objects ----

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, IntPtr data, uint flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int max, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void CreateShortcut(string lnk, string target, string workingDir, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            IShellLinkW link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(lnk, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}
