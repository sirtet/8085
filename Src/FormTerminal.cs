using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace _8085
{
    public partial class FormTerminal : Form
    {
        #region Members

        // Initial location of window
        private int x, y;

        // Keyboard buffer
        public string keyBuffer;
        public int InitialBaudRate = 9600;
        public int BaudRate { get { return cbBaudRate.SelectedItem == null ? InitialBaudRate : int.Parse(cbBaudRate.SelectedItem.ToString().Trim().Split(' ')[0]); } }
        private bool lastOutputWasCr;
        private int pendingHardwareCount;
        private string pendingHardwarePreview = "";
        public void AppendOutput(string text)
        {
            var visible = new StringBuilder();
            foreach (char ch in text)
            {
                if (ch == '\b')
                {
                    if (visible.Length > 0) visible.Length--;
                    else if (tbTerminal.TextLength > 0)
                    { tbTerminal.Select(tbTerminal.TextLength - 1, 1); tbTerminal.SelectedText = ""; }
                }
                else if (ch == '\r') visible.Append(Environment.NewLine);
                else if (ch == '\n') { if (!lastOutputWasCr) visible.Append(Environment.NewLine); }
                else if (!char.IsControl(ch)) visible.Append(ch);
                else visible.Append("<" + ((int)ch).ToString("X2") + ">");
                lastOutputWasCr = ch == '\r';
            }
            if (visible.Length > 0) tbTerminal.AppendText(visible.ToString());
        }

        internal static string NormalizeInput(string text)
        {
            var result = new StringBuilder();
            foreach (char ch in text.Replace("\r\n", "\r").Replace('\n', '\r'))
                if (!char.IsControl(ch) || ch == '\r' || ch == '\b' || ch == '\t' || ch == '\x1B')
                    result.Append(ch <= 127 ? ch : '?');
            return result.ToString();
        }
        public void QueueText(string text)
        { keyBuffer += NormalizeInput(text); UpdateBufferText(); }
        internal Func<string> ReadClipboardText = () => Clipboard.ContainsText() ? Clipboard.GetText() : "";
        public void HandleInputKeyDown(KeyEventArgs e)
        {
            if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                e.SuppressKeyPress = true;
                try { QueueText(ReadClipboardText()); }
                catch (System.Runtime.InteropServices.ExternalException) { System.Media.SystemSounds.Beep.Play(); }
            }
            else if (e.KeyCode == Keys.CapsLock || e.KeyCode == Keys.NumLock || e.KeyCode == Keys.Scroll)
                e.SuppressKeyPress = true;
        }


        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public FormTerminal(int x, int y)
        {
            InitializeComponent();

            this.x = x;
            this.y = y;

            keyBuffer = "";
            if (components == null) components = new Container();
            ((TerminalTextBox)tbTerminal).PasteRequested = () => HandleInputKeyDown(new KeyEventArgs(Keys.Control | Keys.V));
            ((TerminalTextBox)tbTerminal).EnterRequested = () => QueueText("\r");
            tbKeyBuffer.KeyPress += (sender, e) => e.Handled = true;
            tbKeyBuffer.ReadOnly = true; tbKeyBuffer.TabStop = false;
            lblKeyBuffer.Text = "Pending TX:";
            tbKeyBuffer.Left = 86; tbKeyBuffer.Width -= 12;
            tbTerminal.ReadOnly = true;
            tbTerminal.KeyDown += (sender, e) => HandleInputKeyDown(e);
            var tip = new ToolTip(components);
            tip.SetToolTip(tbKeyBuffer, "Queued characters, including PKW serial input. Type or paste in the large terminal area.");
            var menu = new ContextMenuStrip(components);
            menu.Items.Add("Copy", null, (sender, e) => tbTerminal.Copy());
            menu.Items.Add("Paste / send", null, (sender, e) => HandleInputKeyDown(new KeyEventArgs(Keys.Control | Keys.V)));
            tbTerminal.ContextMenuStrip = menu;
            UpdateBufferText();
        }

        #endregion

        #region EventHandlers

        /// <summary>
        /// Form loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormTerminal_Load(object sender, EventArgs e)
        {
            // Set location of window
            this.Location = new Point(x, y);

            tbTerminal.Font = new Font(FontFamily.GenericMonospace, 10.25F);

            cbBaudRate.SelectedIndex = InitialBaudRate == 4800 ? 6 : 7;
        }

        /// <summary>
        /// Key pressed, send to terminal
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void tbTerminal_KeyPress(object sender, KeyPressEventArgs e)
        {
            QueueText(e.KeyChar.ToString());
            e.Handled = true;
        }

        #endregion

        #region Methods

        public void Clear()
        {
            tbTerminal.Text = "";
            keyBuffer = "";
            lastOutputWasCr = false; UpdateBufferText(0, "");
        }

        public void UpdateBufferText() { UpdateBufferText(pendingHardwareCount, pendingHardwarePreview); }
        public void UpdateBufferText(int hardwarePending, string hardwarePreview)
        {
            pendingHardwareCount = hardwarePending; pendingHardwarePreview = hardwarePreview;
            string preview = hardwarePreview + keyBuffer;
            bool truncated = preview.Length > 80 || hardwarePending + keyBuffer.Length > 80;
            if (preview.Length > 80) preview = preview.Substring(0, 80);
            preview = preview.Replace("\r", "<CR>").Replace("\n", "<LF>")
                .Replace("\b", "<BS>").Replace("\t", "<TAB>").Replace("\x1B", "<ESC>");
            string value = (hardwarePending + keyBuffer.Length) + (preview.Length == 0 ? "" : ": " + preview + (truncated ? "..." : ""));
            if (tbKeyBuffer.Text != value) tbKeyBuffer.Text = value;
        }

        /// <summary>
        /// bitCycles = 3.072 * 10^6 / Br
        /// </summary>
        /// <returns></returns>
        public UInt64 GetBitCycles()
        {
            switch (cbBaudRate.SelectedItem.ToString().Trim())
            {
                case "110 Bd":
                    return 27927;
                case "150 Bd":
                    return 20480;
                case "300 Bd":
                    return 10240;
                case "600 Bd":
                    return 5120;
                case "1200 Bd":
                    return 2560;
                case "2400 Bd":
                    return 1280;
                case "4800 Bd":
                    return 640;
                case "9600 Bd":
                    return 320;
                case "19200 Bd":
                    return 160;
                default:
                    return 0;
            }
        }

        #endregion
    }
    internal sealed class TerminalTextBox : RichTextBox
    {
        internal Action PasteRequested;
        internal Action EnterRequested;
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Read-only RichEdit consumes Enter before KeyPress and otherwise beeps.
            // Route it to the serial queue before native editing/dialog handling.
            if (keyData == Keys.Enter || keyData == (Keys.Shift | Keys.Enter))
            { EnterRequested?.Invoke(); return true; }
            if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
            { PasteRequested?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0302) { PasteRequested?.Invoke(); return; } // WM_PASTE: send, never edit received text.
            base.WndProc(ref m);
        }
    }

}
