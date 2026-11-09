using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenProStreetLauncher
{
    // An ordered key list, as Steam's KeyValues files keep it. Keys match in any case, as Steam's do.
    class VdfMap
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

        int IndexOf(string key)
        {
            for (int i = 0; i < Items.Count; i++)
                if (string.Equals(Items[i].Key, key, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void Set(string key, object value)
        {
            int i = IndexOf(key);
            if (i >= 0) Items[i] = new KeyValuePair<string, object>(Items[i].Key, value);
            else Items.Add(new KeyValuePair<string, object>(key, value));
        }

        public void SetDefault(string key, object value)
        {
            if (IndexOf(key) < 0) Items.Add(new KeyValuePair<string, object>(key, value));
        }

        public object Get(string key)
        {
            int i = IndexOf(key);
            return i >= 0 ? Items[i].Value : null;
        }

        public VdfMap GetMap(string key) => Get(key) as VdfMap;
        public string GetString(string key) => Get(key) as string;

        // The map at this path of keys, created where it's missing.
        public VdfMap Path(params string[] keys)
        {
            VdfMap map = this;
            foreach (string key in keys)
            {
                VdfMap next = map.GetMap(key);
                if (next == null)
                {
                    next = new VdfMap();
                    map.Set(key, next);
                }
                map = next;
            }
            return map;
        }
    }

    // Steam's binary KeyValues (shortcuts.vdf): a type byte, a key (UTF-8, NUL-terminated), then the
    // value. 0 = map, 1 = string, 2 = int32, 3 = float, 7 = uint64, 8 = end of map.
    static class BinaryVdf
    {
        public static VdfMap Read(byte[] data)
        {
            int pos = 0;
            return ReadMap(data, ref pos);
        }

        static VdfMap ReadMap(byte[] d, ref int pos)
        {
            VdfMap map = new VdfMap();
            while (pos < d.Length)
            {
                byte type = d[pos++];
                if (type == 8) return map;
                string key = ReadString(d, ref pos);
                switch (type)
                {
                    case 0: map.Items.Add(new KeyValuePair<string, object>(key, ReadMap(d, ref pos))); break;
                    case 1: map.Items.Add(new KeyValuePair<string, object>(key, ReadString(d, ref pos))); break;
                    case 2: map.Items.Add(new KeyValuePair<string, object>(key, BitConverter.ToInt32(d, pos))); pos += 4; break;
                    case 3: map.Items.Add(new KeyValuePair<string, object>(key, BitConverter.ToSingle(d, pos))); pos += 4; break;
                    case 7: map.Items.Add(new KeyValuePair<string, object>(key, BitConverter.ToUInt64(d, pos))); pos += 8; break;
                    default: throw new InvalidDataException("shortcuts.vdf has a value type the launcher doesn't know (" + type + "), so it wasn't changed.");
                }
            }
            return map; // the file's root ends without its own 8
        }

        static string ReadString(byte[] d, ref int pos)
        {
            int end = Array.IndexOf(d, (byte)0, pos);
            if (end < 0) throw new InvalidDataException("shortcuts.vdf is damaged, so it wasn't changed.");
            string s = Encoding.UTF8.GetString(d, pos, end - pos);
            pos = end + 1;
            return s;
        }

        public static byte[] Write(VdfMap root)
        {
            MemoryStream m = new MemoryStream();
            WriteItems(m, root);
            m.WriteByte(8);
            return m.ToArray();
        }

        static void WriteItems(MemoryStream m, VdfMap map)
        {
            foreach (KeyValuePair<string, object> item in map.Items)
            {
                switch (item.Value)
                {
                    case VdfMap child:
                        m.WriteByte(0); WriteString(m, item.Key);
                        WriteItems(m, child);
                        m.WriteByte(8);
                        break;
                    case string s: m.WriteByte(1); WriteString(m, item.Key); WriteString(m, s); break;
                    case int i: m.WriteByte(2); WriteString(m, item.Key); m.Write(BitConverter.GetBytes(i)); break;
                    case float f: m.WriteByte(3); WriteString(m, item.Key); m.Write(BitConverter.GetBytes(f)); break;
                    case ulong u: m.WriteByte(7); WriteString(m, item.Key); m.Write(BitConverter.GetBytes(u)); break;
                }
            }
        }

        static void WriteString(MemoryStream m, string s)
        {
            m.Write(Encoding.UTF8.GetBytes(s));
            m.WriteByte(0);
        }
    }

    // Steam's text KeyValues (config.vdf, libraryfolders.vdf, compatibilitytool.vdf): quoted keys and
    // values, braces for maps, // comments. Values are kept as strings.
    static class TextVdf
    {
        public static VdfMap Read(string text)
        {
            int pos = 0;
            VdfMap root = ReadMap(text, ref pos, true);
            return root;
        }

        static VdfMap ReadMap(string t, ref int pos, bool root)
        {
            VdfMap map = new VdfMap();
            while (true)
            {
                string key = NextToken(t, ref pos, out bool quoted);
                if (key == null)
                {
                    if (root) return map;
                    throw new InvalidDataException("A Steam settings file ends early, so it wasn't changed.");
                }
                if (key == "}" && !quoted)
                {
                    if (root) throw new InvalidDataException("A Steam settings file has an extra }, so it wasn't changed.");
                    return map;
                }
                string value = NextToken(t, ref pos, out bool valueQuoted);
                if (value == null) throw new InvalidDataException("A Steam settings file ends early, so it wasn't changed.");
                if (value == "{" && !valueQuoted) map.Items.Add(new KeyValuePair<string, object>(key, ReadMap(t, ref pos, false)));
                else map.Items.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        static string NextToken(string t, ref int pos, out bool quoted)
        {
            quoted = false;
            while (pos < t.Length)
            {
                char c = t[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }
                if (c == '/' && pos + 1 < t.Length && t[pos + 1] == '/')
                {
                    while (pos < t.Length && t[pos] != '\n') pos++;
                    continue;
                }
                break;
            }
            if (pos >= t.Length) return null;
            if (t[pos] == '{' || t[pos] == '}') return t[pos++].ToString();
            StringBuilder sb = new StringBuilder();
            if (t[pos] == '"')
            {
                quoted = true;
                pos++;
                while (pos < t.Length && t[pos] != '"')
                {
                    if (t[pos] == '\\' && pos + 1 < t.Length)
                    {
                        char e = t[pos + 1];
                        sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                        pos += 2;
                        continue;
                    }
                    sb.Append(t[pos++]);
                }
                pos++; // the closing quote
                return sb.ToString();
            }
            while (pos < t.Length && !char.IsWhiteSpace(t[pos]) && t[pos] != '{' && t[pos] != '}' && t[pos] != '"') sb.Append(t[pos++]);
            return sb.ToString();
        }

        public static string Write(VdfMap root)
        {
            StringBuilder sb = new StringBuilder();
            WriteItems(sb, root, 0);
            return sb.ToString();
        }

        static void WriteItems(StringBuilder sb, VdfMap map, int depth)
        {
            string indent = new string('\t', depth);
            foreach (KeyValuePair<string, object> item in map.Items)
            {
                if (item.Value is VdfMap child)
                {
                    sb.Append(indent).Append(Quote(item.Key)).Append('\n');
                    sb.Append(indent).Append("{\n");
                    WriteItems(sb, child, depth + 1);
                    sb.Append(indent).Append("}\n");
                }
                else
                {
                    sb.Append(indent).Append(Quote(item.Key)).Append("\t\t").Append(Quote(Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture))).Append('\n');
                }
            }
        }

        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
