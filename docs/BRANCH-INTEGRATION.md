# Branch integration — 2026-09-27

Integrated into the existing `feature/pkw-photo-panel` worktree. Other worktrees
were read only; no merge, reset, commit or push was performed there.

## Sources

- `fix/cpu-instructions` at `f6990eb`, changes relative to its upstream base
  `c46000a`: CPU corrections and independent instruction/flag audits.
- Uncommitted `main` worktree: 3 MHz Realtime execution, reset-aware pacing,
  execution-toolbar layout, hardware-menu placement and associated tests.
- Locally available `upstream/main` at `c46000a`: direct BIN loading menu and
  public disassembler API; binary export and live disassembly adapted here.

## Integration decisions

CPU memory writes retain `WriteMemory` and PKW ROM protection. EI is immediately
visible to RIM, while both PKW and SDK interrupt acceptance respect the delayed
enable state. Hardware HLT still waits for interrupts; the PC advances once.
Opcode handlers now contain the correct cycle counts; the old timing adjustment
after execution was removed. The unresolved DSUB AC/P behavior is unchanged.
See the donor's [CPU audit](../Tests/CPU-AUDIT.md) for scope and limitations.

Realtime uses the existing batched Fast loop, pacing to the 3 MHz clock and
rebasing time after a front-panel reset. It does not introduce another CPU.

Load Binary preserves the ASM editor/save path and attaches newly created CPUs to
the selected hardware. Existing RAM outside the loaded interval is retained;
overwritten addresses lose their stale source mapping. Invalid/empty/oversized
images are rejected before mutation. File and address-dialog cancellation leave
memory unchanged. Open Binary remains the disassemble-to-ASM path, with a new
save path for the resulting source instead of overwriting the input BIN.

Save Binary uses the minimum/maximum assembled or loaded address, including DS
and zero bytes. Bounds are independent of ORG ordering. Gaps are exported as their
current memory contents; the format does not carry an address. This replaces
upstream's final-location-counter/nonzero-byte heuristic.

The live Binary Code view shows 16 instructions at the next PC. It wraps operands
at FFFF and refreshes only at existing UI updates, without per-instruction text
history or additional `Application.DoEvents`. Closing it dismisses it until the
next load, assemble, New or simulator reset.

Older photo assets, removed COM-port code, local publish settings, prebuilt
upstream executables and unrelated version/resource changes were not imported.

## Validation

Build `Tests/CpuInstructionTests.csproj`, then run its executable with no arguments,
`--audit` and `--flags`. These cover 256 CALL/RET cases, 20,000 BCD additions,
8,192 opcode vectors and 1,327,931 flag/boundary cases. DSUB AC/P remain excluded.

Build `Tests/Pkw3000Tests.csproj`; run with the original ROM and `hellorld.asm`
paths plus `--ui`. This checks PKW EPROM/ROM protection, EI/HLT interrupt wakeup,
photo controls, file chooser, firmware-source loading, Realtime pacing/reset,
breakpoints, terminal, SDK switching and BIN load/export/disassembly boundaries.
The terminal workflow also supports `--terminal-e2e --firmware <pkw2_8085.asm>`.
