using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenProStreetLauncher
{
    // DXVK on Windows: its d3d9.dll next to nfs.exe makes the game draw through Vulkan, not DirectX 9.
    // That lets tools that only hook Vulkan, such as ReShade's Vulkan version, work with the game.
    // Under Proton the game runs on DXVK already, so this is for Windows only.
    //
    // A small file next to the dll, d3d9.dll.dxvk, holds the version the launcher installed. A d3d9.dll
    // without it came from something else, such as ReShade for DirectX 9, and is never touched.
    static class Dxvk
    {
        const string LatestUrl = "https://api.github.com/repos/doitsujin/dxvk/releases/latest";
        public const string DllName = "d3d9.dll";
        const string MarkerSuffix = ".dxvk";

        // The Windows build, such as dxvk-2.7.tar.gz: not the "native" builds for Linux games.
        static readonly Regex ArchiveName = new Regex(@"^dxvk-\d[\d.]*\.tar\.gz$", RegexOptions.IgnoreCase);

        public static string DllPath(string gameDir) => Game.FindFile(gameDir, DllName) ?? Path.Combine(gameDir, DllName);
        static string MarkerPath(string gameDir) => DllPath(gameDir) + MarkerSuffix;

        public static bool IsInstalled(string gameDir) => File.Exists(DllPath(gameDir)) && File.Exists(MarkerPath(gameDir));

        // A d3d9.dll the launcher didn't put there.
        public static bool HasOtherDll(string gameDir) => File.Exists(DllPath(gameDir)) && !File.Exists(MarkerPath(gameDir));

        // "v2.7", or null when DXVK isn't installed.
        public static string InstalledVersion(string gameDir)
        {
            if (!IsInstalled(gameDir)) return null;
            try { return File.ReadAllText(MarkerPath(gameDir)).Trim(); }
            catch (IOException) { return ""; }
        }

        // Downloads the newest DXVK release and installs its 32-bit d3d9.dll.
        public static void Install(string gameDir)
        {
            if (HasOtherDll(gameDir))
                throw new InvalidOperationException("There's already a d3d9.dll in the game folder that isn't DXVK, such as ReShade for DirectX 9. Remove it first.");

            string version, url = null;
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, LatestUrl))
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = Downloads.Http.Send(request);
                response.EnsureSuccessStatusCode();
                using Stream body = response.Content.ReadAsStream();
                using JsonDocument doc = JsonDocument.Parse(body);
                version = doc.RootElement.GetProperty("tag_name").GetString();
                foreach (JsonElement asset in doc.RootElement.GetProperty("assets").EnumerateArray())
                    if (ArchiveName.IsMatch(asset.GetProperty("name").GetString() ?? "")) url = asset.GetProperty("browser_download_url").GetString();
            }
            if (url == null) throw new Exception("DXVK's newest release has no Windows build to download.");

            string archive = Path.Combine(Path.GetTempPath(), "openprostreet-dxvk-" + Guid.NewGuid().ToString("N") + ".tar.gz");
            try
            {
                Downloads.SaveUrl(url, archive);
                using FileStream file = File.OpenRead(archive);
                InstallFrom(gameDir, file, version);
            }
            finally
            {
                if (File.Exists(archive)) File.Delete(archive);
            }
        }

        // Installs the 32-bit d3d9.dll from a DXVK release archive (.tar.gz).
        public static void InstallFrom(string gameDir, Stream archive, string version)
        {
            if (HasOtherDll(gameDir))
                throw new InvalidOperationException("There's already a d3d9.dll in the game folder that isn't DXVK, such as ReShade for DirectX 9. Remove it first.");
            string dll = DllPath(gameDir);
            string temp = dll + ".download";
            bool found = false;
            try
            {
                using (GZipStream unzipped = new GZipStream(archive, CompressionMode.Decompress))
                using (TarReader tar = new TarReader(unzipped))
                {
                    TarEntry entry;
                    while ((entry = tar.GetNextEntry()) != null)
                    {
                        // The game is a 32-bit program: x32, not x64.
                        if (entry.DataStream == null || !entry.Name.Replace('\\', '/').EndsWith("/x32/" + DllName, StringComparison.OrdinalIgnoreCase)) continue;
                        using (FileStream output = File.Create(temp)) entry.DataStream.CopyTo(output);
                        found = true;
                        break;
                    }
                }
                if (!found || new FileInfo(temp).Length < 2 || !StartsLikeDll(temp)) throw new InvalidDataException("The DXVK download has no 32-bit d3d9.dll in it.");
                File.Move(temp, dll, true);
                File.WriteAllText(dll + MarkerSuffix, version ?? "");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // Back to DirectX 9. Leaves a d3d9.dll alone that the launcher didn't install.
        public static void Remove(string gameDir)
        {
            if (!IsInstalled(gameDir)) return;
            string dll = DllPath(gameDir);
            File.Delete(dll);
            File.Delete(dll + MarkerSuffix);
        }

        static bool StartsLikeDll(string path)
        {
            using FileStream file = File.OpenRead(path);
            return file.ReadByte() == 'M' && file.ReadByte() == 'Z';
        }
    }
}
