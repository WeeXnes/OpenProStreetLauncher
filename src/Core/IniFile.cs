using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenProStreetLauncher
{
    // A small ini reader and writer that reads files the way Windows' GetPrivateProfileString does
    // (sections and keys in any case, ; comments) and changes only the lines it sets, so comments
    // and anything else in the file stay as they are.
    class IniFile
    {
        readonly string m_path;
        readonly List<string> m_lines = new List<string>();
        string m_newline = "\r\n"; // the client reads either, and Windows tools expect this

        public IniFile(string path)
        {
            m_path = path;
            if (path == null || !File.Exists(path)) return;
            string text = File.ReadAllText(path);
            if (!text.Contains("\r\n") && text.Contains('\n')) m_newline = "\n";
            m_lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
            if (m_lines.Count > 0 && m_lines[m_lines.Count - 1] == "") m_lines.RemoveAt(m_lines.Count - 1);
        }

        public bool Exists => m_path != null && File.Exists(m_path);

        static string SectionOf(string line)
        {
            string t = line.Trim();
            return t.StartsWith('[') && t.Contains(']') ? t.Substring(1, t.IndexOf(']') - 1).Trim() : null;
        }

        static bool IsKey(string line, string key, out string value)
        {
            value = null;
            string t = line.TrimStart();
            if (t.StartsWith(';') || t.StartsWith('#')) return false;
            int eq = t.IndexOf('=');
            if (eq <= 0 || !string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return false;
            value = Regex.Replace(t.Substring(eq + 1), @"\s+//.*$", "").Trim(); // a // comment after the value isn't part of it
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') value = value.Substring(1, value.Length - 2);
            return true;
        }

        // null when the key isn't there. A key that's there but empty reads as "".
        public string Get(string section, string key)
        {
            string current = null;
            foreach (string line in m_lines)
            {
                string s = SectionOf(line);
                if (s != null) { current = s; continue; }
                if (string.Equals(current, section, StringComparison.OrdinalIgnoreCase) && IsKey(line, key, out string value)) return value;
            }
            return null;
        }

        public bool GetBool(string section, string key, bool def)
        {
            string value = Get(section, key);
            return value != null && int.TryParse(value, out int n) ? n != 0 : def;
        }

        public void Set(string section, string key, string value)
        {
            string current = null;
            int sectionEnd = -1;
            for (int i = 0; i < m_lines.Count; i++)
            {
                string s = SectionOf(m_lines[i]);
                if (s != null)
                {
                    current = s;
                    if (string.Equals(s, section, StringComparison.OrdinalIgnoreCase)) sectionEnd = i + 1;
                    continue;
                }
                if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsKey(m_lines[i], key, out _))
                {
                    // Keeps the line's own spelling, spacing and any // comment after the value,
                    // as in MultiFix's "EnablePrints = 1    // Enables printing to the console."
                    string line = m_lines[i];
                    int eq = line.IndexOf('=');
                    string after = line.Substring(eq + 1);
                    string space = after.Length > 0 && (after[0] == ' ' || after[0] == '\t') ? after.Substring(0, after.Length - after.TrimStart().Length) : "";
                    Match comment = Regex.Match(after, @"\s+//.*$");
                    m_lines[i] = line.Substring(0, eq + 1) + space + value + (comment.Success ? comment.Value : "");
                    return;
                }
                if (m_lines[i].Trim().Length > 0) sectionEnd = i + 1;
            }
            if (sectionEnd >= 0)
            {
                m_lines.Insert(sectionEnd, key + "=" + value);
                return;
            }
            if (m_lines.Count > 0 && m_lines[m_lines.Count - 1].Trim().Length > 0) m_lines.Add("");
            m_lines.Add("[" + section + "]");
            m_lines.Add(key + "=" + value);
        }

        public void SetBool(string section, string key, bool value) => Set(section, key, value ? "1" : "0");

        public void Save(string header = null)
        {
            // A new file gets the header as its first line.
            if (!Exists && header != null && (m_lines.Count == 0 || m_lines[0] != header)) m_lines.Insert(0, header);
            StringBuilder sb = new StringBuilder();
            foreach (string line in m_lines) sb.Append(line).Append(m_newline);
            string temp = m_path + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            File.Move(temp, m_path, true);
        }
    }
}
