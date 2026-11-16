using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenProStreetLauncher
{
    // Texture packs for the game, without TexMod. A pack is unpacked into
    // scripts\OpenProStreetTextures\<pack>, one file per texture, named <id>.dds after the id TexMod
    // gives the game's texture it replaces. The client swaps the textures in as the game loads them.
    //
    // Packs come as TexMod's .tpf files, or as a .zip of the same contents. A .tpf is a zip file with
    // every byte scrambled and its entries encrypted with one fixed password. Inside is texmod.def,
    // which lists "0x<id>|<file>" per texture, and the texture files themselves.
    static class TexturePacks
    {
        public const string FolderName = "OpenProStreetTextures";

        // The largest single texture that's unpacked. A real one is a few megabytes.
        const int MaxTextureBytes = 256 * 1024 * 1024;

        static readonly byte[] TpfScramble = { 0xA4, 0x3F, 0xA4, 0x3F };
        static readonly byte[] TpfPassword =
        {
            0x73, 0x2A, 0x63, 0x7D, 0x5F, 0x0A, 0xA6, 0xBD, 0x7D, 0x65, 0x7E, 0x67, 0x61, 0x2A, 0x7F, 0x7F, 0x74, 0x61, 0x67, 0x5B, 0x60,
            0x70, 0x45, 0x74, 0x5C, 0x22, 0x74, 0x5D, 0x6E, 0x6A, 0x73, 0x41, 0x77, 0x6E, 0x46, 0x47, 0x77, 0x49, 0x0C, 0x4B, 0x46, 0x6F,
        };

        static readonly Regex IdInName = new Regex(@"(?:^|_0x|[\\/])([0-9A-Fa-f]{8})\.[A-Za-z0-9]+$");
        static readonly uint[] CrcTable = BuildCrcTable();

        public class Pack
        {
            public string Name;
            public int Textures;
            public long Bytes;
        }

        public static string Root(string gameDir) => Path.Combine(Game.ScriptsDir(gameDir), FolderName);

        public static List<Pack> Installed(string gameDir)
        {
            var packs = new List<Pack>();
            string root = Root(gameDir);
            if (!Directory.Exists(root)) return packs;
            foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".")) continue; // a pack that was being unpacked when the launcher closed
                FileInfo[] files = new DirectoryInfo(dir).GetFiles("*.dds");
                packs.Add(new Pack { Name = name, Textures = files.Length, Bytes = files.Sum(f => f.Length) });
            }
            return packs;
        }

        // Unpacks a .tpf or .zip into its own folder, replacing a pack of the same name.
        public static Pack Install(string gameDir, string file)
        {
            string name = CleanName(Path.GetFileNameWithoutExtension(file));
            byte[] data = File.ReadAllBytes(file);
            if (!StartsWithZip(data))
            {
                for (int i = 0; i < data.Length; i++) data[i] ^= TpfScramble[i & 3];
                if (!StartsWithZip(data)) throw new InvalidDataException("That isn't a TexMod pack (.tpf) or a .zip file.");
            }

            List<Entry> entries = ReadDirectory(data);
            Dictionary<string, uint> ids = ReadDefinition(data, entries);
            string root = Root(gameDir);
            string temp = Path.Combine(root, ".new-" + name);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            try
            {
                int count = 0;
                foreach (Entry entry in entries)
                {
                    if (entry.Name.EndsWith("/") || entry.Name.EndsWith(".def", StringComparison.OrdinalIgnoreCase)) continue;
                    uint id;
                    if (!ids.TryGetValue(Path.GetFileName(entry.Name), out id))
                    {
                        Match m = IdInName.Match(entry.Name);
                        if (!m.Success) continue; // not a texture, such as a readme
                        id = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
                    }
                    // The client finds the picture's real format from the file itself, whatever its name.
                    File.WriteAllBytes(Path.Combine(temp, id.ToString("X8") + ".dds"), Unpack(data, entry));
                    count++;
                }
                if (count == 0) throw new InvalidDataException("That pack has no textures in it.");
                string target = Path.Combine(root, name);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(temp, target);
            }
            catch
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                throw;
            }
            return Installed(gameDir).First(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static void Remove(string gameDir, string name)
        {
            string dir = Path.Combine(Root(gameDir), CleanName(name));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        // A folder name from a file name: nothing that leaves the textures folder.
        static string CleanName(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in name ?? "")
                clean.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' ? c : '_');
            string result = clean.ToString().Trim(' ', '.');
            return result.Length == 0 ? "pack" : result;
        }

        class Entry
        {
            public string Name;
            public int Flags, Method;
            public uint Crc;
            public long Packed, Size, LocalHeader;
        }

        static bool StartsWithZip(byte[] d) => d.Length > 22 && d[0] == 0x50 && d[1] == 0x4B && d[2] == 0x03 && d[3] == 0x04;

        static List<Entry> ReadDirectory(byte[] d)
        {
            // TexMod leaves bytes after the zip's end record, so it's searched for from the back.
            int end = -1;
            for (int i = d.Length - 22; i >= 0; i--)
                if (d[i] == 0x50 && d[i + 1] == 0x4B && d[i + 2] == 0x05 && d[i + 3] == 0x06) { end = i; break; }
            if (end < 0) throw new InvalidDataException("The pack is damaged: its list of files is missing.");
            int count = BitConverter.ToUInt16(d, end + 10);
            long at = BitConverter.ToUInt32(d, end + 16);
            if (count == 0xFFFF || at == 0xFFFFFFFF) throw new InvalidDataException("The pack is too large for the launcher to read.");

            var entries = new List<Entry>();
            for (int i = 0; i < count; i++)
            {
                if (at + 46 > d.Length || BitConverter.ToUInt32(d, (int)at) != 0x02014B50) throw new InvalidDataException("The pack is damaged: its list of files is cut short.");
                int p = (int)at;
                int nameLength = BitConverter.ToUInt16(d, p + 28);
                var e = new Entry
                {
                    Flags = BitConverter.ToUInt16(d, p + 8),
                    Method = BitConverter.ToUInt16(d, p + 10),
                    Crc = BitConverter.ToUInt32(d, p + 16),
                    Packed = BitConverter.ToUInt32(d, p + 20),
                    Size = BitConverter.ToUInt32(d, p + 24),
                    LocalHeader = BitConverter.ToUInt32(d, p + 42),
                    Name = Encoding.Latin1.GetString(d, p + 46, nameLength),
                };
                entries.Add(e);
                at += 46 + nameLength + BitConverter.ToUInt16(d, p + 30) + BitConverter.ToUInt16(d, p + 32);
            }
            return entries;
        }

        // texmod.def: one "0x<id>|<file>" line per texture. A pack without it names its files by id.
        static Dictionary<string, uint> ReadDefinition(byte[] d, List<Entry> entries)
        {
            var ids = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            Entry def = entries.FirstOrDefault(e => Path.GetFileName(e.Name).Equals("texmod.def", StringComparison.OrdinalIgnoreCase));
            if (def == null) return ids;
            foreach (string raw in Encoding.Latin1.GetString(Unpack(d, def)).Split('\n'))
            {
                string line = raw.Trim('\r', ' ', '\0');
                int bar = line.IndexOf('|');
                if (bar < 3 || !line.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) continue;
                uint id;
                if (!uint.TryParse(line.Substring(2, bar - 2), NumberStyles.HexNumber, null, out id)) continue;
                string name = Path.GetFileName(line.Substring(bar + 1).Replace('\\', '/'));
                if (name.Length > 0) ids[name] = id;
            }
            return ids;
        }

        static byte[] Unpack(byte[] d, Entry e)
        {
            if (e.Size > MaxTextureBytes || e.Packed > MaxTextureBytes) throw new InvalidDataException(e.Name + " in the pack is too large.");
            long header = e.LocalHeader;
            if (header + 30 > d.Length || BitConverter.ToUInt32(d, (int)header) != 0x04034B50) throw new InvalidDataException("The pack is damaged at " + e.Name + ".");
            long start = header + 30 + BitConverter.ToUInt16(d, (int)header + 26) + BitConverter.ToUInt16(d, (int)header + 28);
            if (start + e.Packed > d.Length) throw new InvalidDataException("The pack is cut short at " + e.Name + ".");

            byte[] packed = new byte[e.Packed];
            Buffer.BlockCopy(d, (int)start, packed, 0, packed.Length);
            int skip = 0;
            if ((e.Flags & 1) != 0)
            {
                if (packed.Length < 12) throw new InvalidDataException("The pack is damaged at " + e.Name + ".");
                Decrypt(packed);
                skip = 12; // the encryption's own header
            }

            byte[] result;
            if (e.Method == 0)
            {
                result = new byte[packed.Length - skip];
                Buffer.BlockCopy(packed, skip, result, 0, result.Length);
            }
            else if (e.Method == 8)
            {
                result = new byte[e.Size];
                try
                {
                    using (var inflate = new DeflateStream(new MemoryStream(packed, skip, packed.Length - skip), CompressionMode.Decompress))
                    {
                        int got = 0, n;
                        while (got < result.Length && (n = inflate.Read(result, got, result.Length - got)) > 0) got += n;
                        if (got != result.Length) throw new InvalidDataException();
                    }
                }
                catch (InvalidDataException)
                {
                    throw new InvalidDataException(WrongContents(e));
                }
            }
            else throw new InvalidDataException(e.Name + " in the pack is packed in a way the launcher can't read.");

            if (Crc(result) != e.Crc) throw new InvalidDataException(WrongContents(e));
            return result;
        }

        static string WrongContents(Entry e) => (e.Flags & 1) != 0
            ? "The pack is protected with a password other than TexMod's, so it can't be read."
            : "The pack is damaged at " + e.Name + ".";

        // The classic zip encryption, with TexMod's password.
        static void Decrypt(byte[] data)
        {
            uint k0 = 0x12345678, k1 = 0x23456789, k2 = 0x34567890;
            foreach (byte c in TpfPassword) Update(ref k0, ref k1, ref k2, c);
            for (int i = 0; i < data.Length; i++)
            {
                uint t = (k2 | 2) & 0xFFFF;
                data[i] ^= (byte)((t * (t ^ 1)) >> 8);
                Update(ref k0, ref k1, ref k2, data[i]);
            }
        }

        static void Update(ref uint k0, ref uint k1, ref uint k2, byte c)
        {
            k0 = CrcTable[(k0 ^ c) & 0xFF] ^ (k0 >> 8);
            k1 = unchecked((k1 + (k0 & 0xFF)) * 134775813 + 1);
            k2 = CrcTable[(k2 ^ (k1 >> 24)) & 0xFF] ^ (k2 >> 8);
        }

        static uint Crc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
