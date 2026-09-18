namespace _8085
{
    // All times are absolute CPU T-states. No Windows/UI timing in a device model.
    interface ISimulatedHardware
    {
        byte ReadPort(byte port, ulong cycle);
        void WritePort(byte port, byte value, ulong cycle);
        void Advance(ulong cycle);
        bool TakeInterrupt75();
        bool CanWriteMemory(ushort address);
    }
}
