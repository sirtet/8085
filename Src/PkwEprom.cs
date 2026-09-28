using System;
using System.IO;
using System.Text;

namespace _8085
{
    // A removable chip, independent of CPU/reset state. Addresses in image files start at zero.
    sealed class PkwEprom
    {
        internal static readonly string[] Names = { "2764", "2564", "2732", "2532", "2732A", "2716", "48016" };
        internal static int TypeFromSwitches(byte switches)
        {
            switch (switches & 0x3C) {
                case 0x1C: return 0; case 0x2C: return 1; case 0x18: return 2;
                case 0x28: return 3; case 0x38: return 4; case 0x14: return 5;
                case 0x34: return 6; default: return -1;
            }
        }
        internal static int Capacity(int type) { return type < 2 ? 8192 : type < 5 ? 4096 : 2048; }
        internal byte[] Data { get; private set; }
        internal int Type { get; private set; }
        internal string FilePath { get; private set; }
        internal bool Closed { get; set; }
        internal bool Dirty { get; private set; }
        private bool hex;

        internal static PkwEprom Open(string path, int type)
        {
            if (type < 0 || type >= Names.Length) throw new ArgumentException("Invalid EP-ROM selection.");
            var chip = new PkwEprom { Type = type, FilePath = Path.GetFullPath(path),
                Data = new byte[Capacity(type)], hex = string.Equals(Path.GetExtension(path), ".hex", StringComparison.OrdinalIgnoreCase) };
            for (int i = 0; i < chip.Data.Length; i++) chip.Data[i] = 255;
            if (File.Exists(path)) {
                if (chip.hex) chip.ReadHex(File.ReadAllLines(path));
                else {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.Length > chip.Data.Length) throw new InvalidDataException("The file is larger than the selected EP-ROM.");
                    Array.Copy(bytes, chip.Data, bytes.Length);
                }
            } else { chip.Dirty = true; chip.Save(); }
            return chip;
        }
        internal void Program(int address, byte value)
        {
            if (!Closed || address < 0 || address >= Data.Length) return;
            byte next = (byte)(Data[address] & value);
            if (next != Data[address]) { Data[address] = next; Dirty = true; }
        }
        internal void Erase()
        {
            if (!Closed || Type != 6) return;
            for (int i = 0; i < Data.Length; i++) if (Data[i] != 255) { Data[i] = 255; Dirty = true; }
        }
        internal void Save()
        {
            if (!Dirty) return;
            // Replace only after the complete new file has been written successfully.
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                if (hex) {
                    var output = new StringBuilder();
                    for (int offset = 0; offset < Data.Length; offset += 16) {
                        int count = Math.Min(16, Data.Length - offset), sum = count + (offset >> 8) + (offset & 255);
                        output.AppendFormat(":{0:X2}{1:X4}00", count, offset);
                        for (int j = 0; j < count; j++) { byte b = Data[offset + j]; output.Append(b.ToString("X2")); sum += b; }
                        output.AppendLine(((byte)-sum).ToString("X2"));
                    }
                    output.AppendLine(":00000001FF"); File.WriteAllText(temporary, output.ToString(), new UTF8Encoding(false));
                } else File.WriteAllBytes(temporary, Data);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
                Dirty = false;
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void ReadHex(string[] lines)
        {
            long bank = 0; bool eof = false;
            var assigned = new bool[Data.Length];
            foreach (string raw in lines) {
                string line = raw.Trim(); if (line.Length == 0) continue;
                if (eof || line[0] != ':' || line.Length < 11 || (line.Length & 1) != 1) throw new InvalidDataException("Invalid Intel HEX record.");
                byte[] record = new byte[(line.Length - 1) / 2]; int sum = 0;
                for (int i = 0; i < record.Length; i++) { record[i] = Convert.ToByte(line.Substring(1 + i * 2, 2), 16); sum += record[i]; }
                int count = record[0], address = record[1] * 256 + record[2], kind = record[3];
                if (record.Length != count + 5 || (sum & 255) != 0) throw new InvalidDataException("Invalid Intel HEX length or checksum.");
                if (kind == 0) {
                    long start = bank + address;
                    if (start + count > Data.Length) throw new InvalidDataException("HEX address is outside the EP-ROM (base address 0000).");
                    for (int i = 0; i < count; i++) {
                        int target = (int)start + i;
                        if (assigned[target] && Data[target] != record[4 + i]) throw new InvalidDataException("Conflicting HEX data at the same address.");
                        assigned[target] = true; Data[target] = record[4 + i];
                    }
                } else if (kind == 1 && count == 0 && address == 0) eof = true;
                else if ((kind == 2 || kind == 4) && count == 2 && address == 0) bank = (long)(record[4] * 256 + record[5]) << (kind == 2 ? 4 : 16);
                else if ((kind == 3 || kind == 5) && count == 4 && address == 0) { /* execution address does not belong to chip data */ }
                else throw new InvalidDataException("Unsupported Intel HEX record.");
            }
            if (!eof) throw new InvalidDataException("Intel HEX: missing EOF record.");
        }
    }
}
