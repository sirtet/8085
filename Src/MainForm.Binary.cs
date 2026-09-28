using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace _8085
{
    public partial class MainForm
    {
        private readonly DisAssembler85 liveDisassembler = new DisAssembler85();
        private Form binaryProgram;
        private TextBox binaryCode;
        private bool binaryProgramDismissed;

        private void loadBinary_Click(object sender, EventArgs e)
        {
            using (var picker = new OpenFileDialog { Title = "Load Binary into Memory", Filter = "Binary image (*.bin)|*.bin|All files (*.*)|*.*" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    byte[] bytes = File.ReadAllBytes(picker.FileName);
                    using (var addresses = new FormAddresses())
                    {
                        addresses.chkLabels.Visible = false;
                        if (addresses.ShowDialog(this) != DialogResult.OK) return;
                        LoadBinaryImage(bytes, addresses.loadAddress, addresses.startAddress);
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load Binary", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        internal void LoadBinaryImage(byte[] bytes, ushort loadAddress, ushort startAddress)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidDataException("The binary file is empty.");
            if (loadAddress + bytes.Length > 0x10000) throw new InvalidDataException("The binary file does not fit in memory at this load address.");
            stop_Click(this, EventArgs.Empty);
            CloseBinaryProgram();
            if (assembler85 == null) assembler85 = new Assembler85(new string[0]);
            // Loading is a debugger operation: ROM protection applies during CPU execution.
            Array.Copy(bytes, 0, assembler85.RAM, loadAddress, bytes.Length);
            for (int i = loadAddress; i < loadAddress + bytes.Length; i++) assembler85.RAMprogramLine[i] = -2;
            assembler85.firstAddress = assembler85.firstAddress < 0 ? loadAddress : Math.Min(assembler85.firstAddress, loadAddress);
            assembler85.lastAddress = Math.Max(assembler85.lastAddress, loadAddress + bytes.Length - 1);
            assembler85.ResetHardwareCpu();
            if (pkwSelected) AttachPkwBoard();
            assembler85.startLocation = assembler85.registerPC = nextInstrAddress = startAddress;
            lineBreakPoint = -1;
            ClearColorRTBLine();
            tbSetProgramCounter.Text = tbMemoryStartAddress.Text = startAddress.ToString("X4");
            tbCycles.Text = "0";
            ResetSerial();
            UpdateMemoryPanel(startAddress, startAddress); UpdatePortPanel(); UpdateRegisters(); UpdateFlags(); UpdateInterrupts(); ClearDisplay();
            toolStripButtonRun.Enabled = toolStripButtonFast.Enabled = toolStripButtonStep.Enabled = true;
            toolStripButtonDebug.Enabled = toolStripButtonNew.Enabled = toolStripButtonReset.Enabled = true;
            resetSimulatorToolStripMenuItem.Enabled = true; toolStripButtonStop.Enabled = false;
            UpdateBinaryProgram();
        }

        internal byte[] CreateBinaryImage()
        {
            if (assembler85 == null || assembler85.firstAddress < 0 || assembler85.lastAddress < assembler85.firstAddress)
                throw new InvalidOperationException("No assembled or loaded bytes to save.");
            var bytes = new byte[assembler85.lastAddress - assembler85.firstAddress + 1];
            Array.Copy(assembler85.RAM, assembler85.firstAddress, bytes, 0, bytes.Length);
            return bytes;
        }

        internal string DisassembleMemory(ushort address)
        {
            var text = new StringBuilder();
            for (int i = 0; i < 16; i++)
            {
                byte opcode = assembler85.RAM[address];
                uint operands; DisAssembler85.TYPE type;
                string instruction = liveDisassembler.Decode(opcode, out operands, out type);
                string bytes = opcode.ToString("X2");
                byte lo = assembler85.RAM[(ushort)(address + 1)], hi = assembler85.RAM[(ushort)(address + 2)];
                if (operands >= 1) bytes += " " + lo.ToString("X2");
                if (operands == 2) bytes += " " + hi.ToString("X2");
                if (operands == 1) instruction += " " + lo.ToString("X2") + "H";
                if (operands == 2) instruction += " " + ((hi << 8) | lo).ToString("X4") + "H";
                text.AppendLine(address.ToString("X4") + "   " + bytes.PadRight(10) + instruction);
                address = (ushort)(address + 1 + operands);
            }
            return text.ToString();
        }

        private void UpdateBinaryProgram()
        {
            if (assembler85 == null || binaryProgramDismissed || assembler85.RAMprogramLine[nextInstrAddress] >= 0) return;
            if (binaryProgram == null)
            {
                binaryCode = new TextBox { Multiline = true, ReadOnly = true, WordWrap = false,
                    ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 10) };
                binaryProgram = new Form { Text = "Binary Code", Size = new Size(440, 390), MinimumSize = new Size(320, 240) };
                binaryProgram.Controls.Add(binaryCode);
                binaryProgram.FormClosed += (s, e) => { binaryProgramDismissed = true; binaryProgram = null; binaryCode = null; };
                binaryProgram.Show(this);
            }
            // Bounded view, refreshed only at existing UI updates, never an instruction trace.
            string text = DisassembleMemory(nextInstrAddress);
            if (binaryCode.Text != text) binaryCode.Text = text;
        }

        private void CloseBinaryProgram()
        {
            binaryProgram?.Close();
            binaryProgramDismissed = false;
        }
    }
}
