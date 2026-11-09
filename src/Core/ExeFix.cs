using System;
using System.IO;

namespace OpenProStreetLauncher
{
    // The 1.1 No-CD nfs.exe keeps a few small functions in the last 0x66 bytes of the file, past the
    // end its last section (.securom) declares. Windows maps whole pages from the file, so it gets
    // them anyway. Wine copies only the declared data and fills the rest of the page with zeros, so
    // the game jumps into zeros right after its window opens and crashes.
    //
    // The fix makes the last section's headers cover the whole file: its raw size goes up to the
    // file alignment (the file is padded with zeros to match), its virtual size follows, and so does
    // SizeOfImage. No byte of the game's code or data changes, and Windows maps it as before.
    static class ExeFix
    {
        struct Layout
        {
            public int OptionalHeader;   // file offset of the optional header
            public int LastSection;      // file offset of the section header that ends the file
            public uint FileAlignment, SectionAlignment;
            public uint RawPointer, RawSize, VirtualAddress, VirtualSize;
        }

        static bool Read(byte[] b, out Layout l)
        {
            l = default;
            if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') return false;
            int pe = BitConverter.ToInt32(b, 0x3C);
            if (pe <= 0 || pe + 24 > b.Length || BitConverter.ToUInt32(b, pe) != 0x00004550) return false; // "PE\0\0"
            int sections = BitConverter.ToUInt16(b, pe + 6);
            int optSize = BitConverter.ToUInt16(b, pe + 20);
            l.OptionalHeader = pe + 24;
            if (l.OptionalHeader + 60 > b.Length || BitConverter.ToUInt16(b, l.OptionalHeader) != 0x10B) return false; // PE32
            l.SectionAlignment = BitConverter.ToUInt32(b, l.OptionalHeader + 32);
            l.FileAlignment = BitConverter.ToUInt32(b, l.OptionalHeader + 36);
            if (l.FileAlignment == 0 || l.SectionAlignment == 0) return false;

            // The section whose data comes last in the file.
            uint lastEnd = 0;
            l.LastSection = -1;
            for (int i = 0; i < sections; i++)
            {
                int s = l.OptionalHeader + optSize + i * 40;
                if (s + 40 > b.Length) return false;
                uint raw = BitConverter.ToUInt32(b, s + 20), size = BitConverter.ToUInt32(b, s + 16);
                if (size == 0 || raw + size < lastEnd) continue;
                lastEnd = raw + size;
                l.LastSection = s;
            }
            if (l.LastSection < 0) return false;
            l.VirtualSize = BitConverter.ToUInt32(b, l.LastSection + 8);
            l.VirtualAddress = BitConverter.ToUInt32(b, l.LastSection + 12);
            l.RawSize = BitConverter.ToUInt32(b, l.LastSection + 16);
            l.RawPointer = BitConverter.ToUInt32(b, l.LastSection + 20);
            return true;
        }

        static uint AlignUp(uint value, uint alignment) => (value + alignment - 1) / alignment * alignment;

        // Only this exact case: the last section's declared size stops partway into its file alignment
        // and a few more bytes follow. Extra data after a properly aligned section, like a signature,
        // is something else and is left alone.
        static bool Applies(byte[] b, Layout l)
        {
            uint dataEnd = l.RawPointer + l.RawSize;
            return b.Length > dataEnd && l.RawSize % l.FileAlignment != 0 && b.Length - dataEnd < l.FileAlignment;
        }

        // True when the file has bytes after its last section's data that Wine wouldn't map.
        public static bool Needed(string exe)
        {
            try
            {
                byte[] b = File.ReadAllBytes(exe);
                return Read(b, out Layout l) && Applies(b, l);
            }
            catch (Exception) { return false; }
        }

        // Fixes the exe in place, keeping the original next to it. Returns false if it didn't need it.
        public static bool Apply(string exe)
        {
            byte[] b = File.ReadAllBytes(exe);
            if (!Read(b, out Layout l) || !Applies(b, l)) return false;

            uint rawSize = AlignUp((uint)b.Length - l.RawPointer, l.FileAlignment);
            uint virtualSize = Math.Max(l.VirtualSize, rawSize);
            uint imageSize = AlignUp(l.VirtualAddress + virtualSize, l.SectionAlignment);
            uint oldImageSize = BitConverter.ToUInt32(b, l.OptionalHeader + 56);

            byte[] fixedExe = new byte[l.RawPointer + rawSize];
            Array.Copy(b, fixedExe, b.Length); // the padding stays zero
            BitConverter.GetBytes(virtualSize).CopyTo(fixedExe, l.LastSection + 8);
            BitConverter.GetBytes(rawSize).CopyTo(fixedExe, l.LastSection + 16);
            BitConverter.GetBytes(Math.Max(imageSize, oldImageSize)).CopyTo(fixedExe, l.OptionalHeader + 56);

            string backup = exe + ".before-proton-fix";
            if (!File.Exists(backup)) File.Copy(exe, backup);
            string temp = exe + ".tmp";
            File.WriteAllBytes(temp, fixedExe);
            File.Move(temp, exe, true);
            return true;
        }
    }
}
