using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenProStreetLauncher
{
    class Build
    {
        public long Id;
        public int Number;
        public string Version;
    }

    // Downloads from OneDev's build artifacts and the ASI loader's GitHub release, the same places the
    // PowerShell installer uses. The calls block, so the window runs them on a worker thread.
    static class Downloads
    {
        const string OneDev = "https://code.weexnes.dev";
        const string LoaderUrl = "https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases/download/Win32-latest/dinput8-Win32.zip";
        const string ClientQuery = "\"Project\" is \"OpenProStreetClient\" and \"Job\" is \"Build Release Tag\" and successful";
        const string MultiFixQuery = "\"Project\" is \"xnfsps-multifix\" and \"Job\" is \"Build\" and \"Branch\" is \"master\" and successful";

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenProStreetLauncher/" + AppInfo.Version);
            return client;
        }

        static Build LatestBuild(string query)
        {
            string url = OneDev + "/~api/builds?query=" + Uri.EscapeDataString(query) + "&offset=0&count=1";
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using HttpResponseMessage response = Http.Send(request);
            response.EnsureSuccessStatusCode();
            using Stream body = response.Content.ReadAsStream();
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                throw new Exception("No build found on OneDev.");
            JsonElement b = doc.RootElement[0];
            return new Build
            {
                Id = b.GetProperty("id").GetInt64(),
                Number = b.GetProperty("number").GetInt32(),
                Version = b.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
            };
        }

        public static Build LatestClient() => LatestBuild(ClientQuery);

        const string LauncherQuery = "\"Project\" is \"OpenProStreetClient/OpenProStreetLauncher\" and \"Job\" is \"Build Release Tag\" and successful";
        public static string LauncherArtifact => OperatingSystem.IsWindows() ? "OpenProStreetLauncher.exe" : "OpenProStreetLauncher";

        public static Build LatestLauncher() => LatestBuild(LauncherQuery);

        // Downloads one of a build's files to the given path.
        public static void DownloadArtifact(Build build, string name, string target) =>
            SaveUrl(OneDev + "/~api/artifacts/" + build.Id + "/contents/" + Uri.EscapeDataString(name), target);

        // "8.5" from a release build's version, or null.
        public static Version ParseVersion(string text)
        {
            if (text == null) return null;
            Match m = Regex.Match(text, @"\d+(\.\d+){1,3}");
            if (!m.Success) return null;
            Version v = new Version(m.Value);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        static void SaveUrl(string url, string target)
        {
            using HttpResponseMessage response = Http.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using Stream body = response.Content.ReadAsStream();
            using FileStream file = File.Create(target);
            body.CopyTo(file);
        }

        // Saves next to the target first, so a broken download never replaces a working file.
        static void Save(string url, string target)
        {
            string temp = target + ".download";
            try
            {
                SaveUrl(url, temp);
                File.Move(temp, target, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        static void SaveArtifact(Build build, string name, string target)
        {
            Save(OneDev + "/~api/artifacts/" + build.Id + "/contents/" + Uri.EscapeDataString(name), target);
        }

        public static void InstallClient(string scripts, Build build, bool replaceAsi)
        {
            Directory.CreateDirectory(scripts);
            string asi = (Game.FindFile(scripts, Game.ClientAsi) ?? Path.Combine(scripts, Game.ClientAsi));
            if (replaceAsi || !File.Exists(asi)) SaveArtifact(build, Game.ClientAsi, asi);
            string sdk = (Game.FindFile(scripts, Game.DiscordSdk) ?? Path.Combine(scripts, Game.DiscordSdk));
            if (!File.Exists(sdk)) SaveArtifact(build, Game.DiscordSdk, sdk);
        }

        // An existing MultiFix ini holds the player's own settings, so it's never replaced.
        public static void InstallMultiFix(string scripts)
        {
            Directory.CreateDirectory(scripts);
            Build build = LatestBuild(MultiFixQuery);
            string asi = (Game.FindFile(scripts, Game.MultiFixAsi) ?? Path.Combine(scripts, Game.MultiFixAsi));
            string ini = (Game.FindFile(scripts, Game.MultiFixIni) ?? Path.Combine(scripts, Game.MultiFixIni));
            if (!File.Exists(asi)) SaveArtifact(build, Game.MultiFixAsi, asi);
            if (!File.Exists(ini)) SaveArtifact(build, Game.MultiFixIni, ini);
        }

        // The zip carries the SHA512 of its dll, and a file that doesn't match it is refused.
        public static void InstallLoader(string gameDir)
        {
            string dll = (Game.FindFile(gameDir, Game.Loader) ?? Path.Combine(gameDir, Game.Loader));
            if (File.Exists(dll)) return;
            string work = Path.Combine(Path.GetTempPath(), "openprostreet-loader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string zip = Path.Combine(work, "loader.zip");
                SaveUrl(LoaderUrl, zip);
                string x = Path.Combine(work, "x");
                ZipFile.ExtractToDirectory(zip, x);
                string fresh = Path.Combine(x, Game.Loader);
                if (!File.Exists(fresh)) throw new Exception("The loader download has no dinput8.dll in it.");
                string sumFile = Directory.GetFiles(x, "*.SHA512").FirstOrDefault();
                if (sumFile != null)
                {
                    string expected = Regex.Match(File.ReadAllText(sumFile), "[0-9A-Fa-f]{128}").Value;
                    string actual = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(fresh)));
                    if (expected.Length == 0 || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("dinput8.dll doesn't match its SHA512 checksum, so it wasn't installed.");
                }
                File.Copy(fresh, dll);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (Exception) { }
            }
        }
    }
}
