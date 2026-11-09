using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;

namespace OpenProStreetLauncher
{
    // Copies a DVD image's files to a folder, so its installer can run in Proton. The image is read
    // directly, so nothing has to be mounted and no admin password is needed.
    static class Disc
    {
        static DiscFileSystem Open(Stream iso)
        {
            if (UdfReader.Detect(iso)) return new UdfReader(iso);
            iso.Position = 0;
            if (CDReader.Detect(iso)) return new CDReader(iso, true);
            throw new InvalidDataException("That file isn't a DVD or CD image.");
        }

        // Copies every file, reporting (bytes done, bytes in all) as it goes.
        public static void Extract(string isoPath, string target, Action<long, long> progress = null)
        {
            using FileStream iso = File.OpenRead(isoPath);
            using DiscFileSystem fs = Open(iso);
            List<string> files = fs.GetFiles("", "*.*", SearchOption.AllDirectories).ToList();
            long total = files.Sum(f => fs.GetFileLength(f));
            long done = 0;
            byte[] buffer = new byte[1 << 20];
            foreach (string file in files)
            {
                string relative = file.TrimStart('\\').Replace('\\', '/');
                int version = relative.IndexOf(';'); // ISO 9660 file versions, like SETUP.EXE;1
                if (version >= 0) relative = relative.Substring(0, version);
                string output = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using Stream source = fs.OpenFile(file, FileMode.Open, FileAccess.Read);
                using FileStream dest = File.Create(output);
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dest.Write(buffer, 0, read);
                    done += read;
                    progress?.Invoke(done, total);
                }
            }
        }

        // The disc's installer: what autorun.inf starts, or EA's easetup.exe, or setup.exe.
        public static string FindInstaller(string dir)
        {
            string autorun = Game.FindFile(dir, "autorun.inf");
            if (autorun != null)
            {
                foreach (string raw in File.ReadAllLines(autorun))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("open=", StringComparison.OrdinalIgnoreCase)) continue;
                    string program = line.Substring(5).Trim().Trim('"').Split(' ')[0].Replace('\\', '/');
                    string path = FindPath(dir, program);
                    if (path != null) return path;
                }
            }
            foreach (string name in new[] { "easetup.exe", "setup.exe", "autorun.exe" })
            {
                string path = Game.FindFile(dir, name);
                if (path != null) return path;
            }
            return null;
        }

        // A relative path, matched in any case one folder at a time, as Windows would.
        static string FindPath(string dir, string relative)
        {
            string current = dir;
            string[] parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                current = i == parts.Length - 1 ? Game.FindFile(current, parts[i]) : Game.FindDir(current, parts[i]);
                if (current == null) return null;
            }
            return current;
        }
    }

    static class AppInfo
    {
        public static string Version
        {
            get
            {
                Version v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "0" : v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
