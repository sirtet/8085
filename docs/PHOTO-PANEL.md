# PKW photo panel

Worktree: `C:\Users\toro\source\repos\8085-photo-panel`

Branch: `feature/pkw-photo-panel`, based on `3205de7`.

This branch contains only the photo front panel, embedded image resources, and
its UI regression checks. Realtime, main-window layout and other work from the
concurrent task have not been copied. The previous shared working tree was left
as found; no further builds or edits from this task are made there.

The user requested the original generated panel from before the housing-rounding
experiment. `pkw-photo-base.png` is that exact image. `Prepare-PkwPhoto.py` inserts
only the empty socket and complete hinge from the later edited image and removes
the upper-right stain. The rest of the housing is retained; the script verifies
pixel identity of the bottom edge and unaffected upper housing. These source
assets are image-generation outputs, not a 3D reconstruction or geometric scan.

Run `python scripts/Prepare-PkwPhoto.py` with Pillow to reproduce the panel and
switch-cap sprites. The actual caps are cropped from `pkw-photo-original.jpg`.

Photo keys, switches and LED backgrounds paint their own photo region explicitly,
avoiding the WinForms transparency corruption reported by the user. The UI tests
check alignment at three window sizes, all nine selector combinations, dragging,
keyboard input, reset, and shared CPU display operation.

LED spacing: the PKW manual, printed page 1-9 (PDF page 17), Fig. 1-5, shows two
four-digit modules. COMMAND and DATA are sections of the same left module, not
separate modules with a gap. Both modules use a uniform 43-pixel digit pitch in
the 1536-pixel artwork coordinate system. The layout regression verifies equal
pitch (allowing one pixel of scaling roundoff) at three window sizes.
The local `tr7800.pdf`, page 13, Fig. 13, documents the TLR4135 four-digit display;
`Display-Module_TLR4135_from-tr7800.pdf.png` is the extracted diagram. This is the
TR-7800 service manual's component data, not a standalone mechanical datasheet.

Build/run the executable in this worktree's `Src/bin/Release`, not the old shared
checkout. Uncommitted changes already present in the shared checkout still need
coordination before integrating this branch; do not overwrite its whole project,
test or documentation files with this branch's versions.

## Virtual EPROM

The socket flap is up whenever the horizontal selector is left, including the
unsupported left/down combination. Otherwise it is down. 2764/2564 have 28 pins;
2732/2532/2732A/2716/48016 have 24 pins. Raising the flap exposes only two extra
contact pairs with an unlettered centre; the socket's outer top stays fixed.
The separate green clamp
lever opens on a click on the socket and presents a file picker. An existing
file is loaded; a new filename creates an erased, FF-filled chip. Closing the
picker automatically closes the lever and connects the chip. Right-click offers
file selection and removal. Cancelling the picker reconnects the previous chip;
a failed load also preserves it. The socket is disconnected while the picker is open.
The rendered moving parts reuse the accepted photograph and a locally integrated
Imagegen edit for the raised flap/lever; see [SOCKET-ARTWORK.md](SOCKET-ARTWORK.md).
The inserted chip is
a schematic drawing. This is not a mechanical 3D model.

BIN is a raw, zero-based image. Intel HEX supports checked data/EOF and extended
segment/linear addresses; data outside the chosen chip capacity is rejected.
Gaps and short BIN files are filled with FF. HEX output uses 16-byte records at
base address zero, suitable for the original firmware's Intel/Intellec HEX tape
format. The manual (PDF pages 83–84, Appendix 2) distinguishes that format from
the serial binary tape format, whose initial FF marker is **not** part of a raw
BIN file. Motorola, ASCII HEX SPACE and Tektronix tape formats are not image-file
formats in this picker.

Programming updates the mounted image in memory (EPROM bits can only change
from 1 to 0). Opening/removing/changing the image and closing the panel/application
save dirty data through a temporary file and atomic replacement. A failed save
leaves data dirty and cancels removal/closure. CPU reset keeps the chip and its
clamp state. Selecting another type disconnects a mismatched mounted chip;
return the selectors to its type or open the lever and select another image.
Existing files are not modified merely by loading them. HEX is normalized on
the first actual write (record layout/comments/start-address metadata are not
preserved). Saving a shortened BIN expands it to the chip's capacity.

The hardware model implements address/data ports A0–A3, C0's multiplexed digital
comparators, and C1's programming transitions. Control patterns are taken from
the original ROM table at 0D45; this is functional emulation, without analogue
voltage or pulse-width tolerances. The 48016 supports its electrical erase
sequence; UV EPROMs require a new blank image to represent an erased chip.

Regression tests execute the original ROM's read/program routines for all seven
types at their final address, check BIN/HEX save/reload, disconnected-chip behavior,
HEX checksum/range rejection, clamp interactions, selector-driven flap position,
and chip preservation over CPU reset. UI tests also exercise key OnPaint directly
after press/release with a poisoned buffer, without relying on background painting.

`Pkw3000Tests.exe <ROM.bin> --socket-audit` checks the complete original-firmware
workflow: select a 2764, LOD/SET, finish automatic comparison, enter terminal mode,
read a PROM byte with W0012 and the loaded buffer byte with L0012. R/Rn read serial
tape data, not the socket (manual sections 4-3-9/11/12). File selection inserts
the chip and automatically closes the lever. The window title explicitly reports the
open/disconnected state. Reading the chip does not change or save its image file.

Unsupported switch combinations produce type index 8 in the original ROM. Routine
00FC filters keycodes for index >=8; the simulator does not disable the key controls.
The audit reproduces that behavior for both unsupported combinations and verifies
that returning to a valid selection restores command entry without CPU reset.

LED character size was checked against PKW-3000_d780.jpg and hellorld-on-PKW-3000.jpg.
The lit outline is approximately 22 x 36 artwork pixels instead of 34 x 47,
about one third of the filter height. The 43-pixel pitch remains unchanged.
PhotoDigit paints tapered segments in floating-point artwork coordinates to
preserve their proportions and thin middle bars at smaller window sizes.
