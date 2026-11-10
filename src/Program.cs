using System;
using Avalonia;
using Avalonia.Media;

namespace OpenProStreetLauncher
{
    static class Program
    {
        public static string InitialGameDir;
        public static string ScreenshotDir; // --screenshot <dir>: saves each page as a PNG, then exits

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && Cli.IsCommand(args[0])) return Cli.Run(args);
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-gamedir") InitialGameDir = args[i + 1];
                if (args[i] == "--screenshot") ScreenshotDir = args[i + 1];
            }
            if (ScreenshotDir == null && RestartedAsAdmin()) return 0;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // A game folder under Program Files can only be changed with admin rights. When the saved
        // folder is like that, the launcher asks for them as it starts, instead of after a click and
        // a restart. Saying no to Windows' prompt just opens it without them.
        static bool RestartedAsAdmin()
        {
            if (!OperatingSystem.IsWindows() || IsAdmin()) return false;
            string dir = InitialGameDir ?? Game.FindGameDir();
            if (!Game.IsGameDir(dir) || Game.IsWritable(dir)) return false;
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" };
                info.ArgumentList.Add("-gamedir");
                info.ArgumentList.Add(dir);
                return System.Diagnostics.Process.Start(info) != null;
            }
            catch (Exception) { return false; }
        }

        public static bool IsAdmin()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
                // Matches StartupWMClass in the menu entry, so docks group the window with its icon.
                .With(new X11PlatformOptions { WmClass = "OpenProStreetLauncher" })
                .LogToTrace();
    }

    // The commands that run without a window: install, uninstall, update, version, help.
    static class Cli
    {
        public static bool IsCommand(string arg) =>
            arg is "install" or "uninstall" or "update" or "version" or "--version" or "-v" or "help" or "--help" or "-h";

        public static int Run(string[] args)
        {
            bool system = Array.IndexOf(args, "--system") >= 0;
            bool force = Array.IndexOf(args, "--force") >= 0;
            try
            {
                switch (args[0])
                {
                    case "install": return Install(system);
                    case "uninstall": return Uninstall(system);
                    case "update":
                        Console.Write(SelfInstall.Update(force));
                        return 0;
                    case "version":
                    case "--version":
                    case "-v":
                        Console.WriteLine("OpenProStreet Launcher " + AppInfo.Version);
                        return 0;
                    default:
                        Help();
                        return 0;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 1;
            }
        }

        // For everyone with sudo (or --system), otherwise just for this user.
        static InstallTarget Target(bool system)
        {
            if (system && !SelfInstall.IsRoot)
                throw new Exception("Installing for everyone needs root. Run: sudo " + Environment.ProcessPath + " install --system");
            return system || SelfInstall.IsRoot ? InstallTarget.ForSystem() : InstallTarget.ForUser(LauncherSettings.Home);
        }

        static int Install(bool system)
        {
            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("Installation is only needed on Linux to integrate with the desktop application menu.");
                return 0;
            }
            InstallTarget t = Target(system);
            Console.Write(SelfInstall.Install(t, Environment.ProcessPath));
            Console.WriteLine();
            Console.WriteLine("OpenProStreet Launcher is in your application menu (and rofi's drun mode).");
            if (SelfInstall.OnPath(t.BinDir))
                Console.WriteLine("Run it with: " + InstallTarget.Command + ", and update it with: " + InstallTarget.Command + " update");
            else
                Console.WriteLine(t.BinDir + " isn't on your PATH, so run it as " + t.Binary + ", or add that folder to PATH in your shell's profile.");
            return 0;
        }

        static int Uninstall(bool system)
        {
            if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("Installation is only needed on Linux.");
                return 0;
            }
            Console.Write(SelfInstall.Uninstall(Target(system)));
            Console.WriteLine("The game, its prefix and your settings are left as they are.");
            return 0;
        }

        static void Help()
        {
            Console.WriteLine("OpenProStreet Launcher " + AppInfo.Version);
            Console.WriteLine();
            Console.WriteLine("Usage: " + InstallTarget.Command + " [command]");
            Console.WriteLine();
            Console.WriteLine("Without a command, the launcher window opens.");
            Console.WriteLine();
            Console.WriteLine("  install [--system]    Copy this binary to ~/.local/bin and add it to the application menu.");
            Console.WriteLine("                        With sudo or --system: /usr/local/bin, for every user.");
            Console.WriteLine("  uninstall [--system]  Remove what install added. The game and its prefix stay.");
            Console.WriteLine("  update [--force]      Replace this binary with the newest release.");
            Console.WriteLine("  version               Show the version.");
            Console.WriteLine("  help                  Show this.");
        }
    }
}
