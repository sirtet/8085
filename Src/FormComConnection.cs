using System;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace _8085
{
    // Connect to one endpoint of an existing virtual null-modem pair (or a physical port).
    internal sealed class FormComConnection : Form
    {
        private SerialPort port;
        private readonly ComboBox ports = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox baud = new ComboBox { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox lines = new CheckBox { Text = "Use CTS / DSR / RTS", AutoSize = true };
        private readonly Button connect = new Button { Text = "Connect", AutoSize = true };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(490, 0) };
        private int lastPoll;
        public bool Connected { get { return port != null && port.IsOpen; } }
        public FormComConnection()
        {
            Text = "PKW-3000 — COM connection"; ClientSize = new Size(520, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) }; Controls.Add(layout);
            var help = new Label { AutoSize = true, MaximumSize = new Size(490, 0), Text =
                "Virtual COM pair: simulator on one port, external terminal on the other.\r\nBoth ends: same baud rate, 8 data bits, no parity, 2 stop bits. Baud must match JOB 4. A virtual COM driver must already be installed." };
            layout.Controls.Add(help); layout.SetFlowBreak(help, true);
            ports.Items.AddRange(SerialPort.GetPortNames().OrderBy(x => x).Cast<object>().ToArray());
            if (ports.Items.Count != 0) ports.SelectedIndex = 0;
            baud.Items.AddRange(new object[] { 4800, 2400, 1200, 600, 300, 110 }); baud.SelectedIndex = 0;
            layout.Controls.Add(ports); layout.Controls.Add(baud); layout.Controls.Add(connect); layout.SetFlowBreak(connect, true);
            layout.Controls.Add(lines); layout.SetFlowBreak(lines, true); layout.Controls.Add(status);
            status.Text = "Disconnected. With modem lines off, use no flow control in the terminal.";
            connect.Click += (s, e) => {
                if (Connected) { Disconnect(); status.Text = "Disconnected."; return; }
                try
                {
                    port = new SerialPort(ports.Text.Trim(), (int)baud.SelectedItem, Parity.None, 8, StopBits.Two)
                    { Handshake = Handshake.None, DtrEnable = true, RtsEnable = true, ReadTimeout = 20, WriteTimeout = 50 };
                    port.Open(); connect.Text = "Disconnect"; ports.Enabled = baud.Enabled = lines.Enabled = false;
                    status.Text = "Connected: " + port.PortName + ". Run the simulator; JOB E SET enters terminal mode.";
                }
                catch (Exception ex) { Disconnect(); status.Text = ex.Message; }
            };
            FormClosed += (s, e) => Disconnect();
        }
        private void Disconnect()
        {
            if (port != null) { port.Dispose(); port = null; }
            connect.Text = "Connect"; ports.Enabled = baud.Enabled = lines.Enabled = true;
        }
        internal void Pump(Pkw3000Hardware board)
        {
            if (!Connected) return;
            board.BaudRate = port.BaudRate;
            int now = Environment.TickCount;
            if (unchecked((uint)(now - lastPoll)) < 10) return;
            lastPoll = now;
            try
            {
                board.CtsReady = !lines.Checked || port.CtsHolding;
                board.DsrReady = !lines.Checked || port.DsrHolding;
                port.RtsEnable = !lines.Checked || board.HostMaySend;
                int available = Math.Min(port.BytesToRead, 256);
                if (available > 0 && board.QueuedInput < 4096)
                {
                    byte[] bytes = new byte[available]; int read = port.Read(bytes, 0, bytes.Length);
                    if (read != bytes.Length) Array.Resize(ref bytes, read);
                    board.QueueInput(bytes);
                }
                string output = board.DrainOutput();
                if (output.Length > 0)
                {
                    byte[] bytes = output.Select(ch => (byte)ch).ToArray();
                    port.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex) { Disconnect(); status.Text = "Connection stopped; last transfer may be incomplete. " + ex.Message; }
        }
    }
}
