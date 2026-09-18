using System;
using System.Drawing;
using System.Windows.Forms;

namespace _8085
{
    // Only the physical front panel. CPU, program and run controls belong to MainForm.
    public sealed class FormPkw3000 : Form
    {
        private Pkw3000Hardware board;
        private readonly SevenSegment[] digits = new SevenSegment[8];
        private readonly Panel panel = new Panel();
        private readonly ToolTip tips = new ToolTip();
        private readonly Image artwork;
        private Action updateSwitches;
        private readonly TrackBar selectorVertical = new TrackBar { Name = "S1Selector", Minimum = 0, Maximum = 2, Value = 1,
            Orientation = Orientation.Vertical, TickStyle = TickStyle.Both, SmallChange = 1, LargeChange = 1, AutoSize = false };
        private readonly TrackBar selectorHorizontal = new TrackBar { Name = "S2Selector", Minimum = 0, Maximum = 2, Value = 1,
            Orientation = Orientation.Horizontal, TickStyle = TickStyle.Both, SmallChange = 1, LargeChange = 1, AutoSize = false };
        public event EventHandler ResetRequested;

        public FormPkw3000()
        {
            Text = "PKW-3000";
            ClientSize = new Size(930, 645); MinimumSize = new Size(636, 469);
            using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-panel.png"))
            using (var original = Image.FromStream(stream)) artwork = new Bitmap(original);
            panel.BackgroundImage = artwork; panel.BackgroundImageLayout = ImageLayout.Stretch;
            Controls.Add(panel);
            for (int i = 0; i < 8; i++)
            {
                digits[i] = new SevenSegment { Padding = new Padding(4, 3, 3, 3), SegementsWidth = 3, ColorLight = Color.OrangeRed, ColorDark = Color.FromArgb(35, 8, 4) };
                Place(digits[i], new Rectangle(64 + i * 21, 201, 21, 38));
            }
            string[] names = { "C", "D", "E", "F", "LOD", "RST", "8", "9", "A", "B", "ERS", "JOB",
                "4", "5", "6", "7", "PRG", "-", "0", "1", "2", "3", "CMP", "SET" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var key = new Button { Text = name, Padding = Padding.Empty, Margin = Padding.Empty,
                    FlatStyle = FlatStyle.Flat, BackColor = Color.WhiteSmoke, TabStop = true };
                key.FlatAppearance.BorderColor = Color.DimGray;
                Place(key, new Rectangle(65 + i % 6 * 29, 276 + i / 6 * 26, 23, 21));
                tips.SetToolTip(key, name == "RST" ? "CPU reset" : name);
                if (name == "RST") key.Click += (s, e) => ResetRequested?.Invoke(this, EventArgs.Empty);
                else
                {
                    key.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) board?.SetKey(name, true); };
                    key.MouseUp += (s, e) => board?.SetKey(name, false);
                    key.MouseCaptureChanged += (s, e) => { if (!key.Capture) board?.SetKey(name, false); };
                    key.KeyDown += (s, e) => { if (e.KeyCode == Keys.Space) board?.SetKey(name, true); };
                    key.KeyUp += (s, e) => { if (e.KeyCode == Keys.Space) board?.SetKey(name, false); };
                }
            }
            Place(selectorVertical, new Rectangle(375, 289, 30, 58));
            Place(selectorHorizontal, new Rectangle(442, 347, 67, 29));
            updateSwitches = () => {
                tips.SetToolTip(selectorVertical, "S1: " + new[] { "down", "middle", "up" }[selectorVertical.Value]);
                tips.SetToolTip(selectorHorizontal, "S2: " + new[] { "left", "middle", "right" }[selectorHorizontal.Value]);
                ApplySwitches();
            };
            selectorVertical.ValueChanged += (s, e) => updateSwitches();
            selectorHorizontal.ValueChanged += (s, e) => updateSwitches();
            updateSwitches();
            Resize += (s, e) => LayoutPanel(); LayoutPanel();
            Deactivate += (s, e) => board?.ReleaseKeys();
            FormClosed += (s, e) => { board?.ReleaseKeys(); artwork.Dispose(); tips.Dispose(); };
        }
        private void Place(Control control, Rectangle bounds)
        { control.Tag = bounds; panel.Controls.Add(control); }
        private void LayoutPanel()
        {
            float scale = Math.Min(ClientSize.Width / 620f, ClientSize.Height / 430f);
            panel.Bounds = new Rectangle((ClientSize.Width - (int)(620 * scale)) / 2,
                (ClientSize.Height - (int)(430 * scale)) / 2, (int)(620 * scale), (int)(430 * scale));
            foreach (Control control in panel.Controls)
            {
                var r = (Rectangle)control.Tag;
                control.Bounds = new Rectangle((int)(r.X * scale), (int)(r.Y * scale), (int)(r.Width * scale), (int)(r.Height * scale));
                if (control is Button) { var old = control.Font; control.Font = new Font("Segoe UI", Math.Max(5, 4.5f * scale)); if (old != Font) old.Dispose(); }
            }
        }
        private void ApplySwitches()
        { if (board != null) board.SwitchInputs = (byte)(((3 - selectorVertical.Value) << 4) | ((3 - selectorHorizontal.Value) << 2)); }
        internal void Bind(Pkw3000Hardware hardware)
        {
            board = hardware;
            if (board != null)
            {
                byte saved = board.SwitchInputs;
                // Detach while restoring the controls, so the first ValueChanged does not alter the second field.
                board = null;
                selectorVertical.Value = Math.Max(0, Math.Min(2, 3 - ((saved >> 4) & 3)));
                selectorHorizontal.Value = Math.Max(0, Math.Min(2, 3 - ((saved >> 2) & 3)));
                board = hardware;
            }
            updateSwitches(); RefreshHardware();
        }
        internal void RefreshHardware()
        {
            for (int i = 0; i < 8; i++)
            {
                byte value = board == null ? (byte)0 : board.DisplaySegments(i);
                int segments = ((value << 4) | (value >> 4)) & 255;
                if (digits[i].SegmentsValue != segments) digits[i].SegmentsValue = segments;
            }
        }
    }
}
