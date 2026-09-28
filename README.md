# PKW-3000 Simulator

**PKW-Sim by toro / Codex**, based on the Intel 8085 assembler, disassembler and simulator by Dirk Prins.

This simulator is part of the [PKW-3000 project](https://github.com/sirtet/pkw-3000), which contains firmware sources, hardware documentation and research on the original EP-ROM programmer.

## Download and run

Download **pkw-sim-v0.1.zip** from the [releases page](https://github.com/sirtet/8085/releases). Extract the entire ZIP and run **pkw-3000-emulator.exe**.

Requires Windows with **.NET Framework 4.7.2 or later**. No installation is needed.

1. Select **Hardware > PKW-3000**.
2. Accept the prompt to load and assemble the included firmware source.
3. Click the green **RT** arrow to run at approximately **3 MHz**.

Lift the socket lever to insert or change an EP-ROM file. The file dialog also provides **Eject ROM**. Both **BIN** and **Intel HEX** files are supported.

Enable **Terminal** to use the built-in serial terminal. On the front panel, press **JOB**, **E**, **SET** to enter terminal mode.

For device operation and commands, see the [PKW-3000 User Manual](https://github.com/sirtet/pkw-3000/blob/main/PKW-3000_User_Manual_with_notes_OCR.pdf).

## Features

- Photographic front panel with interactive keys, switches, display and EP-ROM socket.
- Simulated EP-ROM reading and programming backed by files.
- Realtime execution, single stepping and breakpoints.
- Shared 8085 debugger with registers, memory and disassembly.
- CPU binary loading and export.
- The original assembler, disassembler and SDK-85 simulation remain available.

## Firmware and documentation

The included firmware source is based on Edgar’s disassembly of the original ROM, adapted for this assembler. It includes a **Hellorld!** terminal greeting and changed transfer defaults: **ASCII Hex Space** format and **S/X** start/end markers. Loading it asks for confirmation before replacing existing ASM text.

Implementation details and test notes are available in [PKW-3000 documentation](docs/PKW-3000.md) and [integration notes](docs/BRANCH-INTEGRATION.md).

Known limitation: the undocumented **DSUB** instruction’s AC/P flags remain unverified.

## Credits and license

Original 8085 Simulator by **Dirk Prins**.\
PKW-Sim by **toro / Codex**.

This fork adds PKW-3000 emulation and modifies parts of the original simulator. These changes are maintained independently of Dirk Prins.

Copyright (c) 2022 Dirk Prins

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
