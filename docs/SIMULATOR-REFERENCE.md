# Simulator reference

Detailed simulator instructions retained from the former in-app help.
The PKW-3000 hardware manual is linked from the application.

<Introduction>

This is an assembler/disassembler for the Intel-8085 microprocessor.
It can also simulate an Intel SDK-85 developers board (keyboard/display).

<Operations>

After loading a source file into the main window you can debug (assemble) it with the debugger button.
If the checkbox 'Insert Monitor' is checked it will also insert the monitor program version 2.1 (after assembly of the user program) at 0x0000 to 0x0800.

If the code doesn't start with an ORG directive the startaddress is set to 0000H.
The first error it encounters will be highlighted in light red.
Ajustments can be made in the main window (don't forget to save).
If no errors were encountered, the simulator is ready to run (or step) through the program and the first instruction to be executed will be highlighted in light green.
This focus line (in green) can be adjusted with the numeric entry at the top of the window.
This will set the number of lines displayed above the line to run (in green) if you step through the code.

Running will be with a delay of 50 ms between instructions (this can be adjusted in the main screen).

A breakpoint can be set by clicking with the mouse on the grey bar just left of the codewindow.
Only one breakpoint is supported, so by clicking this bar, the previous breakpoint will be deleted.

Setting the program counter to a specific address can be done by entering this address in the the 'set program counter' textbox and close with an enter.

At any time the memory and ports can be viewed in the right windows.
The memory window will follow the next instruction to be executed, this can be prevented with the 'Lock' checkbox.
The memory window can be easily set to the program counter or stack pointer with the designated buttons.

On the left all registers of the 8085 are shown.
Also the flags (including the undocumented ones, K(X5) and V) are displayed.

Above the memory window the interrupt register is shown.
Also the IE bit in this register is shown as a green (Enabled) or red (Disabled) label.

With the checkbox 'SDK-85' a keyboard/display of a SDK-85 can be shown/hidden.
This will simulate the SDK-85, the display will show what has been written to 0x1800 (remember to set the control address too: 0x1900).
The keyboard will fill address 0x1800 with the key value pressed (the key turns red and stays that way until it is handled).
Only the 'Reset' key and 'Vect Intr' key will act directly as an interrupt in the running program.

The status of the SID en SOD lines of the 8085 processor are visible as red/green leds.
Also a graphical representation is visible, timing is in micro seconds based on a 6.144 MHz crystal which makes a 3.072 MHz machine clock cycle.
Toggling of the SOD line is posible by left clicking the led (if the assembler is active).

A terminal screen is available to code typed data onto the SID line and decode data from the SOD line.
Remember that if the terminal screen is active on start of the monitor program the display/keyboard will be disabled (because the SID line is high, you could put it manually to low).

For timing of the SID and SOD line the number of cycles executed is available at the bottom of the screen.
You can reset the number of cycles with the button next to it.

A program can be exported to a (cassette tape) audio file (wav) with File->Export.
This will be according to the 'AP-29 Application Note', using a 3 kHz signal.

A program can be imported from an audio file (wav) with File->Import.
This will be according to the 'AP-29 Application Note', with an arbitrary signal frequency.

The two versions of the monitor program of the SDK-85 have been provided in the folder 'Monitor'.
Slight adjustments have been made to account for some of the directives used (e.g IF ENDIF and >>).
Also the source code of the 'AP-29 Application Note' has been provided.

Tweak: the monitor program will overwrite address 0x0000 at address 0x000F (PUSH PSW) because the stack pointer wasn't initialized.
This isn't a problem with the SDK-85 because you can't write at a ROM address.

<Assembler>

The assembler can use all of the instructions of the 8085 processor including the undocumented instructions.
All instructions are implemented by buttons on the left of the screen for reference and/or inserting the selected instruction into the program.
Numbers should be denoted in decimal, hexadecimal (0x00, $00 or 00H style) or binary (10101010B style).

The following directives can be used (example on next row(s)):

`ASEG` Use the location counter for the absolute program segment
`CSEG` Use the location counter for the code program segment
`DSEG` Use the location counter for the data program segment
Followed by and 'ORG' directive the assembler will set the segemnt to this address

`ORG address` Set locationcounter to this address (while assembling)
ORG 0000H

`LABEL EQU constant` Assignement of a constant
DSPLY EQU 1800H

`$` Current location counter (while assembling)
NUMC EQU $ - CMDTB ; NUMBER OF COMMANDS

`[LABEL] DB value` Reserving a (number of) byte(s) (with or without label), strings in double quotes will be terminated by a zero
STRING DB "SDK-85"
CHARARRAY DB 'SDK-85'
AT DB '@', 00H
DB 00H, 01H, 02H
DB 'A', 'B', 'C'

`[LABEL] DW value` Reserving a word (with or without label)
DW 0000H
CMDAD:
        DW      SSTEP   ; ADDRESS OF SINGLE STEP ROUTINE
        DW      EXAM    ; ADDRESS OF EXAMINE REGISTERS ROUTINE
        DW      SUBST   ; ADDRESS OF SUBSTITUTE MEMORY ROUTINE
        DW      GOCMD   ; ADDRESS OF GO ROUTINE

`[LABEL] DS number` Reserving 'number' of bytes

`LOW([LABEL])` will give the low byte of 2 bytes (usually an address)
`HIGH([LABEL])` will give the high byte of 2 bytes (usually an address)

`Arithmetic` e.g. +1, -2, *4, /2
USRBR EQU RAMST + 256 - (RMUSE + SKLN + UBRLN)

`Logical` AND, OR
FLAG & 01H
FLAG | 80H

<DisAssembler>

The disassembler will follow all possible paths from an entry address.
Additional paths can be provided.
As an option labels can be inserted for jump and call addresses.

<Menu>

`File->New`

Delete source file and reset simulator

`File->Open`

Load a new source file

`File->Import`

Import an audio file with a format according to the 'AP-29 Application Note'

`File->Export`

Export to an audio file with a format according to the 'AP-29 Application Note'

`File->Save`

Save the current source file

`File->Save As`

Save the current source file under a given name

`File->Save Binary`

Save the binary from assembling the current source

`File->Quit`

Quit program

`Reset->Reset RAM`

Clear RAM

`Reset->Reset Ports`

Clear Ports

`Reset->Reset Simulator`

Clear RAM, Ports, Registers, Flags

`DisAssembler->Open Binary`

Open a binary file for disassembly

`Help->Manual`

Show this manual

`Help->About`

Show program info


<PKW-3000 EP-ROM socket>

Select PKW-3000 from the Hardware menu and select the EP-ROM type with the two
front-panel switches. When switching to PKW-3000, the simulator asks whether to
load the adapted firmware source. Yes loads the bundled pkw2_8085.asm into the
ASM editor and assembles it. Then use Run, Fast or Step.
This version is based on Edgar's original-ROM disassembly, adapted for this
simulator. Changes: "Hellorld!" terminal greeting, ASCII Hex Space transfer
format (X6=4), and printable S/X start/end markers (X8=53, X9=58).
If the editor is not empty, a separate OK/Cancel warning asks before replacing
its contents. Cancel preserves the source. Saving the loaded firmware asks for
a new filename. No leaves firmware loading to your own ASM workflow.
The left selector column raises the 28-pin flap; the other
columns use the 24-pin position. Unsupported switch combinations are handled by
the original firmware and may prevent keyboard commands.

Click the lever or an inserted ROM to raise the lever. The native Windows file
dialog opens half a second later. After the dialog closes, the lever stays raised
for another half second before lowering. Repeated clicks during this sequence are ignored.
Neither click ejects the chip immediately. The empty socket body and flap are
inactive. Keyboard users can focus the lever with Tab and activate it with Space.

The current file is selected in the native file list. Its directory, filename
and BIN/HEX filter are restored. Choose another file, enter a new filename, or
click the additional Eject ROM button to remove the inserted chip. Eject ROM is
disabled when the socket is empty. Cancel keeps the previous chip.

BIN is a raw binary image; Intel HEX includes addresses and checksums.
Addresses start at 0000, unused bytes are FF, and images must fit the selected
chip. New files represent erased chips. Selecting, cancelling or ejecting lowers
the lever. Ejection leaves the socket empty. Hover help covers the entire socket.
The chip is disconnected
while the dialog is open. The selector must match the inserted chip.

Programming changes are saved before opening the dialog or closing the app.
Eject ROM removes the chip from the simulator; it does not delete the image file.
The title shows the file and connection state; an asterisk marks unsaved changes.
Reset preserves the inserted chip.

LOD followed by SET loads the EP-ROM into the programmer buffer. R/Rn refers to
serial import, not reading the inserted EP-ROM. BIN and Intel HEX are simulator
file formats; they do not select a firmware serial transfer protocol.

<Terminal window>

The terminal can be resized, minimized, maximized, restored and closed using the
standard title-bar controls. Closing it also clears the Terminal checkbox; use
that checkbox to reopen it. In PKW-3000 mode it opens at 4800 baud. Pending TX is
a read-only view of queued outgoing input. Enter sends a single carriage return;
pasting sends the clipboard text with normalized line endings.



<Realtime and binary programs>

Realtime runs the PKW-3000 at its nominal 3 MHz clock rate. Fast runs as fast as
the host allows; Run uses the selected instruction delay. Realtime supports
Stop, breakpoints and the front-panel reset. It is available only for PKW-3000.

File > Load Binary copies a BIN into CPU memory at the selected load address
and starts from the selected entry address. It preserves the ASM editor and
the selected hardware. This loads simulator memory, not the EP-ROM in the socket.
Cancel either dialog to keep memory unchanged. The Binary Code window shows a
bounded disassembly around the next instruction when no ASM source is available.
Closing this window dismisses it until the next load, assemble or simulator reset.

Open Binary keeps the existing disassemble-to-ASM workflow. Save Binary exports
from the lowest to the highest assembled/reserved or loaded address, including
leading/trailing zero bytes and gaps between ORG blocks. BIN contains no address
metadata: choose the corresponding load address when loading it again.
