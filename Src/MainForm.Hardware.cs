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
        private readonly ToolStripButton realtimeButton = new ToolStripButton("Realtime") {
            Name = "realtimeButton", DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = "Run in realtime — emulate the PKW-3000 at 3 MHz", Visible = false, Enabled = false };

        internal Func<bool> ConfirmFirmwareSource;
        internal Func<bool> ConfirmReplaceSource;

        private void InitializeHardwareSelection()
        {
            var realtimeImage = CreateRealtimeImage(toolStripButtonRun.Image);
            realtimeButton.Image = realtimeImage;
            realtimeButton.ImageTransparentColor = toolStripButtonRun.ImageTransparentColor;
            Disposed += (s, e) => realtimeImage.Dispose();
            toolStrip.Items.Insert(toolStrip.Items.IndexOf(toolStripButtonFast) + 1, realtimeButton);
            toolStripButtonFast.EnabledChanged += (s, e) => realtimeButton.Enabled = toolStripButtonFast.Enabled;
            realtimeButton.Enabled = toolStripButtonFast.Enabled;
            realtimeButton.Click += startFast_Click;
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
            menu.DropDownItems.AddRange(new ToolStripItem[] { sdkMenu, pkwMenu }); menuStrip.Items.Insert(menuStrip.Items.IndexOf(helpToolStripMenuItem), menu);
            toolStrip.Layout += (s, e) => LayoutExecutionControls();
            FormClosed += (s, e) => { formTerminal?.Close(); formSDK_85?.Close(); pkwWindow?.Close(); };
            FormClosing += (s, e) => {
                try { pkwBoard?.Eprom?.Save(); }
                catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "Could not save the EP-ROM file:\n" + ex.Message, "EPROM", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            ResumeLayout(true);
            LayoutExecutionControls();
        }
        private static Bitmap CreateRealtimeImage(Image runImage)
        {
            // Keep the original Run artwork pixel-for-pixel outside the lettering.
            // A small pixel-aligned stencil stays legible at the toolbar's 16px size.
            var result = new Bitmap(runImage);
            string[] stencil = { "1100111", "1010010", "1100010", "1010010", "1010010" };
            for (int y = 0; y < stencil.Length; y++)
                for (int x = 0; x < stencil[y].Length; x++)
                    if (stencil[y][x] == '1')
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                                result.SetPixel(6 + x * 2 + dx, 11 + y * 2 + dy, Color.Transparent);
            return result;
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
        private void LayoutExecutionControls()
        {
            // These are separate form controls, not toolbar items. Follow the actual
            // toolbar width, including the PKW-only text button and DPI/font scaling.
            int gap = Math.Max(4, toolStrip.Height / 5);
            lblSetProgramCounter.Left = toolStrip.Right + gap;
            tbSetProgramCounter.Left = lblSetProgramCounter.Right + gap;
            lblFocusLine.Left = tbSetProgramCounter.Right + gap;
            numFocusLine.Left = lblFocusLine.Right + gap;
            chkInsertMonitor.Left = numFocusLine.Right + 3 * gap;
            chkSDK85.Left = btnViewProgram.Right + gap;
            chkTerminal.Left = chkSDK85.Right + gap;
        }
        private void SelectHardware(bool pkw)
        {
            if (pkwSelected == pkw) return;
            stop_Click(this, EventArgs.Empty);
            chkTerminal.Checked = false; chkSIDSOD.Checked = false; chkSDK85.Checked = false;
            pkwSelected = pkw;
            realtimeButton.Visible = pkw;
            sdkMenu.Checked = !pkw; pkwMenu.Checked = pkw;
            chkSDK85.Text = pkw ? "PKW-3000" : "SDK-85";
            chkSDK85.Left = chkTerminal.Left - chkSDK85.Width - 8;
            chkInsertMonitor.Checked = false; chkInsertMonitor.Visible = !pkw;
            chkSIDSOD.Visible = !pkw;
            resetSimulator_Click(this, EventArgs.Empty);
            chkSDK85.Checked = true;
            LayoutExecutionControls();
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
