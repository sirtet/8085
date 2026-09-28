using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;

namespace _8085
{
    // Only the physical front panel. CPU, program and run controls belong to MainForm.
    public sealed class FormPkw3000 : Form
    {
        private Pkw3000Hardware board;
        private readonly SevenSegment[] digits = new SevenSegment[8];
        private readonly PhotoLamp[] lamps = new PhotoLamp[4];
        private readonly Panel panel = new Panel();
        private readonly ToolTip tips = new ToolTip { ShowAlways = true, AutoPopDelay = 12000 };
        private readonly Image artwork;
        private readonly Image verticalPhoto;
        private readonly Image horizontalPhoto;
        private Action updateSwitches;
        private readonly PhotoSocket socket = new PhotoSocket();
        private string socketStatus;
        internal Func<int, string> SelectEpromFile;
        private readonly CancellationTokenSource socketLifetime = new CancellationTokenSource();
        internal bool SocketOperationPending { get; private set; }
        private readonly PhotoSelector selectorVertical = new PhotoSelector(true) { Name = "S1Selector", AccessibleName = "S1" };
        private readonly PhotoSelector selectorHorizontal = new PhotoSelector(false) { Name = "S2Selector", AccessibleName = "S2" };
        public event EventHandler ResetRequested;

        public FormPkw3000()
        {
            Text = "PKW-3000";
            ClientSize = new Size(960, 640); MinimumSize = new Size(656, 466);
            BackColor = Color.FromArgb(28, 30, 32);
            using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-photo-panel.png"))
            using (var original = Image.FromStream(stream)) artwork = new Bitmap(original);
            using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-switch-vertical.png"))
            using (var original = Image.FromStream(stream)) verticalPhoto = new Bitmap(original);
            using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-switch-horizontal.png"))
            using (var original = Image.FromStream(stream)) horizontalPhoto = new Bitmap(original);
            selectorVertical.Photo = verticalPhoto;
            selectorHorizontal.Photo = horizontalPhoto;
            socket.Photo = artwork;
            SelectEpromFile = ChooseEpromFile;
            panel.BackgroundImage = artwork; panel.BackgroundImageLayout = ImageLayout.Stretch;
            Controls.Add(panel);
            for (int i = 0; i < 8; i++)
            {
                digits[i] = new PhotoDigit {
                    ColorLight = Color.FromArgb(255, 55, 110), ColorDark = Color.Transparent };
                // Manual Fig. 1-5: two four-digit modules. COMMAND/DATA share
                // one module, so their label boundary must not add a digit gap.
                const int digitPitch = 43;
                int moduleLeft = i < 4 ? 158 : 365;
                Place(digits[i], new Rectangle(moduleLeft + i % 4 * digitPitch, 459, 40, 53));
            }
            // TLR4135: pin 23 (AM common) follows digit 1; pin 22 (Dot 3
            // anode) follows digit 2. Pins 14/15/18/21 are not connected in
            // the PKW schematic, so these are NOT eight decimal-point LEDs.
            for (int module = 0; module < 2; module++)
            {
                int left = module == 0 ? 158 : 365;
                lamps[module * 2] = new PhotoLamp();
                lamps[module * 2 + 1] = new PhotoLamp();
                Place(lamps[module * 2], new Rectangle(left - 13, 465, 6, 6));
                Place(lamps[module * 2 + 1], new Rectangle(left + 37, 503, 6, 6));
            }
            string[] names = { "C", "D", "E", "F", "LOD", "RST", "8", "9", "A", "B", "ERS", "JOB",
                "4", "5", "6", "7", "PRG", "-", "0", "1", "2", "3", "CMP", "SET" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var key = new PhotoKey { Text = name, AccessibleName = name, TabStop = true };
                Place(key, new Rectangle(122 + i % 6 * 78, 608 + i / 6 * 68, 55, 48));
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
            Place(selectorVertical, new Rectangle(943, 680, 29, 64));
            Place(selectorHorizontal, new Rectangle(1162, 830, 69, 31));
            Place(socket, new Rectangle(688, 478, 200, 512));
            // Hover help covers the complete socket control, independently of
            // the narrower lever/ROM click regions, including after a modal dialog.
            socket.MouseHover += (s, e) => tips.Show(SocketToolTip, socket, socket.Width / 2, socket.Height, 12000);
            socket.MouseLeave += (s, e) => tips.Hide(socket);
            socket.Click += (s, e) => ToggleSocket();
            socket.RemoveRequested += (s, e) => OpenSocketFile();
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
            FormClosed += (s, e) => board?.ReleaseKeys();
            FormClosing += (s, e) => { if (!SaveEprom()) e.Cancel = true; };
        }
        private void Place(Control control, Rectangle bounds)
        { control.Tag = bounds; panel.Controls.Add(control); }
        private void LayoutPanel()
        {
            float scale = Math.Min(ClientSize.Width / 1536f, ClientSize.Height / 1024f);
            panel.Bounds = new Rectangle((ClientSize.Width - (int)(1536 * scale)) / 2,
                (ClientSize.Height - (int)(1024 * scale)) / 2, (int)(1536 * scale), (int)(1024 * scale));
            foreach (Control control in panel.Controls)
            {
                var r = (Rectangle)control.Tag;
                control.Bounds = new Rectangle((int)(r.X * scale), (int)(r.Y * scale), (int)(r.Width * scale), (int)(r.Height * scale));
                if (control is PhotoSocket photoSocket)
                {
                    photoSocket.RenderPanelSize = panel.Size;
                    photoSocket.RenderOrigin = control.Location;
                }
                control.Invalidate();
            }
            // Each HWND gets its own finished crop. Never repaint the entire
            // parent image into a reused child buffer with negative coordinates.
            if (panel.Width > 0 && panel.Height > 0)
            using (var scaled = new Bitmap(panel.Width, panel.Height))
            {
                using (var g = Graphics.FromImage(scaled)) g.DrawImage(artwork, panel.ClientRectangle);
                foreach (Control control in panel.Controls)
                {
                    if (control.Width <= 0 || control.Height <= 0) continue;
                    var old = control.BackgroundImage;
                    control.BackgroundImage = scaled.Clone(control.Bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    old?.Dispose();
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                socketLifetime.Cancel();
                foreach (Control control in panel.Controls) control.BackgroundImage?.Dispose();
                tips.Dispose(); artwork?.Dispose(); verticalPhoto?.Dispose(); horizontalPhoto?.Dispose();
            }
            base.Dispose(disposing);
        }
        private void ApplySwitches()
        {
            if (board != null) board.SwitchInputs = (byte)(((3 - selectorVertical.Value) << 4) | ((3 - selectorHorizontal.Value) << 2));
            UpdateSocket();
        }
        private bool SaveEprom()
        {
            try { board?.Eprom?.Save(); return true; }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the EP-ROM file:\n" + ex.Message, "EPROM", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        private void ToggleSocket()
        {
            if (board == null || SocketOperationPending) return;
            if (!socket.Open || board.Eprom == null) { OpenSocketFile(); return; }
            if (board.Eprom != null && board.Eprom.Type != board.SelectedEpromType) {
                MessageBox.Show(this, "The switches must match the inserted " + PkwEprom.Names[board.Eprom.Type] + ".", "EPROM"); return;
            }
            socket.Open = false;
            if (board.Eprom != null) board.Eprom.Closed = true;
            UpdateSocket();
        }
        private async void OpenSocketFile()
        {
            if (board == null || SocketOperationPending || !SaveEprom()) return;
            int type = board.SelectedEpromType;
            if (type < 0) { MessageBox.Show(this, "Select a valid EP-ROM type first.", "EPROM"); return; }
            var operationBoard = board;
            SocketOperationPending = true;
            selectorVertical.Enabled = selectorHorizontal.Enabled = false;
            socket.Open = true;
            if (board.Eprom != null) board.Eprom.Closed = false;
            UpdateSocket(); socket.Update();
            try {
                await Task.Delay(500, socketLifetime.Token);
                if (IsDisposed || !ReferenceEquals(board, operationBoard)) return;
                try {
                    string path = SelectEpromFile(type);
                    if (path == "") board.Eprom = null;
                    else if (path != null) board.Eprom = PkwEprom.Open(path, type);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "EP-ROM file", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                if (IsDisposed) return;
                // Show the selected/empty socket with the lever still raised.
                UpdateSocket(); socket.Update();
                await Task.Delay(500, socketLifetime.Token);
            }
            catch (OperationCanceledException) { }
            finally {
                // The dialog represents changing the chip, including closing its clamp.
                // Cancellation or a rejected file leaves the previous chip inserted.
                if (operationBoard.Eprom != null) operationBoard.Eprom.Closed = true;
                SocketOperationPending = false;
                if (!IsDisposed) {
                    socket.Open = false;
                    selectorVertical.Enabled = selectorHorizontal.Enabled = true;
                    UpdateSocket();
                    tips.Active = false; tips.Active = true;
                }
            }
        }
        private string ChooseEpromFile(int type)
        {
            using (var dialog = new NativeEpromDialog(type, board.Eprom?.FilePath))
                return dialog.Show(this);
        }
        private void UpdateSocket()
        {
            int type = board == null ? -1 : board.SelectedEpromType;
            // The mechanical flap follows the left selector column even when
            // the other switch produces an unsupported electrical combination.
            socket.Pins28 = selectorHorizontal.Value == 0;
            socket.ChipName = board?.Eprom == null ? null : PkwEprom.Names[board.Eprom.Type];
            socket.ChipPins28 = board?.Eprom != null && board.Eprom.Type < 2;
            string status = (type < 0 ? "Invalid selector position; the original ROM does not accept commands." : PkwEprom.Names[type] + (socket.Pins28 ? " · 28 Pins" : " · 24 Pins")) +
                "\nFlap " + (socket.Pins28 ? "up" : "down") + "\nLever " + (socket.Open ? "open" : "closed") + "\n" + (board?.Eprom?.FilePath ?? "Socket empty") +
                (board?.Eprom?.Dirty == true ? " · unsaved" : "") +
                "\n" + (board?.Eprom == null ? "Lift lever to insert EP-ROM (File)" : "Click ROM or lift lever to change or eject.");
            if (socketStatus == status) return;
            socketStatus = status; tips.SetToolTip(socket, SocketToolTip);
            Text = "PKW-3000" + (board?.Eprom == null ? "" : " — " + PkwEprom.Names[board.Eprom.Type] + " · " +
                System.IO.Path.GetFileName(board.Eprom.FilePath) + (socket.Open ? " — lever open: close to read" :
                board.Eprom.Type != type ? " — disconnected: EP-ROM selection does not match" : " — connected") +
                (board.Eprom.Dirty ? " *" : ""));
            socket.Invalidate();
        }
        private string SocketToolTip { get { return board?.Eprom == null ? "Lift lever to insert EP-ROM (File)" : "Click ROM or lift lever to change / eject"; } }
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
            socket.Open = board?.Eprom != null && !board.Eprom.Closed;
            updateSwitches(); RefreshHardware();
        }
        internal void RefreshHardware()
        {
            UpdateSocket();
            for (int i = 0; i < 8; i++)
            {
                byte value = board == null ? (byte)0 : board.DisplaySegments(i);
                int segments = ((value << 4) | (value >> 4)) & 247; // no generic digit DP
                if (digits[i].SegmentsValue != segments) digits[i].SegmentsValue = segments;
            }
            for (int module = 0; module < 2; module++)
                for (int indicator = 0; indicator < 2; indicator++)
                    lamps[module * 2 + indicator].Lit = board != null &&
                        (board.DisplaySegments(module * 4 + indicator) & 128) != 0;
        }
    }

    // Independent moving parts are painted over the unchanged panel photograph.
    internal sealed class PhotoSocket : Button
    {
        internal Image Photo { get; set; }
        internal Size RenderPanelSize;
        internal Point RenderOrigin;
        private readonly Bitmap[] states = new Bitmap[4];
        internal bool Open, Pins28, ChipPins28;
        internal string ChipName;
        private bool mouseActivation, leverPressed, romPressed;
        internal event EventHandler RemoveRequested;
        private PointF ArtworkPoint(Point location)
        {
            float x, y;
            if (!RenderPanelSize.IsEmpty) {
                x = (location.X + RenderOrigin.X) * 1536f / RenderPanelSize.Width - 688;
                y = (location.Y + RenderOrigin.Y) * 1024f / RenderPanelSize.Height - 478;
            } else { x = location.X * 200f / Width; y = location.Y * 512f / Height; }
            return new PointF(x,y);
        }
        internal bool IsLeverPoint(Point location)
        {
            return (Open ? new RectangleF(121, 356, 34, 29) : new RectangleF(120, 367, 38, 120)).Contains(ArtworkPoint(location));
        }
        internal bool IsRomPoint(Point location)
        {
            return ChipName != null && new RectangleF(45, (ChipPins28 ? 609 : 637) + 5 - 478, 67, (ChipPins28 ? 14 : 12)*15).Contains(ArtworkPoint(location));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            mouseActivation = true;
            leverPressed = e.Button == MouseButtons.Left && IsLeverPoint(e.Location);
            romPressed = e.Button == MouseButtons.Left && IsRomPoint(e.Location);
            base.OnMouseDown(e);
        }
        protected override void OnClick(EventArgs e)
        {
            // Mouse activation is resolved from the actual release event below.
            // Keyboard/accessibility activation continues to operate the lever.
            if (!mouseActivation) base.OnClick(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool remove = mouseActivation && romPressed && e.Button == MouseButtons.Left && IsRomPoint(e.Location);
            bool change = mouseActivation && leverPressed && e.Button == MouseButtons.Left && IsLeverPoint(e.Location);
            base.OnMouseUp(e);
            mouseActivation = leverPressed = romPressed = false;
            if (remove) RemoveRequested?.Invoke(this,EventArgs.Empty);
            else if (change) base.OnClick(EventArgs.Empty);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = IsLeverPoint(e.Location) || IsRomPoint(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        internal PhotoSocket()
        {
            string[] names = { "24-closed", "24-open", "28-closed", "28-open" };
            for (int i = 0; i < names.Length; i++)
                using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-socket-photo-" + names[i] + ".png"))
                using (var original = Image.FromStream(stream)) states[i] = new Bitmap(original);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Default; AccessibleName = "EP-ROM clamping lever"; TabStop = true;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { PhotoBackground.Paint(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PhotoBackground.Paint(this, e);
            var g = e.Graphics; var state = g.Save();
            if (!RenderPanelSize.IsEmpty) {
                // Use the parent's exact image transform, including fractional
                // child origin. Integer HWND sizes must not define a second zoom.
                float sx = RenderPanelSize.Width / 1536f, sy = RenderPanelSize.Height / 1024f;
                g.ScaleTransform(sx, sy);
                g.TranslateTransform(-RenderOrigin.X / sx, -RenderOrigin.Y / sy);
            } else {
                g.ScaleTransform(Width / 200f, Height / 512f);
                g.TranslateTransform(-688, -478);
            }
            g.DrawImage(states[(Pins28 ? 2 : 0) + (Open ? 1 : 0)], new Rectangle(688, 478, 200, 512));
            if (ChipName != null) {
                g.TranslateTransform(0, 5); // Follow the corrected socket artwork position.
                int top = ChipPins28 ? 609 : 637, count = ChipPins28 ? 14 : 12;
                using (var pin = new SolidBrush(Color.Silver))
                    for (int i = 0; i < count; i++) {
                        int y = top + 7 + i * 15;
                        g.FillRectangle(pin, 718, y, 18, 6); g.FillRectangle(pin, 798, y, 18, 6);
                    }
                using (var body = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(733, top, 67, count * 15),
                    Color.FromArgb(62, 60, 61), Color.FromArgb(34, 33, 35), 0f)) g.FillRectangle(body, 733, top, 67, count * 15);
                using (var notch = new SolidBrush(Color.FromArgb(20, 20, 20))) g.FillEllipse(notch, 755, top - 6, 22, 16);
                using (var glass = new SolidBrush(Color.FromArgb(144, 147, 153))) g.FillEllipse(glass, 751, top + 60, 31, 31);
                using (var font = new Font(FontFamily.GenericMonospace, 11, FontStyle.Bold))
                using (var ink = new SolidBrush(Color.LightGray)) g.DrawString(ChipName, font, ink, 737, top + 116);
            }
            g.Restore(state);
            if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var bitmap in states) bitmap.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class PhotoBackground
    {
        internal static void Paint(Control control, PaintEventArgs e)
        {
            e.Graphics.Clear(control.BackColor);
            if (control.BackgroundImage != null) e.Graphics.DrawImageUnscaled(control.BackgroundImage, 0, 0);
        }
    }

    internal sealed class PhotoDigit : SevenSegment
    {
        public PhotoDigit() { SetStyle(ControlStyles.Opaque, true); }
        protected override void OnPaintBackground(PaintEventArgs e) { PhotoBackground.Paint(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PhotoBackground.Paint(this, e);
            var state = e.Graphics.Save();
            try
            {
                // Photo measurements: lit characters occupy about one third of
                // the filter height. Keep their geometry in artwork units so
                // thin bars do not disappear through integer rounding at zoom.
                e.Graphics.ScaleTransform(Width / 40f, Height / 53f);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(ColorLight))
                {
                    DrawBar(e.Graphics, brush, 0x10, 12, 10, 28, 10);
                    DrawBar(e.Graphics, brush, 0x20, 29, 12, 29, 25);
                    DrawBar(e.Graphics, brush, 0x40, 29, 29, 29, 42);
                    DrawBar(e.Graphics, brush, 0x80, 12, 44, 28, 44);
                    DrawBar(e.Graphics, brush, 0x01, 11, 29, 11, 42);
                    DrawBar(e.Graphics, brush, 0x02, 11, 12, 11, 25);
                    DrawBar(e.Graphics, brush, 0x04, 12, 27, 28, 27);
                }
            }
            finally { e.Graphics.Restore(state); }
        }
        private void DrawBar(Graphics graphics, Brush brush, int mask, float x1, float y1, float x2, float y2)
        {
            if ((SegmentsValue & mask) == 0) return;
            const float halfWidth = 1.1f;
            bool horizontal = y1 == y2;
            float dx = horizontal ? halfWidth : 0, dy = horizontal ? 0 : halfWidth;
            float nx = horizontal ? 0 : halfWidth, ny = horizontal ? halfWidth : 0;
            var points = new[] {
                new PointF(x1, y1), new PointF(x1 + dx + nx, y1 + dy + ny),
                new PointF(x2 - dx + nx, y2 - dy + ny), new PointF(x2, y2),
                new PointF(x2 - dx - nx, y2 - dy - ny), new PointF(x1 + dx - nx, y1 + dy - ny)
            };
            for (int i = 0; i < points.Length; i++) points[i].X -= 0.1f * (points[i].Y - 27);
            graphics.FillPolygon(brush, points);
        }
    }

    internal sealed class PhotoLamp : Control
    {
        private bool lit;
        internal bool Lit { get { return lit; } set { if (lit != value) { lit = value; Invalidate(); } } }
        public PhotoLamp() { SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true); TabStop = false; }
        protected override void OnPaintBackground(PaintEventArgs e) { PhotoBackground.Paint(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PhotoBackground.Paint(this, e);
            if (lit) using (var brush = new SolidBrush(Color.FromArgb(255, 55, 110)))
                e.Graphics.FillEllipse(brush, ClientRectangle);
        }
    }

    // Draw the actual key from the panel photograph, then interaction feedback.
    internal sealed class PhotoKey : Button
    {
        private bool pressed;
        public PhotoKey()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            BackColor = Color.FromArgb(64, 64, 64);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = FlatAppearance.MouseDownBackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { PhotoBackground.Paint(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase can skip WM_ERASEBKGND on interaction; paint a complete
            // frame here on every press/hover/focus, even without background paint.
            PhotoBackground.Paint(this, e);
            if (pressed) using (var shade = new SolidBrush(Color.FromArgb(65, Color.Black)))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
    }

    internal sealed class PhotoSelector : Control
    {
        private readonly bool vertical;
        private int value = 1;
        private float dragOffset;
        public Image Photo { get; set; }
        public event EventHandler ValueChanged;
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(2, value));
                if (this.value == next) return;
                this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public PhotoSelector(bool vertical)
        {
            this.vertical = vertical;
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(64, 64, 64); TabStop = true; Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE;
            AccessibleRole = AccessibleRole.Slider;
        }
        private float Length { get { return vertical ? Height : Width; } }
        private float KnobLength { get { return Length * (vertical ? .55f : .53f); } }
        private float Position { get { return (vertical ? 2 - Value : Value) * (Length - KnobLength) / 2; } }
        protected override void OnPaintBackground(PaintEventArgs e) { PhotoBackground.Paint(this, e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PhotoBackground.Paint(this, e);
            base.OnPaint(e);
            var target = vertical ? new RectangleF(0, Position, Width, KnobLength) : new RectangleF(Position, 0, KnobLength, Height);
            // Caps are cut from the original device photograph.
            if (Photo != null) e.Graphics.DrawImage(Photo, target, new RectangleF(0, 0, Photo.Width, Photo.Height), GraphicsUnit.Pixel);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
        private void MoveTo(float coordinate)
        {
            float travel = Length - KnobLength;
            if (travel <= 0) return;
            int position = (int)Math.Round(2 * (coordinate - dragOffset) / travel, MidpointRounding.AwayFromZero);
            Value = vertical ? 2 - position : position;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            float coordinate = vertical ? e.Y : e.X;
            dragOffset = coordinate >= Position && coordinate <= Position + KnobLength ? coordinate - Position : KnobLength / 2;
            Capture = true; MoveTo(coordinate);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) MoveTo(vertical ? e.Y : e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Right: Value++; break;
                case Keys.Down: case Keys.Left: Value--; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = 2; break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
}
