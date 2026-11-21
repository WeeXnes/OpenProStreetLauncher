using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenProStreetLauncher
{
    readonly struct DiagnosticsResult
    {
        public DiagnosticsResult(int filesIncluded, long totalBytes, List<string> entries)
        {
            FilesIncluded = filesIncluded;
            TotalBytes = totalBytes;
            Entries = entries;
        }

        public int FilesIncluded { get; }
        public long TotalBytes { get; }
        public List<string> Entries { get; }
    }

    // Collects redacted configs, logs, and crash/race telemetry into a single shareable zip archive
    // without ever exporting account passwords or identity.key.
    static class Diagnostics
    {
        const long MaxEntryBytes = 8L * 1024 * 1024;

        public static string DefaultFileName()
        {
            return "openprostreet-diagnostics-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip";
        }

        internal static bool IsSensitiveFile(string fileName)
        {
            string name = (Path.GetFileName(fileName) ?? "").Trim();
            if (name.Length == 0) return true;
            if (name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Keeps all settings intact while masking [Account] Password=... regardless of casing or comments.
        public static string RedactClientIni(string rawText)
        {
            if (string.IsNullOrEmpty(rawText)) return "";
            string newline = rawText.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = rawText.Replace("\r\n", "\n").Split('\n');
            bool trailingNewline = rawText.EndsWith("\n");
            int count = trailingNewline && lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
            StringBuilder sb = new StringBuilder(rawText.Length);
            bool inAccount = false;

            for (int i = 0; i < count; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.Contains(']'))
                {
                    string section = trimmed.Substring(1, trimmed.IndexOf(']') - 1).Trim();
                    inAccount = string.Equals(section, "Account", StringComparison.OrdinalIgnoreCase);
                }
                else if (inAccount && !trimmed.StartsWith(';') && !trimmed.StartsWith('#'))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), "Password", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line.Substring(eq + 1).Trim();
                        line = line.Substring(0, eq + 1) + (val.Length > 0 ? "<redacted>" : "");
                    }
                }
                sb.Append(line);
                if (i + 1 < count || trailingNewline) sb.Append(newline);
            }
            return sb.ToString();
        }

        public static DiagnosticsResult CreateBundle(string gameDir, string destinationZipPath, IEnumerable<string> extraAppDataDirs = null)
        {
            if (string.IsNullOrWhiteSpace(destinationZipPath))
                throw new ArgumentException("Destination path is required.", nameof(destinationZipPath));

            string parent = Path.GetDirectoryName(Path.GetFullPath(destinationZipPath));
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            List<string> entries = new List<string>();
            long totalBytes = 0;

            string tempZip = destinationZipPath + ".tmp";
            if (File.Exists(tempZip)) File.Delete(tempZip);

            try
            {
                using (FileStream fs = File.Create(tempZip))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    void AddTextEntry(string entryName, string content)
                    {
                        byte[] data = Encoding.UTF8.GetBytes(content ?? "");
                        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                        using Stream s = entry.Open();
                        s.Write(data, 0, data.Length);
                        entries.Add(entryName);
                        totalBytes += data.Length;
                    }

                    void AddFileEntry(string entryName, string sourcePath)
                    {
                        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath) || IsSensitiveFile(sourcePath)) return;
                        try
                        {
                            using FileStream src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            long len = src.Length;
                            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                            using Stream dst = entry.Open();
                            if (len <= MaxEntryBytes)
                            {
                                src.CopyTo(dst);
                                totalBytes += len;
                            }
                            else
                            {
                                // Keep the newest tail for oversized log files so crashes are never clipped off.
                                byte[] header = Encoding.UTF8.GetBytes($"[Truncated: showing last {MaxEntryBytes} of {len} bytes]\n");
                                dst.Write(header, 0, header.Length);
                                src.Seek(-MaxEntryBytes, SeekOrigin.End);
                                src.CopyTo(dst);
                                totalBytes += header.Length + MaxEntryBytes;
                            }
                            entries.Add(entryName);
                        }
                        catch (Exception) { }
                    }

                    AddTextEntry("summary.txt", BuildSummary(gameDir));

                    if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
                    {
                        string clientIni = Game.FindFile(Game.ScriptsDir(gameDir), Game.ClientIni);
                        if (clientIni != null && File.Exists(clientIni))
                        {
                            try
                            {
                                AddTextEntry("config/" + Path.GetFileName(clientIni), RedactClientIni(File.ReadAllText(clientIni)));
                            }
                            catch (Exception) { }
                        }

                        string multifixIni = ConsoleWindow.IniPath(gameDir);
                        if (multifixIni != null) AddFileEntry("config/" + Path.GetFileName(multifixIni), multifixIni);

                        CollectLogs(gameDir, "game", AddFileEntry, entries);
                        string scripts = Game.ScriptsDir(gameDir);
                        if (Directory.Exists(scripts)) CollectLogs(scripts, "scripts", AddFileEntry, entries);
                    }

                    int appIdx = 0;
                    foreach (string dir in FindAppDataDirs(extraAppDataDirs))
                    {
                        string prefix = appIdx == 0 ? "appdata" : $"appdata-{appIdx}";
                        CollectAppDataFiles(dir, prefix, AddFileEntry, entries);
                        appIdx++;
                    }
                }

                File.Move(tempZip, destinationZipPath, true);
            }
            catch
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch (Exception) { }
                throw;
            }

            return new DiagnosticsResult(entries.Count, totalBytes, entries);
        }

        static void CollectLogs(string dir, string archivePrefix, Action<string, string> addFile, List<string> seenEntries)
        {
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { return; }

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (IsSensitiveFile(name)) continue;
                bool isLog = name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase);
                if (!isLog) continue;
                string target = archivePrefix + "/" + name;
                if (!seenEntries.Contains(target)) addFile(target, file);
            }
        }

        static void CollectAppDataFiles(string dir, string archivePrefix, Action<string, string> addFile, List<string> seenEntries)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly); }
            catch (Exception) { return; }

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (IsSensitiveFile(name)) continue;
                if (string.Equals(name, "event_catalog.json", StringComparison.OrdinalIgnoreCase)) continue;
                string target = archivePrefix + "/" + name;
                if (!seenEntries.Contains(target)) addFile(target, file);
            }
        }

        static IEnumerable<string> FindAppDataDirs(IEnumerable<string> extraDirs)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Consider(string candidate, List<string> list)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) return;
                string full = Path.GetFullPath(candidate);
                if (seen.Add(full)) list.Add(full);
            }

            List<string> results = new List<string>();
            if (extraDirs != null)
            {
                foreach (string d in extraDirs) Consider(d, results);
                return results;
            }

            if (OperatingSystem.IsWindows())
            {
                Consider(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenProStreet"), results);
            }
            else
            {
                Consider(LauncherSettings.ConfigDir, results);
                Consider(LauncherSettings.DataDir, results);
                string prefixUsers = Path.Combine(WinePrefix.DriveC(LauncherSettings.PrefixDir), "users");
                if (Directory.Exists(prefixUsers))
                {
                    try
                    {
                        foreach (string userDir in Directory.GetDirectories(prefixUsers))
                        {
                            Consider(Path.Combine(userDir, "AppData", "Local", "OpenProStreet"), results);
                        }
                    }
                    catch (Exception) { }
                }
            }
            return results;
        }

        static string BuildSummary(string gameDir)
        {
            StringBuilder sb = new StringBuilder();
            Version launcherVer = typeof(Diagnostics).Assembly.GetName().Version;
            sb.AppendLine("OpenProStreet Diagnostics Summary");
            sb.AppendLine("Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("Launcher Version: " + (launcherVer != null ? launcherVer.ToString() : "unknown"));
            sb.AppendLine("OS: " + RuntimeInformation.OSDescription);
            sb.AppendLine("Arch: " + RuntimeInformation.OSArchitecture);
            sb.AppendLine("Game Dir: " + (string.IsNullOrEmpty(gameDir) ? "(not set)" : gameDir));

            if (!string.IsNullOrEmpty(gameDir) && Directory.Exists(gameDir))
            {
                string exe = Game.ExePath(gameDir);
                sb.AppendLine("nfs.exe: " + (exe ?? "(missing)"));
                if (exe != null)
                {
                    sb.AppendLine("Supported Exe (v1.1): " + Game.IsSupportedExe(exe));
                    sb.AppendLine("Console Subsystem: " + (ConsoleWindow.Shown(gameDir)?.ToString() ?? "unknown"));
                    sb.AppendLine("Framerate Uncapped: " + (Framerate.Uncapped(gameDir)?.ToString() ?? "unknown"));
                }

                string scripts = Game.ScriptsDir(gameDir);
                string clientAsi = Game.FindFile(scripts, Game.ClientAsi);
                sb.AppendLine("Client ASI: " + (clientAsi != null ? (Game.FileVersionOf(clientAsi)?.ToString() ?? "present") : "(missing)"));
                if (Directory.Exists(scripts))
                {
                    try
                    {
                        List<string> plugins = new List<string>();
                        foreach (string file in Directory.GetFiles(scripts, "*.asi"))
                            plugins.Add(Path.GetFileName(file));
                        plugins.Sort(StringComparer.OrdinalIgnoreCase);
                        sb.AppendLine("Scripts ASIs: " + (plugins.Count > 0 ? string.Join(", ", plugins) : "(none)"));
                    }
                    catch (Exception) { }
                }
            }

            return sb.ToString();
        }
    }
}
