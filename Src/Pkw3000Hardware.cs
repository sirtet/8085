using System;
using System.Collections.Generic;
using System.Text;

namespace _8085
{
    // Functional digital model; analogue voltage/timing tolerances are not simulated.
    sealed class Pkw3000Hardware : ISimulatedHardware
    {
        public const ulong ClockHz = 3000000;
        public int BaudRate { get; set; } = 4800;
        public byte SwitchInputs { get; set; } = 0x28;
        public bool CtsReady { get; set; } = true;
        public bool DsrReady { get; set; } = true;
        public byte[] Ports { get; } = new byte[256];
        public byte[] BufferRam { get; } = new byte[8192];
        internal PkwEprom Eprom { get; set; }
        internal int SelectedEpromType { get { return PkwEprom.TypeFromSwitches(SwitchInputs); } }
        private bool SocketConnected { get { return Eprom != null && Eprom.Closed && Eprom.Type == SelectedEpromType; } }
        private int EpromAddress { get { return Ports[0xA1] | ((Ports[0xA2] & 31) << 8); } }
        private byte EpromByte { get { return SocketConnected ? Eprom.Data[EpromAddress % Eprom.Data.Length] : (byte)255; } }
        private byte epromMode = 0x90;
        // DA/DB/VCC transition patterns from original ROM table 0D45, functions 7/8.
        private static readonly byte[] ProgramIdle = { 0x43, 0xC2, 0xC2, 0xC2, 0x42, 0xC1, 0xC1 };
        private static readonly byte[] ProgramPulse = { 0x42, 0xC0, 0xC0, 0xC0, 0x40, 0xC3, 0xC3 };
        public ulong TimerPulses { get; private set; }
        public ulong Cycles { get; private set; }
        public bool HostMaySend { get { return (Ports[0xC2] & 0x20) == 0; } }
        public int QueuedInput { get { return incoming.Count + (rxActive ? 1 : 0); } }

        public string InputPreview
        {
            get
            {
                var text = new StringBuilder();
                if (rxActive) text.Append((char)rxByte);
                foreach (byte ch in incoming) { if (text.Length >= 80) break; text.Append((char)ch); }
                return text.ToString();
            }
        }

        private readonly bool[,] keys = new bool[8, 4];
        private readonly byte[] display = new byte[8];
        private readonly ulong[] litAt = new ulong[8];
        private readonly Queue<byte> incoming = new Queue<byte>();
        private readonly StringBuilder output = new StringBuilder();
        private bool timerRunning, stopAfterPeriod, interrupt75;
        private bool timerExpired, restartAtPeriod;
        private byte command8155;
        private ushort timerReload, activeReload;
        private ulong nextTimer, rxStart, rxNotBefore, txSample;
        private bool rxActive, txActive;
        private byte rxByte, txByte;
        private int txBit;
        private bool txLevel;
        private ulong BitCycles { get { return ClockHz / (ulong)Math.Max(1, BaudRate); } }

        // Electrical rows, not firmware keycodes. RST is a separate CPU reset button.
        private static readonly string[,] KeyNames = {
            { "5", "B", "PRG", "SET" }, { "9", "F", "JOB", "-" },
            { "D", "2", "CMP", "" }, { "0", "6", "ERS", "" },
            { "4", "A", "LOD", "" }, { "8", "E", "3", "" },
            { "C", "1", "7", "" }, { "", "", "", "" }
        };

        public void SetKey(string name, bool pressed)
        {
            for (int c = 0; c < 8; c++)
                for (int r = 0; r < 4; r++)
                    if (KeyNames[c, r] == name) keys[c, r] = pressed;
        }

        public void ReleaseKeys() { Array.Clear(keys, 0, keys.Length); }
        public bool CanWriteMemory(ushort address) { return address >= 0x6000 && address < 0x6100; }
        public void QueueInput(string text)
        {
            foreach (char ch in text) incoming.Enqueue((byte)(ch <= 127 ? ch : '?'));
        }
        public void QueueInput(byte[] bytes) { foreach (byte value in bytes) incoming.Enqueue(value); }
        public string DrainOutput()
        {
            string text = output.ToString(); output.Clear(); return text;
        }
        public byte DisplaySegments(int digit)
        {
            // Persistence across multiplex blanking; age in simulated time, not wall time.
            return Cycles - litAt[digit] <= ClockHz / 25 ? display[digit] : (byte)0;
        }
        private bool DecoderEnabled { get { return (Ports[0x6A] & 0x18) == 0x08; } }
        private void Illuminate()
        {
            if ((command8155 & 3) != 3 || !DecoderEnabled || Ports[0x69] == 0) return;
            int digit = Ports[0x6A] & 7;
            display[digit] = Ports[0x69]; litAt[digit] = Cycles;
        }

        public void Advance(ulong cycle)
        {
            if (cycle < Cycles) throw new ArgumentOutOfRangeException(nameof(cycle));
            // Transmitter level remains constant until the next OUT instruction.
            while (txActive && txSample <= cycle)
            {
                if (txBit < 8)
                {
                    if (!txLevel) txByte |= (byte)(1 << txBit);
                    txBit++; txSample += BitCycles;
                }
                else
                {
                    if (!txLevel) output.Append((char)txByte); // valid stop bit
                    txActive = false;
                }
            }
            while (timerRunning && nextTimer <= cycle)
            {
                ulong period = (ulong)(activeReload & 0x3FFF);
                bool continuous = (activeReload & 0x4000) != 0;
                ulong pulses = continuous && !stopAfterPeriod && !restartAtPeriod ? 1 + (cycle - nextTimer) / period : 1;
                TimerPulses += pulses; interrupt75 = true; timerExpired = true;
                if (restartAtPeriod)
                {
                    activeReload = timerReload; restartAtPeriod = false;
                    timerRunning = (activeReload & 0x3FFF) >= 2;
                    nextTimer += (ulong)(activeReload & 0x3FFF);
                    continue;
                }
                nextTimer += pulses * period;
                if (!continuous || stopAfterPeriod) timerRunning = false;
            }
            Cycles = cycle;
            if (rxActive && cycle >= rxStart + 11 * BitCycles)
            {
                rxActive = false;
                // Give firmware time to finish the receive routine and assert handshake.
                rxNotBefore = cycle + 3 * BitCycles;
            }
            if (!rxActive && incoming.Count > 0 && HostMaySend && cycle >= rxNotBefore)
            {
                rxByte = incoming.Dequeue(); rxStart = cycle; rxActive = true;
            }
            Illuminate();
        }

        public bool TakeInterrupt75()
        {
            bool pending = interrupt75; interrupt75 = false; return pending;
        }

        public byte ReadPort(byte port, ulong cycle)
        {
            Advance(cycle);
            switch (port)
            {
                case 0x68:
                    byte status = timerExpired ? (byte)0x40 : (byte)0; timerExpired = false; return status;
                case 0x6B:
                    byte rows = 0;
                    if (DecoderEnabled)
                        for (int r = 0; r < 4; r++)
                            if (keys[Ports[0x6A] & 7, r]) rows |= (byte)(1 << r);
                    return rows;
                case 0x83:
                    int bitAddress = Ports[0x81] | Ports[0x82] << 8;
                    return (byte)((BufferRam[bitAddress >> 3] >> (bitAddress & 7)) & 1);
                case 0xA0: return EpromByte;
                case 0xC0:
                    // Both comparator outputs agree for valid digital levels. A2[7:5]
                    // selects one data bit through the analogue multiplexer.
                    return (byte)((SwitchInputs & 0xFC) | (((EpromByte >> (Ports[0xA2] >> 5)) & 1) != 0 ? 3 : 0));
                case 0xC2:
                    int bit = rxActive ? (int)((cycle - rxStart) / BitCycles) : 11;
                    bool rx = bit == 0 || (bit >= 1 && bit <= 8 && (rxByte & (1 << (bit - 1))) == 0);
                    return (byte)((Ports[0xC2] & 0xF0) | (rx ? 1 : 0) |
                        (CtsReady ? 0 : 2) | (DsrReady ? 0 : 4));
                default: return Ports[port];
            }
        }

        public void WritePort(byte port, byte value, ulong cycle)
        {
            Advance(cycle);
            byte previous = Ports[port];
            Ports[port] = value;
            switch (port)
            {
                case 0x68:
                    command8155 = value;
                    switch (value >> 6)
                    {
                        case 1: timerRunning = false; restartAtPeriod = false; break;
                        case 2: stopAfterPeriod = true; break;
                        case 3:
                            stopAfterPeriod = false;
                            if (timerRunning) { restartAtPeriod = true; break; }
                            activeReload = timerReload;
                            timerRunning = (activeReload & 0x3FFF) >= 2;
                            stopAfterPeriod = false;
                            nextTimer = cycle + (ulong)(activeReload & 0x3FFF); break;
                    }
                    break;
                case 0x69: case 0x6A: Illuminate(); break;
                case 0x6C: timerReload = (ushort)((timerReload & 0xFF00) | value); break;
                case 0x6D: timerReload = (ushort)((timerReload & 0x00FF) | value << 8); break;
                case 0x82:
                    if ((Ports[0x80] & 2) == 0)
                    {
                        int bitAddress = Ports[0x81] | value << 8;
                        byte mask = (byte)(1 << (bitAddress & 7));
                        int address = bitAddress >> 3;
                        BufferRam[address] = (byte)((BufferRam[address] & ~mask) |
                            ((Ports[0x80] & 1) != 0 ? mask : 0));
                    }
                    break;
                case 0xA3:
                    if ((value & 128) != 0) { epromMode = value; Ports[0xA0] = Ports[0xA1] = Ports[0xA2] = 0; }
                    else {
                        int mask = 1 << ((value >> 1) & 7);
                        Ports[0xA2] = (byte)((Ports[0xA2] & ~mask) | ((value & 1) != 0 ? mask : 0));
                    }
                    break;
                case 0xC1:
                    if (SocketConnected) {
                        int type = SelectedEpromType;
                        // Mask VPP selection and pull-up control; preserve DA/DB enable and VCC.
                        int before = previous & 0xC7, after = value & 0xC7;
                        if ((epromMode & 0x10) == 0 && before == ProgramIdle[type] && after == ProgramPulse[type])
                            Eprom.Program(EpromAddress % Eprom.Data.Length, Ports[0xA0]);
                        if (type == 6 && before == 0xC0 && after == 0xC2) Eprom.Erase();
                    }
                    break;
                case 0xC2:
                    bool level = (value & 0x80) != 0;
                    if (!txActive && !txLevel && level)
                    {
                        txActive = true; txByte = 0; txBit = 0;
                        txSample = cycle + BitCycles + BitCycles / 2;
                    }
                    txLevel = level; break;
                case 0xC3:
                    if ((value & 0x80) != 0)
                    {
                        Ports[0xC1] = 0;
                        WritePort(0xC2, 0, cycle);
                    }
                    else
                    {
                        int mask = 1 << ((value >> 1) & 7);
                        byte data = (byte)((Ports[0xC2] & ~mask) | ((value & 1) != 0 ? mask : 0));
                        WritePort(0xC2, data, cycle);
                    }
                    break;
            }
        }
    }
}
