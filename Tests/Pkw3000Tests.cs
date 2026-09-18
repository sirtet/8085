using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using _8085;

static class Pkw3000Tests
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
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
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pkw-window.png"));
                }
                form.Close();
                Check(true, "PKW hardware-only form renders shared CPU display");
            }
            if (args.Contains("--ui")) MainWindowRegression();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static object Field(object target, string name)
    { return target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(target); }
    static void Invoke(object target, string name, params object[] args)
    { target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(target, args); }
    static void MainWindowRegression()
    {
        using (var main = new MainForm())
        {
            main.ShowInTaskbar = false; main.Show();
            main.Location = new System.Drawing.Point(-20000, -20000);
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
            breakpointField.SetValue(main, -1);
            var front = (FormPkw3000)Field(main, "pkwWindow");
            var vertical = (System.Windows.Forms.TrackBar)Field(front, "selectorVertical");
            var horizontal = (System.Windows.Forms.TrackBar)Field(front, "selectorHorizontal");
            for (int v = 0; v < 3; v++) for (int h = 0; h < 3; h++)
            {
                vertical.Value = v; horizontal.Value = h;
                Check(((Pkw3000Hardware)cpu.Hardware).SwitchInputs == ((3 - v) << 4 | (3 - h) << 2),
                    "front panel selector wiring " + v + "/" + h);
            }
            var reset = (System.Windows.Forms.Button)FindKey(front, "RST");
            ((Pkw3000Hardware)cpu.Hardware).BufferRam[123] = 0xA5;
            cpu.RAM[0x6090] = 0xC7;
            reset.PerformClick();
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
            var terminalCheck = (System.Windows.Forms.CheckBox)Field(main, "chkTerminal"); terminalCheck.Checked = true;
            var terminal = (FormTerminal)Field(main, "formTerminal");
            Check(terminal.BaudRate == 4800, "existing terminal opens at PKW baud rate");
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
            using (var bitmap = new System.Drawing.Bitmap(main.Width, main.Height))
            {
                main.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, main.Width, main.Height));
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "main-pkw.png"));
            }
            Invoke(main, "SelectHardware", false);
            Check(Field(main, "assembler85") == null && source.Text == originalSource, "switch to SDK resets simulation and preserves ASM");
            Check(((System.Windows.Forms.CheckBox)Field(main, "chkSIDSOD")).Visible && !((System.Windows.Forms.CheckBox)Field(main, "chkCom")).Visible,
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

