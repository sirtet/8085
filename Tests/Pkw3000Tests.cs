using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using _8085;

static class Pkw3000Tests
{
    delegate bool NativeChildCallback(IntPtr hwnd, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd, NativeChildCallback callback, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,System.Text.StringBuilder text,int max);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr hwnd);
    static IntPtr FindNativeButton(IntPtr parent,string title)
    {
        IntPtr found=IntPtr.Zero;
        EnumChildWindows(parent,(hwnd,p) => { var text=new System.Text.StringBuilder(256); GetWindowText(hwnd,text,256); if(text.ToString()==title) { found=hwnd; return false; } return true; },IntPtr.Zero);
        return found;
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    static void WaitForSocket(FormPkw3000 form)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (form.SocketOperationPending && timeout.ElapsedMilliseconds < 7000) {
            System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(5);
        }
        Check(!form.SocketOperationPending,"socket animation finishes without blocking the UI");
    }
    static void Run(Assembler85 cpu, ulong clocks)
    {
        ulong until = cpu.cycles + clocks;
        while (cpu.cycles < until)
        {
            ushort next = cpu.registerPC;
            string error = cpu.RunInstruction(next, ref next);
            if (error != "") throw new Exception(error);
        }
    }
    static string Display(Pkw3000Hardware hw)
    {
        return string.Join(" ", Enumerable.Range(0, 8).Select(i => hw.DisplaySegments(i).ToString("X2")));
    }
    static void Press(Assembler85 cpu, Pkw3000Hardware hw, string key)
    {
        hw.SetKey(key, true); Run(cpu, 3000000);
        hw.SetKey(key, false); Run(cpu, 3000000);
        Console.WriteLine("KEY " + key + " display=" + Display(hw) + " PC=" + cpu.registerPC.ToString("X4"));
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new ArgumentException("Usage: Pkw3000Tests.exe <original ROM.bin> [hellorld.asm]");
            if (args.Contains("--socket-audit")) { EpromWorkflowAudit(args[0]); return 0; }
            if (args.Contains("--display-audit"))
            {
                var bytes = File.ReadAllBytes(args[0]);
                int sourceIndex = Array.IndexOf(args, "--firmware");
                if (sourceIndex >= 0)
                {
                    var assembled = new Assembler85(File.ReadAllLines(args[sourceIndex + 1]));
                    Check(assembled.FirstPass() == "OK" && assembled.SecondPass() == "OK", "display audit firmware assembles");
                    bytes = assembled.RAM;
                }
                foreach (var key in new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F", "LOD", "ERS", "JOB", "PRG", "-", "CMP", "SET" })
                {
                    var hw = new Pkw3000Hardware(); var machine = new Assembler85(new string[0]) { Hardware = hw };
                    Array.Copy(bytes, machine.RAM, bytes.Length); Run(machine, 3000000);
                    Press(machine, hw, key);
                    Console.WriteLine("AUDIT " + key + " RAM=" + BitConverter.ToString(machine.RAM, 0x6070, 8));
                    Press(machine, hw, "2");
                }
                {
                    var hw = new Pkw3000Hardware(); var machine = new Assembler85(new string[0]) { Hardware = hw };
                    Array.Copy(bytes, machine.RAM, bytes.Length); Run(machine, 3000000); hw.SetKey("C", true);
                    var seen = new System.Collections.Generic.HashSet<string>();
                    for (int n = 0; n < 3000; n++) { Run(machine, 1000); string value = Display(hw); if (seen.Add(value)) Console.WriteLine("TRANSIENT " + value + " PC=" + machine.registerPC.ToString("X4")); }
                }
                for (int vertical = 0; vertical < 3; vertical++) for (int horizontal = 0; horizontal < 3; horizontal++)
                {
                    var hw = new Pkw3000Hardware { SwitchInputs = (byte)(((3 - vertical) << 4) | ((3 - horizontal) << 2)) };
                    var machine = new Assembler85(new string[0]) { Hardware = hw }; Array.Copy(bytes, machine.RAM, bytes.Length); Run(machine, 3000000);
                    Console.WriteLine("SELECT vertical=" + vertical + " horizontal=" + horizontal + " input=" + hw.SwitchInputs.ToString("X2") + " display=" + Display(hw));
                }
                return 0;
            }
            if (args.Contains("--perf-only")) { FastPerformance(args[0]); return 0; }
            int firmwareIndex = Array.IndexOf(args, "--firmware");
            if (args.Contains("--terminal-e2e"))
            {
                TerminalEndToEnd(args[0], null, args.Contains("--physical-keys"));
                if (firmwareIndex >= 0) TerminalEndToEnd(args[0], args[firmwareIndex + 1], args.Contains("--physical-keys"));
                return 0;
            }
            if (firmwareIndex >= 0)
            {
                var assembled = new Assembler85(File.ReadAllLines(args[firmwareIndex + 1]));
                string pass = assembled.FirstPass(); Check(pass == "OK", "firmware ASM first pass: " + pass);
                pass = assembled.SecondPass(); Check(pass == "OK", "firmware ASM second pass: " + pass);
                var hardware = new Pkw3000Hardware(); assembled.Hardware = hardware;
                Run(assembled, 3000000);
                Check(hardware.TimerPulses > 400, "existing firmware ASM boots with hardware timer");
                Press(assembled, hardware, "C");
                Check(hardware.DisplaySegments(0) == 0x40 && hardware.DisplaySegments(1) == 0x66,
                    "invalid first key in firmware ASM returns to dash/type display without stack corruption");
            }
            Check(FormTerminal.NormalizeInput("X4\r\nABC\n\x16\x14") == "X4\rABC\r", "paste normalizes line endings and excludes shortcut/control artifacts");
            CpuRegression();
            EpromRegression(args[0]);
            SerialRegression();
            var board = new Pkw3000Hardware();
            board.WritePort(0x6C, 0x70, 0); board.WritePort(0x6D, 0xD7, 0); board.WritePort(0x68, 0xC3, 0);
            board.Advance(5999); Check(!board.TakeInterrupt75(), "timer not early");
            board.Advance(6000); Check(board.TakeInterrupt75(), "6000-cycle timer edge");
            board.Advance(18000); Check(board.TimerPulses == 3, "periodic timer independent of polling interval");
            Check(board.ReadPort(0x68, 18000) == 0x40 && board.ReadPort(0x68, 18000) == 0, "timer status read clears only status latch");
            board.WritePort(0x6A, 0x89, 18000); board.SetKey("JOB", true);
            Check(board.ReadPort(0x6B, 18000) == 4, "JOB matrix column 1 row 2");
            board.SetKey("JOB", false); Check(board.ReadPort(0x6B, 18000) == 0, "key release");
            board.WritePort(0x69, 0x76, 18000); Check(board.DisplaySegments(1) == 0x76, "firmware display enable polarity");
            board.WritePort(0x69, 0, 18000); board.Advance(18000 + 120001);
            Check(board.DisplaySegments(1) == 0, "blanked display expires");

            var irq = new Pkw3000Hardware(); var cpu = new Assembler85(new string[0]) { Hardware = irq };
            cpu.registerSP = 0x6040; cpu.RAM[0] = 0xFB; cpu.RAM[1] = 0; cpu.intrP75 = true;
            ushort pc = 0; cpu.RunInstruction(0, ref pc); Check(pc == 1, "EI executed");
            cpu.RunInstruction(pc, ref pc); Check(pc == 2, "EI defers interrupt for one instruction");
            cpu.RunInstruction(pc, ref pc); Check(pc == 0x3C && !cpu.intrIE && cpu.RAM[0x603E] == 2, "RST7.5 vectors and saves return PC");
            cpu.registerA = 0x10; cpu.intrP75 = true; cpu.RAM[0x3C] = 0x30;
            cpu.RunInstruction(0x3C, ref pc); Check(!cpu.intrP75, "SIM clears RST7.5 latch");

            cpu = new Assembler85(new string[0]) { Hardware = new Pkw3000Hardware() };
            cpu.registerSP = 0x6040; cpu.RAM[0] = 0xFB; cpu.RAM[1] = 0x76; cpu.intrP75 = true;
            cpu.RunInstruction(0, ref pc); cpu.RunInstruction(pc, ref pc);
            Check(pc == 2 && cpu.cycles == 9, "PKW EI/HLT advances PC once and completes EI delay");
            cpu.RunInstruction(pc, ref pc);
            Check(pc == 0x3C && cpu.RAM[0x603E] == 2, "pending PKW interrupt wakes HLT with correct return address");

            byte[] rom = File.ReadAllBytes(args[0]); Check(rom.Length == 8192, "ROM is 8 KB");
            cpu = new Assembler85(new string[0]); Array.Copy(rom, cpu.RAM, rom.Length);
            cpu.RAM[0x6051] = 4; cpu.registerPC = 0x0347; cpu.registerSP = 0x60F0; cpu.RAM[0x60F1] = 0x61;
            for (int i = 0; i < 10000 && cpu.registerPC != 0x6100; i++)
            {
                ushort n = cpu.registerPC;
                string err = cpu.RunInstruction(n, ref n); if (err != "") throw new Exception(err);
            }
            Check(cpu.registerPC == 0x6100 && cpu.RAM[0x6071] == 0xFF && cpu.RAM[0x6072] == 6,
                "ROM keyboard routine decodes JOB without device shortcuts");
            using (var sha = SHA256.Create()) Console.WriteLine("ROM SHA256 " + BitConverter.ToString(sha.ComputeHash(rom)).Replace("-", ""));
            board = new Pkw3000Hardware(); cpu = new Assembler85(new string[0]) { Hardware = board };
            Array.Copy(rom, cpu.RAM, rom.Length); Run(cpu, 3000000);
            Console.WriteLine("BOOT PC=" + cpu.registerPC.ToString("X4") + " SP=" + cpu.registerSP.ToString("X4") + " display=" + Display(board));
            Check(board.TimerPulses > 400, "original ROM starts timer");
            Check(Enumerable.Range(0, 8).Any(i => board.DisplaySegments(i) != 0), "original ROM drives display");
            Press(cpu, board, "JOB"); Press(cpu, board, "E"); Press(cpu, board, "SET");
            Run(cpu, 3000000);
            string greeting = board.DrainOutput();
            Check(cpu.RAM[0x6067] == 1 && greeting == "\r\nPKW-3000\r\n*", "JOB E SET enters terminal and transmits original greeting");
            board.QueueInput("X4\r"); Run(cpu, 6000000);
            string reply = board.DrainOutput();
            Console.WriteLine("REPLY " + reply.Replace("\r", "<CR>").Replace("\n", "<LF>"));
            Check(reply == "X4   00  " && board.QueuedInput == 0, "serial RX command X4 and TX baud parameter response");
            board.QueueInput("\r"); Run(cpu, 3000000);
            Check(board.DrainOutput().Contains("*"), "serial parameter dialog returns to prompt");
            Check(rom.SequenceEqual(cpu.RAM.Take(8192)), "original ROM remains unmodified");
            BufferRegression(cpu, board);
            if (args.Length > 1 && !args[1].StartsWith("--")) HelloRegression(cpu, board, args[1]);
            if (args.Contains("--ui")) using (var form = new FormPkw3000())
            {
                form.Bind(board);
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-20000, -20000);
                form.ShowInTaskbar = false;
                form.Show(); System.Windows.Forms.Application.DoEvents();
                // Render a real running ROM state without injecting display characters.
                form.RefreshHardware();
                form.PerformLayout();
                PhotoBackgroundRegression(form);
                SocketUiRegression(form, board);
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-window.png"));
                }
                form.Close();
                Check(true, "PKW hardware-only form renders shared CPU display");
            }
            if (args.Contains("--ui")) { MainWindowRegression(args[0]); BinaryIntegrationRegression(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static object Field(object target, string name)
    { return target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(target); }
    static void PhotoBackgroundRegression(FormPkw3000 form)
    {
        var panel = (System.Windows.Forms.Panel)Field(form, "panel");
        foreach (var size in new[] { new System.Drawing.Size(960, 640), new System.Drawing.Size(1200, 800), new System.Drawing.Size(780, 700) })
        {
            form.ClientSize = size;
            System.Windows.Forms.Application.DoEvents();
            var digits = (System.Windows.Forms.Control[])Field(form, "digits");
            var pitches = Enumerable.Range(0, 7).Where(i => i != 3)
                .Select(i => digits[i + 1].Left - digits[i].Left).ToArray();
            Check(pitches.Max() - pitches.Min() <= 1 &&
                digits[4].Left - digits[3].Left > pitches.Max(),
                "both four-digit modules have equal pitch, with a gap only between modules at " + size.Width);
            using (var expected = new System.Drawing.Bitmap(panel.Width, panel.Height))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(expected))
                    graphics.DrawImage(panel.BackgroundImage, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
                foreach (System.Windows.Forms.Control control in panel.Controls)
                {
                    using (var actual = new System.Drawing.Bitmap(control.Width, control.Height))
                    using (var graphics = System.Drawing.Graphics.FromImage(actual))
                    {
                        // Exercise the actual background paint override independently of parent repaint.
                        Invoke(control, "OnPaintBackground", new System.Windows.Forms.PaintEventArgs(graphics, control.ClientRectangle));
                        int difference = 0, samples = 0;
                        for (int y = 0; y < control.Height; y += 3)
                            for (int x = 0; x < control.Width; x += 3)
                            {
                                var a = actual.GetPixel(x, y);
                                var b = expected.GetPixel(control.Left + x, control.Top + y);
                                difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                                samples += 3;
                            }
                        // GDI+ can round interpolation slightly differently for a clipped image.
                        Check(difference <= samples * 3, "photo background stays aligned at " + size.Width + " for " + control.GetType().Name + " " + control.Text + " (mean error " + (difference / (double)samples).ToString("F2") + ")");
                        if (control is PhotoKey) {
                            graphics.Clear(System.Drawing.Color.Magenta);
                            Invoke(control, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 2, 2, 0));
                            Invoke(control, "OnPaint", new System.Windows.Forms.PaintEventArgs(graphics, control.ClientRectangle));
                            Invoke(control, "OnMouseUp", new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 2, 2, 0));
                            graphics.Clear(System.Drawing.Color.Magenta);
                            Invoke(control, "OnPaint", new System.Windows.Forms.PaintEventArgs(graphics, control.ClientRectangle));
                            bool same = true;
                            for (int y = 5; y < control.Height - 5; y += 3)
                                for (int x = 5; x < control.Width - 5; x += 3)
                                    same &= actual.GetPixel(x, y) == expected.GetPixel(control.Left + x, control.Top + y);
                            Check(same, "key press/release repaints complete photo without background phase: " + control.Text);
                        }
                    }
                }
            }
        }
        form.ClientSize = new System.Drawing.Size(1200, 800);
        System.Windows.Forms.Application.DoEvents();
    }
    static void Invoke(object target, string name, params object[] args)
    { target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(target, args); }
    static void SocketUiRegression(FormPkw3000 form, Pkw3000Hardware board)
    {
        // Render both actual control states side by side at native artwork scale.
        using (var comparison = new System.Drawing.Bitmap(440, 560))
        using (var graphics = System.Drawing.Graphics.FromImage(comparison))
        using (var preview = new PhotoSocket())
        using (var font = new System.Drawing.Font("Arial", 12)) {
            graphics.Clear(System.Drawing.Color.FromArgb(45, 46, 48));
            preview.Photo = (System.Drawing.Image)Field(form, "artwork");
            preview.Size = new System.Drawing.Size(200, 512);
            using (var background = ((System.Drawing.Bitmap)preview.Photo).Clone(new System.Drawing.Rectangle(688, 478, 200, 512), System.Drawing.Imaging.PixelFormat.Format32bppArgb)) {
                preview.BackgroundImage = background;
                for (int position = 0; position < 2; position++) {
                    preview.Pins28 = position == 1;
                    using (var rendered = new System.Drawing.Bitmap(200, 512))
                    using (var paint = System.Drawing.Graphics.FromImage(rendered)) {
                        Invoke(preview, "OnPaint", new System.Windows.Forms.PaintEventArgs(paint, preview.ClientRectangle));
                        graphics.DrawImageUnscaled(rendered, 10 + position * 220, 38);
                    }
                    graphics.DrawString(position == 0 ? "24 Pins / 12 Paare" : "28 Pins / 14 Paare", font, System.Drawing.Brushes.White, 10 + position * 220, 10);
                }
                preview.BackgroundImage = null;
            }
            comparison.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "socket-contact-comparison.png"));
            foreach (int width in new[] { 777, 997, 1199, 1536, 1843 }) {
                int height = width * 2 / 3;
                float sx = width / 1536f, sy = height / 1024f;
                var bounds = new System.Drawing.Rectangle((int)(688*sx), (int)(478*sy), (int)(200*sx), (int)(512*sy));
                preview.Size = bounds.Size;
                preview.RenderPanelSize = new System.Drawing.Size(width, height);
                preview.RenderOrigin = bounds.Location;
                using (var full = new System.Drawing.Bitmap(width, height))
                using (var paint = System.Drawing.Graphics.FromImage(full))
                using (var stream = typeof(FormPkw3000).Assembly.GetManifestResourceStream("pkw-socket-photo-28-closed.png"))
                using (var asset = System.Drawing.Image.FromStream(stream)) {
                    paint.DrawImage(preview.Photo, new System.Drawing.Rectangle(0,0,width,height));
                    using (var background = full.Clone(bounds, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (var actual = new System.Drawing.Bitmap(bounds.Width,bounds.Height))
                    using (var childPaint = System.Drawing.Graphics.FromImage(actual)) {
                        preview.BackgroundImage = background;
                        Invoke(preview,"OnPaint",new System.Windows.Forms.PaintEventArgs(childPaint,preview.ClientRectangle));
                        paint.ScaleTransform(sx,sy);
                        paint.DrawImage(asset,new System.Drawing.Rectangle(688,478,200,512));
                        double error = 0; int samples = 0;
                        for (int y=2;y<bounds.Height-2;y++) for (int x=2;x<bounds.Width-2;x++) {
                            var a=actual.GetPixel(x,y); var b=full.GetPixel(x+bounds.X,y+bounds.Y);
                            error += Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B); samples+=3;
                        }
                        Check(error/samples < 1.0, "socket uses front-panel pixel alignment at width " + width);
                        var downGrip = new System.Drawing.Point((int)((688+138)*sx)-bounds.X,(int)((478+458)*sy)-bounds.Y);
                        var upGrip = new System.Drawing.Point((int)((688+138)*sx)-bounds.X,(int)((478+370)*sy)-bounds.Y);
                        var bodyPoint = new System.Drawing.Point((int)((688+80)*sx)-bounds.X,(int)((478+240)*sy)-bounds.Y);
                        Check(preview.IsLeverPoint(downGrip) && !preview.IsLeverPoint(bodyPoint), "closed lever hit region follows zoom " + width);
                        preview.Open = true;
                        Check(preview.IsLeverPoint(upGrip) && !preview.IsLeverPoint(downGrip) && !preview.IsLeverPoint(bodyPoint), "open lever hit region follows pivot at zoom " + width);
                        preview.Open = false;
                        preview.BackgroundImage = null;
                    }
                }
            }
        }
        string path = Path.Combine(Path.GetTempPath(), "pkw-socket-" + Guid.NewGuid().ToString("N") + ".bin");
        var socket = (PhotoSocket)Field(form, "socket");
        var vertical = (PhotoSelector)Field(form, "selectorVertical");
        var horizontal = (PhotoSelector)Field(form, "selectorHorizontal");
        try {
            vertical.Value = 2; horizontal.Value = 0;
            Check(socket.Pins28, "2764 automatically raises pin-count flap");
            vertical.Value = 0;
            Check(socket.Pins28 && board.SelectedEpromType == -1, "left/down invalid combination still raises mechanical flap");
            vertical.Value = 2;
            int unexpectedPickers = 0;
            form.SelectEpromFile = type => { unexpectedPickers++; return null; };
            var bodyClick = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,socket.Width/2,socket.Height/2,0);
            Invoke(socket,"OnMouseDown",bodyClick);
            Invoke(socket,"OnClick",EventArgs.Empty);
            Invoke(socket,"OnMouseUp",bodyClick);
            Check(unexpectedPickers == 0 && !socket.Open,"clicking socket body does not open file picker");
            Check(((System.Windows.Forms.ToolTip)Field(form,"tips")).GetToolTip(socket) == "Lift lever to insert EP-ROM (File)","socket tooltip uses requested English text");
            var leverTiming = System.Diagnostics.Stopwatch.StartNew();
            long pickerReturnedAt = 0;
            int pickerCalls = 0;
            form.SelectEpromFile = type => {
                pickerCalls++;
                Check(leverTiming.ElapsedMilliseconds >= 450,"file picker waits half a second after raising lever");
                Check(socket.Open && (board.Eprom == null || !board.Eprom.Closed), "socket disconnected while file picker is open");
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-socket-raised-detail.png"));
                }
                pickerReturnedAt = leverTiming.ElapsedMilliseconds;
                return path;
            };
            socket.PerformClick();
            Check(socket.Open && form.SocketOperationPending && pickerCalls == 0,"raised lever paints before deferred file picker");
            socket.PerformClick();
            Check(pickerCalls == 0,"repeat click cannot bypass lever delay");
            WaitForSocket(form);
            Check(pickerCalls == 1 && leverTiming.ElapsedMilliseconds-pickerReturnedAt >= 450,"lever closes half a second after picker returns");
            Check(!socket.Open && board.Eprom != null && board.Eprom.Closed && form.Text.Contains("connected"), "file selection automatically closes lever and connects image");
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-socket-closed-28.png"));
            }
            board.Eprom.Program(123, 0xA5);
            var inserted = board.Eprom;
            form.SelectEpromFile = type => {
                Check(socket.Open && !inserted.Closed, "existing chip disconnected during file change");
                return null;
            };
            socket.PerformClick(); WaitForSocket(form);
            Check(!socket.Open && ReferenceEquals(board.Eprom, inserted) && board.Eprom.Closed && File.ReadAllBytes(path)[123] == 0xA5, "opening saves modifications; cancel reconnects previous chip");
            Check(((System.Windows.Forms.ToolTip)Field(form,"tips")).GetToolTip(socket).Contains("change / eject"),"inserted ROM exposes remove and change actions");
            using (var chooser = new NativeEpromDialog(0,path)) {
                chooser.ReadyForTest = d => d.Cancel();
                Check(chooser.Show(form) == null,"native Cancel does not request ejection");
                Check(chooser.CurrentFileSelected,"native dialog selects current file in list");
            }
            using (var chooser = new NativeEpromDialog(0,path)) {
                bool foundButton = false;
                using (var timeout = new System.Windows.Forms.Timer { Interval=5000 }) {
                timeout.Tick += (s,e) => { timeout.Stop(); chooser.Cancel(); };
                chooser.ReadyForTest = d => {
                    NativeRect bounds; GetWindowRect(d.WindowHandle,out bounds);
                    using(var bitmap=new System.Drawing.Bitmap(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top))
                    using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {
                        var hdc=graphics.GetHdc();
                        try { PrintWindow(d.WindowHandle,hdc,2); } finally { graphics.ReleaseHdc(hdc); }
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-eprom-dialog.png"));
                    }
                    var button=FindNativeButton(d.WindowHandle,"Eject ROM");
                    foundButton=button!=IntPtr.Zero;
                    timeout.Start();
                    if(foundButton) { SetActiveWindow(d.WindowHandle); SendMessage(button,0xF5,IntPtr.Zero,IntPtr.Zero); }
                    else d.Cancel();
                };
                Check(chooser.Show(form) == "","native Eject ROM returns explicit ejection");
                Check(foundButton,"native custom button dispatches Eject ROM event");
                }
            }
            inserted.Program(124,0x5A);
            var socketPanelSize = socket.RenderPanelSize; var socketOrigin = socket.RenderOrigin;
            var romPoint = new System.Drawing.Point((int)(766*socketPanelSize.Width/1536f)-socketOrigin.X,
                (int)(710*socketPanelSize.Height/1024f)-socketOrigin.Y);
            Check(socket.IsRomPoint(romPoint),"inserted ROM has its own removal hit region");
            var previousCursor = System.Windows.Forms.Cursor.Position;
            try {
                System.Windows.Forms.Cursor.Position = socket.PointToScreen(romPoint);
                var romClick = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,romPoint.X,romPoint.Y,0);
                Invoke(socket,"OnMouseDown",romClick);
                Invoke(socket,"OnClick",EventArgs.Empty);
                Invoke(socket,"OnMouseUp",romClick);
            } finally { System.Windows.Forms.Cursor.Position = previousCursor; }
            WaitForSocket(form);
            Check(ReferenceEquals(board.Eprom,inserted) && !socket.Open && File.ReadAllBytes(path)[124] == 0x5A,"ROM click opens chooser; cancel keeps chip and saves pending changes");
            form.SelectEpromFile = type => path;
            socket.PerformClick(); WaitForSocket(form);
            Check(board.Eprom != null && board.Eprom.Closed,"open empty lever can select a replacement file");
            form.SelectEpromFile = type => "";
            socket.PerformClick(); WaitForSocket(form);
            Check(board.Eprom == null && !socket.Open,"Eject ROM removes chip and lowers lever");
            var socketTips = (System.Windows.Forms.ToolTip)Field(form,"tips");
            Check(socketTips.Active && socketTips.ShowAlways && socketTips.GetToolTip(socket) == "Lift lever to insert EP-ROM (File)","whole socket retains hover help after ejection");
            form.SelectEpromFile = type => null;
            board.Eprom = null; horizontal.Value = 1;
            Check(!socket.Pins28, "2732 automatically lowers pin-count flap");
            form.RefreshHardware();
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-socket-empty-24.png"));
            }
            socket.PerformClick(); WaitForSocket(form);
            horizontal.Value = 0; form.RefreshHardware();
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) {
                form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-socket-empty-28.png"));
            }
        } finally { board.Eprom = null; if (File.Exists(path)) File.Delete(path); }
    }
    static void BinaryIntegrationRegression()
    {
        using (var main = new MainForm())
        {
            main.ShowInTaskbar = false; main.Show(); main.Location = new System.Drawing.Point(-20000, -20000);
            Invoke(main, "SelectHardware", true);
            var editor = (System.Windows.Forms.RichTextBox)Field(main, "richTextBoxProgram");
            editor.Text = "; preserved ASM";
            var image = new byte[] { 0, 0x3E, 0x5A, 0, 0 };
            main.LoadBinaryImage(image, 0x0100, 0x0101);
            var cpu = (Assembler85)Field(main, "assembler85");
            Check(cpu.Hardware == Field(main, "pkwBoard") && cpu.registerPC == 0x0101 && editor.Text == "; preserved ASM", "BIN loader binds PKW and preserves ASM with nonzero entry address");
            Check(((System.Windows.Forms.TextBox)Field(main,"tbSetProgramCounter")).Text == "0101" && main.CreateBinaryImage().SequenceEqual(image), "BIN roundtrip preserves load extent and leading/trailing zero bytes");
            Check(main.DisassembleMemory(0x0101).Contains("MVI A") && main.DisassembleMemory(0x0101).Contains("5AH"), "loaded bytes are disassembled without source");
            Invoke(main, "startStep_Click", main, EventArgs.Empty);
            Check(cpu.registerA == 0x5A && cpu.registerPC == 0x0103, "Step executes loaded BIN on shared CPU");
            Invoke(main, "startRun_Click", main, EventArgs.Empty);
            ((System.Windows.Forms.Timer)Field(main,"timer")).Interval = 60000;
            Invoke(main, "TimerEventProcessor", Field(main,"timer"), EventArgs.Empty);
            Check(((System.Windows.Forms.Timer)Field(main,"timer")).Enabled && cpu.registerPC == 0x0104, "Run does not treat missing source mapping as a breakpoint");
            Invoke(main, "stop_Click", main, EventArgs.Empty);
            bool rejected = false;
            try { main.LoadBinaryImage(new byte[2], 0xFFFF, 0xFFFF); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && ReferenceEquals(cpu, Field(main,"assembler85")) && main.CreateBinaryImage().SequenceEqual(image), "oversized BIN rejected without changing CPU or memory");
            cpu.RAM[0xFFFF] = 0xC3; cpu.RAM[0] = 0x34; cpu.RAM[1] = 0x12;
            Check(main.DisassembleMemory(0xFFFF).Contains("JMP 1234H"), "live disassembler wraps operands at FFFF");
            editor.Text = "ORG 200H\nDB 0,0\nORG 100H\nNOP\nDS 3\nEND";
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            cpu = (Assembler85)Field(main,"assembler85");
            Check(cpu.firstAddress == 0x100 && cpu.lastAddress == 0x201 && main.CreateBinaryImage().Length == 0x102 && main.CreateBinaryImage().All(b => b == 0), "BIN export includes zero bytes, reserved bytes and reverse ORG ranges");
            editor.Text = "ORG 100H\nNOP\nDS 3\nEND";
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            Check(main.CreateBinaryImage().Length == 4, "BIN export retains trailing reserved zero bytes");
            Invoke(main, "SelectHardware", false);
            main.LoadBinaryImage(image, 0x0100, 0x0101);
            cpu = (Assembler85)Field(main,"assembler85");
            Check(cpu.Hardware == null, "BIN loading in SDK mode does not attach PKW hardware");
        }
    }
    static void MainWindowRegression(string romPath)
    {
        using (var main = new MainForm())
        {
            main.ShowInTaskbar = false; main.Show();
            main.Location = new System.Drawing.Point(-20000, -20000);
            var editor = (System.Windows.Forms.RichTextBox)Field(main,"richTextBoxProgram");
            editor.Text = "; Keep my ASM source";
            var sourceFileField = typeof(MainForm).GetField("sourceFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            sourceFileField.SetValue(main, "my-program.asm");
            int prompts = 0, replacements = 0;
            main.ConfirmFirmwareSource = () => { prompts++; return false; };
            main.ConfirmReplaceSource = () => { replacements++; return false; };
            var firmwareMenu = (System.Windows.Forms.ToolStripMenuItem)Field(main,"pkwMenu");
            firmwareMenu.PerformClick();
            Check(prompts == 1 && replacements == 0 && Field(main,"assembler85") == null && editor.Text == "; Keep my ASM source", "declining firmware leaves ASM intact without overwrite prompt");
            Invoke(main,"SelectHardware",false);
            main.ConfirmFirmwareSource = () => { prompts++; return true; };
            firmwareMenu.PerformClick();
            Check(replacements == 1 && editor.Text == "; Keep my ASM source" && (string)sourceFileField.GetValue(main) == "my-program.asm" && Field(main,"assembler85") == null, "cancelling overwrite preserves source and save path");
            Invoke(main,"SelectHardware",false);
            main.ConfirmReplaceSource = () => { replacements++; return true; };
            firmwareMenu.PerformClick();
            string firmware = MainForm.ReadAdaptedFirmwareSource();
            var expected = new Assembler85(firmware.Replace("\r", "").Split('\n'));
            Check(expected.FirstPass() == "OK" && expected.SecondPass() == "OK", "bundled adapted firmware assembles");
            var firmwareCpu = (Assembler85)Field(main,"assembler85");
            Check(replacements == 2 && editor.Text.Replace("\r", "") == firmware.Replace("\r", "") && firmwareCpu.RAM.SequenceEqual(expected.RAM), "confirmed firmware replaces editor and assembles matching bytes");
            Check((string)sourceFileField.GetValue(main) == "" && firmwareCpu.registerPC == 0 && firmwareCpu.Hardware == Field(main,"pkwBoard"), "firmware starts on shared hardware and detaches previous save path");
            Invoke(main,"startStep_Click",main,EventArgs.Empty);
            Check(firmwareCpu.cycles > 0, "adapted firmware supports main-window Step");
            Invoke(main,"SelectHardware",false);
            editor.Clear();
            firmwareMenu.PerformClick();
            Check(replacements == 2 && Field(main,"assembler85") != null && editor.Text.Contains("Hellorld!"), "empty editor loads adapted firmware without overwrite prompt");
            Invoke(main, "SelectHardware", true);
            var source = (System.Windows.Forms.RichTextBox)Field(main, "richTextBoxProgram");
            source.Text = "ORG 0\nMVI A,03H\nOUT 68H\nMVI A,08H\nOUT 6AH\nMVI A,76H\nOUT 69H\nLOOP: JMP LOOP\nEND";
            string originalSource = source.Text;
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            var cpu = (Assembler85)Field(main, "assembler85");
            Check(cpu.Hardware == Field(main, "pkwBoard"), "main assembler CPU owns selected PKW hardware");
            for (int i = 0; i < 6; i++) Invoke(main, "startStep_Click", main, EventArgs.Empty);
            Check(((Pkw3000Hardware)cpu.Hardware).DisplaySegments(0) == 0x76, "main Step executes assembled ASM through PKW ports");
            var breakpointField = typeof(MainForm).GetField("lineBreakPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            breakpointField.SetValue(main, 7); // LOOP, target of the very next JMP.
            ulong beforeFast = cpu.cycles;
            Invoke(main, "startFast_Click", main, EventArgs.Empty);
            Check(cpu.cycles == beforeFast + 10 && cpu.registerPC == 12, "batched Fast stops at exact breakpoint instruction");
            var realtimeButton = (System.Windows.Forms.ToolStripButton)Field(main, "realtimeButton");
            Check(realtimeButton.Visible && realtimeButton.Enabled, "Realtime available for assembled PKW");
            beforeFast = cpu.cycles;
            realtimeButton.PerformClick();
            Check(cpu.cycles == beforeFast + 10 && cpu.registerPC == 12, "Realtime stops at exact breakpoint");
            breakpointField.SetValue(main, -1);
            var front = (FormPkw3000)Field(main, "pkwWindow");
            var vertical = (PhotoSelector)Field(front, "selectorVertical");
            var horizontal = (PhotoSelector)Field(front, "selectorHorizontal");
            for (int v = 0; v < 3; v++) for (int h = 0; h < 3; h++)
            {
                vertical.Value = v; horizontal.Value = h;
                Check(((Pkw3000Hardware)cpu.Hardware).SwitchInputs == ((3 - v) << 4 | (3 - h) << 2),
                    "front panel selector wiring " + v + "/" + h);
            }
            var leftButton = System.Windows.Forms.MouseButtons.Left;
            Invoke(vertical, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(leftButton, 1, vertical.Width / 2, vertical.Height - 1, 0));
            Invoke(vertical, "OnMouseMove", new System.Windows.Forms.MouseEventArgs(leftButton, 0, vertical.Width / 2, 0, 0));
            Invoke(vertical, "OnMouseUp", new System.Windows.Forms.MouseEventArgs(leftButton, 1, vertical.Width / 2, 0, 0));
            Check(vertical.Value == 2 && !vertical.Capture, "photo S1 drag reaches upper detent and releases capture");
            Invoke(horizontal, "OnMouseDown", new System.Windows.Forms.MouseEventArgs(leftButton, 1, 0, horizontal.Height / 2, 0));
            Invoke(horizontal, "OnMouseMove", new System.Windows.Forms.MouseEventArgs(leftButton, 0, horizontal.Width - 1, horizontal.Height / 2, 0));
            Invoke(horizontal, "OnMouseUp", new System.Windows.Forms.MouseEventArgs(leftButton, 1, horizontal.Width - 1, horizontal.Height / 2, 0));
            Check(horizontal.Value == 2 && !horizontal.Capture, "photo S2 drag reaches right detent and releases capture");
            Invoke(horizontal, "OnKeyDown", new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Left));
            Check(horizontal.Value == 1 && ((Pkw3000Hardware)cpu.Hardware).SwitchInputs == 0x18,
                "photo selector keyboard input snaps to middle and updates hardware");
            var reset = (System.Windows.Forms.Button)FindKey(front, "RST");
            string resetChipPath = Path.Combine(Path.GetTempPath(), "pkw-reset-" + Guid.NewGuid().ToString("N") + ".bin");
            var resetChip = PkwEprom.Open(resetChipPath, 2); resetChip.Closed = true; resetChip.Program(17, 0x5A);
            ((Pkw3000Hardware)cpu.Hardware).Eprom = resetChip;
            ((Pkw3000Hardware)cpu.Hardware).BufferRam[123] = 0xA5;
            cpu.RAM[0x6090] = 0xC7;
            reset.PerformClick();
            Check(ReferenceEquals(((Pkw3000Hardware)cpu.Hardware).Eprom, resetChip) && resetChip.Closed && resetChip.Data[17] == 0x5A,
                "CPU reset preserves inserted chip, clamp state and unsaved bytes");
            ((Pkw3000Hardware)cpu.Hardware).Eprom = null; File.Delete(resetChipPath);
            Check(cpu.registerPC == 0 && cpu.cycles == 0 && cpu.RAM[0x6090] == 0xC7 &&
                ((Pkw3000Hardware)cpu.Hardware).BufferRam[123] == 0xA5 &&
                ((System.Windows.Forms.ToolStripButton)Field(main, "toolStripButtonFast")).Enabled,
                "paused hardware reset resets CPU without erasing RAM or starting execution");
            Invoke(main, "startRun_Click", main, EventArgs.Empty);
            reset.PerformClick();
            Check(((System.Windows.Forms.Timer)Field(main, "timer")).Enabled, "hardware reset preserves Run timer");
            Invoke(main, "stop_Click", main, EventArgs.Empty);
            bool resetDuringFast = false;
            var resetWatch = System.Diagnostics.Stopwatch.StartNew();
            using (var resetTimer = new System.Windows.Forms.Timer { Interval = 15 })
            {
                resetTimer.Tick += (s, e) => {
                    if (!resetDuringFast && cpu.cycles >= 100000) { reset.PerformClick(); resetDuringFast = true; }
                    else if (cpu.cycles >= 100000 || resetWatch.ElapsedMilliseconds > 5000)
                    { resetTimer.Stop(); Invoke(main, "stop_Click", main, EventArgs.Empty); }
                };
                resetTimer.Start(); Invoke(main, "startFast_Click", main, EventArgs.Empty);
            }
            Check(resetDuringFast && cpu.cycles >= 100000, "Fast continues executing after front-panel reset");
            foreach (bool resetRealtime in new[] { false, true })
            {
                bool didReset = false;
                ulong origin = cpu.cycles;
                var realtimeWatch = System.Diagnostics.Stopwatch.StartNew();
                double resetAt = 0;
                using (var stopRealtime = new System.Windows.Forms.Timer { Interval = 15 })
                {
                    stopRealtime.Tick += (s, e) => {
                        if (resetRealtime && !didReset && realtimeWatch.ElapsedMilliseconds >= 300)
                        { reset.PerformClick(); origin = 0; resetAt = realtimeWatch.Elapsed.TotalSeconds; didReset = true; }
                        if (realtimeWatch.ElapsedMilliseconds >= 1300)
                        { realtimeWatch.Stop(); stopRealtime.Stop(); Invoke(main, "stop_Click", main, EventArgs.Empty); }
                    };
                    stopRealtime.Start(); realtimeButton.PerformClick();
                }
                double elapsed = realtimeWatch.Elapsed.TotalSeconds - resetAt;
                double mhz = (cpu.cycles - origin) / elapsed / 1000000;
                Check(mhz > 2.7 && mhz < 3.15 && (!resetRealtime || didReset),
                    "Realtime runs near 3 MHz, reset=" + resetRealtime + ": " + mhz.ToString("F3") + " MHz");
                Check(realtimeButton.Enabled, "Stop re-enables Realtime");
            }
            var terminalCheck = (System.Windows.Forms.CheckBox)Field(main, "chkTerminal"); terminalCheck.Checked = true;
            var terminal = (FormTerminal)Field(main, "formTerminal");
            Check(terminal.BaudRate == 4800, "existing terminal opens at PKW baud rate");
            Check(terminal.ControlBox && terminal.MinimizeBox && terminal.MaximizeBox && terminal.FormBorderStyle == System.Windows.Forms.FormBorderStyle.Sizable,"terminal has standard resizable window controls");
            var pending = (System.Windows.Forms.TextBox)Field(terminal, "tbKeyBuffer");
            Check(pending.ReadOnly && !pending.TabStop, "Pending TX is a read-only status field");
            var enterMessage = System.Windows.Forms.Message.Create(terminal.tbTerminal.Handle,
                0x0100, (IntPtr)System.Windows.Forms.Keys.Enter, IntPtr.Zero);
            bool enterHandled = terminal.tbTerminal.PreProcessMessage(ref enterMessage);
            Check(enterHandled && terminal.keyBuffer == "\r" && terminal.tbTerminal.Text == "",
                "Enter is consumed before native RichEdit handling and queues exactly one CR");
            terminal.Clear();
            terminal.ReadClipboardText = () => "X4\r\n";
            var command = typeof(TerminalTextBox).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool handled = (bool)command.Invoke(terminal.tbTerminal, new object[] { new System.Windows.Forms.Message(), System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.V });
            Check(handled && terminal.keyBuffer == "X4\r" && terminal.tbTerminal.Text == "", "Ctrl+V sends clipboard exactly once without locally pasting or appending SYN");
            var caps = new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.CapsLock);
            terminal.HandleInputKeyDown(caps);
            Invoke(terminal, "tbTerminal_KeyPress", terminal.tbTerminal, new System.Windows.Forms.KeyPressEventArgs('\x14'));
            Check(caps.SuppressKeyPress && terminal.keyBuffer == "X4\r", "Caps Lock and unexpected control characters add no serial byte");
            Check(pending.Text == "3: X4<CR>", "Pending TX displays CR as readable text");
            Invoke(main, "UpdateTerminal");
            Check(pending.Text == "3: X4<CR>", "Pending TX includes bytes waiting inside PKW hardware");
            terminal.AppendOutput("A\bB\r"); terminal.AppendOutput("\n");
            Check(terminal.tbTerminal.Text.Replace("\r\n", "\n") == "B\n", "received backspace and split CR LF do not produce rectangles or duplicate newlines");
            Check(((Pkw3000Hardware)cpu.Hardware).QueuedInput == 3, "existing terminal input reaches PKW RX");
            terminal.Close(); Check(!terminalCheck.Checked, "closing terminal synchronizes checkbox");
            var menu = (System.Windows.Forms.MenuStrip)Field(main, "menuStrip");
            var toolbar = (System.Windows.Forms.ToolStrip)Field(main, "toolStrip");
            Check(menu.Bottom <= toolbar.Top, "hardware menu has its own unobstructed row");
            Check(toolbar.Right < ((System.Windows.Forms.Label)Field(main,"lblSetProgramCounter")).Left &&
                ((System.Windows.Forms.Button)Field(main,"btnViewProgram")).Right < ((System.Windows.Forms.CheckBox)Field(main,"chkSDK85")).Left,
                "Realtime toolbar and hardware checkbox do not overlap adjacent controls");
            using (var bitmap = new System.Drawing.Bitmap(main.Width, main.Height))
            {
                main.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, main.Width, main.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "main-pkw.png"));
            }
            Invoke(main, "SelectHardware", false);
            Check(Field(main, "assembler85") == null && source.Text == originalSource, "switch to SDK resets simulation and preserves ASM");
            Check(((System.Windows.Forms.CheckBox)Field(main, "chkSIDSOD")).Visible,
                "SDK-specific checkboxes restored");
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            Check(((Assembler85)Field(main, "assembler85")).Hardware == null, "SDK uses original CPU I/O path");
            main.Close();
        }
    }

    static void FastPerformance(string romPath)
    {
        foreach (bool terminalOpen in new[] { false, true }) using (var main = new MainForm())
        {
            main.ShowInTaskbar = false; main.Show(); main.Location = new System.Drawing.Point(-20000, -20000);
            Invoke(main, "SelectHardware", true);
            ((System.Windows.Forms.RichTextBox)Field(main, "richTextBoxProgram")).Text = "ORG 0\nJMP 0\nEND";
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            var cpu = (Assembler85)Field(main, "assembler85");
            Array.Copy(File.ReadAllBytes(romPath), cpu.RAM, 8192);
            ((System.Windows.Forms.CheckBox)Field(main, "chkTerminal")).Checked = terminalOpen;
            foreach (System.Windows.Forms.Form owned in main.OwnedForms) { owned.ShowInTaskbar = false; owned.Location = main.Location; }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var stop = new System.Windows.Forms.Timer { Interval = 15 })
            {
                stop.Tick += (s, e) => {
                    if (cpu.cycles >= 1000000 || watch.ElapsedMilliseconds > 60000)
                    { stop.Stop(); Invoke(main, "stop_Click", main, EventArgs.Empty); }
                };
                stop.Start(); Invoke(main, "startFast_Click", main, EventArgs.Empty); watch.Stop();
            }
            Check(cpu.cycles >= 1000000, "Fast reaches one million cycles; terminal=" + terminalOpen);
            Check(((Pkw3000Hardware)cpu.Hardware).TimerPulses > 100, "Fast preserves hardware timer");
            Console.WriteLine("PERF terminal={0} cycles={1} elapsed_ms={2} ms_per_million={3:F1}",
                terminalOpen, cpu.cycles, watch.ElapsedMilliseconds, watch.Elapsed.TotalMilliseconds * 1000000 / cpu.cycles);
            main.Close();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    static System.Windows.Forms.Control FindKey(System.Windows.Forms.Control parent, string text)
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            if (child is System.Windows.Forms.Button && child.Text == text) return child;
            var found = FindKey(child, text); if (found != null) return found;
        }
        return null;
    }
    static void TerminalEnter(FormTerminal terminal)
    {
        var message = System.Windows.Forms.Message.Create(terminal.tbTerminal.Handle, 0x100,
            (IntPtr)System.Windows.Forms.Keys.Enter, IntPtr.Zero);
        if (!terminal.tbTerminal.PreProcessMessage(ref message))
        {
            SendMessage(terminal.tbTerminal.Handle, 0x100, (IntPtr)13, IntPtr.Zero);
            SendMessage(terminal.tbTerminal.Handle, 0x102, (IntPtr)13, IntPtr.Zero);
        }
    }
    static void TerminalEndToEnd(string romPath, string sourcePath, bool physicalKeys)
    {
        using (var main = new MainForm())
        {
            main.ShowInTaskbar = false; main.Show(); main.Location = new System.Drawing.Point(-20000, -20000);
            Invoke(main, "SelectHardware", true);
            ((System.Windows.Forms.RichTextBox)Field(main, "richTextBoxProgram")).Text = sourcePath == null
                ? "ORG 0\nJMP 0\nEND" : File.ReadAllText(sourcePath);
            Invoke(main, "startDebug_Click", main, EventArgs.Empty);
            var cpu = (Assembler85)Field(main, "assembler85");
            if (sourcePath == null) Array.Copy(File.ReadAllBytes(romPath), cpu.RAM, 8192);
            var hw = (Pkw3000Hardware)cpu.Hardware;
            ((System.Windows.Forms.CheckBox)Field(main, "chkTerminal")).Checked = true;
            var terminal = (FormTerminal)Field(main, "formTerminal");
            foreach (System.Windows.Forms.Form owned in main.OwnedForms) { owned.ShowInTaskbar = false; owned.Location = main.Location; }
            var panel = (FormPkw3000)Field(main, "pkwWindow");
            var keys = new[] { "JOB", "E", "SET" };
            int stage = -2; ulong deadline = 3000000; long wallDeadline = 0; bool answered = false, driving = false; Exception failure = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (var drive = new System.Windows.Forms.Timer { Interval = 15 })
            {
                drive.Tick += (sender, e) => {
                    if (driving) return;
                    driving = true;
                    try
                    {
                        if (watch.ElapsedMilliseconds > 15000) throw new Exception("E2E timeout stage " + stage);
                        if (cpu.cycles < deadline || watch.ElapsedMilliseconds < wallDeadline) return;
                        if (stage < 0)
                        {
                            Invoke(FindKey(panel, "C"), stage == -2 ? "OnMouseDown" : "OnMouseUp",
                                new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 4, 4, 0));
                            stage++; deadline = cpu.cycles + 3000000; wallDeadline = watch.ElapsedMilliseconds + 1000; return;
                        }
                        if (stage == 0)
                        {
                            Check(hw.DisplaySegments(0) == 0x40 && hw.DisplaySegments(1) == 0x66,
                                "E2E slow mouse key restores dash/type display: " + (sourcePath ?? "original ROM"));
                            panel.RefreshHardware();
                            using (var bitmap = new System.Drawing.Bitmap(panel.Width, panel.Height))
                            {
                                panel.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, panel.Width, panel.Height));
                                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, sourcePath == null ? "display-rom.png" : "display-asm.png"));
                            }
                        }
                        if (stage < 6)
                        {
                            var key = FindKey(panel, keys[stage / 2]);
                            Invoke(key, stage % 2 == 0 ? "OnMouseDown" : "OnMouseUp",
                                new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 4, 4, 0));
                            stage++; deadline = cpu.cycles + 3000000; return;
                        }
                        if (stage == 6)
                        {
                            Check(terminal.tbTerminal.Text.Contains("*"), "E2E physical JOB E SET reaches terminal prompt: " + (sourcePath ?? "original ROM"));
                            if (physicalKeys) { terminal.Location = new System.Drawing.Point(100, 100); terminal.Activate(); terminal.tbTerminal.Focus(); System.Windows.Forms.SendKeys.SendWait("X"); }
                            else SendMessage(terminal.tbTerminal.Handle, 0x102, (IntPtr)'X', IntPtr.Zero);
                            stage++; deadline = cpu.cycles + 3000000; return;
                        }
                        if (stage == 7)
                        {
                            if (physicalKeys) System.Windows.Forms.SendKeys.SendWait("4");
                            else SendMessage(terminal.tbTerminal.Handle, 0x102, (IntPtr)'4', IntPtr.Zero);
                            stage++; deadline = cpu.cycles + 3000000; return;
                        }
                        if (stage == 8)
                        {
                            if (physicalKeys) System.Windows.Forms.SendKeys.SendWait("{ENTER}"); else TerminalEnter(terminal);
                            stage++; deadline = cpu.cycles + 6000000; return;
                        }
                        if (stage == 9)
                        {
                            Console.WriteLine("E2E reply=" + terminal.tbTerminal.Text.Replace("\r", "<CR>").Replace("\n", "<LF>") + " queued=" + hw.QueuedInput + " PC=" + cpu.registerPC.ToString("X4") + " portC2=" + hw.Ports[0xC2].ToString("X2"));
                            Check(terminal.tbTerminal.Text.Contains("X4   00  "), "E2E typed X4 + Enter produces ROM parameter response");
                            if (physicalKeys) System.Windows.Forms.SendKeys.SendWait("{ENTER}"); else TerminalEnter(terminal);
                            stage++; deadline = cpu.cycles + 3000000; return;
                        }
                        answered = terminal.tbTerminal.Text.TrimEnd().EndsWith("*") && hw.QueuedInput == 0 && terminal.keyBuffer.Length == 0;
                        drive.Stop(); Invoke(main, "stop_Click", main, EventArgs.Empty);
                    }
                    catch (Exception ex) { failure = ex; drive.Stop(); Invoke(main, "stop_Click", main, EventArgs.Empty); }
                    finally { driving = false; }
                };
                drive.Start(); Invoke(main, "startFast_Click", main, EventArgs.Empty);
            }
            if (failure != null) throw failure;
            Check(answered, "E2E second Enter returns to prompt and drains Pending TX");
            main.Close();
        }
    }

    static void Step(Assembler85 cpu)
    {
        ushort pc = cpu.registerPC;
        string error = cpu.RunInstruction(pc, ref pc);
        if (error.Length > 0) throw new Exception(error);
    }
    static void CpuRegression()
    {
        for (int condition = 0; condition < 8; condition++) for (int flags = 0; flags < 16; flags++)
        {
            var test = new Assembler85(new string[0]);
            test.flagZ = (flags & 1) != 0; test.flagC = (flags & 2) != 0;
            test.flagP = (flags & 4) != 0; test.flagS = (flags & 8) != 0;
            bool taken = new[] { !test.flagZ, test.flagZ, !test.flagC, test.flagC,
                !test.flagP, test.flagP, !test.flagS, test.flagS }[condition];
            test.RAM[0] = (byte)(0xC4 + condition * 8); test.RAM[1] = 0x34; test.RAM[2] = 0x12;
            test.registerSP = 0x6040; test.registerA = 0x55; Step(test);
            if (test.registerPC != (taken ? 0x1234 : 3) || test.registerSP != (taken ? 0x603E : 0x6040) ||
                test.registerA != 0x55 || test.cycles != (taken ? 18UL : 9UL) ||
                (taken && (test.RAM[0x603E] != 3 || test.RAM[0x603F] != 0)))
                throw new Exception("conditional CALL regression: condition=" + condition + " flags=" + flags);
        }
        Check(true, "all eight conditional CALLs: target, stack, accumulator and timing across 16 flag combinations");
        var cpu = new Assembler85(new string[0]);
        cpu.RAM[0] = 0xCC; cpu.RAM[1] = 0x34; cpu.RAM[2] = 0x12;
        cpu.registerA = 0x55; cpu.registerSP = 0x6040; cpu.flagZ = true; Step(cpu);
        Check(cpu.registerPC == 0x1234 && cpu.registerA == 0x55 && cpu.registerSP == 0x603E &&
            cpu.RAM[0x603E] == 3 && cpu.cycles == 18, "CZ taken calls and preserves A (inherited bug)");
        cpu.registerPC = 0; cpu.flagZ = false; ulong before = cpu.cycles; Step(cpu);
        Check(cpu.registerPC == 3 && cpu.cycles - before == 9, "CZ untaken takes 9 cycles");
        cpu.RAM[3] = 0xC0; cpu.flagZ = true; before = cpu.cycles; Step(cpu);
        Check(cpu.registerPC == 4 && cpu.cycles - before == 6, "RNZ untaken takes 6 cycles");
        cpu.RAM[4] = 0x2F; before = cpu.cycles; Step(cpu);
        Check(cpu.cycles - before == 4, "CMA takes 4 cycles");
        cpu.RAM[5] = 0x27; cpu.registerA = 0x9A; cpu.flagC = cpu.flagAC = false; Step(cpu);
        Check(cpu.registerA == 0 && cpu.flagC && cpu.flagZ && cpu.flagP, "DAA decimal carry and result flags");
        cpu.registerPC = 5; cpu.registerA = 0x42; cpu.flagC = cpu.flagAC = false; Step(cpu);
        Check(cpu.registerA == 0x42 && !cpu.flagC, "DAA preserves upper digit (inherited bug)");
        for (int a = 0; a < 100; a++) for (int b = 0; b < 100; b++) for (int carry = 0; carry < 2; carry++)
        {
            cpu.RAM[20] = 0xCE; cpu.RAM[21] = (byte)((b / 10) * 16 + b % 10); cpu.RAM[22] = 0x27;
            cpu.registerPC = 20; cpu.registerA = (byte)((a / 10) * 16 + a % 10); cpu.flagC = carry != 0;
            Step(cpu); Step(cpu); int expected = (a + b + carry) % 100;
            if (cpu.registerA != (expected / 10) * 16 + expected % 10 || cpu.flagC != (a + b + carry >= 100))
                throw new Exception("BCD addition failed for " + a + "+" + b + "+" + carry);
        }
        Check(true, "DAA: all 20000 valid BCD additions with/without carry");
        cpu.registerPC = 6; cpu.RAM[6] = 0xD3; cpu.RAM[7] = 0x42;
        cpu.registerA = 0xAB; Step(cpu); cpu.RAM[8] = 0xDB; cpu.RAM[9] = 0x42; cpu.registerA = 0; Step(cpu);
        Check(cpu.registerA == 0xAB && cpu.PORT[0x42] == 0xAB, "legacy flat IN/OUT without hardware remains available");
        cpu = new Assembler85(new string[0]) { Hardware = new Pkw3000Hardware() };
        cpu.RAM[0] = 0x34; cpu.RAM[0x100] = 0xFF; cpu.registerH = 1;
        Step(cpu);
        Check(cpu.RAM[0x100] == 0xFF && cpu.flagZ, "INR on ROM changes flags but cannot change ROM data");
    }
    static void SerialRegression()
    {
        foreach (int baud in new[] { 4800, 2400, 1200, 600, 300, 110 })
        {
            var hw = new Pkw3000Hardware { BaudRate = baud };
            ulong bit = Pkw3000Hardware.ClockHz / (ulong)baud;
            hw.WritePort(0xC2, 0xA0, 0); // inverted start bit, RTS blocks RX
            byte value = 0xA5;
            for (int i = 0; i < 8; i++) hw.WritePort(0xC2,
                (byte)(0x20 | ((value & (1 << i)) == 0 ? 0x80 : 0)), bit * (ulong)(i + 1));
            hw.WritePort(0xC2, 0x20, 9 * bit); hw.Advance(11 * bit);
            Check(hw.DrainOutput() == "\u00A5", "8N2 TX at " + baud + " baud");
            hw.QueueInput("A"); hw.Advance(12 * bit);
            Check(hw.QueuedInput == 1 && (hw.ReadPort(0xC2, 12 * bit) & 1) == 0, "RTS holds queued RX at " + baud);
            hw.WritePort(0xC2, 0, 12 * bit); hw.Advance(13 * bit);
            ulong start = 13 * bit;
            Check((hw.ReadPort(0xC2, start) & 1) == 1, "inverted RX start at " + baud);
            int received = 0;
            for (int i = 0; i < 8; i++)
                if ((hw.ReadPort(0xC2, start + (ulong)(i + 1) * bit + bit / 2) & 1) == 0) received |= 1 << i;
            Check(received == 'A', "8N2 RX bits at " + baud);
        }
    }
    static void EpromWorkflowAudit(string romPath)
    {
        foreach (byte switches in new byte[] { 0x1C, 0x3C, 0x24 }) {
            var hw = new Pkw3000Hardware { SwitchInputs = switches };
            var cpu = new Assembler85(new string[0]) { Hardware = hw };
            Array.Copy(File.ReadAllBytes(romPath), cpu.RAM, 8192); Run(cpu, 3000000);
            Console.WriteLine("SWITCH " + switches.ToString("X2") + " PC=" + cpu.registerPC.ToString("X4") + " type=" + cpu.RAM[0x6069]);
            Press(cpu, hw, "JOB"); Press(cpu, hw, "E"); Press(cpu, hw, "SET");
            string greeting = hw.DrainOutput();
            Check(switches == 0x1C ? cpu.RAM[0x6067] == 1 && greeting.Contains("PKW-3000") :
                cpu.RAM[0x6067] == 0 && cpu.RAM[0x6069] == 8 && greeting == "",
                "original ROM accepts commands only with valid selector combination " + switches.ToString("X2"));
            if (switches != 0x1C) {
                hw.SwitchInputs = 0x1C; Run(cpu, 3000000);
                Press(cpu, hw, "JOB"); Press(cpu, hw, "E"); Press(cpu, hw, "SET");
                Check(cpu.RAM[0x6067] == 1 && hw.DrainOutput().Contains("PKW-3000"), "valid selection restores keyboard without reset");
            }
        }
        string path = Path.Combine(Path.GetTempPath(), "pkw-workflow-" + Guid.NewGuid().ToString("N") + ".bin");
        try {
            byte[] bytes = Enumerable.Range(0, 8192).Select(i => (byte)(i * 37 + (i >> 8))).ToArray();
            File.WriteAllBytes(path, bytes);
            var chip = PkwEprom.Open(path, 0); chip.Closed = true;
            var hw = new Pkw3000Hardware { SwitchInputs = 0x1C, Eprom = chip };
            var cpu = new Assembler85(new string[0]) { Hardware = hw };
            Array.Copy(File.ReadAllBytes(romPath), cpu.RAM, 8192); Run(cpu, 3000000);
            Press(cpu, hw, "LOD"); Press(cpu, hw, "SET"); Run(cpu, 300000000);
            Check(hw.BufferRam.SequenceEqual(bytes), "LOD SET copies entire mounted 2764 into buffer through firmware ports");
            Press(cpu, hw, "JOB"); Press(cpu, hw, "E"); Press(cpu, hw, "SET"); hw.DrainOutput();
            hw.QueueInput("W0012\r"); Run(cpu, 6000000);
            string output = hw.DrainOutput(); Console.WriteLine("PROM W0012: " + output.Replace("\r", "<CR>").Replace("\n", "<LF>"));
            Check(output.Contains(bytes[0x12].ToString("X2")), "terminal W0012 reads selected BIN byte");
            hw.QueueInput("\r"); Run(cpu, 3000000); hw.DrainOutput();
            hw.QueueInput("L0012\r"); Run(cpu, 6000000);
            output = hw.DrainOutput(); Console.WriteLine("BUFFER L0012: " + output.Replace("\r", "<CR>").Replace("\n", "<LF>"));
            Check(output.Contains("0012  " + bytes[0x12].ToString("X2")), "loaded PROM contents remain available in RAM after LOD verification finishes");
        } finally { File.Delete(path); }
    }
    static void EpromRegression(string romPath)
    {
        string folder = Path.Combine(Path.GetTempPath(), "pkw-eprom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            byte[] selectors = { 0x1C, 0x2C, 0x18, 0x28, 0x38, 0x14, 0x34 };
            for (int type = 0; type < 7; type++) {
                string path = Path.Combine(folder, type + (type % 2 == 0 ? ".hex" : ".bin"));
                var chip = PkwEprom.Open(path, type); chip.Closed = true;
                Check(chip.Data.All(b => b == 255), PkwEprom.Names[type] + " new image is erased");
                var hw = new Pkw3000Hardware { SwitchInputs = selectors[type], Eprom = chip };
                var cpu = new Assembler85(new string[0]) { Hardware = hw };
                Array.Copy(File.ReadAllBytes(romPath), cpu.RAM, 8192); Run(cpu, 3000000);
                Check(cpu.RAM[0x6069] == type, "ROM selects " + PkwEprom.Names[type]);
                int address = chip.Data.Length - 1;
                cpu.registerD = (byte)(address >> 8); cpu.registerE = (byte)address; cpu.registerC = 0xA5;
                CallEpromRoutine(cpu, 0x10D8);
                Check(chip.Data[address] == 0xA5 && chip.Dirty, "original ROM programs last byte of " + PkwEprom.Names[type]);
                cpu.registerD = (byte)(address >> 8); cpu.registerE = (byte)address;
                CallEpromRoutine(cpu, 0x106A);
                Check(cpu.registerA == 0xA5, "original ROM reads " + PkwEprom.Names[type] + " through both comparators");
                chip.Program(address, 0xFF);
                Check(chip.Data[address] == 0xA5, "programming cannot restore erased bits");
                chip.Save();
                Check(!chip.Dirty && PkwEprom.Open(path, type).Data.SequenceEqual(chip.Data), "image survives save/reload " + Path.GetExtension(path));
                chip.Closed = false;
                cpu.registerD = (byte)(address >> 8); cpu.registerE = (byte)address;
                CallEpromRoutine(cpu, 0x106A);
                Check(cpu.registerA == 0xFF, "open lever disconnects chip");
                chip.Program(address, 0); Check(chip.Data[address] == 0xA5, "open lever blocks programming");
                chip.Closed = true;
                if (type == 6) {
                    hw.WritePort(0xC1, 0xC0, cpu.cycles); hw.WritePort(0xC1, 0xC2, cpu.cycles);
                    Check(chip.Data.All(b => b == 255), "48016 erase pulse erases whole chip");
                } else { chip.Erase(); Check(chip.Data[address] == 0xA5, "UV EPROM cannot be electrically erased"); }
            }
            string bad = Path.Combine(folder, "bad.hex");
            File.WriteAllText(bad, ":01000000A500\n:00000001FF\n");
            bool rejected = false; try { PkwEprom.Open(bad, 5); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "bad HEX checksum rejected without changing file");
            File.WriteAllText(bad, ":01080000A552\n:00000001FF\n");
            rejected = false; try { PkwEprom.Open(bad, 5); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "out-of-range HEX address rejected");
        } finally { Directory.Delete(folder, true); }
    }
    static void CallEpromRoutine(Assembler85 cpu, ushort routine)
    {
        cpu.intrIE = false; cpu.registerSP = 0x60F0;
        cpu.RAM[0x60F0] = 0; cpu.RAM[0x60F1] = 0x61; cpu.registerPC = routine;
        int i = 0; while (cpu.registerPC != 0x6100 && i++ < 2000000) Step(cpu);
        Check(cpu.registerPC == 0x6100, "ROM routine " + routine.ToString("X4") + " returns");
    }
    static void BufferRegression(Assembler85 cpu, Pkw3000Hardware hw)
    {
        // Execute the actual ROM put/get routines (RST1/RST2 targets) with a sentinel return.
        cpu.intrIE = false; cpu.registerH = 0x80; cpu.registerL = 0x12; cpu.registerA = 0xA5;
        cpu.registerSP = 0x60F0; cpu.RAM[0x60F0] = 0; cpu.RAM[0x60F1] = 0x61; cpu.registerPC = 0x03CE;
        for (int i = 0; i < 10000 && cpu.registerPC != 0x6100; i++) Step(cpu);
        Check(cpu.registerPC == 0x6100 && hw.BufferRam[0x12] == 0xA5, "ROM writes bit-addressed buffer RAM");
        cpu.registerA = 0; cpu.registerSP = 0x60F0; cpu.registerPC = 0x0403;
        for (int i = 0; i < 10000 && cpu.registerPC != 0x6100; i++) Step(cpu);
        Check(cpu.registerPC == 0x6100 && cpu.registerA == 0xA5, "ROM reads bit-addressed buffer RAM");
    }
    static void HelloRegression(Assembler85 cpu, Pkw3000Hardware hw, string source)
    {
        var assembled = new Assembler85(File.ReadAllLines(source));
        Check(assembled.FirstPass() == "OK" && assembled.SecondPass() == "OK", "existing hellorld.asm assembles");
        int end = Enumerable.Range(0x6080, 0x80).Where(i => assembled.RAMprogramLine[i] >= 0).Max() + 1;
        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hellorld-6080.bin"),
            assembled.RAM.Skip(0x6080).Take(end - 0x6080).ToArray());
        for (int i = 0x6080; i < 0x6100; i++)
            if (assembled.RAMprogramLine[i] >= 0) cpu.RAM[i] = assembled.RAM[i];
        cpu.registerPC = 0x6080; Run(cpu, 300000);
        byte[] expected = { 0x76, 0x79, 0x38, 0x38, 0x3F, 0x50, 0x30, 0x5E };
        Check(expected.SequenceEqual(Enumerable.Range(0, 8).Select(hw.DisplaySegments)), "existing hellorld.asm drives all eight digits");
    }
}
