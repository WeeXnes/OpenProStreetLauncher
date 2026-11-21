using System;
using System.IO;
using System.Linq;

namespace OpenProStreetLauncher
{
    // What the install from a disc image needs from the player.
    class InstallPlan
    {
        public string IsoPath;     // the game's DVD as an .iso
        public string PatchPath;   // the official 1.1 patch, or null if the disc already has it
        public string NoCdPath;    // a supported nfs.exe 1.1, which replaces the installed one
        public ProtonTool Tool;
    }

    // The steps from a disc image to a game Steam can start, each reported as it happens.
    class Setup
    {
        readonly Steam m_steam;
        readonly Action<string> m_step;          // a new step started
        readonly Action<double> m_progress;      // 0..1 within the step, or -1 when it can't tell

        public Setup(Steam steam, Action<string> step, Action<double> progress)
        {
            m_steam = steam;
            m_step = step;
            m_progress = progress;
        }

        // Returns the installed game's folder.
        public string Install(InstallPlan plan)
        {
            string prefix = LauncherSettings.PrefixDir;
            string disc = Path.Combine(LauncherSettings.DataDir, "disc");
            try
            {
                m_step("Copying the game's files from the disc image");
                if (Directory.Exists(disc)) Directory.Delete(disc, true);
                Disc.Extract(plan.IsoPath, disc, (done, total) => m_progress(total > 0 ? (double)done / total : -1));
                string installer = Disc.FindInstaller(disc) ?? throw new Exception("The disc image has no installer (autorun.inf, easetup.exe or setup.exe).");

                m_step("Running the game's installer. Click through it and enter your CD key. Keep the folder it suggests.");
                m_progress(-1);
                Proton.RunAndWait(m_steam, plan.Tool, prefix, installer, null, Path.GetDirectoryName(installer));
                string gameDir = WinePrefix.FindGame(prefix)
                    ?? throw new Exception("The installer finished, but there's no nfs.exe in the prefix. If you closed the installer early, try again.");

                if (!string.IsNullOrEmpty(plan.PatchPath))
                {
                    m_step("Installing the 1.1 patch. Click through its window.");
                    Proton.RunAndWait(m_steam, plan.Tool, prefix, plan.PatchPath, null, Path.GetDirectoryName(plan.PatchPath));
                }

                FinishGameFolder(gameDir, plan.NoCdPath);
                return gameDir;
            }
            finally
            {
                try { if (Directory.Exists(disc)) Directory.Delete(disc, true); } catch (Exception) { }
            }
        }

        // The steps after the game is installed, which also apply to a game folder copied from elsewhere.
        public void FinishGameFolder(string gameDir, string noCdPath)
        {
            if (!string.IsNullOrEmpty(noCdPath))
            {
                m_step("Putting your nfs.exe in place");
                m_progress(-1);
                if (!Game.IsSupportedExe(noCdPath)) throw new Exception("That nfs.exe isn't the unpacked version 1.1 the client needs.");
                string exe = Game.ExePath(gameDir) ?? Path.Combine(gameDir, Game.ExeName);
                string original = exe + ".original";
                if (File.Exists(exe) && !File.Exists(original)) File.Copy(exe, original);
                File.Copy(noCdPath, exe, true);
            }
            string gameExe = Game.ExePath(gameDir);
            if (OperatingSystem.IsLinux() && gameExe != null && ExeFix.Needed(gameExe))
            {
                m_step("Fixing nfs.exe so it runs in Proton");
                ExeFix.Apply(gameExe);
            }

            string scripts = Game.ScriptsDir(gameDir);
            m_step("Installing the Ultimate ASI Loader");
            Downloads.InstallLoader(gameDir);
            m_step("Installing XNFSPS MultiFix");
            Downloads.InstallMultiFix(scripts);
            m_step("Installing the OpenProStreet client");
            Downloads.InstallClient(scripts, Downloads.LatestClient(LauncherSettings.ClientBranch), false);
            // Controller support, on by default, on Windows and under Proton.
            m_step("Installing controller support");
            Downloads.InstallXtendedInput(gameDir, false);
            LauncherSettings.GameDir = gameDir;
        }

        // Adds the game to Steam with Proton and the ASI loader's launch option. A game installed into
        // the launcher's prefix keeps using it. Steam must be closed.
        public uint AddToSteam(string gameDir, ProtonTool tool, string icon)
        {
            m_step("Adding the game to Steam");
            string exe = Game.ExePath(gameDir) ?? throw new Exception("There's no nfs.exe in " + gameDir + ".");
            if (m_steam.AccountConfigDirs().Count == 0) throw new Exception("No Steam account has signed in on this PC yet. Sign in to Steam once, then try again.");
            m_steam.AddShortcut(exe, icon);
            uint appId = Steam.ShortcutAppId(exe);
            if (OperatingSystem.IsLinux())
            {
                if (tool != null) m_steam.SetCompatTool(appId, tool);
                string prefix = LauncherSettings.PrefixDir;
                if (Directory.Exists(prefix) && Path.GetFullPath(gameDir).StartsWith(Path.GetFullPath(prefix) + "/", StringComparison.Ordinal))
                    m_steam.LinkPrefix(appId, prefix);
            }
            return appId;
        }
    }
}
