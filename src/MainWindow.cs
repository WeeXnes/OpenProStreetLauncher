using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = System.IO.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenProStreetLauncher
{
    public class MainWindow : Window
    {
        const double Pad = 32;

        readonly Steam m_steam = Steam.Find();
        string m_gameDir = Program.InitialGameDir ?? Game.FindGameDir();
        Build m_latest;
        bool m_latestFailed, m_busy, m_updateOffered;

        readonly Button m_navHome, m_navInstall, m_navSettings;
        readonly ContentControl m_page = new ContentControl();
        Control m_homePage, m_installPage, m_settingsPage;

        // Home
        TextBlock m_folder, m_status;
        StatusRow m_rowGame, m_rowLoader, m_rowMultiFix, m_rowClient, m_rowControllers, m_rowHdReflections, m_rowTextures, m_rowDxvk, m_rowSteam, m_rowConflicts;
        Button m_play, m_admin, m_steamPlay, m_uninstallGame;
        Border m_footer;
        StackPanel m_homeReady, m_homeEmpty;

        // Install
        TextBox m_iso, m_patch, m_nocd;
        TextBlock m_nocdNote, m_stepText, m_installNote;
        ComboBox m_tool;
        ProgressBar m_progress;
        StackPanel m_stepLog;
        Button m_installButton;

        // Settings
        RadioButton m_serverDefault, m_serverCustom, m_serverLan;
        TextBox m_address, m_port, m_username, m_password;
        CheckBox m_autoLogin;
        CheckBox m_reshifted, m_shiftMemory, m_shiftCoast, m_shiftQuick, m_shiftSkip, m_shiftIndicator;
        RadioButton m_shiftAdaptive, m_shiftClassic;
        Slider m_shortShift, m_sportShift, m_ecoThrottle, m_sportThrottle, m_quickShiftTime;
        StackPanel m_shiftOptions, m_shiftAdaptiveOptions;
        Control m_quickShiftTimeRow;
        CheckBox m_tank, m_speed, m_borderless, m_vibration, m_console, m_uncap;
        CheckBox m_overlay, m_notifications, m_scoreboard, m_overlayPing, m_overlayChat, m_overlayLobbyCars, m_overlayPaceBar;
        ComboBox m_overlayPosition, m_overlayToggleKey, m_overlayScoreboardKey, m_overlaySettingsKey;
        Slider m_overlayScale;
        StackPanel m_overlayOptions;
        CheckBox m_atmosphere;
        ComboBox m_atmosphereStart;
        StackPanel m_atmosphereOptions;
        CheckBox m_rightStickOrbit, m_orbitBlockShift, m_orbitInvertX;
        Slider m_orbitDeadzone, m_orbitSnapTime, m_orbitBackTime;
        StackPanel m_cameraOptions;
        CheckBox m_verbose, m_colour, m_dumpEvents;
        ComboBox m_clientBranch;
        TextBox m_clientBranchCustom;
        TextBlock m_clientBranchNote;
        TextBlock m_settingsNote, m_diagnosticsNote;
        TextBlock m_installText;
        Button m_installButton2, m_uninstallButton;

        public MainWindow()
        {
            Title = "OpenProStreet Launcher";
            Width = 760;
            Height = 720;
            MinWidth = 640;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OpenProStreetLauncher/assets/logo.png"))); } catch (Exception) { }

            // Header: the logo and wordmark on the left, plain text navigation on the right.
            Image logo = new Image { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
            try { logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://OpenProStreetLauncher/assets/logo.png"))); } catch (Exception) { }
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(logo);
            brand.Children.Add(Ui.Text("OpenProStreet", "wordmark"));
            TextBlock app = Ui.Text("/  Launcher", "wordmark");
            app.Foreground = Ui.Idle;
            brand.Children.Add(app);
            m_navHome = Nav("Home", () => ShowPage(m_homePage));
            m_navInstall = Nav("Install", () => ShowPage(m_installPage));
            m_navInstall.IsVisible = !OperatingSystem.IsWindows();
            m_navSettings = Nav("Settings", () => ShowPage(m_settingsPage));
            StackPanel nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            nav.Children.Add(m_navHome);
            nav.Children.Add(m_navInstall);
            nav.Children.Add(m_navSettings);
            Grid header = new Grid { Height = 60, Margin = new Thickness(24, 0, 20, 0) };
            header.Children.Add(brand);
            header.Children.Add(nav);

            m_homePage = BuildHome();
            m_installPage = BuildInstall();
            m_settingsPage = BuildSettings();

            DockPanel root = new DockPanel();
            Border headerBorder = new Border { Child = header, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(headerBorder, Dock.Top);
            root.Children.Add(headerBorder);
            // Play and the status line stay at the bottom, so they fit on a Steam Deck's screen too.
            Grid footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(Pad, 14, Pad, 14) };
            m_status.VerticalAlignment = VerticalAlignment.Center;
            m_status.Margin = new Thickness(0, 0, 16, 0);
            footerGrid.Children.Add(m_status);

            StackPanel footerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
            if (m_admin != null) footerButtons.Children.Add(m_admin);
            if (OperatingSystem.IsWindows()) footerButtons.Children.Add(m_steamPlay);
            footerButtons.Children.Add(m_play);
            Grid.SetColumn(footerButtons, 1);
            footerGrid.Children.Add(footerButtons);

            m_footer = new Border { Child = footerGrid, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(m_footer, Dock.Bottom);
            root.Children.Add(m_footer);
            root.Children.Add(new ScrollViewer { Content = m_page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            Content = root;

            ShowPage(m_homePage);
            Opened += async (s, e) =>
            {
                // Whichever way the game was found, remember it for next time.
                if (Program.ScreenshotDir == null && Game.IsGameDir(m_gameDir) && LauncherSettings.GameDir != m_gameDir) LauncherSettings.GameDir = m_gameDir;
                RefreshHome();
                if (Program.ScreenshotDir != null)
                {
                    await SaveScreenshots(Program.ScreenshotDir);
                    Close();
                    return;
                }
                await CheckForUpdate();
                await CheckLauncherUpdate();
            };
        }

        Button Nav(string text, Action click)
        {
            Button b = Ui.Button(text, false, click);
            b.Classes.Add("nav");
            return b;
        }

        void ShowPage(Control page)
        {
            m_page.Content = page;
            m_navHome.Classes.Set("active", page == m_homePage);
            m_navInstall.Classes.Set("active", page == m_installPage);
            m_navSettings.Classes.Set("active", page == m_settingsPage);
            if (page == m_homePage) RefreshHome();
            else m_footer.IsVisible = false;
            if (page == m_settingsPage) LoadSettings();
            if (page == m_installPage) RefreshTools();
        }

        static StackPanel Page() => new StackPanel { Margin = new Thickness(Pad, 28, Pad, Pad), Spacing = 0 };

        static Border List(params StatusRow[] rows)
        {
            StackPanel p = new StackPanel();
            foreach (StatusRow r in rows) p.Children.Add(r);
            Border b = new Border { Child = p };
            b.Classes.Add("list");
            return b;
        }

        // ---- Home ----

        Control BuildHome()
        {
            StackPanel page = Page();

            // No game yet: the ways to get one.
            if (OperatingSystem.IsWindows())
            {
                StatusRow fromFolder = new StatusRow("Select game folder", true);
                fromFolder.Set(Ui.Idle, "Pick the folder with nfs.exe in it.", "Choose", ChooseFolder);
                m_homeEmpty = Ui.Stack(0,
                    Ui.Text("Select Need for Speed: ProStreet folder", "title"),
                    Spaced(Ui.Text("Pick the folder where Need for Speed: ProStreet is installed. The launcher will check components and configure OpenProStreet."), 8, 24),
                    List(fromFolder));
            }
            else
            {
                StatusRow fromDisc = new StatusRow("Install from your disc image");
                fromDisc.Set(Ui.Idle, "Runs the game's installer, the 1.1 patch and your nfs.exe in Proton, then adds everything else.", "Start", () => { ShowPage(m_installPage); return Task.CompletedTask; });
                StatusRow fromFolder = new StatusRow("Use a game folder you already have", true);
                fromFolder.Set(Ui.Idle, "A folder with nfs.exe in it. The launcher installs the rest and adds it to Steam.", "Choose", ChooseFolder);
                m_homeEmpty = Ui.Stack(0,
                    Ui.Text("Set up Need for Speed: ProStreet", "title"),
                    Spaced(Ui.Text("The game runs in Steam with Proton. Install it from your own disc image, or use a game folder you already have, for example one copied from Windows."), 8, 24),
                    List(fromDisc, fromFolder));
            }
            page.Children.Add(m_homeEmpty);

            // A game: what's installed, and Play.
            m_folder = Ui.Text("", "strong");
            Grid folderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            folderRow.Children.Add(Ui.Stack(6, Ui.Micro("Game folder"), m_folder));
            Button change = Ui.Button("Change", false, async () => await ChooseFolder());
            m_uninstallGame = Ui.Button("Uninstall", false, async () => await UninstallGame());
            m_uninstallGame.IsVisible = false;
            StackPanel folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            folderButtons.Children.Add(m_uninstallGame);
            folderButtons.Children.Add(change);
            Grid.SetColumn(folderButtons, 1);
            folderRow.Children.Add(folderButtons);

            m_rowGame = new StatusRow("Game");
            m_rowLoader = new StatusRow("Ultimate ASI Loader");
            m_rowMultiFix = new StatusRow("XNFSPS MultiFix");
            m_rowClient = new StatusRow("OpenProStreet client");
            m_rowControllers = new StatusRow("Controller support");
            m_rowHdReflections = new StatusRow("HD reflections");
            m_rowTextures = new StatusRow("Texture packs");
            m_rowDxvk = new StatusRow("DXVK (Vulkan)");
            m_rowDxvk.IsVisible = OperatingSystem.IsWindows(); // Proton runs the game on DXVK already
            m_rowSteam = new StatusRow(OperatingSystem.IsWindows() ? "Steam shortcut" : "Steam and Proton");
            m_rowConflicts = new StatusRow("Conflicting plugins", true);

            m_status = Ui.Text("", "small");
            m_admin = Ui.Button("Restart as admin", false, RestartAsAdmin);
            m_admin.Classes.Add("big");
            m_admin.IsVisible = false;
            // On Windows, a second way to start: through the game's Steam shortcut, so Steam's overlay,
            // playtime and controller settings apply. Until there's a shortcut, it adds one.
            m_steamPlay = Ui.Button("Add to Steam", false, async () => await SteamPlay());
            m_steamPlay.Classes.Add("big");
            m_steamPlay.IsVisible = false;
            m_play = Ui.Button("Play", true, async () => await Play());
            m_play.Classes.Add("big");
            m_play.HorizontalAlignment = HorizontalAlignment.Right;

            m_homeReady = Ui.Stack(0,
                folderRow,
                Spaced(Ui.Micro("Components"), 32, 12),
                List(m_rowGame, m_rowLoader, m_rowMultiFix, m_rowClient, m_rowControllers, m_rowHdReflections, m_rowTextures, m_rowDxvk, m_rowSteam, m_rowConflicts),
                new Control { Height = 8 });
            page.Children.Add(m_homeReady);
            return page;
        }

        static T Spaced<T>(T c, double top, double bottom) where T : Control
        {
            c.Margin = new Thickness(0, top, 0, bottom);
            return c;
        }

        string Scripts => Game.ScriptsDir(m_gameDir);

        void SetStatus(string text, IBrush colour = null)
        {
            m_status.Text = text;
            m_status.Foreground = colour ?? new SolidColorBrush(Color.Parse("#A3A3A3"));
        }

        void RefreshHome()
        {
            bool have = Game.IsGameDir(m_gameDir);
            m_homeEmpty.IsVisible = !have;
            m_homeReady.IsVisible = have;
            if (m_footer != null) m_footer.IsVisible = have && m_page.Content == m_homePage;
            if (!have) return;

            m_folder.Text = m_gameDir;
            Framerate.FixOnce(m_gameDir);
            m_uninstallGame.IsVisible = OperatingSystem.IsLinux() && GameUninstall.IsInLauncherPrefix(m_gameDir);
            m_uninstallGame.IsEnabled = !m_busy;
            m_play.IsEnabled = !m_busy;
            m_steamPlay.IsEnabled = !m_busy;
            string exe = Game.ExePath(m_gameDir);

            if (OperatingSystem.IsLinux() && Game.IsSupportedExe(exe) && ExeFix.Needed(exe))
                m_rowGame.Set(Ui.Warning, "nfs.exe version 1.1, but it crashes in Proton until its headers are fixed", "Fix",
                    () => Run("Fixing nfs.exe for Proton...", () => ExeFix.Apply(exe)));
            else if (Game.IsSupportedExe(exe)) m_rowGame.Set(Ui.Success, OperatingSystem.IsWindows() ? "nfs.exe version 1.1, ready" : "nfs.exe version 1.1, ready for Proton");
            else m_rowGame.Set(Ui.Danger, "This nfs.exe can't be patched. Use the unpacked 1.1 No-CD exe.", "Replace", ReplaceExe);

            if (Game.FindFile(m_gameDir, Game.Loader) != null) m_rowLoader.Set(Ui.Success, "Installed");
            else m_rowLoader.Set(Ui.Danger, "Missing. Without it, nothing in the scripts folder loads.", "Install",
                () => Run("Installing the ASI loader...", () => Downloads.InstallLoader(m_gameDir)));

            bool mfAsi = Game.FindFile(Scripts, Game.MultiFixAsi) != null, mfIni = Game.FindFile(Scripts, Game.MultiFixIni) != null;
            if (mfAsi && mfIni) m_rowMultiFix.Set(Ui.Success, "Installed");
            else m_rowMultiFix.Set(Ui.Danger, mfAsi ? "Its ini file is missing" : "Missing. The client needs it.", "Install",
                () => Run("Installing XNFSPS MultiFix...", () => Downloads.InstallMultiFix(Scripts)));

            string branch = LauncherSettings.ClientBranch;
            bool featureBranch = !Downloads.IsDefaultBranch(branch);
            string clientAsi = Game.FindFile(Scripts, Game.ClientAsi);
            Version installed = Game.FileVersionOf(clientAsi);
            string installedProduct = Game.ProductVersionOf(clientAsi);
            string shownInstalled = !string.IsNullOrEmpty(installedProduct) && installedProduct.Contains("-fb-", StringComparison.OrdinalIgnoreCase)
                ? installedProduct
                : Game.Show(installed);
            Version latest = m_latest != null ? Downloads.ParseVersion(m_latest.Version) : null;
            bool sdk = Game.FindFile(Scripts, Game.DiscordSdk) != null;
            bool hasFbInstalled = !string.IsNullOrEmpty(installedProduct) && installedProduct.Contains("-fb-", StringComparison.OrdinalIgnoreCase);

            if (installed == null)
                m_rowClient.Set(Ui.Danger, "Not installed", "Install", () => InstallClient(false));
            else if (featureBranch && m_latest != null && !string.Equals(installedProduct, m_latest.Version, StringComparison.OrdinalIgnoreCase))
                m_rowClient.Set(Ui.Warning, "Installed: " + shownInstalled + ". Branch '" + branch + "' (" + (m_latest.Version ?? "latest") + ") is ready.", "Switch", () => InstallClient(true));
            else if (!featureBranch && m_latest != null && (hasFbInstalled || (latest != null && latest > installed)))
                m_rowClient.Set(Ui.Warning, hasFbInstalled
                    ? ("Feature build " + shownInstalled + " installed. Stable " + Game.Show(latest) + " is available.")
                    : ("Version " + shownInstalled + ". Version " + Game.Show(latest) + " is out."),
                    hasFbInstalled ? "Switch" : "Update", () => InstallClient(true));
            else if (!sdk)
                m_rowClient.Set(Ui.Warning, "Version " + shownInstalled + ", but discord_game_sdk.dll is missing", "Install", () => InstallClient(false));
            else if (m_latest != null)
                m_rowClient.Set(Ui.Success, "Version " + shownInstalled + (featureBranch ? " (" + branch + ")" : "") + ", the newest");
            else
                m_rowClient.Set(Ui.Success, "Version " + shownInstalled + (featureBranch ? " (" + branch + ")" : "") + (m_latestFailed ? ". Couldn't check for updates." : ". Checking for updates..."));

            // NFS-XtendedInput: on while its plugin is in scripts. Updated with the client.
            if (Game.FindFile(Scripts, Game.XtendedInputAsi) != null)
                m_rowControllers.Set(Ui.Success, "On (NFS-XtendedInput). Xbox pads, and Stadia or PlayStation pads through Steam Input.", "Turn off",
                    () => Run("Turning controller support off...", () => Downloads.RemoveXtendedInput(m_gameDir)));
            else
                m_rowControllers.Set(Ui.Idle, "Off. The game reads controllers through DirectInput.", "Turn on",
                    () => Run("Turning controller support on...", () => Downloads.InstallXtendedInput(m_gameDir, true)));

            // NFS HD Reflections: on while its plugin is in scripts. Updated with the client.
            if (Game.FindFile(Scripts, Game.HdReflectionsAsi) != null)
                m_rowHdReflections.Set(Ui.Success, "On (NFSPSHDReflections). Higher-resolution car reflections in races.", "Turn off",
                    () => Run("Turning HD reflections off...", () => Downloads.RemoveHdReflections(m_gameDir)));
            else
                m_rowHdReflections.Set(Ui.Idle, "Off. Cars use the game's built-in 256 px reflections.", "Turn on",
                    () => Run("Turning HD reflections on...", () => Downloads.InstallHdReflections(m_gameDir, true)));

            // Texture packs: unpacked into scripts, where the client swaps them in.
            List<TexturePacks.Pack> packs = TexturePacks.Installed(m_gameDir);
            if (packs.Count == 0)
                m_rowTextures.Set(Ui.Idle, "None. Add a TexMod pack (.tpf) to change how the game looks.", "Add pack", AddTexturePack);
            else
                m_rowTextures.Set(Ui.Success, string.Join(", ", packs.Select(p => p.Name + " (" + p.Textures + " textures)")), "Manage", ManageTexturePacks);

            // DXVK: its d3d9.dll next to nfs.exe. One from something else is left alone.
            if (Dxvk.IsInstalled(m_gameDir))
                m_rowDxvk.Set(Ui.Success, "On (" + Dxvk.InstalledVersion(m_gameDir) + "). The game draws through Vulkan, so ReShade's Vulkan version works with it.", "Turn off",
                    () => Run("Turning DXVK off...", () => Dxvk.Remove(m_gameDir)));
            else if (Dxvk.HasOtherDll(m_gameDir))
                m_rowDxvk.Set(Ui.Warning, "Another d3d9.dll is in the game folder, such as ReShade for DirectX 9. Remove it to use DXVK.");
            else
                m_rowDxvk.Set(Ui.Idle, "Off. The game draws through DirectX 9. DXVK needs a graphics card and driver with current Vulkan support.", "Turn on",
                    () => Run("Downloading DXVK...", () => Dxvk.Install(m_gameDir)));

            if (OperatingSystem.IsWindows())
            {
                bool inSteam = m_steam != null && exe != null && m_steam.HasShortcut(exe);
                if (m_steam == null) m_rowSteam.Set(Ui.Idle, "Steam not found (optional)");
                else if (inSteam) m_rowSteam.Set(Ui.Success, "In your Steam library");
                else m_rowSteam.Set(Ui.Idle, "Not in your Steam library (optional)", "Add", () => AddToSteam(true));
                m_steamPlay.IsVisible = m_steam != null && exe != null;
                m_steamPlay.Content = inSteam ? "Play via Steam" : "Add to Steam";
            }
            else
            {
                if (m_steam == null) m_rowSteam.Set(Ui.Danger, "Steam isn't installed. Install it from your distribution, start it once and sign in.");
                else if (exe != null && m_steam.HasShortcut(exe))
                {
                    string tool = m_steam.CompatToolOf(Steam.ShortcutAppId(exe));
                    if (tool != null) m_rowSteam.Set(Ui.Success, "In your Steam library, with " + (m_steam.ProtonTools().FirstOrDefault(t => t.Name == tool)?.DisplayName ?? tool));
                    else m_rowSteam.Set(Ui.Warning, "In your Steam library, but Proton isn't turned on for it", "Fix", () => AddToSteam());
                }
                else m_rowSteam.Set(Ui.Warning, "Not in your Steam library yet", "Add", () => AddToSteam());
            }

            string fusion = Game.FindFile(Scripts, "NFSProStreet.FusionFix.asi"), rpc = Game.FindFile(Scripts, "ProStreetRPC.asi");
            if (fusion != null) m_rowConflicts.Set(Ui.Warning, "Fusion Fix doesn't work together with the client.", "Disable", () => Disable(fusion));
            else if (rpc != null) m_rowConflicts.Set(Ui.Warning, "ProStreetRPC is the client's old version. Both together break the game.", "Disable", () => Disable(rpc));
            else m_rowConflicts.Set(Ui.Success, "None found");

            if (OperatingSystem.IsWindows() && !Game.IsWritable(m_gameDir))
            {
                m_admin.IsVisible = true;
                m_admin.IsEnabled = !m_busy;
                SetStatus("Game folder is read-only. Restart as admin to make changes.", Ui.Warning);
            }
            else
            {
                m_admin.IsVisible = false;
            }
        }

        async Task CheckForUpdate()
        {
            string branch = LauncherSettings.ClientBranch;
            m_latestFailed = false;
            try { m_latest = await Task.Run(() => Downloads.LatestClient(branch)); }
            catch (Exception) { m_latest = null; m_latestFailed = true; }
            RefreshHome();
            if (!Game.IsGameDir(m_gameDir)) return;
            string clientAsi = Game.FindFile(Scripts, Game.ClientAsi);
            Version installed = Game.FileVersionOf(clientAsi);
            string installedProduct = Game.ProductVersionOf(clientAsi);
            Version latest = m_latest != null ? Downloads.ParseVersion(m_latest.Version) : null;
            if (installed == null || latest == null || Downloads.IsDefaultBranch(branch) && latest <= installed || m_updateOffered) return;
            if (!Downloads.IsDefaultBranch(branch) && string.Equals(installedProduct, m_latest.Version, StringComparison.OrdinalIgnoreCase)) return;
            m_updateOffered = true;
            string latestText = !string.IsNullOrEmpty(m_latest.Version) ? m_latest.Version : Game.Show(latest);
            if (await Dialog.Ask(this, "Update available", "OpenProStreet client " + latestText + " (" + branch + ") is out. You have " + (installedProduct ?? Game.Show(installed)) + ". Update now?", "Update", "Not now"))
                await InstallClient(true);
        }

        // Offers the launcher's own new release, the same thing "openprostreet-launcher update" does.
        async Task CheckLauncherUpdate()
        {
            Build build;
            try { build = await Task.Run(Downloads.LatestLauncher); }
            catch (Exception) { return; }
            Version latest = Downloads.ParseVersion(build.Version), current = Downloads.ParseVersion(AppInfo.Version);
            if (latest == null || current == null || latest <= current) return;
            if (!await Dialog.Ask(this, "Launcher update", "OpenProStreet Launcher " + build.Version + " is out. You have " + AppInfo.Version + ". Update now?", "Update", "Not now"))
                return;
            string result;
            try { result = await Task.Run(() => SelfInstall.Update(false)); }
            catch (Exception e)
            {
                await Dialog.Show(this, "Couldn't update the launcher", e.Message);
                return;
            }
            if (await Dialog.Ask(this, "Launcher updated", result.Trim() + " Restart it now?", "Restart", "Later"))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelfInstall.CurrentBinary) { UseShellExecute = false }); }
                catch (Exception) { }
                Close();
            }
        }

        Task InstallClient(bool replace) => Run(replace ? "Updating the client..." : "Installing the client...", () =>
        {
            Build build = m_latest ?? Downloads.LatestClient(LauncherSettings.ClientBranch);
            m_latest = build;
            Downloads.InstallClient(Scripts, build, replace);
            // Controller support and HD reflections that are on update with the client.
            if (Game.FindFile(Scripts, Game.XtendedInputAsi) != null) Downloads.InstallXtendedInput(m_gameDir, replace);
            if (Game.FindFile(Scripts, Game.HdReflectionsAsi) != null) Downloads.InstallHdReflections(m_gameDir, replace);
        });

        async Task<bool> EnsureWritable()
        {
            if (!OperatingSystem.IsWindows() || Game.IsWritable(m_gameDir)) return true;
            if (await Dialog.Ask(this, "Admin rights needed", "The game folder can only be changed with admin rights. Restart the launcher as admin?", "Restart", "Cancel"))
                RestartAsAdmin();
            return false;
        }

        void RestartAsAdmin()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" };
                if (m_gameDir != null) { info.ArgumentList.Add("-gamedir"); info.ArgumentList.Add(m_gameDir); }
                System.Diagnostics.Process.Start(info);
                Close();
            }
            catch (Exception) { }
        }

        async Task Disable(string path)
        {
            if (!await EnsureWritable()) return;
            try
            {
                string disabled = path + ".disabled";
                if (File.Exists(disabled)) File.Delete(disabled);
                File.Move(path, disabled);
                SetStatus("Disabled " + Path.GetFileName(path) + ". Remove .disabled from its name to turn it back on.");
            }
            catch (Exception e) { await Dialog.Show(this, "Couldn't disable it", e.Message); }
            RefreshHome();
        }

        async Task Run(string message, Action work)
        {
            if (m_busy) return;
            if (!await EnsureWritable()) return;
            m_busy = true;
            SetStatus(message);
            m_play.IsEnabled = false;
            m_steamPlay.IsEnabled = false;
            if (m_admin != null) m_admin.IsEnabled = false;
            try
            {
                await Task.Run(work);
                SetStatus("Done.");
            }
            catch (Exception e)
            {
                SetStatus("");
                await Dialog.Show(this, "Something went wrong", e.Message);
            }
            finally
            {
                m_busy = false;
                RefreshHome();
            }
        }

        async Task AddTexturePack()
        {
            string file = await PickFile("TexMod texture pack", "*.tpf", "*.zip");
            if (file == null) return;
            await Run("Unpacking the texture pack. A large one takes a moment...", () => TexturePacks.Install(m_gameDir, file));
        }

        // The installed packs, each with a way to remove it, and a way to add another.
        async Task ManageTexturePacks()
        {
            Func<Task> next = null;
            Window w = new Window
            {
                Title = "Texture packs",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            StackPanel list = new StackPanel { Spacing = 12 };
            foreach (TexturePacks.Pack pack in TexturePacks.Installed(m_gameDir))
            {
                string name = pack.Name;
                Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                StackPanel text = Ui.Stack(2, Ui.Text(name, "strong"),
                    Ui.Text(pack.Textures + " textures, " + (pack.Bytes / (1024 * 1024)) + " MB", "small"));
                text.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(text);
                Button remove = Ui.Button("Remove", false, () =>
                {
                    next = () => Run("Removing the texture pack...", () => TexturePacks.Remove(m_gameDir, name));
                    w.Close();
                });
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button("Add pack", false, () => { next = AddTexturePack; w.Close(); }));
            buttons.Children.Add(Ui.Button("Close", true, () => w.Close()));
            w.Content = new Border
            {
                Padding = new Thickness(28),
                Child = Ui.Stack(16, Ui.Text("Texture packs", "strong"),
                    Ui.Text("When two packs change the same texture, the one later in the alphabet wins. Changes show the next time the game starts.", "small"),
                    list, buttons),
            };
            await w.ShowDialog(this);
            if (next != null) await next();
        }

        // Linux: deletes the launcher's prefix with the game in it, so it can be installed again
        // from the Install page. Only offered for a game the launcher installed into its prefix.
        async Task UninstallGame()
        {
            if (m_busy || !OperatingSystem.IsLinux() || !GameUninstall.IsInLauncherPrefix(m_gameDir)) return;
            string prefix = LauncherSettings.PrefixDir;
            if (!await Dialog.Ask(this, "Uninstall the game",
                    "This deletes " + prefix + ": the game, every mod and file in its folder, the client's settings and saved login, and the prefix's Windows settings. " +
                    "If the game is running, it's closed. The Steam entry stays, and installing again from the Install page uses it." +
                    "\n\nThis can't be undone.", "Uninstall", "Cancel"))
                return;
            ProtonTool tool = m_steam?.ProtonTools().FirstOrDefault();
            bool done = false;
            await Run("Uninstalling the game...", () =>
            {
                GameUninstall.Uninstall(tool, prefix);
                done = true;
            });
            if (!done) return;
            m_gameDir = null;
            SetStatus("");
            ShowPage(m_homePage);
            await Dialog.Show(this, "Game uninstalled", "The prefix is gone. Install the game again from the Install page.");
        }

        async Task<string> PickFile(string title, params string[] patterns)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = patterns }, FilePickerFileTypes.All },
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        async Task ChooseFolder()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select your Need for Speed ProStreet folder (the one with nfs.exe)" });
            if (folders.Count == 0) return;
            string dir = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(dir) || !Game.IsGameDir(dir))
            {
                await Dialog.Show(this, "Not the game folder", "There's no nfs.exe in that folder. Pick the folder that has nfs.exe in it.");
                return;
            }
            m_gameDir = dir.TrimEnd('/', '\\');
            LauncherSettings.GameDir = m_gameDir;
            ShowPage(m_homePage);
        }

        async Task ReplaceExe()
        {
            if (!await EnsureWritable()) return;
            string file = await PickFile("nfs.exe version 1.1", "*.exe", "*.EXE");
            if (file == null) return;
            if (!Game.IsSupportedExe(file))
            {
                await Dialog.Show(this, "Not the right nfs.exe", "That file isn't the unpacked version 1.1 the client needs. The \"Battery\" 1.1 No-CD exe works.");
                return;
            }
            await Run("Putting your nfs.exe in place...", () =>
            {
                string exe = Game.ExePath(m_gameDir);
                if (!File.Exists(exe + ".original")) File.Copy(exe, exe + ".original");
                File.Copy(file, exe, true);
                if (OperatingSystem.IsLinux() && ExeFix.Needed(exe)) ExeFix.Apply(exe);
            });
        }

        // Adds the game to Steam. On Linux, ensures Proton is turned on. Steam has to be closed for that;
        // with restartSteam it's started again afterwards, if it was running.
        async Task<bool> AddToSteam(bool restartSteam = false)
        {
            if (m_steam == null)
            {
                await Dialog.Show(this, "Steam not found", OperatingSystem.IsWindows()
                    ? "Steam isn't installed, or hasn't been started on this PC yet."
                    : "Install Steam, start it once and sign in, then try again.");
                return false;
            }
            if (m_steam.AccountConfigDirs().Count == 0)
            {
                await Dialog.Show(this, "No Steam account", "No Steam account has signed in on this PC yet. Sign in to Steam once, then try again.");
                return false;
            }
            ProtonTool tool = null;
            if (OperatingSystem.IsLinux())
            {
                tool = m_steam.ProtonTools().FirstOrDefault();
                if (tool == null)
                {
                    await Dialog.Show(this, "No Proton installed", "Steam has no Proton yet. In Steam, search your library for \"Proton Experimental\" and install it, then try again.");
                    return false;
                }
            }
            bool wasRunning = Steam.IsRunning();
            if (!await CloseSteam()) return false;
            bool ok = false;
            await Run("Adding the game to Steam...", () =>
            {
                new Setup(m_steam, _ => { }, _ => { }).AddToSteam(m_gameDir, tool, IconFile());
                ok = true;
            });
            if (ok)
            {
                if (OperatingSystem.IsLinux() && tool != null)
                    SetStatus("Added to Steam as \"" + Steam.AppName + "\", with " + tool.DisplayName + ".");
                else
                    SetStatus("Added \"" + Steam.AppName + "\" to Steam.");
                if (restartSteam && wasRunning)
                {
                    try { m_steam.Start(); }
                    catch (Exception) { } // the shortcut is there either way, and Steam can be started by hand
                }
            }
            return ok;
        }

        async Task<bool> CloseSteam()
        {
            if (!Steam.IsRunning()) return true;
            if (!await Dialog.Ask(this, "Close Steam", "Steam has to be closed to add a game to it, and starts again afterwards. Close Steam now?", "Close Steam", "Cancel")) return false;
            SetStatus("Closing Steam...");
            bool closed = await Task.Run(m_steam.Close);
            if (!closed) await Dialog.Show(this, "Steam is still open", "Steam didn't close. Close it yourself, then try again.");
            return closed;
        }

        // The logo as a file, for the Steam library entry.
        static string IconFile()
        {
            try
            {
                string file = System.IO.Path.Combine(LauncherSettings.DataDir, "icon.png");
                Directory.CreateDirectory(LauncherSettings.DataDir);
                using Stream s = AssetLoader.Open(new Uri("avares://OpenProStreetLauncher/assets/logo.png"));
                using FileStream f = File.Create(file);
                s.CopyTo(f);
                return file;
            }
            catch (Exception) { return ""; }
        }

        async Task Play()
        {
            string exe = Game.ExePath(m_gameDir);
            if (exe == null) { await Dialog.Show(this, "Can't start the game", "There's no nfs.exe in " + m_gameDir); return; }
            bool ready = Game.FindFile(m_gameDir, Game.Loader) != null && Game.FindFile(Scripts, Game.ClientAsi) != null && Game.FindFile(Scripts, Game.MultiFixAsi) != null;
            if (!ready && !await Dialog.Ask(this, "Not ready", "Some parts are missing, so OpenProStreet won't work. Start the game anyway?", "Start", "Cancel")) return;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { WorkingDirectory = m_gameDir, UseShellExecute = true });
                    LauncherSettings.GameDir = m_gameDir;
                    SetStatus("Starting the game...");
                }
                catch (Exception ex)
                {
                    await Dialog.Show(this, "Couldn't start the game", ex.Message);
                }
                return;
            }

            if (m_steam == null) { await Dialog.Show(this, "Can't start the game", "The game runs through Steam. Install Steam, start it once and sign in."); return; }
            if (ExeFix.Needed(exe))
            {
                try { ExeFix.Apply(exe); }
                catch (Exception e) { await Dialog.Show(this, "Couldn't fix nfs.exe", "It would crash in Proton without the fix. " + e.Message); return; }
                RefreshHome();
            }
            if (!m_steam.HasShortcut(exe) && !await AddToSteam()) return;
            try
            {
                m_steam.Start(Steam.RunUrl(Steam.ShortcutAppId(exe)));
                SetStatus("Starting the game through Steam...");
            }
            catch (Exception e) { await Dialog.Show(this, "Couldn't start Steam", e.Message); }
        }

        // The Steam button on Windows: adds the shortcut, or starts the game through it.
        async Task SteamPlay()
        {
            string exe = Game.ExePath(m_gameDir);
            if (m_steam == null || exe == null) return;
            if (!m_steam.HasShortcut(exe))
            {
                await AddToSteam(true);
                RefreshHome();
                return;
            }
            bool ready = Game.FindFile(m_gameDir, Game.Loader) != null && Game.FindFile(Scripts, Game.ClientAsi) != null && Game.FindFile(Scripts, Game.MultiFixAsi) != null;
            if (!ready && !await Dialog.Ask(this, "Not ready", "Some parts are missing, so OpenProStreet won't work. Start the game anyway?", "Start", "Cancel")) return;
            try
            {
                m_steam.Start(Steam.RunUrl(Steam.ShortcutAppId(exe)));
                LauncherSettings.GameDir = m_gameDir;
                SetStatus("Starting the game through Steam...");
            }
            catch (Exception e) { await Dialog.Show(this, "Couldn't start Steam", e.Message); }
        }

        // ---- Install from disc ----

        Control BuildInstall()
        {
            StackPanel page = Page();
            page.Children.Add(Ui.Text("Install from your disc", "title"));
            page.Children.Add(Spaced(Ui.Text("The game's own installer runs in Proton, like on Windows: click through it and enter your CD key. The launcher then adds the patch, your nfs.exe, the ASI loader, MultiFix and the client, and puts the game in your Steam library."), 8, 28));

            m_iso = PathBox();
            m_patch = PathBox();
            m_nocd = PathBox();
            m_nocdNote = Ui.Text("", "small");
            page.Children.Add(Field("Disc image", m_iso, "Your game DVD as an .iso file.", async () =>
            {
                string f = await PickFile("Disc image", "*.iso", "*.ISO");
                if (f != null) m_iso.Text = f;
            }));
            page.Children.Add(Field("1.1 patch (optional)", m_patch, "The official 1.1 patch's .exe. Leave it empty if your disc already has 1.1.", async () =>
            {
                string f = await PickFile("1.1 patch", "*.exe", "*.EXE");
                if (f != null) m_patch.Text = f;
            }));
            page.Children.Add(Field("nfs.exe 1.1", m_nocd, "The unpacked 1.1 No-CD exe, which replaces the installed one. The \"Battery\" one works.", async () =>
            {
                string f = await PickFile("nfs.exe version 1.1", "*.exe", "*.EXE");
                if (f == null) return;
                m_nocd.Text = f;
                bool ok = Game.IsSupportedExe(f);
                m_nocdNote.Text = ok ? "This is the supported 1.1 exe." : "This isn't the unpacked 1.1 exe the client needs.";
                m_nocdNote.Foreground = ok ? Ui.Success : Ui.Danger;
            }));
            page.Children.Add(Spaced(m_nocdNote, -12, 20));

            m_tool = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            page.Children.Add(Ui.Stack(8, Ui.Micro("Proton"), m_tool));

            m_installButton = Ui.Button("Install", true, async () => await Install());
            m_installButton.HorizontalAlignment = HorizontalAlignment.Right;
            page.Children.Add(Spaced(m_installButton, 28, 0));

            m_stepText = Ui.Text("", "strong");
            m_progress = new ProgressBar { Minimum = 0, Maximum = 1, IsVisible = false };
            m_stepLog = new StackPanel { Spacing = 6 };
            m_installNote = Ui.Text("", "small");
            page.Children.Add(Spaced(Ui.Stack(10, m_stepText, m_progress, m_stepLog, m_installNote), 24, 0));
            return page;
        }

        static TextBox PathBox() => new TextBox { IsReadOnly = true, Watermark = "Not chosen" };

        static Control Field(string label, TextBox box, string hint, Func<Task> choose)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(box);
            Button b = Ui.Button("Choose", false, async () => await choose());
            b.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(b, 1);
            row.Children.Add(b);
            StackPanel p = Ui.Stack(8, Ui.Micro(label), row, Ui.Text(hint, "small"));
            p.Margin = new Thickness(0, 0, 0, 20);
            return p;
        }

        void RefreshTools()
        {
            List<ProtonTool> tools = m_steam?.ProtonTools() ?? new List<ProtonTool>();
            m_tool.ItemsSource = tools.Select(t => t.DisplayName).ToList();
            m_tool.Tag = tools;
            if (tools.Count > 0 && m_tool.SelectedIndex < 0) m_tool.SelectedIndex = 0;
            m_tool.PlaceholderText = m_steam == null ? "Steam isn't installed" : "No Proton installed in Steam";
        }

        void LogStep(string text, IBrush dot)
        {
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = dot, VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(Ui.Text(text, "small"));
            m_stepLog.Children.Add(line);
        }

        async Task Install()
        {
            if (m_busy) return;
            List<ProtonTool> tools = m_tool.Tag as List<ProtonTool>;
            ProtonTool tool = tools != null && m_tool.SelectedIndex >= 0 ? tools[m_tool.SelectedIndex] : null;
            string problem =
                m_steam == null ? "Install Steam, start it once and sign in." :
                tool == null ? "Install Proton in Steam first: search your library for \"Proton Experimental\"." :
                string.IsNullOrEmpty(m_iso.Text) ? "Choose your disc image." :
                string.IsNullOrEmpty(m_nocd.Text) ? "Choose the nfs.exe 1.1 to use." :
                !Game.IsSupportedExe(m_nocd.Text) ? "That nfs.exe isn't the unpacked 1.1 exe the client needs." : null;
            if (problem != null) { await Dialog.Show(this, "Not ready to install", problem); return; }
            if (Game.IsGameDir(WinePrefix.FindGame(LauncherSettings.PrefixDir)) &&
                !await Dialog.Ask(this, "Already installed", "The game is already installed in the launcher's prefix. Run the installer again anyway?", "Install again", "Cancel"))
                return;

            InstallPlan plan = new InstallPlan { IsoPath = m_iso.Text, PatchPath = string.IsNullOrEmpty(m_patch.Text) ? null : m_patch.Text, NoCdPath = m_nocd.Text, Tool = tool };
            m_busy = true;
            m_installButton.IsEnabled = false;
            m_stepLog.Children.Clear();
            m_installNote.Text = "";
            string current = null;
            void Step(string text) => Dispatcher.UIThread.Post(() =>
            {
                if (current != null) LogStep(current, Ui.Success);
                current = text;
                m_stepText.Text = text;
                m_progress.IsVisible = true;
                m_progress.IsIndeterminate = true;
            });
            void Progress(double value) => Dispatcher.UIThread.Post(() =>
            {
                m_progress.IsIndeterminate = value < 0;
                if (value >= 0) m_progress.Value = value;
            });
            try
            {
                Setup setup = new Setup(m_steam, Step, Progress);
                string gameDir = await Task.Run(() => setup.Install(plan));
                m_gameDir = gameDir;
                Step("Adding the game to Steam");
                bool added = await CloseSteam();
                if (added) await Task.Run(() => setup.AddToSteam(gameDir, tool, IconFile()));
                Dispatcher.UIThread.Post(() =>
                {
                    if (current != null) LogStep(current, added ? Ui.Success : Ui.Warning);
                    current = null;
                    m_progress.IsVisible = false;
                    m_stepText.Text = "Installed";
                    m_installNote.Text = added ? "Everything's in place. Go to Home and press Play." : "Installed. Add it to Steam from Home when you're ready.";
                });
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (current != null) LogStep(current, Ui.Danger);
                    m_progress.IsVisible = false;
                    m_stepText.Text = "The install stopped";
                    m_installNote.Text = e.Message;
                    m_installNote.Foreground = Ui.Danger;
                });
            }
            finally
            {
                m_busy = false;
                m_installButton.IsEnabled = true;
            }
        }

        // ---- Settings ----

        Control BuildSettings()
        {
            StackPanel page = Page();
            m_serverDefault = new RadioButton { Content = "OpenProStreet server (" + ClientSettings.DefaultAddress + ")", GroupName = "server" };
            m_serverCustom = new RadioButton { Content = "Another server", GroupName = "server" };
            m_serverLan = new RadioButton { Content = "Search my local network", GroupName = "server" };
            m_address = new TextBox { Watermark = "Address, like play.example.com" };
            m_port = new TextBox { Watermark = "Port (" + ClientSettings.DefaultPort + ")", Width = 150 };
            m_serverCustom.IsCheckedChanged += (s, e) => m_address.IsEnabled = m_port.IsEnabled = m_serverCustom.IsChecked == true;
            Grid custom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(28, 4, 0, 4) };
            custom.Children.Add(m_address);
            m_port.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(m_port, 1);
            custom.Children.Add(m_port);

            m_tank = new CheckBox { Content = "No tank slapper, so cars don't weave at high speed" };
            m_speed = new CheckBox { Content = "No speed limiter, so top speed comes from the engine and gearing" };
            m_borderless = new CheckBox { Content = "Borderless window that fills the screen" };
            m_vibration = new CheckBox { Content = "No camera vibration" };
            m_verbose = new CheckBox { Content = "Detailed log" };
            m_colour = new CheckBox { Content = "Coloured console" };
            m_dumpEvents = new CheckBox { Content = "Write the event catalog, for server admins" };

            m_autoLogin = new CheckBox { Content = "Log in automatically when you open OpenProStreet" };
            m_username = new TextBox { Watermark = "OpenProStreet name" };
            m_password = new TextBox { Watermark = "Password", PasswordChar = '•' };
            m_autoLogin.IsCheckedChanged += (s, e) => m_username.IsEnabled = m_password.IsEnabled = m_autoLogin.IsChecked == true;
            Grid account = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(28, 4, 0, 4) };
            account.Children.Add(m_username);
            m_password.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(m_password, 1);
            account.Children.Add(m_password);
            TextBlock accountNote = Ui.Text(OperatingSystem.IsWindows()
                ? "The password is saved encrypted for your Windows user."
                : "The password is saved in the game folder as you typed it.", "small");
            accountNote.Margin = new Thickness(28, 0, 0, 0);

            page.Children.Add(Section("Account", m_autoLogin, account, accountNote));
            page.Children.Add(Section("Server", m_serverDefault, m_serverCustom, custom, m_serverLan));
            page.Children.Add(Section("Car fixes", m_tank, m_speed));
            page.Children.Add(BuildGearboxSection());
            m_console = new CheckBox { Content = "Show the console window, with MultiFix's and the client's log" };
            m_uncap = new CheckBox { Content = "Uncap the frame rate in races (otherwise the game limits them to 30 fps)" };
            page.Children.Add(Section("Display", m_borderless, m_vibration, m_uncap, m_console));
            page.Children.Add(BuildOverlaySection());
            page.Children.Add(BuildAtmosphereSection());
            page.Children.Add(BuildCameraSection());
            page.Children.Add(Section("Advanced", m_verbose, m_colour, m_dumpEvents));
            page.Children.Add(BuildUpdateBranchSection());
            page.Children.Add(BuildDiagnosticsSection());
            if (OperatingSystem.IsWindows())
            {
                m_installText = Ui.Text("", "small");
                m_installText.TextWrapping = TextWrapping.Wrap;
                m_installButton2 = Ui.Button("Install", false, async () => await InstallLauncher());
                m_uninstallButton = Ui.Button("Uninstall", false, async () => await UninstallLauncher());
                StackPanel installButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
                installButtons.Children.Add(m_installButton2);
                installButtons.Children.Add(m_uninstallButton);
                page.Children.Add(Section("Launcher", m_installText, installButtons));
            }

            m_settingsNote = Ui.Text("", "small");
            m_settingsNote.VerticalAlignment = VerticalAlignment.Center;
            // Defaults leave the account as it is.
            Button defaults = Ui.Button("Defaults", false, () =>
            {
                ClientSettings d = new ClientSettings { AutoLogin = m_autoLogin.IsChecked == true, Username = m_username.Text ?? "", Password = m_password.Text ?? "" };
                ShowSettings(d);
                m_settingsNote.Text = "Defaults shown. Save to keep them.";
            });
            Button save = Ui.Button("Save", true, async () => await SaveSettings());
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(defaults);
            buttons.Children.Add(save);
            Grid footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
            footer.Children.Add(m_settingsNote);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);
            page.Children.Add(footer);
            return page;
        }

        // ---- Installing the launcher into the game folder (Windows) ----

        void ShowInstallState()
        {
            if (m_installText == null) return;
            bool have = Game.IsGameDir(m_gameDir);
            bool installed = have && WindowsInstall.IsInstalled(m_gameDir);
            bool running = installed && WindowsInstall.IsRunningInstalled(m_gameDir);
            if (!have) m_installText.Text = "Set up the game first.";
            else if (running) m_installText.Text = "Installed in " + WindowsInstall.InstallDir(m_gameDir) + ", with a Start menu entry. This is the installed launcher.";
            else if (installed) m_installText.Text = "Installed in " + WindowsInstall.InstallDir(m_gameDir) + ". You're running another copy; Install replaces the installed one with it.";
            else m_installText.Text = "Install the launcher into the game folder (" + WindowsInstall.DirName + "), with a Start menu entry. It then always finds the game, and updates itself there.";
            m_installButton2.Content = installed && !running ? "Replace" : "Install";
            m_installButton2.IsVisible = !running;
            m_installButton2.IsEnabled = have;
            m_uninstallButton.IsVisible = installed;
        }

        async Task InstallLauncher()
        {
            if (!Game.IsGameDir(m_gameDir) || !await EnsureWritable()) return;
            try
            {
                await Task.Run(() => WindowsInstall.Install(m_gameDir));
                LauncherSettings.GameDir = m_gameDir;
            }
            catch (Exception e)
            {
                await Dialog.Show(this, "Couldn't install the launcher", e.Message);
                return;
            }
            ShowInstallState();
            string exe = WindowsInstall.InstalledExe(m_gameDir);
            if (!await Dialog.Ask(this, "Launcher installed", "Installed in " + WindowsInstall.InstallDir(m_gameDir) + ", and added to the Start menu as \"OpenProStreet Launcher\". Start the installed launcher now? This one closes, and you can delete it.", "Start it", "Later"))
                return;
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = WindowsInstall.InstallDir(m_gameDir) };
                info.ArgumentList.Add("-gamedir");
                info.ArgumentList.Add(m_gameDir);
                System.Diagnostics.Process.Start(info);
                Close();
            }
            catch (Exception e) { await Dialog.Show(this, "Couldn't start the installed launcher", e.Message); }
        }

        async Task UninstallLauncher()
        {
            if (!Game.IsGameDir(m_gameDir) || !await EnsureWritable()) return;
            bool running = WindowsInstall.IsRunningInstalled(m_gameDir);
            if (!await Dialog.Ask(this, "Uninstall the launcher", "This removes " + WindowsInstall.InstallDir(m_gameDir) + " and the Start menu entry. The game, the client and your settings stay." +
                                  (running ? " This launcher closes afterwards; to use it again, run a downloaded OpenProStreetLauncher.exe." : ""), "Uninstall", "Cancel"))
                return;
            try
            {
                await Task.Run(() => WindowsInstall.Uninstall(m_gameDir));
            }
            catch (Exception e)
            {
                await Dialog.Show(this, "Couldn't uninstall the launcher", e.Message);
                return;
            }
            if (running)
            {
                Close();
                return;
            }
            ShowInstallState();
            m_settingsNote.Text = "The launcher is uninstalled.";
        }

        // A slider with its name on the left and its value on the right.
        static Control SliderRow(string label, Slider slider, string unit)
        {
            TextBlock name = Ui.Text(label, "small");
            name.VerticalAlignment = VerticalAlignment.Center;
            TextBlock value = Ui.Text("", "small");
            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextAlignment = TextAlignment.Right;
            value.Width = 52;
            slider.TickFrequency = 1;
            slider.IsSnapToTickEnabled = true;
            slider.Margin = new Thickness(12, 0, 8, 0);
            void Show() => value.Text = ((int)Math.Round(slider.Value)) + unit;
            slider.PropertyChanged += (s, e) => { if (e.Property == Slider.ValueProperty) Show(); };
            Show();
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,Auto") };
            g.Children.Add(name);
            Grid.SetColumn(slider, 1);
            g.Children.Add(slider);
            Grid.SetColumn(value, 2);
            g.Children.Add(value);
            return g;
        }

        // ReShifted is the master switch: everything under it only shows while it is on, and the
        // Adaptive extras only while that style is picked.
        Control BuildGearboxSection()
        {
            m_reshifted = new CheckBox { Content = "ReShifted: automatic gearbox that follows how you drive" };
            m_shiftAdaptive = new RadioButton { Content = "Adaptive: customizable coast downshifts, kickdown skips, and quick shifts", GroupName = "shiftStyle" };
            m_shiftClassic = new RadioButton { Content = "Classic: follows the throttle only, one gear at a time", GroupName = "shiftStyle" };
            m_shiftMemory = new CheckBox { Content = "Remember recent hard driving before returning to ECO" };
            m_shiftCoast = new CheckBox { Content = "Shift down while coasting off the throttle" };
            m_shiftQuick = new CheckBox { Content = "Quicker gear changes when driving hard" };
            m_shiftSkip = new CheckBox { Content = "Kickdown can skip gears" };
            m_shiftIndicator = new CheckBox { Content = "Show ECO or SPORT on screen while racing (needs the overlay)" };
            m_shortShift = new Slider { Minimum = 10, Maximum = 55 };
            m_sportShift = new Slider { Minimum = 88, Maximum = 99 };
            m_ecoThrottle = new Slider { Minimum = 0, Maximum = 80 };
            m_sportThrottle = new Slider { Minimum = 10, Maximum = 100 };
            m_quickShiftTime = new Slider { Minimum = 30, Maximum = 100 };
            // The sport throttle always stays at least 10 above the eco throttle, as in the client.
            m_ecoThrottle.PropertyChanged += (s, e) =>
            {
                if (e.Property == Slider.ValueProperty && m_sportThrottle.Value < m_ecoThrottle.Value + 10) m_sportThrottle.Value = m_ecoThrottle.Value + 10;
            };
            m_sportThrottle.PropertyChanged += (s, e) =>
            {
                if (e.Property == Slider.ValueProperty && m_ecoThrottle.Value > m_sportThrottle.Value - 10) m_ecoThrottle.Value = Math.Max(0, m_sportThrottle.Value - 10);
            };

            m_quickShiftTimeRow = SliderRow("Gear change time when driving hard", m_quickShiftTime, "%");
            m_shiftAdaptiveOptions = Ui.Stack(8, m_shiftMemory, m_shiftCoast, m_shiftSkip, m_shiftQuick, m_quickShiftTimeRow);
            m_shiftAdaptiveOptions.Margin = new Thickness(28, 0, 0, 4);

            TextBlock profile = Ui.Text("Shift profile", "strong");
            profile.Margin = new Thickness(0, 8, 0, 0);
            m_shiftOptions = Ui.Stack(8,
                m_shiftAdaptive, m_shiftAdaptiveOptions, m_shiftClassic, m_shiftIndicator,
                profile,
                SliderRow("Gentle shift point (idle to redline)", m_shortShift, "%"),
                SliderRow("Sport shift point (of the redline)", m_sportShift, "%"),
                SliderRow("Shift early up to this throttle", m_ecoThrottle, "%"),
                SliderRow("Full sport from this throttle", m_sportThrottle, "%"));
            m_shiftOptions.Margin = new Thickness(28, 4, 0, 0);

            m_reshifted.IsCheckedChanged += (s, e) => UpdateGearboxVisibility();
            m_shiftAdaptive.IsCheckedChanged += (s, e) => UpdateGearboxVisibility();
            m_shiftQuick.IsCheckedChanged += (s, e) => UpdateGearboxVisibility();
            return Section("Gearbox", m_reshifted, m_shiftOptions);
        }

        static readonly string[] OverlayPositions = { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        static readonly string[] AtmosphereLooks = { "Day", "Dusk", "Night" };
        static readonly string[] OverlayHotkeys =
        {
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Tab", "Insert", "Home", "End", "Delete", "PageUp", "PageDown"
        };

        static Control ComboRow(string label, ComboBox combo)
        {
            TextBlock name = Ui.Text(label, "small");
            name.VerticalAlignment = VerticalAlignment.Center;
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid g = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
            g.Children.Add(name);
            Grid.SetColumn(combo, 1);
            g.Children.Add(combo);
            return g;
        }

        static void SelectComboValue(ComboBox combo, string value, string[] defaults)
        {
            List<string> items = new List<string>(defaults);
            string trimmed = (value ?? "").Trim();
            string matched = items.FirstOrDefault(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase));
            if (matched == null && trimmed.Length > 0)
            {
                items.Add(trimmed);
                matched = trimmed;
            }
            combo.ItemsSource = items;
            combo.SelectedItem = matched ?? items[0];
        }

        static string SelectedComboValue(ComboBox combo, string fallback)
        {
            return (combo.SelectedItem as string) ?? fallback;
        }

        Control BuildOverlaySection()
        {
            m_overlay = new CheckBox { Content = "In-game overlay (standing, notifications, settings menu)" };
            m_notifications = new CheckBox { Content = "Race notifications and XP popups" };
            m_overlayLobbyCars = new CheckBox { Content = "Room drivers and cars while in a lobby" };
            m_overlayPaceBar = new CheckBox { Content = "Live pace bar while racing" };
            m_scoreboard = new CheckBox { Content = "Live scoreboard when holding the scoreboard key" };
            m_overlayPing = new CheckBox { Content = "Server and player ping (ms)" };
            m_overlayChat = new CheckBox { Content = "Live in-game room chat messages" };
            m_overlayScale = new Slider { Minimum = 50, Maximum = 200 };
            m_overlayPosition = new ComboBox();
            m_overlayToggleKey = new ComboBox();
            m_overlayScoreboardKey = new ComboBox();
            m_overlaySettingsKey = new ComboBox();

            m_overlayOptions = Ui.Stack(8,
                m_notifications, m_overlayLobbyCars, m_overlayPaceBar, m_scoreboard, m_overlayPing, m_overlayChat,
                SliderRow("Overlay scale", m_overlayScale, "%"),
                ComboRow("Corner position", m_overlayPosition),
                ComboRow("Toggle HUD hotkey", m_overlayToggleKey),
                ComboRow("Scoreboard hotkey", m_overlayScoreboardKey),
                ComboRow("In-game settings hotkey", m_overlaySettingsKey));
            m_overlayOptions.Margin = new Thickness(28, 4, 0, 0);
            m_overlay.IsCheckedChanged += (s, e) => m_overlayOptions.IsVisible = m_overlay.IsChecked == true;
            return Section("Overlay", m_overlay, m_overlayOptions);
        }

        Control BuildAtmosphereSection()
        {
            m_atmosphere = new CheckBox { Content = "Day, dusk and night lighting & sky presets (F8 / F9 in game)" };
            m_atmosphereStart = new ComboBox();
            m_atmosphereOptions = Ui.Stack(8, ComboRow("Starting look", m_atmosphereStart));
            m_atmosphereOptions.Margin = new Thickness(28, 4, 0, 0);
            m_atmosphere.IsCheckedChanged += (s, e) => m_atmosphereOptions.IsVisible = m_atmosphere.IsChecked == true;
            return Section("Atmosphere", m_atmosphere, m_atmosphereOptions);
        }

        Control BuildCameraSection()
        {
            m_rightStickOrbit = new CheckBox { Content = "Right-stick camera orbit around the car (controller)" };
            m_orbitBlockShift = new CheckBox { Content = "Block automatic gear shifts while orbiting" };
            m_orbitInvertX = new CheckBox { Content = "Invert left / right orbit direction" };
            m_orbitDeadzone = new Slider { Minimum = 5, Maximum = 90 };
            m_orbitSnapTime = new Slider { Minimum = 0, Maximum = 1000 };
            m_orbitBackTime = new Slider { Minimum = 0, Maximum = 5000 };

            m_cameraOptions = Ui.Stack(8,
                m_orbitBlockShift,
                m_orbitInvertX,
                SliderRow("Stick deadzone", m_orbitDeadzone, "%"),
                SliderRow("Snap time", m_orbitSnapTime, " ms"),
                SliderRow("Return time", m_orbitBackTime, " ms"));
            m_cameraOptions.Margin = new Thickness(28, 4, 0, 0);
            m_rightStickOrbit.IsCheckedChanged += (s, e) => m_cameraOptions.IsVisible = m_rightStickOrbit.IsChecked == true;
            return Section("Camera orbit", m_rightStickOrbit, m_cameraOptions);
        }

        Control BuildUpdateBranchSection()
        {
            TextBlock desc = Ui.Text("Choose which branch OpenProStreetClient updates come from. Leave on \"master\" for stable releases, or pick an experimental feature branch.", "small");
            desc.TextWrapping = TextWrapping.Wrap;
            m_clientBranch = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            m_clientBranchCustom = new TextBox { Watermark = "Or type a branch name (optional)" };
            m_clientBranchNote = Ui.Text("", "small");
            m_clientBranchNote.VerticalAlignment = VerticalAlignment.Center;

            Button refreshBtn = Ui.Button("Refresh", false, async () => await RefreshClientBranches());
            Button switchBtn = Ui.Button("Apply branch", false, async () => await ApplySelectedBranch());
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(m_clientBranch);
            refreshBtn.Margin = new Thickness(8, 0, 0, 0);
            switchBtn.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(refreshBtn, 1);
            Grid.SetColumn(switchBtn, 2);
            row.Children.Add(refreshBtn);
            row.Children.Add(switchBtn);
            return Section("Client update branch", desc, row, m_clientBranchCustom, m_clientBranchNote);
        }

        string ResolveSelectedBranch()
        {
            string custom = (m_clientBranchCustom?.Text ?? "").Trim();
            if (custom.Length > 0) return custom;
            string picked = SelectedComboValue(m_clientBranch, "master");
            return string.IsNullOrWhiteSpace(picked) ? "master" : picked;
        }

        async Task RefreshClientBranches()
        {
            string current = LauncherSettings.ClientBranch;
            m_clientBranchNote.Foreground = Ui.Idle;
            m_clientBranchNote.Text = "Loading branches from OneDev...";
            try
            {
                string[] list = await Task.Run(Downloads.ClientBranches);
                SelectComboValue(m_clientBranch, current, list);
                m_clientBranchNote.Foreground = Ui.Success;
                m_clientBranchNote.Text = $"Found {list.Length} branch(es). Active: {current}";
            }
            catch (Exception)
            {
                SelectComboValue(m_clientBranch, current, new[] { "master" });
                m_clientBranchNote.Foreground = Ui.Warning;
                m_clientBranchNote.Text = "Couldn't load branches from OneDev. Active: " + current;
            }
        }

        async Task ApplySelectedBranch()
        {
            string chosen = ResolveSelectedBranch();
            LauncherSettings.ClientBranch = chosen;
            m_clientBranchCustom.Text = "";
            SelectComboValue(m_clientBranch, chosen, (m_clientBranch.ItemsSource as IEnumerable<string>)?.ToArray() ?? new[] { "master" });
            m_latest = null;
            m_latestFailed = false;
            m_updateOffered = false;
            m_clientBranchNote.Foreground = Ui.Idle;
            m_clientBranchNote.Text = "Checking builds on '" + chosen + "'...";
            RefreshHome();
            await CheckForUpdate();
            if (m_latest != null)
            {
                m_clientBranchNote.Foreground = Ui.Success;
                m_clientBranchNote.Text = "Selected '" + chosen + "' (latest: " + (m_latest.Version ?? "#" + m_latest.Number) + "). Go to Home to Switch/Update.";
            }
            else
            {
                m_clientBranchNote.Foreground = Ui.Warning;
                m_clientBranchNote.Text = "No successful build found yet on '" + chosen + "'.";
            }
        }

        Control BuildDiagnosticsSection()
        {
            TextBlock desc = Ui.Text("Bundles configs, logs and race telemetry into a .zip file for troubleshooting. Account passwords and identity.key are never included.", "small");
            desc.TextWrapping = TextWrapping.Wrap;
            m_diagnosticsNote = Ui.Text("", "small");
            m_diagnosticsNote.VerticalAlignment = VerticalAlignment.Center;
            Button bundleBtn = Ui.Button("Save diagnostics bundle...", false, async () => await SaveDiagnosticsBundle());
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(bundleBtn);
            row.Children.Add(m_diagnosticsNote);
            return Section("Diagnostics", desc, row);
        }

        async Task SaveDiagnosticsBundle()
        {
            var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save OpenProStreet diagnostics bundle",
                SuggestedFileName = Diagnostics.DefaultFileName(),
                DefaultExtension = "zip",
                FileTypeChoices = new[] { new FilePickerFileType("ZIP archive") { Patterns = new[] { "*.zip" } } }
            });
            string path = picked?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            m_diagnosticsNote.Foreground = Ui.Idle;
            m_diagnosticsNote.Text = "Collecting files...";
            try
            {
                DiagnosticsResult result = await Task.Run(() => Diagnostics.CreateBundle(m_gameDir, path));
                m_diagnosticsNote.Foreground = Ui.Success;
                m_diagnosticsNote.Text = $"Saved {result.FilesIncluded} file(s) to {Path.GetFileName(path)}";
            }
            catch (Exception e)
            {
                m_diagnosticsNote.Foreground = Ui.Danger;
                m_diagnosticsNote.Text = "Failed";
                await Dialog.Show(this, "Couldn't save diagnostics", e.Message);
            }
        }

        void UpdateGearboxVisibility()
        {
            m_shiftOptions.IsVisible = m_reshifted.IsChecked == true;
            m_shiftAdaptiveOptions.IsVisible = m_shiftAdaptive.IsChecked == true;
            m_quickShiftTimeRow.IsVisible = m_shiftQuick.IsChecked == true;
        }

        static Control Section(string title, params Control[] items)
        {
            StackPanel p = Ui.Stack(4, Ui.Micro(title));
            p.Children[0].Margin = new Thickness(0, 0, 0, 6);
            foreach (Control c in items) p.Children.Add(c);
            p.Margin = new Thickness(0, 0, 0, 24);
            return p;
        }

        void LoadSettings()
        {
            string branch = LauncherSettings.ClientBranch;
            SelectComboValue(m_clientBranch, branch, new[] { "master" });
            m_clientBranchCustom.Text = "";
            m_clientBranchNote.Foreground = Ui.Idle;
            m_clientBranchNote.Text = "Active branch: " + branch;
            _ = RefreshClientBranches();
            bool have = Game.IsGameDir(m_gameDir);
            m_settingsPage.IsEnabled = have;
            ShowInstallState();
            if (!have)
            {
                m_settingsNote.Text = "Set up the game first.";
                return;
            }
            string ini = ClientSettings.PathIn(m_gameDir);
            ShowSettings(ClientSettings.Load(ini));
            bool? console = ConsoleWindow.Shown(m_gameDir);
            m_console.IsChecked = console == true;
            m_console.IsEnabled = console != null;
            bool? uncapped = Framerate.Uncapped(m_gameDir);
            m_uncap.IsChecked = uncapped != false;
            m_uncap.IsEnabled = uncapped != null;
            m_settingsNote.Text = File.Exists(ini) ? "Saved in scripts/" + Game.ClientIni : "scripts/" + Game.ClientIni + " doesn't exist yet. Save creates it.";
        }

        void ShowSettings(ClientSettings s)
        {
            bool custom = s.Address.Length > 0 && !(string.Equals(s.Address, ClientSettings.DefaultAddress, StringComparison.OrdinalIgnoreCase) && s.Port == ClientSettings.DefaultPort);
            m_serverLan.IsChecked = s.Address.Length == 0;
            m_serverCustom.IsChecked = custom;
            m_serverDefault.IsChecked = s.Address.Length > 0 && !custom;
            m_address.Text = custom ? s.Address : "";
            m_port.Text = custom && s.Port != ClientSettings.DefaultPort ? s.Port.ToString() : "";
            m_address.IsEnabled = m_port.IsEnabled = custom;
            m_tank.IsChecked = s.NoTankSlapper;
            m_speed.IsChecked = s.NoSpeedLimiter;
            m_borderless.IsChecked = s.Borderless;
            m_vibration.IsChecked = s.NoCameraVibration;
            m_verbose.IsChecked = s.Verbose;
            m_colour.IsChecked = s.Colour;
            m_dumpEvents.IsChecked = s.DumpEvents;
            m_overlay.IsChecked = s.Overlay;
            m_notifications.IsChecked = s.OverlayNotifications;
            m_overlayLobbyCars.IsChecked = s.OverlayLobbyCars;
            m_overlayPaceBar.IsChecked = s.OverlayPaceBar;
            m_scoreboard.IsChecked = s.OverlayScoreboard;
            m_overlayPing.IsChecked = s.OverlayPing;
            m_overlayChat.IsChecked = s.OverlayChat;
            m_overlayScale.Value = s.OverlayScale;
            SelectComboValue(m_overlayPosition, s.OverlayPosition, OverlayPositions);
            SelectComboValue(m_overlayToggleKey, s.OverlayToggleKey, OverlayHotkeys);
            SelectComboValue(m_overlayScoreboardKey, s.OverlayScoreboardKey, OverlayHotkeys);
            SelectComboValue(m_overlaySettingsKey, s.OverlaySettingsKey, OverlayHotkeys);
            m_overlayOptions.IsVisible = s.Overlay;
            m_atmosphere.IsChecked = s.AtmosphereEnabled;
            SelectComboValue(m_atmosphereStart, s.AtmosphereStart, AtmosphereLooks);
            m_atmosphereOptions.IsVisible = s.AtmosphereEnabled;
            m_rightStickOrbit.IsChecked = s.RightStickOrbit;
            m_orbitBlockShift.IsChecked = s.OrbitBlockShift;
            m_orbitInvertX.IsChecked = s.OrbitInvertX;
            m_orbitDeadzone.Value = s.OrbitDeadzone;
            m_orbitSnapTime.Value = s.OrbitSnapTime;
            m_orbitBackTime.Value = s.OrbitBackTime;
            m_cameraOptions.IsVisible = s.RightStickOrbit;
            m_reshifted.IsChecked = s.ReShifted;
            m_shiftAdaptive.IsChecked = s.ShiftStyle != 1;
            m_shiftClassic.IsChecked = s.ShiftStyle == 1;
            m_shiftMemory.IsChecked = s.SportMemory;
            m_shiftCoast.IsChecked = s.CoastDownshift;
            m_shiftQuick.IsChecked = s.QuickShift;
            m_shiftSkip.IsChecked = s.SkipGears;
            m_shiftIndicator.IsChecked = s.ShiftIndicator;
            m_shortShift.Value = s.ShortShift;
            m_sportShift.Value = s.SportShift;
            m_ecoThrottle.Value = s.EcoThrottle;
            m_sportThrottle.Value = Math.Max(s.SportThrottle, s.EcoThrottle + 10);
            m_quickShiftTime.Value = s.QuickShiftTime;
            UpdateGearboxVisibility();
            m_autoLogin.IsChecked = s.AutoLogin;
            m_username.Text = s.Username;
            m_password.Text = s.Password;
            m_username.IsEnabled = m_password.IsEnabled = s.AutoLogin;
        }

        async Task SaveSettings()
        {
            if (!Game.IsGameDir(m_gameDir)) return;
            if (!await EnsureWritable()) return;
            ClientSettings s = new ClientSettings();
            if (m_serverLan.IsChecked == true) s.Address = "";
            else if (m_serverCustom.IsChecked == true)
            {
                s.Address = (m_address.Text ?? "").Trim();
                if (s.Address.Length == 0) { await Dialog.Show(this, "No address", "Enter the server's address, or pick another option."); return; }
                string portText = (m_port.Text ?? "").Trim();
                if (portText.Length > 0)
                {
                    if (!int.TryParse(portText, out int port) || port < 1 || port > 65535) { await Dialog.Show(this, "Port", "The port has to be a number from 1 to 65535."); return; }
                    s.Port = port;
                }
            }
            s.NoTankSlapper = m_tank.IsChecked == true;
            s.NoSpeedLimiter = m_speed.IsChecked == true;
            s.Borderless = m_borderless.IsChecked == true;
            s.NoCameraVibration = m_vibration.IsChecked == true;
            s.Verbose = m_verbose.IsChecked == true;
            s.Colour = m_colour.IsChecked == true;
            s.DumpEvents = m_dumpEvents.IsChecked == true;
            s.Overlay = m_overlay.IsChecked == true;
            s.OverlayNotifications = m_notifications.IsChecked == true;
            s.OverlayLobbyCars = m_overlayLobbyCars.IsChecked == true;
            s.OverlayPaceBar = m_overlayPaceBar.IsChecked == true;
            s.OverlayScoreboard = m_scoreboard.IsChecked == true;
            s.OverlayPing = m_overlayPing.IsChecked == true;
            s.OverlayChat = m_overlayChat.IsChecked == true;
            s.OverlayScale = (int)Math.Round(m_overlayScale.Value);
            s.OverlayPosition = SelectedComboValue(m_overlayPosition, "TopRight");
            s.OverlayToggleKey = SelectedComboValue(m_overlayToggleKey, "F3");
            s.OverlayScoreboardKey = SelectedComboValue(m_overlayScoreboardKey, "Tab");
            s.OverlaySettingsKey = SelectedComboValue(m_overlaySettingsKey, "F4");
            s.AtmosphereEnabled = m_atmosphere.IsChecked == true;
            s.AtmosphereStart = SelectedComboValue(m_atmosphereStart, "Day");
            s.RightStickOrbit = m_rightStickOrbit.IsChecked == true;
            s.OrbitBlockShift = m_orbitBlockShift.IsChecked == true;
            s.OrbitInvertX = m_orbitInvertX.IsChecked == true;
            s.OrbitDeadzone = (int)Math.Round(m_orbitDeadzone.Value);
            s.OrbitSnapTime = (int)Math.Round(m_orbitSnapTime.Value);
            s.OrbitBackTime = (int)Math.Round(m_orbitBackTime.Value);
            s.ReShifted = m_reshifted.IsChecked == true;
            s.ShiftStyle = m_shiftClassic.IsChecked == true ? 1 : 2;
            s.SportMemory = m_shiftMemory.IsChecked == true;
            s.CoastDownshift = m_shiftCoast.IsChecked == true;
            s.QuickShift = m_shiftQuick.IsChecked == true;
            s.SkipGears = m_shiftSkip.IsChecked == true;
            s.ShiftIndicator = m_shiftIndicator.IsChecked == true;
            s.ShortShift = (int)Math.Round(m_shortShift.Value);
            s.SportShift = (int)Math.Round(m_sportShift.Value);
            s.EcoThrottle = (int)Math.Round(m_ecoThrottle.Value);
            s.SportThrottle = (int)Math.Round(m_sportThrottle.Value);
            s.QuickShiftTime = (int)Math.Round(m_quickShiftTime.Value);
            s.AutoLogin = m_autoLogin.IsChecked == true;
            s.Username = (m_username.Text ?? "").Trim();
            s.Password = m_password.Text ?? "";
            if (s.AutoLogin && (s.Username.Length == 0 || s.Password.Length == 0))
            {
                await Dialog.Show(this, "Automatic login", "Enter your OpenProStreet name and password, or turn automatic login off.");
                return;
            }
            try
            {
                s.Save(ClientSettings.PathIn(m_gameDir));
                string chosenBranch = ResolveSelectedBranch();
                bool branchChanged = !string.Equals(chosenBranch, LauncherSettings.ClientBranch, StringComparison.OrdinalIgnoreCase);
                LauncherSettings.ClientBranch = chosenBranch;
                if (branchChanged)
                {
                    m_latest = null;
                    m_latestFailed = false;
                    m_updateOffered = false;
                    _ = CheckForUpdate();
                }
                if (ConsoleWindow.Shown(m_gameDir) is bool shown && shown != (m_console.IsChecked == true))
                    ConsoleWindow.Set(m_gameDir, m_console.IsChecked == true);
                if (Framerate.Uncapped(m_gameDir) is bool uncapped && uncapped != (m_uncap.IsChecked == true))
                    Framerate.Set(m_gameDir, m_uncap.IsChecked == true);
                m_settingsNote.Text = "Saved. The game uses them the next time it starts.";
            }
            catch (Exception e) { await Dialog.Show(this, "Couldn't save", e.Message); }
        }

        // ---- Screenshots, for checking the layout ----

        async Task SaveScreenshots(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var (name, page) in new[] { ("home", m_homePage), ("install", m_installPage), ("settings", m_settingsPage) })
            {
                ShowPage(page);
                await Task.Delay(400);
                double scale = RenderScaling;
                PixelSize size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
                using RenderTargetBitmap bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                bmp.Render(this);
                bmp.Save(System.IO.Path.Combine(dir, name + ".png"));
            }
        }
    }
}
