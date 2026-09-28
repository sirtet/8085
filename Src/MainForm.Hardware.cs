using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

namespace _8085
{
    public partial class MainForm
    {
        private bool pkwSelected;
        private Pkw3000Hardware pkwBoard;
        private FormPkw3000 pkwWindow;
        private ToolStripMenuItem sdkMenu, pkwMenu;
        internal Func<bool> ConfirmFirmwareSource;
        internal Func<bool> ConfirmReplaceSource;

        private void InitializeHardwareSelection()
        {
            // Give the menu its own row, keeping existing debugger controls below it.
            SuspendLayout();
            foreach (Control control in Controls) if (control != menuStrip) control.Top += 28;
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 28);
            MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + 28);
            menuStrip.AutoSize = false; menuStrip.Dock = DockStyle.Top; menuStrip.Height = 26;
            var menu = new ToolStripMenuItem("Hardware");
            sdkMenu = new ToolStripMenuItem("SDK-85") { Checked = true };
            pkwMenu = new ToolStripMenuItem("PKW-3000");
            sdkMenu.Click += (s, e) => SelectHardware(false);
            ConfirmFirmwareSource = () => MessageBox.Show(this,
                "Load the adapted PKW-3000 firmware source?\n\nBased on Edgar's original-ROM disassembly, adapted for this simulator.\n\nChanges: \"Hellorld!\" terminal greeting, ASCII Hex Space transfer format, and printable S/X start/end markers.",
                "PKW-3000", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            ConfirmReplaceSource = () => MessageBox.Show(this,
                "This will replace your current ASM source. Continue?",
                "PKW-3000", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
            pkwMenu.Click += (s, e) => {
                if (pkwSelected) return;
                SelectHardware(true);
                if (!ConfirmFirmwareSource()) return;
                if (richTextBoxProgram.TextLength != 0 && !ConfirmReplaceSource()) return;
                try { LoadAdaptedFirmwareSource(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Firmware source", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            menu.DropDownItems.AddRange(new ToolStripItem[] { sdkMenu, pkwMenu }); menuStrip.Items.Add(menu);
            FormClosed += (s, e) => { formTerminal?.Close(); formSDK_85?.Close(); pkwWindow?.Close(); };
            FormClosing += (s, e) => {
                try { pkwBoard?.Eprom?.Save(); }
                catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "Could not save the EP-ROM file:\n" + ex.Message, "EPROM", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            ResumeLayout(true);
        }
        internal static string ReadAdaptedFirmwareSource()
        {
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("pkw2_8085.asm"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        private void LoadAdaptedFirmwareSource()
        {
            string source = ReadAdaptedFirmwareSource();
            stop_Click(this, EventArgs.Empty);
            sourceFile = ""; // Save As must not overwrite the user's previous source file.
            lineBreakPoint = -1;
            richTextBoxProgram.Text = source;
            startDebug_Click(this, EventArgs.Empty);
        }
        private void SelectHardware(bool pkw)
        {
            if (pkwSelected == pkw) return;
            stop_Click(this, EventArgs.Empty);
            chkTerminal.Checked = false; chkSIDSOD.Checked = false; chkSDK85.Checked = false;
            pkwSelected = pkw;
            sdkMenu.Checked = !pkw; pkwMenu.Checked = pkw;
            chkSDK85.Text = pkw ? "Hardware" : "SDK-85";
            chkInsertMonitor.Checked = false; chkInsertMonitor.Visible = !pkw;
            chkSIDSOD.Visible = !pkw;
            resetSimulator_Click(this, EventArgs.Empty);
            chkSDK85.Checked = true;
        }
        private void AttachPkwBoard()
        {
            byte switches = pkwBoard == null ? (byte)0x28 : pkwBoard.SwitchInputs;
            var eprom = pkwBoard?.Eprom;
            pkwBoard = new Pkw3000Hardware { SwitchInputs = switches, Eprom = eprom };
            if (assembler85 != null) assembler85.Hardware = pkwBoard;
            pkwWindow?.Bind(pkwBoard);
        }
        private void ShowPkwHardware()
        {
            if (chkSDK85.Checked)
            {
                pkwWindow = new FormPkw3000(); pkwWindow.Bind(pkwBoard);
                pkwWindow.ResetRequested += (s, e) => {
                    if (assembler85 == null) return;
                    // Reset in place: an active Run timer or Fast loop continues, a paused CPU remains paused.
                    byte[] buffer = pkwBoard.BufferRam;
                    assembler85.ResetHardwareCpu(); AttachPkwBoard();
                    Array.Copy(buffer, pkwBoard.BufferRam, buffer.Length);
                    nextInstrAddress = 0; tbSetProgramCounter.Text = "0000";
                    assembler85.ClearPorts();
                    if (formTerminal != null) { formTerminal.keyBuffer = ""; formTerminal.UpdateBufferText(0, ""); }
                    tbCycles.Text = "0";
                    UpdateRegisters(); UpdateFlags(); UpdateInterrupts(); UpdatePortPanel(); UpdateDisplay();
                };
                pkwWindow.FormClosed += (s, e) => { pkwWindow = null; chkSDK85.Checked = false; };
                pkwWindow.Show(this);
            }
            else pkwWindow?.Close();
        }
        private void ShowPkwTerminal()
        {
            if (chkTerminal.Checked)
            {
                formTerminal = new FormTerminal(Location.X + 80, Location.Y + 120) { InitialBaudRate = 4800 };
                formTerminal.FormClosed += (s, e) => { formTerminal = null; chkTerminal.Checked = false; };
                formTerminal.Show(this);
            }
            else formTerminal?.Close();
        }
        private void PumpPkwSerial()
        {
            if (assembler85 == null || pkwBoard == null) return;
            pkwBoard.CtsReady = pkwBoard.DsrReady = true;
            if (formTerminal != null)
            {
                pkwBoard.BaudRate = formTerminal.BaudRate;
                if (formTerminal.keyBuffer.Length != 0)
                {
                    pkwBoard.QueueInput(formTerminal.keyBuffer); formTerminal.keyBuffer = "";
                }
                formTerminal.UpdateBufferText(pkwBoard.QueuedInput, pkwBoard.InputPreview);
                string output = pkwBoard.DrainOutput();
                if (output.Length != 0) formTerminal.AppendOutput(output);
            }
            else pkwBoard.DrainOutput();
        }
    }
}
