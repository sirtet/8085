; This is based on the original disassembly by Edgar
; http://matthieu.benoit.free.fr/aval/PKW.ASM
; It has minor Syntax changes, needed to be used in Dirk's Simulator
; https://github.com/ForNextSoftwareDevelopment/8085

; Mainly  more comments to better understand the structure.
; Routines, workspace fields and branches use descriptive labels.
; The previous disassembly label is retained as a "was ..." comment at each definition.
; See ROM_ANALYSIS.md for the routine index and undocumented extension paths.
; Formatting: spaces only; labels at column 1, instructions at 11, operands at 21.
; EQU declarations: directive at column 35, value at 45; inline comments at 67.
; Long lines use one space before a trailing comment; quoted data is preserved.
;
; READING GUIDE
;   Entry points: START -> POWER_ON_DEFAULTS -> DISPATCH_CURRENT_MODE.
;   Panel: PANEL_COMMAND_LOOP -> DISPATCH_PANEL_COMMAND -> command handler.
;   Serial: COMMAND_PROMPT -> COMMAND_HANDLERS / ARGUMENT_HANDLERS -> handler.
;   Background: TIMER_INTERRUPT -> TIMER_SCAN_SERVICE -> display/key scanning.
;
;   Labels name addresses; reaching a label does not call or return by itself.
;   CALL pushes a return address; RET pops it; JMP transfers without pushing.
;   Fall-through means execution continues into the next labeled block.
;   A header frame is documentation only: it does not delimit executable code.
;   In/Out describe registers or memory; Z=zero flag, CY=carry, NC=no carry.
;   HL/DE/BC are register pairs; M means the byte at the address in HL.
;   PORT_* symbols are IN/OUT port numbers, not CPU memory addresses.
;   DB/DW declare bytes/words; DS reserves space; EQU defines a constant.
;   DB immediately after RST_TX_STRING or RST_PROM_CONTROL is inline data:
;   the service advances the return address past it. Do not execute it as code.
;   "was ..." preserves the old disassembly label, not necessarily today's address.
;   Manual references use section numbers and PDF viewer pages (1-based).
;   Manual: PKW-3000_User_Manual_with_notes_OCR.pdf; findings: ROM_ANALYSIS.md.
;

;------------------------------------------------------------------------------
; Original Disassembly:
; DIS8080 V1.02 29.10.1983
; Disassembler Invoked by : PKW-3000.bin -O0 -MOD85
; Reading from PKW-3000.BIN (2000H Bytes), writeing to PKW-3000.ASM
;------------------------------------------------------------------------------

;------------------------------------------------------------------------------
; RST-FUNCTIONS
;------------------------------------------------------------------------------
RST_RESET:                        EQU       0                     ; Reset vector; not used by an RST instruction in this source | was FUN0
RST_MEM_WRITE:                    EQU       1                     ; Write A to memory at HL | was RSTPUT
RST_MEM_READ:                     EQU       2                     ; Read memory at HL into A | was RSTGET
RST_RX_RAW:                       EQU       3                     ; Receive an unfiltered serial byte into A | was FUN3
RST_PROM_CONTROL:                 EQU       4                     ; Apply inline PROM control-table index | was FUN4
RST_TX_STRING:                    EQU       5                     ; Transmit inline string; bit 7 marks its last byte | was FUN5
RST_RX_ASCII:                     EQU       6                     ; Receive 7-bit ASCII; skip NUL and DEL | was RSTRX
RST_WAIT_KEY:                     EQU       7                     ; Wait for and decode a keyboard event | was FUN7
;------------------------------------------------------------------------------
; I/O PORTS
; 8155
;------------------------------------------------------------------------------
PORT_DISPLAY_TIMER_COMMAND:       EQU       068H                  ; COMMAND/STATUS | was PIO6CMD
PORT_LED_SEGMENTS:                EQU       069H                  ; PA         Display Output | was PLEDSEG
PORT_DISPLAY_KEY_BEEPER:          EQU       06AH                  ; PB         Disp.-Nr SELECT / Key-Col SELECT / Beeper | was PDKSELB
PORT_KEYBOARD_ROWS:               EQU       06BH                  ; PC 0..5    Key-Rows INPUT | was PKROWIN
PORT_TIMER_LSB:                   EQU       06CH                  ; TIMER LOW      set to 70H | was PTIMLOW
PORT_TIMER_MSB:                   EQU       06DH                  ; TIMER HIGH            D7H | was PTIMHIG
; *************************************************
; * PORT 68H (READ-WRITE) COMMAND/STATUS
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- 8155 COMMAND/STATUS-REGISTER SEE DATASHEET
;   1     1     0     0     0     0     1     1        boot command C3H (not SIM mask 5BH)
;   |     |     |     |     |     |     |     |
;   |     |     |     |     |     |     |     |------- Port A        : OUT (LEDSEG)
;   |     |     |     |     |     |     |------------- Port B        : OUT (Disp./Key-sel, beep, etc.)
;   |     |     |     |     A-----A------------------- Port C Mode   : ALT1 (all inputs)
;   |     |     |     |------------------------------- PA interrupt  : disabled
;   |     |     |------------------------------------- PB interrupt  : disabled
;   T-----T------------------------------------------- Timer Command : START (11b)
;   Timer registers: D770H = mode 11b (continuous terminal-count pulses) + 1770H (6000).
;   6 MHz crystal -> 8085 CLKOUT/CPU_CLK = 3 MHz -> one pulse every 2 ms (500 Hz).
;   The upper two bits are mode bits, not count bits; this is not a 55152-tick square wave.
;   Sources: Intel8155.pdf pp.4,6,7; PKW_3000_CIRCUIT_DIAG.pdf sheet 2.
;
; *************************************************
; * PORT 69H (READ-WRITE) 7 SEGMENT DATA
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |
;   |     |     |     |     |     |     |     |------- SEGMENT A DATA
;   |     |     |     |     |     |     |------------- SEGMENT B DATA
;   |     |     |     |     |     |------------------- SEGMENT C DATA
;   |     |     |     |     |------------------------- SEGMENT D DATA
;   |     |     |     |------------------------------- SEGMENT E DATA
;   |     |     |------------------------------------- SEGMENT F DATA
;   |     |------------------------------------------- SEGMENT G DATA
;   |------------------------------------------------- SEGMENT DP DATA
;   ---a---
;  |       |
;  f       b
;  |       |
;   ---g---
;  |       |
;  e       c
;  |       |
;   ---d---
;           dp (decimal point, normally bottom-right)
; PKW-3000 dp Position varies between digits. See Manual, LED-Test, p.3-25(pdf p.49)
;
; *************************************************
; * PORT 6AH (READ-WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |
;   |     |     |     |     |     S-----S-----S------- DISPLAY SELECT 0..7
;   |     |     |     |     |------------------------- DISPLAY SELECT CS_LOW
;   |     |     |     |------------------------------- DISPLAY SELECT CS_HIGH
;   |     |     |------------------------------------- BEEPER
;   |     |------------------------------------------- ENCS, ENABLE DRIVE_A AND DRIVE_B
;   |------------------------------------------------- HDIG, HIGH CURRENT ON DIG2
; *************************************************
; * PORT 6BH (READ)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |
;   |     |     |     |     R-----R-----R-----R------- KEYBOARD ROW LINE INPUT
;   |-----|-----|-----|------------------------------- UNUSED
; *************************************************
; * PORT 6CH (READ-WRITE) TIMER LOW
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- 8155 TIMER LOW REGISTER
; *************************************************
; * PORT 6DH (READ-WRITE) TIMER HIGH
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- 8155 TIMER HIGH REGISTER
;------------------------------------------------------------------------------
; DYNAMIC RAM
;------------------------------------------------------------------------------
PORT_DRAM_DATA_CONTROL:           EQU       080H                  ; bit 0: data out; bit 1: 1=read, 0=write | was P80RAM
PORT_DRAM_ADDRESS_LSB:            EQU       081H                  ; DRAM bit-address register, low byte (bits 0..7) | was P81RAM
PORT_DRAM_ADDRESS_MSB:            EQU       082H                  ; DRAM bit-address register, high byte (bits 8..15) | was P82RAM
PORT_DRAM_DATA_IN:                EQU       083H                  ; bit 0: DRAM data input; eight reads form one byte | was P83RAM
; *************************************************
; * PORT 80H (WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |
;   |     |     |     |     |     |     |     |------- DRAM DATA
;   |     |     |     |     |     |     |------------- DRAM WE    1=READ, 0=WRITE
;   |-----|-----|-----|-----|-----|------------------- X
; *************************************************
; * PORT 81H (WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- DRAM ADDRESS REGISTER LSB
;
; *************************************************
; * PORT 82H (WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- DRAM ADDRESS REGISTER MSB
; *************************************************
; * PORT 83H (READ)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |
;   |     |     |     |     |     |     |     |------- DRAM DATA, SINGLE BIT MUST BE EXPANDED
;   |-----|-----|-----|-----|-----|-----|------------- X
;------------------------------------------------------------------------------
; 8255
;------------------------------------------------------------------------------
; PROM address pair:
;   PORT_PROM_ADDRESS_LSB (A1H) supplies A0..A7.
;   PORT_PROM_ADDRESS_MUX (A2H) is the shared HIGH-address register:
;     bits 0..4 -> A8..A12; bits 5..7 -> analog multiplexer channel.
;   It is not a full 8-bit address MSB. WRITE_PROM_DATA_ADDRESS combines
;   the address with cached mux bits; READ_PROM_LOGIC_LEVELS cycles the mux.
PORT_PROM_DATA:                   EQU       0A0H                  ; was PEPRDAT
PORT_PROM_ADDRESS_LSB:            EQU       0A1H                  ; was PEPRADR
PORT_PROM_ADDRESS_MUX:            EQU       0A2H                  ; bits 0..4: address A8..A12; bits 5..7: analog mux | was PEPRAD2
PORT_PROM_PPI_CONTROL:            EQU       0A3H                  ; 8255 mode control for PROM data/address ports | was PIOACMD
; *************************************************
; * PORT A0H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- EPROM DATA D0..D7
; *************************************************
; * PORT A1H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- EPROM ADDRESS A0..A7
; *************************************************
; * PORT A2H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |-----|-----|-----|-----|------- EPROM ADDRESS A8..A12
;   |-----|-----|------------------------------------- ANALOG MUX SELECT
; *************************************************
; * PORT A3H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- 8255 COMMAND/STATUS REGISTER
;------------------------------------------------------------------------------
; 8255
;------------------------------------------------------------------------------
PORT_SWITCHES_COMPARATORS:        EQU       0C0H                  ; PROM selector switches and logic-level comparator inputs | was SWSTAT
; corrected, from schematic:
; S1 In Down   Position A=1, B=1
; S1 In Middle Position A=0, B=1
; S1 In Upper  Position A=1, B=0

; S2 In Left   Position A=1, B=1
; S2 In Middle Position A=0, B=1
; S2 In Right  Position A=1, B=0

PORT_PROM_CONTROL:                EQU       0C1H                  ; was EPCTRL
PORT_SERIAL_SIGNALS:              EQU       0C2H                  ; RXD/CTS/DSR inputs and TXD/handshake outputs | was RS232
PORT_CONTROL_PPI_MODE:            EQU       0C3H                  ; 8255 mode control for switches, PROM control and serial signals | was IOCCMD
; *************************************************
; * PORT C0H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |-----|------- LEVEL COMPARATOR SATTE
;   |     |     |     |     |-----|------------------- SWITCH S2 STATE (PA2=A, PA3=B; schematic sheet 2)
;   |     |     |-----|------------------------------- SWITCH S1 STATE (PA4=A, PA5=B; schematic sheet 2)
;   The earlier comment reversed S1/S2. These names follow the schematic nets.
;   Physical front-panel labeling has not been independently checked on the device.
;   |-----|------------------------------------------- UNUSED
; *************************************************
; * PORT C1H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |------- SIGNAL DA DRIVE
;   |     |     |     |     |     |     |------------- SIGNAL DB DRIVE
;   |     |     |     |     |     |------------------- SIGNAL DA & DB ENABLE
;   |     |     |     |     |------------------------- EPROM DATA PULLUP CONTROL
;   |     |     |-----|------------------------------- VPP VOLTAGE CONTROL
;   |-----|------------------------------------------- VCC VOLTAGE CONTROL
; *************************************************
; * PORT C2H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |     |     |     |     |     |     |     |------- RS232 RXD INPUT
;   |     |     |     |     |     |     |------------- RS232 CTS INPUT
;   |     |     |     |     |     |------------------- RS232 DSR INPUT
;   |     |     |     |     |------------------------- OPTION JUMPER
;   |     |     |     |------------------------------- RS232 DTR TTL OUT
;   |     |     |------------------------------------- RS232 RTS OUT
;   |     |------------------------------------------- RS232 DTR OUT
;   |------------------------------------------------- RS232 TXD OUT
; *************************************************
; * PORT C3H (READ/WRITE)
; *************************************************
; *  7  *  6  *  5  *  4  *  3  *  2  *  1  *  0  *
; *************************************************
;   |-----|-----|-----|-----|-----|-----|-----|------- 8255 COMMAND/STATUS REGISTER
;------------------------------------------------------------------------------
; DATA SEGMENT (256 BYTES OF RAM IN 8155)
;------------------------------------------------------------------------------
          ASEG
          ORG       6000H
STACK_SPACE:                                                      ; was D6000
          DS        64
STACK_TOP:                        EQU       $                     ; Stack from 603F down to 6000 | was STK
DISPLAY_COMMAND:                                                  ; was D6040
          DS        1                                             ; display character buffer (8 bytes through 6047); 10H blank, 11H dash, bit 7 decimal point
DISPLAY_PROM_TYPE:                                                ; was D6041
          DS        1                                             ; STRUCT
DISPLAY_DATA:                                                     ; was D6042
          DS        2
DISPLAY_ADDRESS_HIGH:                                             ; was D6044
          DS        2
DISPLAY_ADDRESS_LOW:                                              ; was D6046
          DS        2
DISPLAY_SEGMENTS:                                                 ; was D6048
          DS        8                                             ; display segment buffer (8 bytes); rebuilt by BUILD_DISPLAY_SEGMENTS, scanned by SCAN_DIGIT_AND_KEYS
KEY_COLUMN_SAMPLES:                                               ; was ROWDATA
          DS        8                                             ; 6050H ff... Keyrow0-7 Data (see KEY_CODE_TABLE:)read from Port 6B (SCAN_DIGIT_AND_KEYS:)
; all set to C0 in a Mem-Dump on the live system
TRANSFER_IRQ_INHIBIT:                                             ; was D6058
          DS        1                                             ; transfer/IRQ-inhibit flag; DRAM helpers re-enable interrupts only when zero
SHARED_INPUT_STATE:                                               ; was D6059
          DS        1
TARGET_ACCESS_MODE:                                               ; was D605A
          DS        1                                             ; operation-dependent access flag: RAM/PROM editor or receive store/compare
RECEIVE_PAGE_MASK:                                                ; was D605B
          DS        1                                             ; receive page mask; zero selects explicit base-address mapping
SHARED_OFFSET_SUM:                                                ; was D605C
          DS        2                                             ; shared word: receive/output offset or buffer checksum accumulator
SHARED_ERRORS_SAVED_SP:                                           ; was D605E
          DS        2                                             ; shared word: error counter, or saved SP for receive-parser unwind
BAUD_INDEX:                                                       ; was D6060
          DS        1                                             ; baud index X4 (0..5); table indexing reads word 6060/6061
BAUD_INDEX_HIGH:                                                  ; was D6061
          DS        1                                             ; normally zero high byte adjacent to baud index; also read by buffer-range setup
; Xn - Parameters see Manual p.4-11(pdf p.63)
BUFFER_BASE_PAGE:                                                 ; was RAMSTA
          DS        2                                             ; X5 default 00 = RAM @ 8000H
TAPE_FORMAT:                                                      ; was TAPFMT
          DS        1                                             ; X6 default 04 = ASCII Hex Space
DISPLAY_SCAN_COUNTER:                                             ; was D6065
          DS        1                                             ; multiplex scan counter; low three bits select digit/key column
STATUS_FLAGS:                                                     ; was SETSTA
          DS        1                                             ; X7 default 00 = see Manual 3-7-7 p.3-21(pdf p.45)
OPERATING_MODE:                                                   ; was D6067
          DS        1                                             ; mode: 0 panel, 1 terminal, 2 host, 3 test menu, 4 raw-segment LED test
TAPE_START_CODE:                                                  ; was STACOD
          DS        1                                             ; X8 default 02H = STX
PROM_TYPE_INDEX:                                                  ; was D6069
          DS        1                                             ; PROM type index from selector lookup; NOT the ASCII start-code byte at 6068
TAPE_END_CODE:                                                    ; was STOCOD
          DS        1                                             ; X9 default 03H = ETX
PROM_CONTROL_STATE:                                               ; was D606B
          DS        1                                             ; last PROM control-table state index; NOT the ASCII end code at 606A
DISPLAY_SELECT_CACHE:                                             ; was D606C
          DS        1                                             ; cached base select/control C8H; scan routine ORs in column 0..7 without updating this cache
PROM_PPI_MODE_CACHE:                                              ; was D606D
          DS        1                                             ; cached port A3 PROM-PPI mode (92H idle, 90H read, 80H output)
PROM_CONTROL_CACHE:                                               ; was D606E
          DS        1                                             ; cached port C1 PROM control value
PROM_ADDRESS_MUX_CACHE:                                           ; was D606F
          DS        1                                             ; cached port A2 high address / analog mux selection
PREVIOUS_KEY_COUNT:                                               ; was D6070
          DS        1                                             ; previous pressed-key count, for single-key edge detection
KEY_EVENT_PENDING:                                                ; was D6071
          DS        1                                             ; pending-key flag (FFH when a new key is available)
KEY_EVENT_INDEX:                                                  ; was X6072
          DS        1                                             ; pending physical key index; WAIT_KEY translates through KEY_CODE_TABLE
VOLTAGE_SELECT:                                                   ; was D6073
          DS        1                                             ; voltage selection: 80H=5.00V, 40H=4.75V, 00H=5.25V
RX_REFRESH_ENABLED:                                               ; was D6074
          DS        1                                             ; enable selector/display refresh while waiting for serial input
TYPE_SWITCH_CACHE:                                                ; was D6075
          DS        1                                             ; switchstate, read by READ_TYPE_SWITCHES
PROM_SIZE_PAGES:                                                  ; was D6076
          DS        1                                             ; selected PROM capacity in 256-byte pages
RECEIVE_SOURCE_ADDRESS:                                           ; was D6077
          DS        2                                             ; current incoming record/source address, used for even/odd packing
MOVE_SOURCE_START:                                                ; was D6079
          DS        2                                             ; MOVE source start
MOVE_END_DIRECTION:                                               ; was D607B
          DS        2                                             ; MOVE source end; later reused as forward/backward copy flag
MOVE_DESTINATION:                                                 ; was D607D
          DS        2                                             ; MOVE destination start
          DS        1
USER_CODE_RAM:                                                    ; was D6080
          DS        128                                           ; 128-byte user-code/format workspace; test 4 loads it, X6=0 jumps here
;------------------------------------------------------------------------------
; CODE SEGMENT
;------------------------------------------------------------------------------
          ORG       0
START:
          DI
          LXI       SP, STACK_TOP
          JMP       POWER_ON_DEFAULTS
          RST       7
;------------------------------------------------------------------------------
; PUT
;------------------------------------------------------------------------------
MEM_WRITE_VECTOR:                                                 ; was RST1V
          JMP       MEM_WRITE                                     ; 0008=
          RST       7
SERIAL_TX_VECTOR:                                                 ; was X000C
          JMP       SERIAL_TX                                     ; 000C=
          RST       7
;------------------------------------------------------------------------------
; GET
;------------------------------------------------------------------------------
MEM_READ_VECTOR:                                                  ; was RST2V
          JMP       MEM_READ                                      ; 0010=
          RST       7
          JMP       READ_FORMAT_DISPATCH                          ; 0014 =
          RST       7
SERIAL_RX_RAW_VECTOR:                                             ; was RST3V
          JMP       SERIAL_RX_RAW                                 ; 0018 =
          RST       7
          JMP       RX_HEX_BYTE_CHECKSUM                          ; 001C =
          RST       7
PROM_CONTROL_VECTOR:                                              ; was RST4V
          JMP       PROM_CONTROL_INLINE                           ; 0020 =
          RST       7
;------------------------------------------------------------------------------
; TRAP INTERRUPT HANDLER
;------------------------------------------------------------------------------
          JMP       SET_RECEIVE_ADDRESS                           ; 0024 =
          RST       7
TX_INLINE_STRING_VECTOR:                                          ; was RST5V
          JMP       TX_INLINE_STRING                              ; 0028 =
          RST       7
;------------------------------------------------------------------------------
; RST 5.5 INTERRUPT
;------------------------------------------------------------------------------
          JMP       CHECK_RECEIVE_LIMIT                           ; 002C =
          RST       7
SERIAL_RX_ASCII_VECTOR:                                           ; was RST6V
          JMP       SERIAL_RX_ASCII                               ; 0030 =
          RST       7
;------------------------------------------------------------------------------
; RST 6.5 INTERRUPT HANDLER
;------------------------------------------------------------------------------
          JMP       STORE_OR_COMPARE_RECEIVED                     ; 0034 =
          RST       7
WAIT_KEY_VECTOR:                                                  ; was RST7V
          JMP       WAIT_KEY                                      ; 0038 =
          RST       7
;------------------------------------------------------------------------------
; RST 7.5 INTERRUPT HANDLER
;  8155 TOUT -> SYSTICK -> 8085 RST 7.5 (schematic sheet 2).
;  Reload 1770H=6000 at 3 MHz: one timer pulse every 2 ms (500 Hz).
;  D770H includes mode 11b: continuous terminal-count pulses, not square-wave mode.
;  Interrupt masking and the stack-space guard below can defer or skip scan service.
;------------------------------------------------------------------------------
TIMER_INTERRUPT:                                                  ; was L003C
          PUSH      PSW                                           ; 003C =
          PUSH      H
          MVI       L, 0
          DAD       SP
          MOV       A, L
          CPI       00EH
          JC        TIMER_INTERRUPT_RETURN
          PUSH      D
          PUSH      B
          CALL      TIMER_SCAN_SERVICE
          POP       B
          POP       D
TIMER_INTERRUPT_RETURN:                                           ; was A04
          POP       H
          POP       PSW
          EI
          RET
;------------------------------------------------------------------------------
; Populate Parameter default values
;------------------------------------------------------------------------------
POWER_ON_DEFAULTS:                                                ; was START1
          CALL      INIT_HARDWARE                                 ; configure 8155 (Timer)
          LXI       H, TAPE_FORMAT                                ; load addr. of Tape-Format Parameter
;         INR       M                  ; original ROM: INIT_HARDWARE cleared 6040..607F, so 0 -> 1 (Intel HEX).
;                                       INR M is one byte; MVI M,01H takes two bytes.
          MVI       M, 004H                                       ; I want default tape format = 4 ('ASCII Hex Space', better for experimenting)
          MVI       L, LOW(TAPE_START_CODE)
;         MVI       M,002H             ; Set Start-Byte (for 'ASCII Hex Space' tape format) to STX=02H
; Start/Stop Bytes can be changed by command X8/X9, but are lost on reset.
; as i can't enter the ASCII Chars [STX]&[ETX] manually, i set it to capital Letters S and X
; because they are not in Hex E to F and are the first and last chars in the default [STX] and [ETX]
; This way, playing around with the tape commands is easyer, Tape Data can now be typed in.
          MVI       M, 053H                                       ; changing default to char. 'S'=53H
          INX       H
          INX       H                                             ; move pointer to next parameter
;         MVI       M,003H             ; Set Stop-Byte (for 'ASCII Hex Space' tape format) to ETX=03H
          MVI       M, 058H                                       ; changing default to char. 'X'=58H
DISPATCH_CURRENT_MODE:                                            ; was START2
          LDA       OPERATING_MODE                                ; mode: 0 panel, 1 terminal, 2 host, 3 test menu, 4 LED test
          CPI       003H
          JNC       TEST_MENU
          ORA       A
          JNZ       COMMAND_PROMPT
PANEL_MODE_RESTART:                                               ; was START3
          CALL      ENTER_PANEL_MODE                              ; 8085 Simulator: 3k clock cycles to here
PANEL_COMMAND_LOOP:                                               ; was START4
          LXI       SP, STACK_TOP                                 ; 760k ! to here...
          CALL      WAIT_PANEL_COMMAND
          PUSH      PSW
          CALL      CLEAR_DISPLAY
          POP       PSW
          CALL      DISPATCH_PANEL_COMMAND
          CNZ       CLEAR_AND_ERROR_BEEP
          JMP       PANEL_MODE_RESTART
;------------------------------------------------------------------------------
; INIT_HARDWARE
; Initialize CPU interrupt mask, 8155 timer, PPIs and workspace; called at power-on.
;   Set interrupt masks, timer and PPIs; prime DRAM addressing; clear workspace.
;------------------------------------------------------------------------------
INIT_HARDWARE:                                                    ; was PR0084
          MVI       A, 05BH
          SIM                                                     ; Set Interrupt Mask to 5b=0101 1011
          MVI       A, 070H
          OUT       PORT_TIMER_LSB
          MVI       A, 0D7H
          OUT       PORT_TIMER_MSB
          MVI       A, 0C3H
          OUT       PORT_DISPLAY_TIMER_COMMAND
          MVI       A, 091H
          OUT       PORT_CONTROL_PPI_MODE
; 091H Configures the 8255 PPI ports as follows:
; Port A: Input Port B: Output Port C Upper (PC7-PC4): Output Port C Lower (PC3-PC0): Input
          MVI       B, 010H
          MVI       A, 002H                                       ; 02H = DRAM read
          OUT       PORT_DRAM_DATA_CONTROL
          XRA       A
INIT_DRAM_ADDRESS_LOOP:                                           ; was A01
          OUT       PORT_DRAM_ADDRESS_LSB
          OUT       PORT_DRAM_ADDRESS_MSB
          DCR       B
          JNZ       INIT_DRAM_ADDRESS_LOOP
          MVI       B, 040H
          LXI       H, DISPLAY_COMMAND
;------------------------------------------------------------------------------
; RESET_WORKSPACE_IO
; Also called from RESET_COMMAND_STATE...
;   Clear B bytes at HL, restore idle I/O and clear the display.
;------------------------------------------------------------------------------
RESET_WORKSPACE_IO:                                               ; was PR00AB
          XRA       A
          CALL      FILL_BYTES                                    ; fill B bytes at HL through HL+B-1 with A; advance HL, return B=0
          MVI       A, 0C8H
          CALL      WRITE_DISPLAY_SELECT
          MVI       A, 020H
          OUT       PORT_SERIAL_SIGNALS                           ; set RTS
          MVI       A, 080H
          STA       VOLTAGE_SELECT
          LDA       PROM_PPI_MODE_CACHE                           ; cached mode last written to PROM PPI control port A3H
          ORA       A
          JZ        RESET_PROM_PPI_IDLE
          CPI       092H                                          ; compare if default
          JZ        RESET_PROM_CONTROL_IDLE
          RST       RST_PROM_CONTROL
          DB        2
          CALL      PROM_STATE_ONE_DELAY
RESET_PROM_PPI_IDLE:                                              ; was A02
          MVI       A, 092H
          CALL      WRITE_PROM_PPI_MODE
RESET_PROM_CONTROL_IDLE:                                          ; was A03
          XRA       A
          CALL      WRITE_PROM_CONTROL
          LXI       H, 1
          SHLD      PREVIOUS_KEY_COUNT
;------------------------------------------------------------------------------
; CLEAR_DISPLAY
; Set command digit to dash, then fill 6041H..6047H with blank character 10H.
;   6040 = dash (11H); remaining seven character slots = blank (10H).
;------------------------------------------------------------------------------
CLEAR_DISPLAY:                                                    ; was PR00DD
          LXI       H, DISPLAY_COMMAND
          MVI       M, 011H
          INX       H
          MVI       B, 007H
          MVI       A, 010H
;------------------------------------------------------------------------------
; FILL_BYTES
; fill B bytes at HL through HL+B-1 with A; advance HL, return B=0
;   Fill B bytes at HL with A; advances HL, leaves B=0. B=0 means 256 bytes.
; In: HL=first address, B=count (0 means 256), A=fill value.
; Out: HL=first address after filled range, B=0; A unchanged. Flags changed.
;------------------------------------------------------------------------------
FILL_BYTES:                                                       ; was PR00E7
          MOV       M, A
          INX       H
          DCR       B
          JNZ       FILL_BYTES
          RET
;------------------------------------------------------------------------------
; WAIT_PANEL_COMMAND
; Keep PROM type indication current while waiting for a pending key event.
;   Refresh PROM type display and wait for a decoded key event.
;------------------------------------------------------------------------------
WAIT_PANEL_COMMAND:                                               ; was PR00EE
          CALL      DECODE_PROM_TYPE
          CALL      SHOW_PROM_TYPE
          LDA       KEY_EVENT_PENDING
          ORA       A
          JZ        WAIT_PANEL_COMMAND
          RST       RST_WAIT_KEY
;------------------------------------------------------------------------------
; REJECT_INVALID_PROM_TYPE
;   Refresh type; return A=0 if type index >=8, otherwise preserve A.
;------------------------------------------------------------------------------
REJECT_INVALID_PROM_TYPE:                                         ; was PR00FC
          PUSH      PSW
          CALL      DECODE_PROM_TYPE
          LDA       PROM_TYPE_INDEX
          CPI       008H
          JNC       INVALID_PROM_TYPE_RETURN
          POP       PSW
          RET
INVALID_PROM_TYPE_RETURN:                                         ; was A05
          POP       PSW
          XRA       A
          RET
;------------------------------------------------------------------------------
; POLL_KEY_EVENT
;   Return Z if no pending key; otherwise fall through to WAIT_KEY.
;------------------------------------------------------------------------------
POLL_KEY_EVENT:                                                   ; was PR010D
          LDA       KEY_EVENT_PENDING
          ORA       A
          RZ
;------------------------------------------------------------------------------
; WAIT_KEY
;   Consume pending key, look up KEY_CODE_TABLE, beep. SET: Z; JOB: CY; other keys: NC.
;------------------------------------------------------------------------------
WAIT_KEY:                                                         ; was RST7
          PUSH      B
          PUSH      H
          LXI       H, KEY_EVENT_PENDING
          XRA       A
          MOV       B, A
WAIT_KEY_PENDING_LOOP:                                            ; was A10
          ORA       M                                             ; WAIT FOR EVENT
          JZ        WAIT_KEY_PENDING_LOOP
          MOV       M, B
          INX       H
          MOV       C, M
          LXI       H, KEY_CODE_TABLE
          DAD       B
          CALL      KEY_BEEP
          MOV       A, M
          POP       H
          POP       B
          CPI       012H
          RZ
          CPI       017H
          CMC
          RET
;------------------------------------------------------------------------------
; REFRESH_TYPE_IF_CHANGED
;   Read selector switches; update type/display only on change.
; Flow: unchanged (Z) -> RET; otherwise fall through to REFRESH_PROM_TYPE_DISPLAY.
;------------------------------------------------------------------------------
REFRESH_TYPE_IF_CHANGED:                                          ; was PR0131
          CALL      READ_TYPE_SWITCHES
          RZ
;------------------------------------------------------------------------------
; REFRESH_PROM_TYPE_DISPLAY
;   Decode switches, then show the selected PROM type.
; Flow: after type decoding, continue directly into SHOW_PROM_TYPE.
;------------------------------------------------------------------------------
REFRESH_PROM_TYPE_DISPLAY:                                        ; was PR0135
          CALL      DECODE_PROM_TYPE
;------------------------------------------------------------------------------
; SHOW_PROM_TYPE
; Show selected PROM type as index+1; invalid index 8 is shown blank.
;   Panel/test-menu modes update character RAM; terminal/host modes drive the type digit directly.
;   Type index+1 on display; mode parity chooses direct segments or character RAM.
;------------------------------------------------------------------------------
SHOW_PROM_TYPE:                                                   ; was PR0138
          PUSH      D
          PUSH      H
          LHLD      PROM_TYPE_INDEX
          MVI       H, 000H
          INR       L
          MOV       A, L
          CPI       009H
          PUSH      PSW
          LDA       OPERATING_MODE
          ORA       A
          JPE       SHOW_TYPE_IN_CHAR_BUFFER
          POP       PSW
          JNZ       SHOW_TYPE_SEGMENT_LOOKUP
          XRA       A
          JMP       SHOW_TYPE_SEGMENT_OUTPUT
SHOW_TYPE_SEGMENT_LOOKUP:                                         ; was A11
          LXI       D, LED_SEGMENT_TABLE
          DAD       D
          LDA       DISPLAY_SELECT_CACHE                          ; cached base select/control value (C8H), not the advancing scan index
          ANI       077H                                          ; clear bits 3 and 7 (77H); retain bit 4
          OUT       PORT_DISPLAY_KEY_BEEPER
          MOV       A, M
SHOW_TYPE_SEGMENT_OUTPUT:                                         ; was A12
          OUT       PORT_LED_SEGMENTS
          POP       H
          POP       D
          RET
SHOW_TYPE_IN_CHAR_BUFFER:                                         ; was A13
          POP       PSW
          JNZ       SHOW_TYPE_VALID_DIGIT
          MVI       A, 010H
          JMP       SHOW_TYPE_STORE_DIGIT
SHOW_TYPE_VALID_DIGIT:                                            ; was A14
          MOV       A, L
SHOW_TYPE_STORE_DIGIT:                                            ; was A15
          STA       DISPLAY_PROM_TYPE
          POP       H
          POP       D
          RET
;------------------------------------------------------------------------------
; READ_TYPE_SWITCHES
; read eprom-type switch-states
;   first called after 761k cycles
;   Save port C0 bits 2..5 at TYPE_SWITCH_CACHE; Z means unchanged.
;------------------------------------------------------------------------------
READ_TYPE_SWITCHES:                                               ; was PR0174
          LXI       H, TYPE_SWITCH_CACHE
          MOV       A, M                                          ; previous masked selector state from TYPE_SWITCH_CACHE
          PUSH      PSW
          IN        PORT_SWITCHES_COMPARATORS                     ; Port C0H
          ANI       03CH                                          ; mask out unrelated bits 0,1,6,7
          MOV       M, A                                          ; save current selector bits; final CMP reports whether they changed
          POP       PSW
          CMP       M
          RET
;------------------------------------------------------------------------------
; CLEAR_AND_ERROR_BEEP
;   Clear display, then fall through to the error tone sequence.
;------------------------------------------------------------------------------
CLEAR_AND_ERROR_BEEP:                                             ; was PR0181
          CALL      CLEAR_DISPLAY
;------------------------------------------------------------------------------
; ERROR_BEEP
;   Repeated tone/delay sequence; shared by invalid input and operation errors.
;------------------------------------------------------------------------------
ERROR_BEEP:                                                       ; was PR0184
          LXI       B, SERIAL_SAMPLE_DELAYS
ERROR_BEEP_REPEAT:                                                ; was A16
          PUSH      B
          CALL      DELAY_LONG_SCAN
          POP       B
          PUSH      B
          CALL      BEEP
          POP       B
          DCR       B
          JNZ       ERROR_BEEP_REPEAT
          RET
;------------------------------------------------------------------------------
; ENTER_PANEL_MODE
;   Set mode=0 and reset command workspace.
;------------------------------------------------------------------------------
ENTER_PANEL_MODE:                                                 ; was PR0196
          XRA       A
;------------------------------------------------------------------------------
; SET_MODE_AND_RESET
; TEST_MENU calls this entry with A=3; ENTER_PANEL_MODE falls through with A=0.
;   Store A in OPERATING_MODE; fall through to command reset.
;------------------------------------------------------------------------------
SET_MODE_AND_RESET:                                               ; was PR0197
          STA       OPERATING_MODE
;------------------------------------------------------------------------------
; RESET_COMMAND_STATE
;   Clear 6058..605F and restore idle I/O; completion tone.
;------------------------------------------------------------------------------
RESET_COMMAND_STATE:                                              ; was PR019A
          MVI       B, 008H
          LXI       H, TRANSFER_IRQ_INHIBIT
          CALL      RESET_WORKSPACE_IO
          MVI       C, 01EH
          JMP       BEEP
;------------------------------------------------------------------------------
; KEY_BEEP
;   Short tone (C=5); falls through to BEEP.
;------------------------------------------------------------------------------
KEY_BEEP:                                                         ; was PR01A7
          MVI       C, 005H
;------------------------------------------------------------------------------
; BEEP
;   C groups of 16 cycles, toggle port 6A bit 5 with display/key service delays.
;------------------------------------------------------------------------------
BEEP:                                                             ; was PR01A9
          EI
BEEP_GROUP_LOOP:                                                  ; was A17
          MVI       B, 010H
BEEP_CYCLE_LOOP:                                                  ; was A18
          LDA       DISPLAY_SELECT_CACHE                          ; restore cached base value with beeper bit 5 clear for first half-cycle
          CALL      WRITE_SELECT_AND_DELAY
          ORI       020H                                          ; set bit 5 (20H) for the second beeper half-cycle
; The next loop reloads DISPLAY_SELECT_CACHE (C8H, bit 5=0); no AND is needed to clear it.
          CALL      WRITE_SELECT_AND_DELAY
          DCR       B
          JNZ       BEEP_CYCLE_LOOP
          DCR       C
          JNZ       BEEP_GROUP_LOOP
          EI
          RET
;------------------------------------------------------------------------------
; DELAY_LONG_SCAN
; Delay between error tones while servicing display and keyboard scanning.
;   C groups of 48 scan delays; used between error tones.
;------------------------------------------------------------------------------
DELAY_LONG_SCAN:                                                  ; was PR01C1
          MVI       B, 030H
DELAY_SCAN_GROUP_LOOP:                                            ; was A19
          CALL      DELAY_SCAN_THREE
          DCR       B
          JNZ       DELAY_SCAN_GROUP_LOOP
          DCR       C
          JNZ       DELAY_LONG_SCAN
          RET
;------------------------------------------------------------------------------
; WRITE_SELECT_AND_DELAY
; only called from BEEP (twice)
;   Output A to port 6A, then fall through to scan delay.
;------------------------------------------------------------------------------
WRITE_SELECT_AND_DELAY:                                           ; was PR01CF
          OUT       PORT_DISPLAY_KEY_BEEPER
;------------------------------------------------------------------------------
; DELAY_SCAN_THREE
; Scan three consecutive digit/key columns; the scan counter persists across calls.
;   DI; scan three digit/key columns, blank segments; preserve A/flags. Leaves DI.
;------------------------------------------------------------------------------
DELAY_SCAN_THREE:                                                 ; was PR01D1
          DI
          PUSH      PSW
          CALL      SCAN_DIGIT_AND_KEYS                           ; service next column selected by DISPLAY_SCAN_COUNTER & 7
          CALL      SCAN_DIGIT_AND_KEYS                           ; service following column; sample stored at KEY_COLUMN_SAMPLES[index]
          CALL      SCAN_DIGIT_AND_KEYS                           ; service third column; wrap modulo eight
          XRA       A
          OUT       PORT_LED_SEGMENTS                             ; leave segment outputs dark before returning with interrupts disabled
          POP       PSW
          RET
;------------------------------------------------------------------------------
; TX_HEX_WORD_CHECKSUM
; Calls do not restart at column 0; the counter advances on every scan.
;   Output HL, high byte first, updating tape checksum C.
;------------------------------------------------------------------------------
TX_HEX_WORD_CHECKSUM:                                             ; was PR01E1
          MOV       A, H
          CALL      TX_HEX_BYTE_CHECKSUM
          MOV       A, L
;------------------------------------------------------------------------------
; TX_HEX_BYTE_CHECKSUM
;   Output A as hex; add byte to C, or nibble sum for Tektronix format 5.
;------------------------------------------------------------------------------
TX_HEX_BYTE_CHECKSUM:                                             ; was PR01E6
          PUSH      PSW
          LDA       TAPE_FORMAT
          CPI       005H
          JNZ       TX_BYTE_CHECKSUM_ADD                          ; !!
          POP       PSW
          CALL      ADD_NIBBLE_CHECKSUM
          JMP       TX_HEX_BYTE
TX_BYTE_CHECKSUM_ADD:                                             ; was A19X
          POP       PSW
          PUSH      PSW
          ADD       C
          MOV       C, A
          POP       PSW
;------------------------------------------------------------------------------
; TX_HEX_BYTE
;   Output A as two uppercase hex digits.
;------------------------------------------------------------------------------
TX_HEX_BYTE:                                                      ; was PR01FB
          PUSH      PSW
          CALL      SWAP_NIBBLES
          CALL      TX_HEX_NIBBLE
          POP       PSW
;------------------------------------------------------------------------------
; TX_HEX_NIBBLE
;   Convert low nibble of A to ASCII using DAA; fall through to SERIAL_TX.
;------------------------------------------------------------------------------
TX_HEX_NIBBLE:                                                    ; was PR0203
          ANI       00FH
          ADI       090H
          DAA
          ACI       040H
          DAA
;------------------------------------------------------------------------------
; SERIAL_TX
; Serial character output; keypad is sampled only for abort handling.
;   Bit-banged serial output of A; handshake/abort checks, baud delay. Preserves BC/DE/HL/A.
;------------------------------------------------------------------------------
SERIAL_TX:                                                        ; was PR020B
          PUSH      B
          PUSH      D
          PUSH      H
          PUSH      PSW
          MOV       E, A
          MVI       D, 009H
          DI
          XRA       A                                             ; Clear Display
          OUT       PORT_LED_SEGMENTS
          LDA       DISPLAY_SELECT_CACHE
          ANI       0E0H                                          ; Mask out bits 0-4
          OUT       PORT_DISPLAY_KEY_BEEPER
          ORI       009H
          OUT       PORT_DISPLAY_KEY_BEEPER
          LXI       B, 0105H
          CALL      DELAY_BC                                      ; short settling delay after selecting the abort-key column
          IN        PORT_KEYBOARD_ROWS
          ANI       004H
          JNZ       DISPATCH_CURRENT_MODE
          CALL      TEST_PUNCH_FLOW_CONTROL
          JNZ       SERIAL_TX_WAIT_CTS
          IN        PORT_SERIAL_SIGNALS
          ANI       001H
          JNZ       DISPATCH_CURRENT_MODE
SERIAL_TX_WAIT_CTS:                                               ; was A20
          IN        PORT_SERIAL_SIGNALS
          ANI       004H
          JNZ       WAIT_FAULT_ACK
          IN        PORT_SERIAL_SIGNALS
          ANI       002H
          JNZ       SERIAL_TX_WAIT_CTS
          MVI       A, 0A0H
SERIAL_TX_BIT_LOOP:                                               ; was A21
          OUT       PORT_SERIAL_SIGNALS
          NOP
          CALL      DELAY_SERIAL_BIT
          MOV       A, E
          RRC
          MOV       E, A
          CMA
          ANI       080H
          ORI       020H
          DCR       D
          JNZ       SERIAL_TX_BIT_LOOP
          MVI       A, 020H
          OUT       PORT_SERIAL_SIGNALS
          CALL      DELAY_SERIAL_BIT
          CALL      DELAY_SERIAL_BIT
SERIAL_IO_RETURN:                                                 ; was A0267
          CALL      DELAY_SERIAL_BIT
          POP       PSW
          POP       H
          POP       D
          POP       B
          ORA       A
          RET
;------------------------------------------------------------------------------
; SERIAL_RX_RAW
; Receive eight serial data bits using baud-dependent sampling delays.
;   Bit-banged 8-bit serial input into A; handshake and optional type refresh.
;------------------------------------------------------------------------------
SERIAL_RX_RAW:                                                    ; was PR0270
          PUSH      B
          PUSH      D
          PUSH      H
          LXI       D, RX_BITS_AND_DATA_INIT
          DI
          XRA       A
          OUT       PORT_LED_SEGMENTS                             ; clear Display
          LDA       OPERATING_MODE
          CPI       002H
          JNC       SERIAL_RX_SET_HANDSHAKE
          LDA       TRANSFER_IRQ_INHIBIT
          ORA       A
          MVI       A, 000H
          JZ        SERIAL_RX_CHECK_REFRESH
SERIAL_RX_SET_HANDSHAKE:                                          ; was A23
          XRA       A
          ORI       010H
SERIAL_RX_CHECK_REFRESH:                                          ; was A24
          PUSH      PSW
          LDA       RX_REFRESH_ENABLED
          ORA       A
          JZ        SERIAL_RX_WITHOUT_REFRESH
          CALL      REFRESH_PROM_TYPE_DISPLAY
          POP       PSW
          OUT       PORT_SERIAL_SIGNALS
SERIAL_RX_WAIT_WITH_REFRESH:                                      ; was A25
          IN        PORT_SERIAL_SIGNALS
          ANI       004H
          JNZ       WAIT_FAULT_ACK
          CALL      REFRESH_TYPE_IF_CHANGED
          IN        PORT_SERIAL_SIGNALS
          ANI       001H
          JZ        SERIAL_RX_WAIT_WITH_REFRESH
          JMP       SERIAL_RX_START_SAMPLE
SERIAL_RX_WITHOUT_REFRESH:                                        ; was A26
          POP       PSW
          OUT       PORT_SERIAL_SIGNALS
SERIAL_RX_WAIT_START:                                             ; was A27
          IN        PORT_SERIAL_SIGNALS
          ANI       004H
          JNZ       WAIT_FAULT_ACK
          IN        PORT_SERIAL_SIGNALS
          ANI       001H
          JZ        SERIAL_RX_WAIT_START
SERIAL_RX_START_SAMPLE:                                           ; was A28
          XRA       A
          OUT       PORT_SERIAL_SIGNALS
          LXI       B, SERIAL_SAMPLE_DELAYS
          CALL      DELAY_BAUD_TABLE
SERIAL_RX_BIT_LOOP:                                               ; was A29
          IN        PORT_SERIAL_SIGNALS
          RRC
          CMA
          ANI       080H
          ORA       E
          DCR       D
          JZ        SERIAL_RX_FINISH
          RRC
          MOV       E, A
          CALL      DELAY_SERIAL_BIT
          JMP       SERIAL_RX_BIT_LOOP
SERIAL_RX_FINISH:                                                 ; was A30
          PUSH      PSW
          MVI       A, 020H
          OUT       PORT_SERIAL_SIGNALS
          JMP       SERIAL_IO_RETURN
;------------------------------------------------------------------------------
; DELAY_SERIAL_BIT
;   Use baud index BAUD_INDEX and SERIAL_BIT_DELAYS timing table.
;------------------------------------------------------------------------------
DELAY_SERIAL_BIT:                                                 ; was PR02E5
          LXI       B, SERIAL_BIT_DELAYS
;------------------------------------------------------------------------------
; DELAY_BAUD_TABLE
; Look up the baud-indexed timing pair at BC; fall through to the software delay.
;   BC points to timing table; index by 2*word at BAUD_INDEX, then delay.
;------------------------------------------------------------------------------
DELAY_BAUD_TABLE:                                                 ; was PR02E8
          LHLD      BAUD_INDEX
          DAD       H
          DAD       B
          MOV       C, M
          INX       H
          MOV       B, M
;------------------------------------------------------------------------------
; DELAY_BC
; Nested B/C decrement-loop delay (zero counters wrap to 256).
;   Nested decrement loops; C=0 gives 256 inner iterations, not a zero delay.
;------------------------------------------------------------------------------
DELAY_BC:                                                         ; was PR02F0
          DCR       C
          JNZ       DELAY_BC
          DCR       B
          JNZ       DELAY_BC
          RET
;------------------------------------------------------------------------------
; SERIAL_BIT_DELAYS: baud indices 0..5 (4800,2400,1200,600,300,110).
;------------------------------------------------------------------------------
SERIAL_BIT_DELAYS:                                                ; was A02F9
          DW        0122H
          DW        014FH
          DW        01A8H
          DW        025AH
          DW        03BEH
          DW        088CH
;------------------------------------------------------------------------------
; SERIAL_SAMPLE_DELAYS: initial receive sampling delays for same baud indices.
;------------------------------------------------------------------------------
SERIAL_SAMPLE_DELAYS:                                             ; was A0305
          DW        013AH
          DW        017DH
          DW        0100H
          DW        030DH
          DW        0523H
          DW        0C58H
;------------------------------------------------------------------------------
; TIMER_SCAN_SERVICE
; only called from Interrupt 7.5
;   Scan one column; after column 6 decode keys and rebuild segments unless mode=4.
;------------------------------------------------------------------------------
TIMER_SCAN_SERVICE:                                               ; was PR0311
          CALL      SCAN_DIGIT_AND_KEYS                           ; multiplex one digit and sample its four row inputs; Z at column 6
          RNZ
          EI
          CALL      DECODE_KEY_EDGE
          LDA       OPERATING_MODE
          CPI       004H
          RZ
;------------------------------------------------------------------------------
; BUILD_DISPLAY_SEGMENTS
; Convert display character codes to segment bitmaps, keeping each decimal-point bit.
;   Translate eight characters at 6040 to segments at 6048; preserve decimal-point bit.
;------------------------------------------------------------------------------
BUILD_DISPLAY_SEGMENTS:                                           ; was PR031F
          LXI       D, DISPLAY_COMMAND
          MVI       B, 008H
          LXI       H, 8
          DAD       D
          PUSH      H
          XCHG
          MVI       D, 000H
BUILD_SEGMENT_LOOP:                                               ; was A31
          MOV       A, M
          ANI       080H
          MOV       C, A
          MOV       A, M
          ANI       07FH
          MOV       E, A
          PUSH      H
          LXI       H, LED_SEGMENT_TABLE
          DAD       D
          MOV       A, M
          ORA       C
          POP       H
          XTHL                                                    ; !!    pass argument to func.
;      https://stackoverflow.com/a/35489629/1331544
          MOV       M, A
          INX       H
          XTHL
          INX       H
          DCR       B
          JNZ       BUILD_SEGMENT_LOOP
          POP       H
          RET
;------------------------------------------------------------------------------
; DECODE_KEY_EDGE
;   Inspect seven key columns; publish only transition from no key to exactly one key.
;------------------------------------------------------------------------------
DECODE_KEY_EDGE:                                                  ; was PR0347
          XRA       A
          LXI       H, KEY_COLUMN_SAMPLES
          LXI       D, 07FFH
          PUSH      PSW
DECODE_KEY_COLUMN_LOOP:                                           ; was A32
          MVI       B, 004H
DECODE_KEY_ROW_LOOP:                                              ; was A33
          INR       E
          MOV       A, M
          RRC
          MOV       M, A
          JNC       DECODE_KEY_NEXT_ROW
          POP       PSW
          INR       A
          PUSH      PSW
          MOV       C, E
DECODE_KEY_NEXT_ROW:                                              ; was A34
          DCR       B
          JNZ       DECODE_KEY_ROW_LOOP
          INX       H
          DCR       D
          JNZ       DECODE_KEY_COLUMN_LOOP
          POP       PSW
          MVI       L, LOW(PREVIOUS_KEY_COUNT)                    ; !!
          MOV       B, M
          MOV       M, A
          DCR       A
          RNZ
          ORA       B
          RNZ
          INX       H
          MVI       M, 0FFH
          INX       H
          MOV       M, C
          RET
;------------------------------------------------------------------------------
; SCAN_DIGIT_AND_KEYS
; Multiplex one of eight display digits and sample its associated keyboard column.
;   Port 6AH selects the column/digit; port 6BH (8155 port C) reads four row bits.
;   Called by timer service and synchronous delay routines, not just panel mode.
;   Multiplex one digit, sample its key column; DISPLAY_SCAN_COUNTER advances; Z at column 6 (the seventh column).
;------------------------------------------------------------------------------
SCAN_DIGIT_AND_KEYS:                                              ; was PR0374
          PUSH      B
          PUSH      H
          LXI       H, DISPLAY_SCAN_COUNTER
          XRA       A
          MOV       B, A
          OUT       PORT_LED_SEGMENTS                             ; clear display
          MOV       A, M
          INR       M
          ANI       007H
          MOV       C, A
          MVI       L, LOW(DISPLAY_SELECT_CACHE)
          ORA       M
          OUT       PORT_DISPLAY_KEY_BEEPER                       ; select digit/key column index (0..7) combined with cached control bits
          MVI       L, LOW(DISPLAY_SEGMENTS)
          DAD       B
          MOV       A, M
          OUT       PORT_LED_SEGMENTS                             ; set display (A not empty)
          MVI       L, LOW(KEY_COLUMN_SAMPLES)
          DAD       B
          IN        PORT_KEYBOARD_ROWS
          MOV       M, A
          MOV       A, C
          CPI       006H
          POP       H
          POP       B
          RET
;------------------------------------------------------------------------------
; LED_SEGMENT_TABLE: 7-segment display character table
;   accessed from SHOW_PROM_TYPE-SHOW_TYPE_SEGMENT_LOOKUP and BUILD_DISPLAY_SEGMENTS-BUILD_SEGMENT_LOOP
;   CLEAR_DISPLAY stores dash code 11H; BUILD_DISPLAY_SEGMENTS maps it to segment G (40H).
;   The boot dash therefore uses this table, just like other display characters.
;------------------------------------------------------------------------------
LED_SEGMENT_TABLE:                                                ; was LEDMAP
          DB        03FH, 006H, 05BH, 04FH                        ; 0 1 2 3
          DB        066H, 06DH, 07DH, 027H                        ; 4 5 6 7
          DB        07FH, 067H, 077H, 07CH                        ; 8 9 A b
          DB        039H, 05EH, 079H, 071H                        ; C d E F
          DB        000H, 040H, 05CH, 073H                        ;   - o P (blank,dash...)
          DB        038H, 079H, 039H, 01EH                        ; L E C J
          DB        077H                                          ; A
;------------------------------------------------------------------------------
;   ---a---
;  |       |
;  f       b
;  |       |
;   ---g---
;  |       |
;  e       c
;  |       |
;   ---d---
;------------------------------------------------------------------------------
;          |dp| (decimal point, normally bottom-right)
;------------------------------------------------------------------------------
;------------------------------------------------------------------------------
; LED Segment Bitmap
;------------------------------------------------------------------------------
; d
; p
; gfedcba
;------------------------------------------------------------------------------
; 00111111    0       3F
; 00000110    1       06
; 01011011    2       5B
; 01001111    3       4F
; 01100110    4       66
; 01101101    5       6D
; 01111101    6       7D
; 00100111    7       27
; 01111111    8       7F
; 01100111    9       67
; 01110111    A       77
; 00000000    [blank] 00
; 01000000    -       40
; 01011100    o       5C     (used in tests)
;            P       73
;            L       38
;            J       1E
;------------------------------------------------------------------------------
; missing for writing "Hellorld!" on Display:
; 01110110    H       76
; 01010000    r       50
;------------------------------------------------------------------------------
; F7H is A with the decimal point lit; see manual page 1-9 for its physical position.
; Pressing "-" in normal mode selects the AUTO prefix and displays A.
; Active PKW-3000 function, documented in manual 3-1-6 (PDF 32): - -> PRG -> SET.
; No evidence here establishes this code as a leftover from the PKW-7000.
; DISPATCH_PANEL_COMMAND stores character 98H: index 18H (A) plus decimal-point flag 80H.
; BUILD_DISPLAY_SEGMENTS looks up 77H and ORs 80H, producing F7H; no separate table entry is needed.

;------------------------------------------------------------------------------
; KEY_CODE_TABLE: map of keycodes, see manual Page 3-26 (pdf p.50), Schematic Pages 4 & 11
;   (RST Key is hard-wired to CPU-Reset)
;   Keypresses (<>Keycodes!) are being read from Port 06BH, written to Memory
;                  Electrical Key Layout
;                    R      R      R      R
;                    o      o      o      o
;                    w      w      w      w
;
;                    0      1      2      3
; Port 6B values:    1      2      4      8
;                    5 ,    B ,   PRG,   SET
;------------------------------------------------------------------------------
KEY_CODE_TABLE:                                                   ; was KEYMAP
          DB        005H, 00BH, 013H, 012H                        ; Col 0 & 8 (Set = Col 8)

;                    9 ,    F ,   JOB,    -
          DB        009H, 00FH, 017H, 011H                        ; Col 1 & 7 (- = Col 7)

;                    D ,    2 ,   CMP
          DB        00DH, 002H, 016H, 0FFH                        ; Col 2

;                    0 ,    6 ,   ERS
          DB        000H, 006H, 015H, 0FFH                        ; Col 3

;                    4 ,    A ,   LOD
          DB        004H, 00AH, 014H, 0FFH                        ; Col 4

;                    8 ,    E ,    3
          DB        008H, 00EH, 003H, 0FFH                        ; Col 5

;                    C ,    1 ,    7
          DB        00CH, 001H, 007H, 0FFH                        ; Col 6
;------------------------------------------------------------------------------
; MEM_WRITE
;   RST 1: A -> [HL]; 8000..9FFF uses serial DRAM, below 6100 native memory, others ignored.
; Native-memory branch also admits workspace/stack at 6000..60FF.
;   This explains the Rn/stack technique in hellorld-story.txt; it is not buffer-only.
; In: HL=firmware address, A=byte. HL unchanged.
; Address space follows MEM_READ; writes outside its windows are ignored.
;------------------------------------------------------------------------------
MEM_WRITE:                                                        ; was PR03CE
          PUSH      PSW
          MOV       A, H
          CPI       080H
          JC        MEM_WRITE_CHECK_NATIVE
          CPI       0A0H
          JNC       MEM_WRITE_CHECK_NATIVE
          POP       PSW
;------------------------------------------------------------------------------
; DRAM_WRITE_BYTE
;   Write A as eight bits through ports 80..82 at logical address HL.
;------------------------------------------------------------------------------
DRAM_WRITE_BYTE:                                                  ; was PR03DB
          PUSH      B
          PUSH      H
          DAD       H
          DAD       H
          DAD       H
          MOV       B, A
          MVI       C, 008H
          DI
DRAM_WRITE_BIT_LOOP:                                              ; was A35
          ANI       001H
          OUT       PORT_DRAM_DATA_CONTROL
          MOV       A, L
          OUT       PORT_DRAM_ADDRESS_LSB
          MOV       A, H
          OUT       PORT_DRAM_ADDRESS_MSB
          MOV       A, B
          RRC
          MOV       B, A
          INX       H
          DCR       C
          JNZ       DRAM_WRITE_BIT_LOOP
          JMP       DRAM_CHECK_IRQ_ENABLE
MEM_WRITE_CHECK_NATIVE:                                           ; was A36
          CPI       061H
          JC        MEM_WRITE_NATIVE
          POP       PSW
          RET
MEM_WRITE_NATIVE:                                                 ; was A37
          POP       PSW
          MOV       M, A
          RET
;------------------------------------------------------------------------------
; MEM_READ
;   RST 2: [HL] -> A; 8000..9FFF uses DRAM, below 6100 native memory, others return 0.
; In: HL=firmware address. Out: A=byte; HL unchanged.
; Address space: CPU memory below 6100H; I/O-accessed DRAM at 8000H..9FFFH.
;------------------------------------------------------------------------------
MEM_READ:                                                         ; was PR0403
          MOV       A, H
          CPI       080H
          JC        MEM_READ_CHECK_NATIVE
          CPI       0A0H
          JNC       MEM_READ_CHECK_NATIVE
;------------------------------------------------------------------------------
; DRAM_READ_BYTE
;   Assemble eight port-83 bits into A; preserve BC/HL; IRQ policy from TRANSFER_IRQ_INHIBIT.
;------------------------------------------------------------------------------
DRAM_READ_BYTE:                                                   ; was PR040E
          PUSH      B
          PUSH      H
          DAD       H
          DAD       H
          DAD       H
          LXI       B, 8
          MVI       A, 002H
          OUT       PORT_DRAM_DATA_CONTROL
          DI
DRAM_READ_BIT_LOOP:                                               ; was A38
          MOV       A, L
          OUT       PORT_DRAM_ADDRESS_LSB
          MOV       A, H
          OUT       PORT_DRAM_ADDRESS_MSB
          IN        PORT_DRAM_DATA_IN
          ANI       001H
          ORA       B
          RRC
          MOV       B, A
          INX       H
          DCR       C
          JNZ       DRAM_READ_BIT_LOOP
DRAM_CHECK_IRQ_ENABLE:                                            ; was A39
          LDA       TRANSFER_IRQ_INHIBIT
          ORA       A
          JNZ       DRAM_ACCESS_RETURN
          EI
DRAM_ACCESS_RETURN:                                               ; was A40
          XRA       A
          MOV       A, B
          POP       H
          POP       B
          RET
MEM_READ_CHECK_NATIVE:                                            ; was A41
          CPI       061H
          JC        MEM_READ_NATIVE
          XRA       A
          RET
MEM_READ_NATIVE:                                                  ; was A42
          XRA       A
          MOV       A, M
          RET
;------------------------------------------------------------------------------
; PANEL_EDIT_PARAMETER
;   JOB 4..9: address 6060+2*A; display and replace low byte after SET.
;------------------------------------------------------------------------------
PANEL_EDIT_PARAMETER:                                             ; was PR0444
          POP       B
          MOV       C, A
          MVI       B, 000H
          LXI       H, BAUD_INDEX
          DAD       B
          DAD       B
          CALL      READ_BUFFER_AND_DISPLAY
          XCHG
          RST       RST_WAIT_KEY
          RZ
          CALL      PANEL_READ_HEX_BYTE
          RC
          RNZ
          XCHG
          MOV       M, E
          RET
;------------------------------------------------------------------------------
; PANEL_MEMORY_EDITOR
;   Select RAM/PROM edit mode, read address/data keys and advance on SET.
;------------------------------------------------------------------------------
PANEL_MEMORY_EDITOR:                                              ; was PR045B
          ADI       002H
          STA       TARGET_ACCESS_MODE
          CALL      PANEL_READ_HEX_WORD
          LXI       D, BUFFER_BASE_ADDRESS
          PUSH      PSW
          DAD       D
          POP       PSW
          XCHG
PANEL_EDITOR_HANDLE_KEY:                                          ; was A43
          RC
          ADI       0EFH
          JZ        PANEL_EDITOR_STEP_ADDRESS
          DCR       A
          RNZ
          LDA       DISPLAY_DATA+1
          CPI       010H
          JZ        PANEL_EDITOR_SHOW_DATA
          MOV       A, L
          CALL      WRITE_AND_VERIFY_EDIT
          XRA       A
PANEL_EDITOR_STEP_ADDRESS:                                        ; was A44
          MVI       A, 00EH
          RAR
          XCHG
          CALL      STEP_DISPLAY_HEX
          XCHG
PANEL_EDITOR_SHOW_DATA:                                           ; was A45
          CALL      READ_EDIT_TARGET
          CALL      SHOW_DATA_BYTE
          CNZ       ERROR_BEEP
          RST       RST_WAIT_KEY
          RC
          JZ        PANEL_EDITOR_STEP_ADDRESS
          CALL      PANEL_READ_HEX_BYTE
          JMP       PANEL_EDITOR_HANDLE_KEY
;------------------------------------------------------------------------------
; CMD_MOVE_PARAMETERS
; MOVE parameters are passed on the stack: destination, source end, source start.
;   Pop destination/end/start, convert logical addresses by +8000H, validate and move.
; Command: Mstart,end,destination; listed in manual Appendix 1 (PDF p.82).
; Detailed terminal description missing; keypad MOVE: 3-3-11 (PDF p.52).
;------------------------------------------------------------------------------
CMD_MOVE_PARAMETERS:                                              ; was PR049B
          POP       H
          LXI       D, BUFFER_BASE_ADDRESS
          DAD       D
          SHLD      MOVE_DESTINATION
          POP       H
          LXI       D, BUFFER_BASE_ADDRESS
          DAD       D
          SHLD      MOVE_END_DIRECTION
          POP       H
          LXI       D, BUFFER_BASE_ADDRESS
          DAD       D
          SHLD      MOVE_SOURCE_START
          XCHG
          LHLD      MOVE_END_DIRECTION
          XCHG
          CALL      COMPARE_DE_HL                                 ; CMP
          RC
          CALL      CHECK_MOVE_ADDRESSES
          RC
          LHLD      MOVE_DESTINATION
          XCHG
          JMP       MOVE_BUFFER_BLOCK
;------------------------------------------------------------------------------
; CHECK_MOVE_ADDRESSES
;   Validate all three stored MOVE addresses.
;------------------------------------------------------------------------------
CHECK_MOVE_ADDRESSES:                                             ; was PR04C7
          LHLD      MOVE_SOURCE_START
          CALL      CHECK_BUFFER_ADDRESS
          RC
          LHLD      MOVE_END_DIRECTION
          CALL      CHECK_BUFFER_ADDRESS
          RC
          LHLD      MOVE_DESTINATION
;------------------------------------------------------------------------------
; CHECK_BUFFER_ADDRESS
;   Check HL is in 8000..9FFF (CY on invalid); HL is changed by +8000H.
;------------------------------------------------------------------------------
CHECK_BUFFER_ADDRESS:                                             ; was PR04D8
          LXI       D, BUFFER_BASE_ADDRESS
          DAD       D
          MOV       A, H
          CPI       020H
          JZ        INVALID_BUFFER_ADDRESS
          CMC
          RET
INVALID_BUFFER_ADDRESS:                                           ; was A46
          MVI       A, 001H
          ANA       A
          STC
          RET
;------------------------------------------------------------------------------
; PANEL_MOVE_OR_EDITOR
;   Dispatch JOB subfunction; MOVE reads source start/end and destination.
; Command: JOB 2: MOVE (also dispatches editors). Manual 3-3-11 (PDF pp. 52).
;------------------------------------------------------------------------------
PANEL_MOVE_OR_EDITOR:                                             ; was PR04E9
          INR       A
          JNZ       PANEL_MEMORY_EDITOR
          CALL      PANEL_READ_BUFFER_ADDRESS
          RC
          RNZ
          SHLD      MOVE_SOURCE_START
          LXI       H, DISPLAY_TWO_BLANKS
          SHLD      DISPLAY_ADDRESS_HIGH
          SHLD      DISPLAY_ADDRESS_LOW
          CALL      PANEL_READ_BUFFER_ADDRESS
          RC
          RNZ
          SHLD      MOVE_END_DIRECTION
          LXI       H, DISPLAY_TWO_BLANKS
          SHLD      DISPLAY_ADDRESS_HIGH
          SHLD      DISPLAY_ADDRESS_LOW
          LHLD      MOVE_END_DIRECTION
          XCHG
          LHLD      MOVE_SOURCE_START
          MOV       A, H
          CMP       D
          JC        PANEL_MOVE_READ_DESTINATION
          RNZ
          MOV       A, L
          CMP       E
          JC        PANEL_MOVE_READ_DESTINATION
          RNZ
PANEL_MOVE_READ_DESTINATION:                                      ; was A47
          CALL      PANEL_READ_BUFFER_ADDRESS
          RC
          RNZ
          SHLD      MOVE_DESTINATION
          XCHG
;------------------------------------------------------------------------------
; MOVE_BUFFER_BLOCK
;   Choose forward/backward copying to handle overlapping source and destination.
;------------------------------------------------------------------------------
MOVE_BUFFER_BLOCK:                                                ; was PR052B
          LHLD      MOVE_SOURCE_START
          MOV       A, D
          CMP       H
          JC        MOVE_FORWARD_SETUP
          JNZ       MOVE_CHECK_SOURCE_END
          MOV       A, E
          CMP       L
          JC        MOVE_FORWARD_SETUP
          JNZ       MOVE_CHECK_SOURCE_END
MOVE_FORWARD_SETUP:                                               ; was A48
          LHLD      MOVE_END_DIRECTION
          MOV       B, H
          MOV       C, L
          LHLD      MOVE_DESTINATION
          XCHG
          LHLD      MOVE_SOURCE_START
          XRA       A
          STA       MOVE_END_DIRECTION
          CALL      COPY_DRAM_RANGE
          RET
MOVE_CHECK_SOURCE_END:                                            ; was A49
          LHLD      MOVE_END_DIRECTION
          MOV       A, H
          CMP       D
          JC        MOVE_FORWARD_SETUP
          JNZ       MOVE_OVERLAP_BACKWARD_SETUP
          MOV       A, L
          CMP       E
          JC        MOVE_FORWARD_SETUP
MOVE_OVERLAP_BACKWARD_SETUP:                                      ; was A50
          LHLD      MOVE_SOURCE_START
          XCHG
          LHLD      MOVE_DESTINATION
          XRA       A
          MOV       A, L
          SBB       E
          MOV       E, A
          MOV       A, H
          SBB       D
          MOV       D, A
          LHLD      MOVE_END_DIRECTION
          DAD       D
          PUSH      H
          LHLD      MOVE_DESTINATION
          MOV       B, H
          MOV       C, L
          POP       H
          XCHG
          LHLD      MOVE_END_DIRECTION
          MVI       A, 001H
          STA       MOVE_END_DIRECTION
          CALL      COPY_DRAM_RANGE
          LHLD      MOVE_DESTINATION
          XCHG
          DCX       D
          MOV       B, D
          MOV       C, E
          INX       D
          LHLD      MOVE_SOURCE_START
          XRA       A
          STA       MOVE_END_DIRECTION
          CALL      COPY_DRAM_RANGE
          RET
;------------------------------------------------------------------------------
; COPY_DRAM_RANGE
;   Copy HL -> DE through inclusive endpoint BC; direction in MOVE_END_DIRECTION; DRAM-only accesses.
;------------------------------------------------------------------------------
COPY_DRAM_RANGE:                                                  ; was PR059A
          MOV       A, H
          CPI       080H
          JC        COPY_CHECK_ENDPOINT
          CPI       0A0H
          JNC       COPY_CHECK_ENDPOINT
          CALL      DRAM_READ_BYTE
          PUSH      PSW
          XCHG
          MOV       A, H
          CPI       080H
          JC        COPY_RESTORE_SOURCE_POINTER
          CPI       0A0H
          JNC       COPY_RESTORE_SOURCE_POINTER
          POP       PSW
          PUSH      PSW
          CALL      DRAM_WRITE_BYTE
COPY_RESTORE_SOURCE_POINTER:                                      ; was A51
          POP       PSW
          XCHG
COPY_CHECK_ENDPOINT:                                              ; was A52
          MOV       A, H
          CMP       B
          JNZ       COPY_CHECK_DIRECTION
          MOV       A, L
          CMP       C
          RZ
COPY_CHECK_DIRECTION:                                             ; was A53
          LDA       MOVE_END_DIRECTION
          ANA       A
          JNZ       COPY_STEP_BACKWARD
          INX       H
          INX       D
          JMP       COPY_DRAM_RANGE
COPY_STEP_BACKWARD:                                               ; was A54
          DCX       H
          DCX       D
          JMP       COPY_DRAM_RANGE
;------------------------------------------------------------------------------
; PANEL_READ_BUFFER_ADDRESS
;   Read hex address; add 8000H; require result inside buffer window.
;------------------------------------------------------------------------------
PANEL_READ_BUFFER_ADDRESS:                                        ; was PR05D5
          CALL      PANEL_READ_HEX_WORD
          RC
          RNZ
          LXI       D, BUFFER_BASE_ADDRESS
          DAD       D
          MOV       A, H
          CPI       080H
          JC        PANEL_ADDRESS_INVALID
          CPI       0A0H
          JNC       PANEL_ADDRESS_INVALID
          XRA       A
          RET
PANEL_ADDRESS_INVALID:                                            ; was A55
          MVI       A, 001H
          ANA       A
          RET
;------------------------------------------------------------------------------
; FINISH_OPERATION
;   Return error-count status; terminal mode prints OK on success.
;------------------------------------------------------------------------------
FINISH_OPERATION:                                                 ; was PR05EF
          CALL      GET_ERROR_STATUS
          RNZ
          LDA       OPERATING_MODE
          DCR       A
          JZ        TX_OK
          XRA       A
          RET
;------------------------------------------------------------------------------
; CMD_BLANK_BUFFER
;   Fill selected buffer range with FFH. No readback or RAM fault test is performed.
; Manual 4-3-6 promises a RAM check; this ROM only writes, then reports error-count status.
; Command: B. Manual 4-3-6 (PDF pp. 59).
;------------------------------------------------------------------------------
CMD_BLANK_BUFFER:                                                 ; was PR05FC
          MVI       C, 0FFH
          JMP       BUFFER_FILL_INVERT_LOOP
;------------------------------------------------------------------------------
; CMD_INVERT_BUFFER
;   Complement every byte in selected buffer range.
; Command: O. Manual 4-3-7 (PDF pp. 59).
;------------------------------------------------------------------------------
CMD_INVERT_BUFFER:                                                ; was PR0601
          MVI       C, 000H
BUFFER_FILL_INVERT_LOOP:                                          ; was A56
          MOV       A, C
          ORA       A
          JNZ       BUFFER_FILL_WRITE
          RST       RST_MEM_READ
          CMA
BUFFER_FILL_WRITE:                                                ; was A57
          RST       RST_MEM_WRITE
          INX       D
          INX       H
          MOV       A, D
          CMP       B
          JC        BUFFER_FILL_INVERT_LOOP
          JMP       FINISH_OPERATION
;------------------------------------------------------------------------------
; PANEL_READ_HEX_WORD
;   Accumulate hex keys in HL and shift four address display digits; SET ends input.
; In: keyboard events. Out: HL=entered hex value; Z on SET, CY on JOB.
;------------------------------------------------------------------------------
PANEL_READ_HEX_WORD:                                              ; was PR0615
          MVI       A, 003H
          STA       SHARED_INPUT_STATE
          LXI       H, 0
PANEL_HEX_WAIT_KEY:                                               ; was A58
          RST       RST_WAIT_KEY
PANEL_HEX_ACCEPT_KEY:                                             ; was A59
          RZ
          CPI       010H
          RNC
          MOV       C, A
          MVI       B, 000H
          DAD       H
          DAD       H
          DAD       H
          DAD       H
          DAD       B
          PUSH      H
          LHLD      SHARED_INPUT_STATE
          MOV       C, L
          LXI       H, DISPLAY_PROM_TYPE
          DAD       B
PANEL_HEX_SHIFT_DIGITS:                                           ; was A60
          INX       H
          MOV       B, M
          DCX       H
          MOV       M, B
          INX       H
          DCR       C
          JNZ       PANEL_HEX_SHIFT_DIGITS
          MOV       M, A
          POP       H
          JMP       PANEL_HEX_WAIT_KEY
;------------------------------------------------------------------------------
; PANEL_READ_HEX_BYTE
;   Clear two data digits; continue hex input using current key in A.
;------------------------------------------------------------------------------
PANEL_READ_HEX_BYTE:                                              ; was PR0641
          PUSH      PSW
          LXI       H, DISPLAY_TWO_BLANKS
          SHLD      DISPLAY_DATA
          XRA       A
          MOV       L, A
          INR       A
          STA       SHARED_INPUT_STATE
          POP       PSW
          JMP       PANEL_HEX_ACCEPT_KEY
;------------------------------------------------------------------------------
; INCREMENT_DISPLAY_ADDRESS
;   Increment the four address digits (rightmost index 7).
;------------------------------------------------------------------------------
INCREMENT_DISPLAY_ADDRESS:                                        ; was PR0652
          MVI       A, 007H
;------------------------------------------------------------------------------
; STEP_DISPLAY_HEX
;   Update up to four digits at index A&0FH and step HL; bit 7 selects decrement.
;------------------------------------------------------------------------------
STEP_DISPLAY_HEX:                                                 ; was PR0654
          PUSH      B
          PUSH      H
          PUSH      PSW
          ANI       00FH
          MOV       C, A
          MVI       B, 000H
          LXI       H, DISPLAY_COMMAND
          DAD       B
          MVI       B, 004H
DISPLAY_HEX_CARRY_LOOP:                                           ; was A61
          POP       PSW
          PUSH      PSW
          RLC
          MOV       A, M
          JC        DISPLAY_HEX_DECREMENT_DIGIT
          INR       A
          ANI       00FH
          JMP       DISPLAY_HEX_STORE_DIGIT
DISPLAY_HEX_DECREMENT_DIGIT:                                      ; was A62
          DCR       A
          ANI       00FH
          CPI       00FH
DISPLAY_HEX_STORE_DIGIT:                                          ; was A63
          MOV       M, A
          JNZ       DISPLAY_HEX_STEP_POINTER
          DCX       H
          DCR       B
          JNZ       DISPLAY_HEX_CARRY_LOOP
DISPLAY_HEX_STEP_POINTER:                                         ; was A64
          POP       PSW
          POP       H
          POP       B
          INX       H
          RLC
          RNC
          DCX       H
          DCX       H
          RET
;------------------------------------------------------------------------------
; BEGIN_VOLTAGE_PASS
;   Set voltage selector A, reset display/counters and reload PROM/buffer range.
;------------------------------------------------------------------------------
BEGIN_VOLTAGE_PASS:                                               ; was PR0686
          STA       VOLTAGE_SELECT
          LDA       PROM_CONTROL_STATE
          ORA       A
          CNZ       APPLY_PROM_CONTROL
          CALL      CLEAR_DISPLAY
          DCX       H
          MOV       M, B                                          ; CLEAR_DISPLAY returned B=0, HL=6048H; after DCX, set digit 6047H to zero
          MOV       L, B
          MOV       H, B
          SHLD      SHARED_OFFSET_SUM
          CALL      SHOW_VOLTAGE_MARKERS
          CALL      DELAY_BC
          JMP       LOAD_PROM_BUFFER_RANGE
;------------------------------------------------------------------------------
; REPORT_COMPARE_ERROR
;   Panel: beep/pause; terminal: count and print voltage/address/actual/expected data.
;------------------------------------------------------------------------------
REPORT_COMPARE_ERROR:                                             ; was PR06A3
          PUSH      B
          MOV       B, A
          LDA       OPERATING_MODE
          ORA       A
          JNZ       REPORT_ERROR_SERIAL_MODE
          CALL      ERROR_BEEP
          POP       B
          LDA       SHARED_INPUT_STATE
          ORA       A
          CNZ       INCREMENT_ERROR_COUNT
          RST       RST_WAIT_KEY
          RET
REPORT_ERROR_SERIAL_MODE:                                         ; was A65
          CALL      INCREMENT_ERROR_COUNT
          DCR       A
          JZ        REPORT_ERROR_TERMINAL_DETAIL
          POP       B
          RET
REPORT_ERROR_TERMINAL_DETAIL:                                     ; was A66
          PUSH      H
          PUSH      D
          MOV       D, A
          CALL      TX_CRLF
          LDA       VOLTAGE_SELECT
          ANI       0C0H
          CALL      SWAP_NIBBLES
          MOV       E, A
          LXI       H, VOLTAGE_TEXT_TABLE
          DAD       D
          CALL      TX_STRING_HL                                  ; !!PRINT
          RST       RST_TX_STRING
          DB        'V', ' '+80H
          POP       H
          CALL      TX_HEX_WORD
          XCHG
          POP       H
          RST       RST_MEM_READ
          MOV       C, A
          LDA       TARGET_ACCESS_MODE
          ORA       A
          JNZ       REPORT_ERROR_ACTUAL_EXPECTED
          DCR       B
          MVI       C, 0FFH
REPORT_ERROR_ACTUAL_EXPECTED:                                     ; was A67
          CALL      TX_TWO_SPACES
          CALL      TX_DATA_OR_DASHES
          RST       RST_TX_STRING
          DB        '*'+80H
          MOV       A, C
          CALL      TX_HEX_BYTE
          POP       B
          RET
;------------------------------------------------------------------------------
; INCREMENT_ERROR_COUNT
;   Increment 16-bit error counter at SHARED_ERRORS_SAVED_SP, preserving HL.
;------------------------------------------------------------------------------
INCREMENT_ERROR_COUNT:                                            ; was PR06FB
          PUSH      H
          LHLD      SHARED_ERRORS_SAVED_SP
          INX       H
          SHLD      SHARED_ERRORS_SAVED_SP
          POP       H
          RET
;------------------------------------------------------------------------------
; TX_DATA_OR_DASHES
;   Print B as hex, or -- when display indicates an invalid logic level.
;------------------------------------------------------------------------------
TX_DATA_OR_DASHES:                                                ; was PR0705
          LDA       DISPLAY_PROM_TYPE
          CPI       011H
          MOV       A, B
          JNZ       TX_HEX_BYTE
TX_INVALID_DATA_DASHES:                                           ; was A68
          RST       RST_TX_STRING
          DB        '-', '-'+80H
          RET
;------------------------------------------------------------------------------
; TX_BYTE_IF_VALID
;   Z: output A as hex; NZ: output --.
;------------------------------------------------------------------------------
TX_BYTE_IF_VALID:                                                 ; was PR0712
          JZ        TX_HEX_BYTE
          JMP       TX_INVALID_DATA_DASHES
;------------------------------------------------------------------------------
; SHOW_VOLTAGE_MARKERS
;   Set decimal-point indicators from voltage selection at VOLTAGE_SELECT.
;------------------------------------------------------------------------------
SHOW_VOLTAGE_MARKERS:                                             ; was PR0718
          LDA       VOLTAGE_SELECT
          LXI       H, DISPLAY_BLANKS_SECOND_DP
          SUI       040H
          JC        SHOW_VOLTAGE_STORE_MARKERS
          LXI       H, DISPLAY_BLANKS_FIRST_DP
          SUI       040H
          JC        SHOW_VOLTAGE_STORE_MARKERS
          MOV       L, H
SHOW_VOLTAGE_STORE_MARKERS:                                       ; was A69
          SHLD      DISPLAY_DATA
          RET
;------------------------------------------------------------------------------
; VOLTAGE_TEXT_TABLE: voltage-margin text for serial reports
;------------------------------------------------------------------------------
VOLTAGE_TEXT_TABLE:                                               ; was A0730
          DB        '5.2', '5'+80H
          DB        '4.7', '5'+80H
          DB        '5.0', '0'+80H
;------------------------------------------------------------------------------
; READ_PROM_AND_DISPLAY
;   Read PROM at DE; show returned byte/invalid-level marker in command/type digits.
;------------------------------------------------------------------------------
READ_PROM_AND_DISPLAY:                                            ; was PR073C
          CALL      READ_PROM_LOGIC_LEVELS
          PUSH      D
          LXI       D, DISPLAY_COMMAND
          JMP       SHOW_BYTE_AT_DISPLAY_POINTER
;------------------------------------------------------------------------------
; READ_BUFFER_AND_DISPLAY
;   Read [HL] through RST 2, then show byte in data digits.
;------------------------------------------------------------------------------
READ_BUFFER_AND_DISPLAY:                                          ; was PR0746
          RST       RST_MEM_READ
;------------------------------------------------------------------------------
; SHOW_BYTE_VALID
;   Clear error status flags before showing A in data digits.
;------------------------------------------------------------------------------
SHOW_BYTE_VALID:                                                  ; was PR0747
          PUSH      B
          MOV       B, A
          XRA       A
          MOV       A, B
          POP       B
;------------------------------------------------------------------------------
; SHOW_DATA_BYTE
;   Show A at 6042..6043; Z means valid hex, NZ means dashes.
;------------------------------------------------------------------------------
SHOW_DATA_BYTE:                                                   ; was PR074C
          PUSH      D
          LXI       D, DISPLAY_DATA
SHOW_BYTE_AT_DISPLAY_POINTER:                                     ; was A70
          PUSH      PSW
          CALL      STORE_DISPLAY_BYTE
          POP       PSW
          POP       D
          RET
          RST       RST_MEM_READ
;------------------------------------------------------------------------------
; STORE_DISPLAY_BYTE
;   Write two hex digits of A at DE; preserve Z validity between nibbles.
;------------------------------------------------------------------------------
STORE_DISPLAY_BYTE:                                               ; was PR0758
          PUSH      PSW
          CALL      SWAP_NIBBLES
          CALL      STORE_DISPLAY_NIBBLE
          POP       PSW
;------------------------------------------------------------------------------
; STORE_DISPLAY_NIBBLE
;   Write low nibble at DE if Z, otherwise dash; preserve decimal point; advance DE.
;------------------------------------------------------------------------------
STORE_DISPLAY_NIBBLE:                                             ; was PR0760
          PUSH      B
          MVI       B, 011H
          JNZ       STORE_NIBBLE_WITH_DECIMAL_POINT
          ANI       00FH
          MOV       B, A
STORE_NIBBLE_WITH_DECIMAL_POINT:                                  ; was A71
          LDAX      D
          ANI       080H
          ORA       B
          STAX      D
          INX       D
          POP       B
          RET
;------------------------------------------------------------------------------
; PARSE_ONE_HEX_ARGUMENT
;   Set argument count C=1; fall through to stack-based parser.
;------------------------------------------------------------------------------
PARSE_ONE_HEX_ARGUMENT:                                           ; was PR0771
          MVI       C, 001H
;------------------------------------------------------------------------------
; PARSE_HEX_ARGUMENTS
;   Parse C hex values, echo digits and push values onto caller stack; separators via CLASSIFY_SEPARATOR.
;------------------------------------------------------------------------------
PARSE_HEX_ARGUMENTS:                                              ; was PR0773
          LXI       H, 0
PARSE_HEX_CHARACTER:                                              ; was A72
          MOV       B, A
          CALL      ASCII_TO_HEX_NIBBLE
          JC        PARSE_HEX_PUSH_VALUE
          DAD       H
          DAD       H
          DAD       H
          DAD       H
          ORA       L
          MOV       L, A
          MOV       A, B
          CALL      SERIAL_TX
          JMP       PARSE_HEX_READ_NEXT
PARSE_HEX_PUSH_VALUE:                                             ; was A73
          XTHL
          PUSH      H
          MOV       A, B
          CALL      CLASSIFY_SEPARATOR
          JNC       PARSE_HEX_CHECK_SEPARATOR
          DCR       C
          JNZ       COMMAND_ERROR
          RET
PARSE_HEX_CHECK_SEPARATOR:                                        ; was A74
          JNZ       COMMAND_ERROR
          DCR       C
          RZ
          MOV       A, B
          CALL      SERIAL_TX
          LXI       H, 0
PARSE_HEX_READ_NEXT:                                              ; was A75
          CALL      SERIAL_RX_ASCII
          JMP       PARSE_HEX_CHARACTER
;------------------------------------------------------------------------------
; CLASSIFY_SEPARATOR
;   CR/LF: CY+Z; space/comma/slash: NC+Z; other input: NC+NZ.
;------------------------------------------------------------------------------
CLASSIFY_SEPARATOR:                                               ; was PR07AA
          CPI       00AH
          STC
          RZ
          CPI       00DH
          STC
          RZ
          CPI       020H
          RZ
          CPI       02CH
          RZ
          CPI       02FH
          STC
          CMC
          RET
;------------------------------------------------------------------------------
; SERIAL_RX_ASCII
;   RST 6: raw receive, strip bit 7 and discard NUL/DEL; return 7-bit A.
;------------------------------------------------------------------------------
SERIAL_RX_ASCII:                                                  ; was RST6
          RST       RST_RX_RAW
          ANI       07FH
          JZ        SERIAL_RX_ASCII
          CPI       07FH
          JZ        SERIAL_RX_ASCII
          RET
;------------------------------------------------------------------------------
; TX_NEWLINE_HEX_WORD
;   CR/LF then print HL as four hex digits.
; Flow: TX_CRLF returns here; execution continues into TX_HEX_WORD.
;------------------------------------------------------------------------------
TX_NEWLINE_HEX_WORD:                                              ; was PR07C9
          CALL      TX_CRLF
;------------------------------------------------------------------------------
; TX_HEX_WORD
;   Print HL as four hex digits, high byte first.
;------------------------------------------------------------------------------
TX_HEX_WORD:                                                      ; was PR07CC
          MOV       A, H
          CALL      TX_HEX_BYTE
          MOV       A, L
          JMP       TX_HEX_BYTE
;------------------------------------------------------------------------------
; TX_THREE_SPACES
;   Fall through through the two-space and one-space entries.
; Flow: output one space, then continue into TX_TWO_SPACES.
;------------------------------------------------------------------------------
TX_THREE_SPACES:                                                  ; was PR07D4
          CALL      TX_SPACE
;------------------------------------------------------------------------------
; TX_TWO_SPACES
;   Output two spaces by fall-through.
; Flow: output one space, then continue into TX_SPACE.
;------------------------------------------------------------------------------
TX_TWO_SPACES:                                                    ; was PR07D7
          CALL      TX_SPACE
;------------------------------------------------------------------------------
; TX_SPACE
;   Output one space through inline-string service.
;------------------------------------------------------------------------------
TX_SPACE:                                                         ; was PR07DA
          RST       RST_TX_STRING
          DB        ' '+80H
          RET
;------------------------------------------------------------------------------
; TX_INLINE_STRING
;   RST 5: text follows instruction; bit 7 terminates last byte; skip text on return, A=0.
; In: inline DB bytes directly after RST RST_TX_STRING; last byte has bit 7 set.
; Return skips the inline data. Out: A=0; HL preserved.
;------------------------------------------------------------------------------
TX_INLINE_STRING:                                                 ; was RST5
          XTHL
          CALL      TX_STRING_HL
          XTHL
          XRA       A                                             ; clear A
          RET
;------------------------------------------------------------------------------
; TX_STRING_HL
;   Print 7-bit characters at HL until bit 7 set; HL ends just after last character.
; In: HL=text address. Out: HL=first byte after text.
; Last character has bit 7 set; bit 7 is stripped before transmission.
;------------------------------------------------------------------------------
TX_STRING_HL:                                                     ; was PR07E4
          MOV       A, M
          ANI       07FH
          CALL      SERIAL_TX
          MOV       A, M
          INX       H
          RLC
          JNC       TX_STRING_HL
          RET
;------------------------------------------------------------------------------
; INIT_BUFFER_RANGE
;   Set output address offset=8000H; DE=end (inclusive), HL=start, B=size in pages.
;------------------------------------------------------------------------------
INIT_BUFFER_RANGE:                                                ; was PR07F1
          PUSH      H
          LXI       H, BUFFER_BASE_ADDRESS
          SHLD      SHARED_OFFSET_SUM
          POP       H
          XCHG
          MOV       H, B
CALCULATE_INCLUSIVE_END:                                          ; was A76
          DAD       D
          XCHG
          DCX       D
          RET
;------------------------------------------------------------------------------
; INIT_RECEIVE_RANGE
;   Compute receive extent/mask; account for even/odd selection (X7 bit 4).
;------------------------------------------------------------------------------
INIT_RECEIVE_RANGE:                                               ; was PR07FF
          LDA       STATUS_FLAGS
          ANI       010H
          MOV       A, B
          JZ        RECEIVE_RANGE_SET_MASK
          DCR       B
          JZ        RECEIVE_RANGE_SET_MASK
          RAL
RECEIVE_RANGE_SET_MASK:                                           ; was A77
          MOV       H, A
          DCR       A
          STA       RECEIVE_PAGE_MASK
          JMP       CALCULATE_INCLUSIVE_END
;------------------------------------------------------------------------------
; GET_PROM_CONTROL_BYTE
; PROM control lookup; PROM_TYPE_INDEX holds the PROM type (X8 start code is D6068).
;   A = table[type*12 + A], type at 6069. PROM control table, not ASCII tape settings.
; 6068=ASCII start code; 6069=PROM type index. These adjacent bytes have different roles.
;------------------------------------------------------------------------------
GET_PROM_CONTROL_BYTE:                                            ; was PR0815
          PUSH      D
          PUSH      H
          LHLD      PROM_TYPE_INDEX
          MVI       H, 000H
          DAD       H
          DAD       H
          MOV       E, L
          MOV       D, H
          DAD       H
          DAD       D
          LXI       D, PROM_CONTROL_TABLE
          DAD       D
          MOV       E, A
          MVI       D, 000H
          DAD       D
          MOV       A, M
          POP       H
          POP       D
          RET
;------------------------------------------------------------------------------
; PROM_CONTROL_INLINE
; RST 4 ENTRY POINT
; PICKS BYTE AFTER RST CODE
;   RST 4: take inline table index, store at PROM_CONTROL_STATE, apply PROM control state.
; In: one inline DB control-table index after RST RST_PROM_CONTROL.
; Return skips that one data byte; control-state cache and I/O are updated.
;------------------------------------------------------------------------------
PROM_CONTROL_INLINE:                                              ; was RST4
          XTHL
          MOV       A, M
          INX       H
          XTHL
          STA       PROM_CONTROL_STATE
;------------------------------------------------------------------------------
; APPLY_PROM_CONTROL
;   Look up index A; combine voltage bits and cached control bit 3, output port C1.
;------------------------------------------------------------------------------
APPLY_PROM_CONTROL:                                               ; was PR0835
          CALL      GET_PROM_CONTROL_BYTE
          PUSH      B
          MOV       C, A
          ANI       030H
          JZ        PROM_CONTROL_USE_VOLTAGE_SELECT
          MOV       A, C
          ANI       0CFH
          JMP       PROM_CONTROL_MERGE_CACHE
PROM_CONTROL_USE_VOLTAGE_SELECT:                                  ; was A78
          LDA       VOLTAGE_SELECT
          RRC
          RRC
          CMA
          ANI       030H
          ORA       C
PROM_CONTROL_MERGE_CACHE:                                         ; was A79
          MOV       C, A
          LDA       PROM_CONTROL_CACHE
          ANI       008H
          ORA       C
          POP       B
;------------------------------------------------------------------------------
; WRITE_PROM_CONTROL
;   Output A to port C1 and cache in PROM_CONTROL_CACHE.
;------------------------------------------------------------------------------
WRITE_PROM_CONTROL:                                               ; was PR0856
          OUT       PORT_PROM_CONTROL
          STA       PROM_CONTROL_CACHE
          RET
;------------------------------------------------------------------------------
; NEXT_AND_COMPARE_END
;   Increment HL, then compare DE-HL.
;------------------------------------------------------------------------------
NEXT_AND_COMPARE_END:                                             ; was PR085C
          INX       H
;------------------------------------------------------------------------------
; COMPARE_DE_HL
; CMP
;   Unsigned DE-HL; Z when equal, CY when HL exceeds DE; destroys A.
; In: DE=end address, HL=current address. Out: Z if equal, CY if HL>DE.
; Changes A and flags; DE and HL remain unchanged.
;------------------------------------------------------------------------------
COMPARE_DE_HL:                                                    ; was PR085D
          MOV       A, D
          SUB       H
          RNZ
          MOV       A, E
          SUB       L
          RET
;------------------------------------------------------------------------------
; RX_ASCII_HEX_NIBBLE
;   Receive with end-code handling, then convert ASCII hex.
;------------------------------------------------------------------------------
RX_ASCII_HEX_NIBBLE:                                              ; was PR0863
          CALL      RX_WITH_END_CODE
;------------------------------------------------------------------------------
; ASCII_TO_HEX_NIBBLE
;   Accept uppercase 0..9/A..F; CY on invalid; result in A.
;------------------------------------------------------------------------------
ASCII_TO_HEX_NIBBLE:                                              ; was PR0866
          ADI       0B9H
          RC
          SUI       0E9H
          RC
          CPI       00AH
          CMC
          RNC
          SUI       007H
          CPI       00AH
          RET
;------------------------------------------------------------------------------
; CMD_PUNCH_RANGE
;   Pop explicit end/start; reject reversed range; continue tape output.
; Command: Pn (physical start,end). Manual 4-4-6 (PDF pp. 66-67).
;------------------------------------------------------------------------------
CMD_PUNCH_RANGE:                                                  ; was PR0875
          POP       D
          POP       H
          CALL      COMPARE_DE_HL
          JNC       PUNCH_CHECK_HANDSHAKE
          RET
;------------------------------------------------------------------------------
; CMD_PUNCH_BUFFER
;   Initialize selected buffer range; optional DC1/DC3 handshake; output selected tape format.
; Command: P. Manual 4-3-8 (PDF pp. 59-60).
;------------------------------------------------------------------------------
CMD_PUNCH_BUFFER:                                                 ; was PR087E
          CALL      INIT_BUFFER_RANGE
PUNCH_CHECK_HANDSHAKE:                                            ; was A80
          CALL      WAIT_PUNCH_DC1_DC3
          RNC
          CALL      TX_TAPE_LEADER
          CALL      PUNCH_FORMAT_DISPATCH
;------------------------------------------------------------------------------
; TX_TAPE_LEADER
;   Output 60 NUL bytes; also used as trailer.
;------------------------------------------------------------------------------
TX_TAPE_LEADER:                                                   ; was PR088B
          MVI       B, 03CH
TX_NUL_BLOCK:                                                     ; was A81
          XRA       A
TX_REPEATED_BYTE_LOOP:                                            ; was A82
          CALL      SERIAL_TX
          DCR       B
          JNZ       TX_REPEATED_BYTE_LOOP
          RET
;------------------------------------------------------------------------------
; TX_TAPE_GAP
;   Output 10 NUL bytes.
;------------------------------------------------------------------------------
TX_TAPE_GAP:                                                      ; was PR0896
          MVI       B, 00AH
          JMP       TX_NUL_BLOCK
;------------------------------------------------------------------------------
; PUNCH_FORMAT_DISPATCH
; Tape something... (loads tape format)
;   X6: 0 -> 6080H user hook; 1 Intel, 2 binary, 3 Motorola, 4 ASCII, 5 Tektronix.
; UNDOCUMENTED X6=0 extension: JMP USER_CODE_RAM, no X7-bit-7 gate here.
;   Same entry is used for reading and punching; user routine must know its context.
;------------------------------------------------------------------------------
PUNCH_FORMAT_DISPATCH:                                            ; was PR089B
          LDA       TAPE_FORMAT
          ORA       A
          JZ        USER_CODE_RAM
          DCR       A
          JZ        PUNCH_INTEL_HEX
          DCR       A
          JZ        PUNCH_BINARY_DATA
          DCR       A
          JZ        PUNCH_MOTOROLA
          DCR       A
          JZ        PUNCH_ASCII_HEX_SPACE
          DCR       A
          JZ        PUNCH_TEKTRONIX
          RNZ
PUNCH_TEKTRONIX:                                                  ; was A83
          CALL      PUNCH_TEKTRONIX_RECORDS
          RST       RST_TX_STRING
          DB        '/00000000', 0DH, 0AH+80H
          CALL      TX_TAPE_LEADER
          RET
;------------------------------------------------------------------------------
; PUNCH_TEKTRONIX_RECORDS
;   Emit /address/count/header-checksum/data/checksum records.
;------------------------------------------------------------------------------
PUNCH_TEKTRONIX_RECORDS:                                          ; was PR08CA
          CALL      TX_TAPE_LEADER
PUNCH_TEK_RECORD_LOOP:                                            ; was A84
          CALL      COMPARE_DE_HL
          RC
          RST       RST_TX_STRING
          DB        '/'+80H
          CALL      GET_RECORD_LENGTH
          MVI       C, 000H
          CALL      ADD_NIBBLE_CHECKSUM
          PUSH      PSW
          CALL      TX_RECORD_ADDRESS
          POP       PSW
          PUSH      B
          CALL      TX_HEX_BYTE_CHECKSUM
          POP       B
          MOV       A, C
          CALL      TX_HEX_BYTE_CHECKSUM
          MVI       C, 000H
          CALL      TX_RECORD_DATA
          MOV       A, C
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_CRLF
          JMP       PUNCH_TEK_RECORD_LOOP
;------------------------------------------------------------------------------
; PUNCH_ASCII_HEX_SPACE
; ASCII HEX SPACE output: X8 start byte, spaced hexadecimal data, X9 end byte.
;   Emit X8 start code, spaced hex bytes, X9 end code.
;------------------------------------------------------------------------------
PUNCH_ASCII_HEX_SPACE:                                            ; was PR08F8
          LDA       TAPE_START_CODE
          CALL      SERIAL_TX
PUNCH_ASCII_BYTE_LOOP:                                            ; was A84X
          RST       RST_MEM_READ                                  ; !!
          CALL      TX_HEX_BYTE
          CALL      TX_SPACE
          CALL      NEXT_AND_COMPARE_END
          JNC       PUNCH_ASCII_BYTE_LOOP
          LDA       TAPE_END_CODE
          JMP       SERIAL_TX
;------------------------------------------------------------------------------
; PUNCH_MOTOROLA
;   Emit S0 header, S1 data and S9 trailer.
;------------------------------------------------------------------------------
PUNCH_MOTOROLA:                                                   ; was PR0911
          RST       RST_TX_STRING
          DB        'S', '0'+80H
          CALL      TX_SREC_CONTROL_SUFFIX
          CALL      PUNCH_S1_RECORDS
          RST       RST_TX_STRING
          DB        'S', '9'+80H
;------------------------------------------------------------------------------
; TX_SREC_CONTROL_SUFFIX
;   Emit 030000FC and CR/LF, used after S0 and S9.
;------------------------------------------------------------------------------
TX_SREC_CONTROL_SUFFIX:                                           ; was PR091D
          RST       RST_TX_STRING
          DB        '030000FC', 0DH, 0AH+80H
          RET
;------------------------------------------------------------------------------
; PUNCH_S1_RECORDS
;   Emit Motorola data records with one's-complement checksum.
;------------------------------------------------------------------------------
PUNCH_S1_RECORDS:                                                 ; was PR0929
          CALL      COMPARE_DE_HL
          JC        TX_TAPE_GAP
          MOV       A, L
          ORA       A
          CZ        TX_TAPE_GAP
          RST       RST_TX_STRING
          DB        'S', '1'+80H
          CALL      GET_RECORD_LENGTH
          MOV       B, A
          ADI       003H
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_RECORD_ADDRESS
          CALL      TX_RECORD_DATA
          MOV       A, C
          CMA
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_CRLF
          JMP       PUNCH_S1_RECORDS
;------------------------------------------------------------------------------
; CMD_BINARY_PUNCH
;   G command: selected buffer as binary, independent of X6.
; Command: G. Manual 4-4-1 (PDF pp. 64).
;------------------------------------------------------------------------------
CMD_BINARY_PUNCH:                                                 ; was PR0951
          CALL      INIT_BUFFER_RANGE
          CALL      WAIT_PUNCH_DC1_DC3
          RNC
          CALL      TX_TAPE_LEADER
          CALL      PUNCH_BINARY_DATA
          JMP       TX_TAPE_LEADER
;------------------------------------------------------------------------------
; TEST_PUNCH_FLOW_CONTROL
;   Z iff communication mode=2 and X7 bit 1 set.
;------------------------------------------------------------------------------
TEST_PUNCH_FLOW_CONTROL:                                          ; was PR0961
          LDA       OPERATING_MODE
          SUI       002H
          RNZ
          LDA       STATUS_FLAGS
          CMA
          ANI       002H
          RET
;------------------------------------------------------------------------------
; WAIT_PUNCH_DC1_DC3
;   When enabled, DC1 returns CY (send), DC3 returns NC (cancel).
;------------------------------------------------------------------------------
WAIT_PUNCH_DC1_DC3:                                               ; was PR096E
          CALL      TEST_PUNCH_FLOW_CONTROL
          STC
          RNZ
PUNCH_WAIT_CONTROL_CHARACTER:                                     ; was A85
          RST       RST_RX_ASCII
          CPI       013H
          RZ
          CPI       011H
          JNZ       PUNCH_WAIT_CONTROL_CHARACTER
          STC
          RET
;------------------------------------------------------------------------------
; PUNCH_BINARY_DATA
;   Emit FFH synchronization byte, followed by raw data from HL..DE.
;------------------------------------------------------------------------------
PUNCH_BINARY_DATA:                                                ; was PR097E
          MVI       A, 0FFH
          CALL      SERIAL_TX
PUNCH_BINARY_BYTE_LOOP:                                           ; was A86
          RST       RST_MEM_READ
          CALL      SERIAL_TX
          CALL      NEXT_AND_COMPARE_END
          JNC       PUNCH_BINARY_BYTE_LOOP
          RET
;------------------------------------------------------------------------------
; PUNCH_INTEL_HEX
;   Emit data records followed by :00000001FF.
;------------------------------------------------------------------------------
PUNCH_INTEL_HEX:                                                  ; was PR098E
          CALL      PUNCH_INTEL_RECORDS
          RST       RST_TX_STRING
          DB        ':00000001FF', 0DH, 0AH+80H
          RET
;------------------------------------------------------------------------------
; PUNCH_INTEL_RECORDS
;   Emit type-00 Intel HEX records with two's-complement checksum.
;------------------------------------------------------------------------------
PUNCH_INTEL_RECORDS:                                              ; was PR09A0
          CALL      COMPARE_DE_HL
          JC        TX_TAPE_GAP
          MOV       A, L
          ORA       A
          CZ        TX_TAPE_GAP
          RST       RST_TX_STRING
          DB        ':'+80H
          CALL      GET_RECORD_LENGTH
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_RECORD_ADDRESS
          XRA       A
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_RECORD_DATA
          XRA       A
          SUB       C
          CALL      TX_HEX_BYTE_CHECKSUM
          CALL      TX_CRLF
          JMP       PUNCH_INTEL_RECORDS
;------------------------------------------------------------------------------
; TX_RECORD_ADDRESS
;   Output HL+word(SHARED_OFFSET_SUM), adding address bytes to checksum C.
;------------------------------------------------------------------------------
TX_RECORD_ADDRESS:                                                ; was PR09C8
          PUSH      D
          XCHG
          LHLD      SHARED_OFFSET_SUM
          DAD       D
          CALL      TX_HEX_WORD_CHECKSUM
          XCHG
          POP       D
          RET
;------------------------------------------------------------------------------
; GET_RECORD_LENGTH
;   Return count in A/B, bounded by next 16-byte boundary and inclusive end DE; C=0.
;------------------------------------------------------------------------------
GET_RECORD_LENGTH:                                                ; was PR09D4
          LXI       B, RECORD_COUNT_CHECKSUM_INIT
          MOV       A, D
          CMP       H
          JNZ       RECORD_LENGTH_TO_BOUNDARY
          MOV       A, L
          ORI       00FH
          CMP       E
          JC        RECORD_LENGTH_TO_BOUNDARY
          MOV       A, E
          ANI       00FH
          INR       A
          MOV       B, A
RECORD_LENGTH_TO_BOUNDARY:                                        ; was A87
          MOV       A, L
          ANI       00FH
          CMA
          INR       A
          ADD       B
          MOV       B, A
          RET
;------------------------------------------------------------------------------
; TX_RECORD_DATA
;   Output B bytes from HL with checksum accumulation, advancing HL.
;------------------------------------------------------------------------------
TX_RECORD_DATA:                                                   ; was PR09F0
          RST       RST_MEM_READ
          CALL      TX_HEX_BYTE_CHECKSUM
          INX       H
          DCR       B
          JNZ       TX_RECORD_DATA
          RET
;------------------------------------------------------------------------------
; SWAP_NIBBLES
;   Rotate A left four times; CY reflects last rotated bit.
;------------------------------------------------------------------------------
SWAP_NIBBLES:                                                     ; was PR09FA
          RLC
          RLC
          RLC
          RLC
          RET
;------------------------------------------------------------------------------
; CMD_COMPARE_TAPE_AT
;   Set receive-compare flag, then consume explicit base address.
; Command: Cn (physical base address). Manual 4-4-5 (PDF pp. 66).
;------------------------------------------------------------------------------
CMD_COMPARE_TAPE_AT:                                              ; was PR09FF
          LXI       H, TARGET_ACCESS_MODE
          DCR       M
;------------------------------------------------------------------------------
; CMD_READ_TAPE_AT
;   Pop physical base address into receive offset SHARED_OFFSET_SUM; read selected format.
; Command: Rn (physical base address). Manual 4-3-12 (PDF pp. 62).
;------------------------------------------------------------------------------
CMD_READ_TAPE_AT:                                                 ; was PR0A03
          POP       H
          JMP       READ_SET_BASE_ADDRESS
;------------------------------------------------------------------------------
; PANEL_READ_OR_EDIT
;   Dispatch JOB 0..3; tape-read path obtains a base address from keys.
;------------------------------------------------------------------------------
PANEL_READ_OR_EDIT:                                               ; was PR0A07
          POP       B
          INR       A
          JNZ       PANEL_MOVE_OR_EDITOR
          CALL      PANEL_READ_HEX_WORD
          RC
          RNZ
READ_SET_BASE_ADDRESS:                                            ; was A88
          SHLD      SHARED_OFFSET_SUM
          MVI       B, 001H
;------------------------------------------------------------------------------
; CMD_READ_TAPE
;   Enter receive mode, send optional DC1, parse format, send optional DC3, report status.
; Command: R. Manual 4-3-9 (PDF pp. 60).
;------------------------------------------------------------------------------
CMD_READ_TAPE:                                                    ; was PR0A16
          XCHG
          LXI       H, TRANSFER_IRQ_INHIBIT
          INR       M
          CALL      TX_READ_DC1
          CALL      READ_FORMAT_DISPATCH
READ_FINISH_AND_REPORT:                                           ; was A89
          PUSH      PSW
          CALL      TEST_READ_FLOW_CONTROL
          MVI       A, 013H
          CZ        SERIAL_TX
          POP       PSW
          RNZ
          LDA       OPERATING_MODE
          ORA       A
          JZ        READ_SHOW_FINAL_ADDRESS
          SUI       002H
          RZ
          CALL      TX_SPACE
          CALL      TX_HEX_WORD
;------------------------------------------------------------------------------
; TX_OK
;   Output space followed by OK.
;------------------------------------------------------------------------------
TX_OK:                                                            ; was PR0A3C
          RST       RST_TX_STRING
          DB        ' O', 'K'+80H
          RET
READ_SHOW_FINAL_ADDRESS:                                          ; was A90
          PUSH      H
          CALL      ENTER_PANEL_MODE
          POP       H
          LXI       D, DISPLAY_ADDRESS_HIGH
          XRA       A
          MOV       A, H
          CALL      STORE_DISPLAY_BYTE
          XRA       A
          MOV       A, L
          CALL      STORE_DISPLAY_BYTE
          JMP       PANEL_COMMAND_LOOP
;------------------------------------------------------------------------------
; READ_FORMAT_DISPATCH
;   Save unwind SP at SHARED_ERRORS_SAVED_SP; X6=0 jumps to 6080H; X6=1..5 select built-in parsers.
; UNDOCUMENTED X6=0 extension also reached here after receive setup.
;   Original ROM addresses in labels are historical: this edited source has shifted code.
;------------------------------------------------------------------------------
READ_FORMAT_DISPATCH:                                             ; was PR0A56
          LXI       H, 0
          DAD       SP
          SHLD      SHARED_ERRORS_SAVED_SP
          MVI       L, LOW(STACK_SPACE)
          CALL      INIT_RECEIVE_RANGE
          SHLD      RECEIVE_SOURCE_ADDRESS
          LDA       TAPE_FORMAT
          ORA       A
          JZ        USER_CODE_RAM
          DCR       A
          JZ        READ_INTEL_HEX
          DCR       A
          JZ        READ_BINARY_DATA
          DCR       A
          JZ        READ_MOTOROLA
          DCR       A
          JZ        READ_ASCII_HEX_SPACE
          DCR       A
          JZ        READ_TEK_RECORD_LOOP
          RNZ
; Tektronix: counts above 16 return an error; zero ends before header checksum.
; Appendix 2-5 (PDF 87) documents zero-count termination and // comments through CR.
READ_TEK_RECORD_LOOP:                                             ; was A91
          CALL      CHECK_RECEIVE_LIMIT
          RST       RST_RX_ASCII
          SUI       02FH
          JNZ       READ_TEK_RECORD_LOOP
          MOV       C, A
          PUSH      D
          CALL      READ_TEK_ADDRESS
          POP       D
          JC        READ_TEK_RECORD_LOOP
          CALL      RX_HEX_BYTE_CHECKSUM
          CPI       011H
          RNC
          MOV       B, A
          ORA       A
          RZ
          PUSH      B
          CALL      RX_HEX_BYTE_CHECKSUM
          POP       B
          CMP       C
          RNZ
          MVI       C, 000H
          CALL      READ_RECORD_DATA
          PUSH      B
          CALL      RX_HEX_BYTE_CHECKSUM
          POP       B
          CMP       C
          RNZ
          JMP       READ_TEK_RECORD_LOOP
;------------------------------------------------------------------------------
; READ_ASCII_HEX_SPACE
;   Wait for X8 unless zero; skip nonhex before a byte; X9 ends transfer.
;------------------------------------------------------------------------------
READ_ASCII_HEX_SPACE:                                             ; was PR0AB2
          LDA       TAPE_START_CODE
          ORA       A
          JZ        READ_ASCII_BYTE_LOOP
          MOV       B, A
READ_ASCII_WAIT_START_CODE:                                       ; was A93
          CALL      RX_WITH_END_CODE
          CMP       B
          JNZ       READ_ASCII_WAIT_START_CODE
READ_ASCII_BYTE_LOOP:                                             ; was A92
          CALL      CHECK_RECEIVE_LIMIT
          CALL      RX_ASCII_HEX_NIBBLE
          JC        READ_ASCII_BYTE_LOOP
          CALL      SWAP_NIBBLES
          MOV       C, A
          CALL      RX_ASCII_HEX_NIBBLE
          JC        ABORT_RECEIVE_ERROR
          ORA       C
          CALL      STORE_RECEIVED_AND_ADVANCE
          JMP       READ_ASCII_BYTE_LOOP
;------------------------------------------------------------------------------
; RX_WITH_END_CODE
;   Read filtered ASCII; matching X9 unwinds transfer via saved SP.
;------------------------------------------------------------------------------
RX_WITH_END_CODE:                                                 ; was PR0ADB
          RST       RST_RX_ASCII
          PUSH      H
          LXI       H, TAPE_END_CODE
          CMP       M
          POP       H
          JZ        UNWIND_RECEIVE
          RET
;------------------------------------------------------------------------------
; READ_MOTOROLA
;   Search S records; load S1 data and validate checksum; >=S9 ends receive.
; Manual Appendix 2-3 (PDF 85) explicitly ends input at S9, before the remaining fields.
; This parser also terminates on SA..SF; these are not additional supported formats.
;------------------------------------------------------------------------------
READ_MOTOROLA:                                                    ; was PR0AE6
          CALL      CHECK_RECEIVE_LIMIT
          RST       RST_RX_ASCII
          CPI       'S'
          JNZ       READ_MOTOROLA
          MVI       C, 0FFH
          CALL      RX_HEX_NIBBLE_STRICT
          CPI       009H
          RNC
          DCR       A
          JNZ       READ_MOTOROLA
          CALL      RX_HEX_BYTE_CHECKSUM
          MOV       B, A
          PUSH      D
          CALL      READ_RECORD_ADDRESS
          POP       D
          MOV       A, B
          SUI       003H
          RC
          MOV       B, A
          CNZ       READ_RECORD_DATA
          CALL      RX_HEX_BYTE_CHECKSUM
          MOV       A, C
          ORA       A
          JZ        READ_MOTOROLA
          RET
;------------------------------------------------------------------------------
; CMD_BINARY_READ
;   S command: receive FFH-prefixed binary directly, independent of X6.
; Command: S. Manual 4-4-2 (PDF pp. 64).
;------------------------------------------------------------------------------
CMD_BINARY_READ:                                                  ; was PR0B15
          MVI       A, 0FFH
          STA       TRANSFER_IRQ_INHIBIT
          CALL      INIT_BUFFER_RANGE
          CALL      TX_READ_DC1
          CALL      READ_BINARY_DATA
          JMP       READ_FINISH_AND_REPORT
;------------------------------------------------------------------------------
; TEST_READ_FLOW_CONTROL
;   Z iff panel/terminal mode and X7 bit 2 set.
;------------------------------------------------------------------------------
TEST_READ_FLOW_CONTROL:                                           ; was PR0B26
          LDA       OPERATING_MODE
          ORA       A
          JZ        READ_FLOW_TEST_STATUS
          DCR       A
          RNZ
READ_FLOW_TEST_STATUS:                                            ; was A93X
          LDA       STATUS_FLAGS                                  ; !!
          CMA
          ANI       004H
          RET
;------------------------------------------------------------------------------
; TX_READ_DC1
;   Send DC1 only when read flow control enabled.
;------------------------------------------------------------------------------
TX_READ_DC1:                                                      ; was PR0B36
          CALL      TEST_READ_FLOW_CONTROL
          RNZ
          MVI       A, 011H
          JMP       SERIAL_TX
;------------------------------------------------------------------------------
; READ_BINARY_DATA
;   Wait for FFH, then receive raw bytes through end DE.
;------------------------------------------------------------------------------
READ_BINARY_DATA:                                                 ; was PR0B3F
          RST       RST_RX_RAW
          INR       A
          JNZ       READ_BINARY_DATA
READ_BINARY_BYTE_LOOP:                                            ; was A94
          RST       RST_RX_RAW
          CALL      STORE_RECEIVED_AND_ADVANCE
          CALL      COMPARE_DE_HL
          JNC       READ_BINARY_BYTE_LOOP
          XRA       A
          RET
;------------------------------------------------------------------------------
; READ_INTEL_HEX
;   Parse count/address/type/data/checksum; zero count returns immediately.
; Manual Appendix 2-1 (PDF 83) explicitly ends input after the zero count.
; Output records have at most 16 data bytes; this input loop accepts counts 1..255.
; A nonzero record type with data is stored and checksum-checked before returning success.
;------------------------------------------------------------------------------
READ_INTEL_HEX:                                                   ; was PR0B50
          CALL      CHECK_RECEIVE_LIMIT
          RST       RST_RX_ASCII
          SUI       ':'
          JNZ       READ_INTEL_HEX
          MOV       C, A
          CALL      RX_HEX_BYTE_CHECKSUM
          MOV       B, A
          ORA       A
          RZ
          PUSH      D
          CALL      READ_RECORD_ADDRESS
          POP       D
          CALL      RX_HEX_BYTE_CHECKSUM
          PUSH      PSW
          CALL      READ_RECORD_DATA
          CALL      RX_HEX_BYTE_CHECKSUM
          MOV       A, C
          POP       B
          ORA       A
          RNZ
          ORA       B
          JZ        READ_INTEL_HEX
          XRA       A
          RET
;------------------------------------------------------------------------------
; CHECK_RECEIVE_LIMIT
; RST 5.5 INTERRUPT HANDLER
;   Also vector 002CH: if bounded receive has passed end, discard caller return and exit.
; Although a hardware-vector stub points here, normal code CALLs this service.
;   Do not infer a separately wired RST 5.5 peripheral from the vector alone.
;------------------------------------------------------------------------------
CHECK_RECEIVE_LIMIT:                                              ; was RST55
          LDA       RECEIVE_PAGE_MASK
          ORA       A
          RZ
          CALL      COMPARE_DE_HL
          RNC
          XRA       A
          POP       B
          RET
;------------------------------------------------------------------------------
; READ_TEK_ADDRESS
;   Accept address, or skip // comment through CR and return CY.
;------------------------------------------------------------------------------
READ_TEK_ADDRESS:                                                 ; was PR0B85
          RST       RST_RX_ASCII
          CPI       02FH
          JNZ       READ_TEK_ADDRESS_FIRST_BYTE
READ_TEK_SKIP_COMMENT:                                            ; was A95
          RST       RST_RX_ASCII
          CPI       00DH
          JNZ       READ_TEK_SKIP_COMMENT
          STC
          RET
READ_TEK_ADDRESS_FIRST_BYTE:                                      ; was A96
          CALL      PARSE_HEX_BYTE_FIRST_CHAR
          JMP       READ_ADDRESS_LOW_BYTE
;------------------------------------------------------------------------------
; PARSE_HEX_BYTE_FIRST_CHAR
;   First character already in A; join shared second-nibble/checksum path.
;------------------------------------------------------------------------------
PARSE_HEX_BYTE_FIRST_CHAR:                                        ; was PR0B99
          PUSH      D
          CALL      CHECK_HEX_NIBBLE_STRICT
          JMP       RX_HEX_SECOND_NIBBLE
;------------------------------------------------------------------------------
; READ_RECORD_ADDRESS
;   Read two hex bytes into DE; store and translate receive address.
;------------------------------------------------------------------------------
READ_RECORD_ADDRESS:                                              ; was PR0BA0
          CALL      RX_HEX_BYTE_CHECKSUM
READ_ADDRESS_LOW_BYTE:                                            ; was A97
          MOV       D, A
          CALL      RX_HEX_BYTE_CHECKSUM
          MOV       E, A
;------------------------------------------------------------------------------
; SET_RECEIVE_ADDRESS
; TRAP vector target and normal receive-address service; vector wiring is separate from software use.
;   Also vector 0024H: store DE in RECEIVE_SOURCE_ADDRESS, then map to buffer/native address.
; TRAP vector points here, but this is also the normal receive-address fall-through.
;------------------------------------------------------------------------------
SET_RECEIVE_ADDRESS:                                              ; was RST4B
          XCHG
          SHLD      RECEIVE_SOURCE_ADDRESS
          XCHG
;------------------------------------------------------------------------------
; MAP_RECEIVE_ADDRESS
;   Unbounded: HL=SHARED_OFFSET_SUM+DE; bounded: mask address page and add buffer base.
;------------------------------------------------------------------------------
MAP_RECEIVE_ADDRESS:                                              ; was PR0BAD
          LDA       RECEIVE_PAGE_MASK
          ORA       A
          JNZ       RECEIVE_MAP_MASKED_ADDRESS
          LHLD      SHARED_OFFSET_SUM
          DAD       D
          XRA       A
          RET
RECEIVE_MAP_MASKED_ADDRESS:                                       ; was A98
          ANA       D
          ADI       080H
          LXI       H, BUFFER_BASE_PAGE
          ADD       M
          MOV       H, A
          MOV       L, E
          XRA       A
          RET
;------------------------------------------------------------------------------
; READ_RECORD_DATA
;   Read/store-or-compare B hex bytes; advance destination.
;------------------------------------------------------------------------------
READ_RECORD_DATA:                                                 ; was PR0BC5
          CALL      RX_HEX_BYTE_CHECKSUM
          CALL      STORE_RECEIVED_AND_ADVANCE
          DCR       B
          JNZ       READ_RECORD_DATA
          RET
;------------------------------------------------------------------------------
; RX_HEX_BYTE_CHECKSUM
;   Receive two hex digits; subtract byte from C, or add nibble sum for Tektronix.
;------------------------------------------------------------------------------
RX_HEX_BYTE_CHECKSUM:                                             ; was PR0BD0
          PUSH      D
          CALL      RX_HEX_NIBBLE_STRICT
RX_HEX_SECOND_NIBBLE:                                             ; was A99
          CALL      SWAP_NIBBLES
          MOV       D, A
          CALL      RX_HEX_NIBBLE_STRICT
          ORA       D
          MOV       D, A
          LDA       TAPE_FORMAT
          CPI       005H
          JNZ       RX_HEX_SUBTRACT_CHECKSUM
          MOV       A, D
          CALL      ADD_NIBBLE_CHECKSUM
          JMP       RX_HEX_BYTE_RETURN
RX_HEX_SUBTRACT_CHECKSUM:                                         ; was B00
          MOV       A, C
          SUB       D
          MOV       C, A
RX_HEX_BYTE_RETURN:                                               ; was B01
          MOV       A, D
          POP       D
          RET
;------------------------------------------------------------------------------
; RX_HEX_NIBBLE_STRICT
;   Receive ASCII then validate; invalid character aborts whole receive.
;------------------------------------------------------------------------------
RX_HEX_NIBBLE_STRICT:                                             ; was PR0BF2
          RST       RST_RX_ASCII
;------------------------------------------------------------------------------
; CHECK_HEX_NIBBLE_STRICT
;   Convert current ASCII A; invalid input falls through to transfer abort.
;------------------------------------------------------------------------------
CHECK_HEX_NIBBLE_STRICT:                                          ; was PR0BF3
          CALL      ASCII_TO_HEX_NIBBLE
          RNC
;------------------------------------------------------------------------------
; ABORT_RECEIVE_ERROR
;   Set A=FFH/NZ, then unwind using saved SP.
;------------------------------------------------------------------------------
ABORT_RECEIVE_ERROR:                                              ; was PR0BF7
          ORI       0FFH
;------------------------------------------------------------------------------
; UNWIND_RECEIVE
;   Restore SP from SHARED_ERRORS_SAVED_SP; return directly to READ_FORMAT_DISPATCH caller.
;------------------------------------------------------------------------------
UNWIND_RECEIVE:                                                   ; was PR0BF9
          XCHG
          LHLD      SHARED_ERRORS_SAVED_SP
          SPHL
          XCHG
          RET
;------------------------------------------------------------------------------
; ADD_NIBBLE_CHECKSUM
; !!
;   Add both nibbles of A to checksum C, preserving A.
;------------------------------------------------------------------------------
ADD_NIBBLE_CHECKSUM:                                              ; was PR0C00
          PUSH      PSW
          ANI       00FH
          ADD       C
          MOV       C, A
          POP       PSW
          PUSH      PSW
          RRC
          RRC
          RRC
          RRC
          ANI       00FH
          ADD       C
          MOV       C, A
          POP       PSW
          RET
;------------------------------------------------------------------------------
; STORE_RECEIVED_AND_ADVANCE
;   Apply receive filtering/store/compare, then increment original HL.
;------------------------------------------------------------------------------
STORE_RECEIVED_AND_ADVANCE:                                       ; was PR0C11
          PUSH      H
          CALL      FILTER_EVEN_ODD_DATA
          POP       H
          INX       H
          RET
;------------------------------------------------------------------------------
; FILTER_EVEN_ODD_DATA
;   X7 bits 4/5 select and pack even/odd source addresses before store/compare.
;------------------------------------------------------------------------------
FILTER_EVEN_ODD_DATA:                                             ; was PR0C18
          PUSH      PSW
          PUSH      B
          LDA       STATUS_FLAGS
          MOV       B, A
          ANI       010H
          JZ        RECEIVE_FILTER_ACCEPT
          PUSH      D
          LHLD      RECEIVE_SOURCE_ADDRESS
          MOV       D, H
          MOV       E, L
          INX       H
          SHLD      RECEIVE_SOURCE_ADDRESS
          MOV       L, E
          ORA       A
          MOV       A, D
          RAR
          MOV       D, A
          MOV       A, E
          RAR
          MOV       E, A
          MOV       A, B
          ANI       020H
          MOV       A, L
          RRC
          JNZ       RECEIVE_SELECT_ODD
          JC        RECEIVE_FILTER_SKIP
          CNC       MAP_RECEIVE_ADDRESS
          JMP       RECEIVE_RESTORE_SOURCE_REGISTERS
RECEIVE_SELECT_ODD:                                               ; was B02
          JNC       RECEIVE_FILTER_SKIP
          CC        MAP_RECEIVE_ADDRESS
RECEIVE_RESTORE_SOURCE_REGISTERS:                                 ; was B03
          POP       D
RECEIVE_FILTER_ACCEPT:                                            ; was B04
          POP       B
          POP       PSW
;------------------------------------------------------------------------------
; STORE_OR_COMPARE_RECEIVED
; RST 6.5 INTERRUPT HANDLER
;   Also vector 0034H: TARGET_ACCESS_MODE=0 writes A; otherwise compare and report differences.
; RST 6.5 vector points here; normal receive code also falls through to this service.
;------------------------------------------------------------------------------
STORE_OR_COMPARE_RECEIVED:                                        ; was RST65
          PUSH      B
          MOV       C, A
          LDA       TARGET_ACCESS_MODE
          ORA       A
          JNZ       RECEIVE_COMPARE_BYTE
          MOV       A, C
          RST       RST_MEM_WRITE
RECEIVE_STORE_COMPARE_RETURN:                                     ; was B05
          POP       B
          RET
RECEIVE_COMPARE_BYTE:                                             ; was B06
          RST       RST_MEM_READ
          CMP       C
          JZ        RECEIVE_STORE_COMPARE_RETURN
          MOV       B, A
          CALL      TX_NEWLINE_HEX_WORD
          JMP       REPORT_ERROR_ACTUAL_EXPECTED
RECEIVE_FILTER_SKIP:                                              ; was B07
          POP       D
          POP       B
          POP       PSW
          RET
;------------------------------------------------------------------------------
; CMD_DUMP_RANGE
;   Pop explicit end/start and validate order before dumping.
; Command: Dn (physical start,end). Manual 4-4-4 (PDF pp. 65).
;------------------------------------------------------------------------------
CMD_DUMP_RANGE:                                                   ; was PR0C6C
          POP       D
          POP       H
          CALL      COMPARE_DE_HL
          JNC       DUMP_FIRST_LINE
          RET
;------------------------------------------------------------------------------
; CMD_DUMP_BUFFER
;   D command: formatted hex dump with 16 columns and address headers.
; Command: D. Manual 4-4-3 (PDF pp. 64-65).
;------------------------------------------------------------------------------
CMD_DUMP_BUFFER:                                                  ; was PR0C75
          CALL      INIT_BUFFER_RANGE
DUMP_FIRST_LINE:                                                  ; was B08
          XRA       A
DUMP_LINE_HEADER:                                                 ; was B09
          CZ        TX_DUMP_HEADER
          PUSH      D
          XCHG
          LHLD      SHARED_OFFSET_SUM
          DAD       D
          MOV       A, L
          ANI       0F0H
          MOV       L, A
          CALL      TX_SPACE
          CALL      TX_HEX_WORD
          RST       RST_TX_STRING
          DB        ' ', ':'+80H
          XCHG
          POP       D
          MOV       A, L
          ANI       00FH
DUMP_LEADING_SPACES:                                              ; was B10
          DCR       A
          PUSH      PSW
          CP        TX_TWO_SPACES
          POP       PSW
          JP        DUMP_LEADING_SPACES
DUMP_BYTE_LOOP:                                                   ; was B11
          CALL      TX_TWO_SPACES
          RST       RST_MEM_READ
          CALL      TX_HEX_BYTE
          CALL      NEXT_AND_COMPARE_END
          JC        TX_CRLF
          MOV       A, L
          ANI       00FH
          JNZ       DUMP_BYTE_LOOP
          CALL      TX_CRLF
          MOV       A, L
          ORA       L
          JMP       DUMP_LINE_HEADER
;------------------------------------------------------------------------------
; TX_DUMP_HEADER
; D Command (Dump) Header-Output
;         16 columns per line; header repeated at a 256-byte page boundary.
;   Print ADDR. : and 0..F column headings (repeated on page boundary).
;------------------------------------------------------------------------------
TX_DUMP_HEADER:                                                   ; was PR0CB8
          RST       RST_TX_STRING
          DB        0DH, 0AH, 'ADDR. ', ':'+80h
          MVI       B, 0F0H
DUMP_COLUMN_HEADING_LOOP:                                         ; was B12
          CALL      TX_THREE_SPACES
          MOV       A, B
          CALL      TX_HEX_NIBBLE
          INR       B
          JNZ       DUMP_COLUMN_HEADING_LOOP
          CALL      TX_CRLF
;------------------------------------------------------------------------------
; TX_CRLF
;   Output CR followed by LF.
;------------------------------------------------------------------------------
TX_CRLF:                                                          ; was PR0CD2
          RST       RST_TX_STRING
          DB        0DH, 0AH+80H
          RET
;------------------------------------------------------------------------------
; CMD_PROM_EDITOR
;   W with address: set PROM access flag then share byte editor.
; Command: Wn (PROM address). Manual 4-3-11 (PDF pp. 61).
;------------------------------------------------------------------------------
CMD_PROM_EDITOR:                                                  ; was PR0CD6
          LXI       H, TARGET_ACCESS_MODE
          DCR       M
;------------------------------------------------------------------------------
; CMD_BUFFER_EDITOR
;   L with address: pop logical address and add 8000H; edit/display sequential bytes.
; Command: Ln (logical address). Manual 4-3-10 (PDF pp. 60-61).
;------------------------------------------------------------------------------
CMD_BUFFER_EDITOR:                                                ; was PR0CDA
          POP       D
          LXI       H, BUFFER_BASE_ADDRESS
          DAD       D
          XCHG
EDITOR_SHOW_LOCATION:                                             ; was B13
          LXI       H, BUFFER_BASE_ADDRESS
          DAD       D
          CALL      TX_NEWLINE_HEX_WORD
          CALL      TX_TWO_SPACES
          CALL      READ_EDIT_TARGET
          PUSH      PSW
          CNZ       ERROR_BEEP
          POP       PSW
          CALL      TX_BYTE_IF_VALID
          CALL      TX_TWO_SPACES
          CALL      SERIAL_RX_ASCII
          CALL      CLASSIFY_SEPARATOR
          RC
          MOV       B, A
          JZ        EDITOR_NEXT_PREVIOUS_ADDRESS
          CALL      PARSE_ONE_HEX_ARGUMENT
          POP       H
          MOV       A, L
          PUSH      PSW
          CALL      WRITE_AND_VERIFY_EDIT
          POP       PSW
          RC
EDITOR_NEXT_PREVIOUS_ADDRESS:                                     ; was B14
          MOV       A, B
          CPI       02FH
          DCX       D
          JZ        EDITOR_SHOW_LOCATION
          INX       D
          INX       D
          JMP       EDITOR_SHOW_LOCATION
;------------------------------------------------------------------------------
; CMD_PARAMETER_EDITOR
;   X4..X9: display/change parameter byte at 6060+2*(n-4).
; Command: X4..X9. Manual 4-3-13 (PDF pp. 62-63).
;------------------------------------------------------------------------------
CMD_PARAMETER_EDITOR:                                             ; was PR0D1A
          XRA       A
          POP       H
          LXI       B, -10
          DAD       B
          INR       A
          RC
          LXI       B, 6
          DAD       B
          RNC
          LXI       B, BAUD_INDEX
          DAD       H
          DAD       B
          CALL      TX_THREE_SPACES
          MOV       A, M
          CALL      TX_HEX_BYTE
          CALL      TX_TWO_SPACES
          CALL      SERIAL_RX_ASCII
          CALL      CLASSIFY_SEPARATOR
          RZ
          XCHG
          CALL      PARSE_ONE_HEX_ARGUMENT
          POP       H
          MOV       A, L
          STAX      D
          RET
;------------------------------------------------------------------------------
; PROM control data accessed through GET_PROM_CONTROL_BYTE.
; PROM_CONTROL_TABLE: seven types, 12 bytes/type; byte 0 is size in 256-byte pages.
;------------------------------------------------------------------------------
PROM_CONTROL_TABLE:                                               ; was A0D45
          DB        020H, 000H, 083H, 083H
          DB        085H, 043H, 045H, 043H
          DB        042H, 083H, 083H, 083H
          DB        020H, 000H, 082H, 082H
          DB        084H, 082H, 084H, 0C2H
          DB        0C0H, 082H, 082H, 082H
          DB        010H, 000H, 002H, 002H
          DB        004H, 002H, 004H, 0C2H
          DB        0C0H, 002H, 002H, 002H
          DB        010H, 000H, 082H, 082H
          DB        084H, 082H, 084H, 0C2H
          DB        0C0H, 082H, 082H, 082H
          DB        010H, 000H, 002H, 002H
          DB        004H, 002H, 004H, 042H
          DB        040H, 002H, 002H, 002H
          DB        008H, 000H, 081H, 081H
          DB        084H, 0C1H, 0C4H, 0C1H
          DB        0C3H, 081H, 081H, 081H
          DB        008H, 0B0H, 081H, 081H
          DB        084H, 0C1H, 0C4H, 0C1H
          DB        0C3H, 0C1H, 0C0H, 0C2H
;------------------------------------------------------------------------------
; DISPATCH_PANEL_COMMAND
;   Handle panel function keys and JOB subcommands through common command table.
; AUTO: "-" (11H) displays A.; subsequent PRG (13H) sets SHARED_INPUT_STATE=93H.
; SET then dispatches CMD_PROGRAM, which first erase-checks when this flag is nonzero.
; Manual 3-1-6 (PDF 32): - -> PRG -> SET; erase errors prevent programming.
; This is distinct from JOB -> - -> SET, which enters the test menu.
;------------------------------------------------------------------------------
DISPATCH_PANEL_COMMAND:                                           ; was PR0D99
          CALL      DECODE_PROM_TYPE
          LXI       H, DISPLAY_COMMAND
          CPI       011H
          JNZ       PANEL_DISPATCH_FUNCTION
          MVI       M, 098H
          RST       RST_WAIT_KEY
          CPI       013H
          RNZ
          ORI       080H
          STA       SHARED_INPUT_STATE
PANEL_DISPATCH_FUNCTION:                                          ; was B15
          MOV       M, A
          ANI       07FH
          SUI       013H
          RC
          MOV       L, A
          CPI       004H
          CZ        DISPATCH_JOB_SUBCOMMAND
          RNC
          RST       RST_WAIT_KEY
          RC
          RNZ
          MVI       H, 000H
          DAD       H
          DAD       H
          LXI       B, COMMAND_HANDLERS+2
          DAD       B
;------------------------------------------------------------------------------
; CALL_TABLE_TARGET
;   Push little-endian routine pointer from HL; RET through range setup invokes target.
;------------------------------------------------------------------------------
CALL_TABLE_TARGET:                                                ; was PR0DC7
          MOV       E, M
          INX       H
          MOV       D, M
          PUSH      D
          EI
;------------------------------------------------------------------------------
; LOAD_PROM_BUFFER_RANGE
;   HL=8000H+word(6061), DE=PROM address derived from buffer-page offset; refresh size B.
;------------------------------------------------------------------------------
LOAD_PROM_BUFFER_RANGE:                                           ; was PR0DCC
          LXI       D, BUFFER_BASE_ADDRESS
          LHLD      BAUD_INDEX_HIGH
          DAD       D
          MOV       D, L
;------------------------------------------------------------------------------
; DECODE_PROM_TYPE
;   Map selector bits through PROM_SWITCH_TYPE_MAP; cache type and capacity in pages (PROM_SIZE_PAGES/B).
;------------------------------------------------------------------------------
DECODE_PROM_TYPE:                                                 ; was PR0DD4
          PUSH      PSW
          PUSH      H
          PUSH      D
          CALL      READ_TYPE_SWITCHES
          RRC
          RRC
          SUI       005H
          LXI       H, PROM_SWITCH_TYPE_MAP
          MOV       E, A
          MVI       D, 000H
          DAD       D
          MOV       A, M
          STA       PROM_TYPE_INDEX
          XRA       A
          CALL      GET_PROM_CONTROL_BYTE
          STA       PROM_SIZE_PAGES
          MOV       B, A
          POP       D
          POP       H
          POP       PSW
          RET
;------------------------------------------------------------------------------
; accessed only from DECODE_PROM_TYPE
; PROM_SWITCH_TYPE_MAP: selector combinations -> type index; 8 means unsupported.
;------------------------------------------------------------------------------
PROM_SWITCH_TYPE_MAP:                                             ; was A0DF5
          DB        005H, 002H, 000H, 008H
          DB        008H, 003H, 001H, 008H
          DB        006H, 004H, 008H
;------------------------------------------------------------------------------
; DISPATCH_JOB_SUBCOMMAND
;   Decode JOB hex key: 0..3 operations, 4..9 parameters, higher codes use table dispatch.
;------------------------------------------------------------------------------
DISPATCH_JOB_SUBCOMMAND:                                          ; was PR0E00
          RST       RST_WAIT_KEY
          STA       DISPLAY_PROM_TYPE
          CMC
          RNC
          SUI       004H
          JC        PANEL_READ_OR_EDIT
          CPI       006H
          JC        PANEL_EDIT_PARAMETER
          ADI       004H
          MOV       L, A
          CPI       012H
          RET
;------------------------------------------------------------------------------
; ENTER_HOST_MODE
;   Set mode=2 and enter command prompt.
;------------------------------------------------------------------------------
ENTER_HOST_MODE:                                                  ; was PR0E16
          MVI       A, 002H
          JMP       ENTER_SERIAL_MODE
;------------------------------------------------------------------------------
; ENTER_TERMINAL_MODE
; Terminal-Mode startup message
;   Print startup banner, set mode=1 and enter command prompt.
;------------------------------------------------------------------------------
ENTER_TERMINAL_MODE:                                              ; was PR0E1B
          RST       RST_TX_STRING
          DB        0DH, 0AH, 'Hellorld!', 0DH, 0AH,
          DB        'X6=4(ASCII Hex Space) X8=53(S), X9=58(X', ')'+80H
          MVI       A, 001H
ENTER_SERIAL_MODE:                                                ; was B16
          STA       OPERATING_MODE
;------------------------------------------------------------------------------
; COMMAND_PROMPT
; Send Command Prompt (*)
;   Reset SP, print *, read command, select no-argument or parameterized handler.
;   INR M in ROM space does not change the ROM; it sets Z at the FFH sentinel.
;   Confirmed against the original ROM; do not replace it with a RAM-table walk.
;------------------------------------------------------------------------------
COMMAND_PROMPT:                                                   ; was PR0E2B
          LXI       SP, STACK_TOP
          RST       RST_TX_STRING
          DB        0DH, 0AH, '*'+80H
          CALL      RESET_COMMAND_STATE
          LXI       H, RX_REFRESH_ENABLED
          DCR       M
          CALL      SERIAL_RX_ASCII
          CALL      SERIAL_TX
          INR       M
          CALL      REJECT_INVALID_PROM_TYPE
          LXI       H, COMMAND_HANDLERS-3
COMMAND_TABLE_SEARCH:                                             ; was B17
          INX       H
          INX       H
          INX       H
          INR       M
          JZ        COMMAND_ERROR
          CMP       M
          INX       H
          JNZ       COMMAND_TABLE_SEARCH
          CALL      SERIAL_RX_ASCII
          CALL      CLASSIFY_SEPARATOR
          JNC       DISPATCH_ARGUMENT_COMMAND
          INX       H
          CALL      CALL_TABLE_TARGET
;------------------------------------------------------------------------------
; CHECK_COMMAND_RESULT
; Check Command-Error
;   Z returns to prompt; NZ falls through to error message.
;------------------------------------------------------------------------------
CHECK_COMMAND_RESULT:                                             ; was PR0E5F
          JZ        COMMAND_PROMPT                                ; command return target: original ROM 0E5FH, current edited build 0E8BH (longer banner/defaults)
;------------------------------------------------------------------------------
; COMMAND_ERROR
; Send the literal '?' command-error response.
;   Clear display/beep, output ?, restart command prompt.
;------------------------------------------------------------------------------
COMMAND_ERROR:                                                    ; was ERRMSG
          CALL      CLEAR_AND_ERROR_BEEP
          RST       RST_TX_STRING
          DB        '?'+80H
          JMP       COMMAND_PROMPT
;------------------------------------------------------------------------------
; DISPATCH_ARGUMENT_COMMAND
;   Select parameter descriptor using command table index, parse arguments, call handler.
;   Three DAD D instructions add 3*(index-1), matching the 3-byte descriptors.
;------------------------------------------------------------------------------
DISPATCH_ARGUMENT_COMMAND:                                        ; was PR0E6A
          MOV       B, A
          MOV       A, M
          DCR       A
          JM        COMMAND_ERROR
          MOV       E, A
          MVI       D, 000H
          LXI       H, ARGUMENT_HANDLERS
          DAD       D
          DAD       D
          DAD       D
          MOV       C, M
          INX       H
          XCHG
          LXI       H, CHECK_COMMAND_RESULT
          PUSH      H
          MOV       A, B
          CALL      PARSE_HEX_ARGUMENTS
          XCHG
          JMP       CALL_TABLE_TARGET
;------------------------------------------------------------------------------
; Shared terminal/host command table and indexed panel dispatch; manual A-1 (PDF 82).
; used in COMMAND_PROMPT and DISPATCH_PANEL_COMMAND
; Numbers select argument descriptors in ARGUMENT_HANDLERS; 0 means no parameter form.
; COMMAND_HANDLERS: 4-byte entries = character, argument-descriptor index, DW handler.
;   Index is not a byte count. Final FFH entry is a sentinel for serial search;
;   its handler is reached through panel JOB dispatch, not by sending FFH.
; COMMAND QUICK REFERENCE (n denotes parameters, not a literal letter)
;   E                                  -> CMD_ERASE_CHECK; manual 4-3-1, PDF 57
;   C                                  -> CMD_COMPARE_PROM; manual 4-3-2, PDF 57-58
;   L                                  -> CMD_LOAD_PROM; manual 4-3-3, PDF 58
;   W                                  -> CMD_PROGRAM; manual 4-3-4, PDF 58
;   A                                  -> CMD_AUTO_PROGRAM; manual 4-3-5, PDF 59
;   B                                  -> CMD_BLANK_BUFFER; manual 4-3-6, PDF 59
;   O                                  -> CMD_INVERT_BUFFER; manual 4-3-7, PDF 59
;   P                                  -> CMD_PUNCH_BUFFER; manual 4-3-8, PDF 59-60
;   R                                  -> CMD_READ_TAPE; manual 4-3-9, PDF 60
;   Ln (logical address)               -> CMD_BUFFER_EDITOR; manual 4-3-10, PDF 60-61
;   Wn (PROM address)                  -> CMD_PROM_EDITOR; manual 4-3-11, PDF 61
;   Rn (physical base address)         -> CMD_READ_TAPE_AT; manual 4-3-12, PDF 62
;   X4..X9                             -> CMD_PARAMETER_EDITOR; manual 4-3-13, PDF 62-63
;   G                                  -> CMD_BINARY_PUNCH; manual 4-4-1, PDF 64
;   S                                  -> CMD_BINARY_READ; manual 4-4-2, PDF 64
;   D                                  -> CMD_DUMP_BUFFER; manual 4-4-3, PDF 64-65
;   Dn (physical start,end)            -> CMD_DUMP_RANGE; manual 4-4-4, PDF 65
;   Cn (physical base address)         -> CMD_COMPARE_TAPE_AT; manual 4-4-5, PDF 66
;   Pn (physical start,end)            -> CMD_PUNCH_RANGE; manual 4-4-6, PDF 66-67
;   JOB - SET, then test number        -> TEST_MENU; manual 3-3-10, PDF 48-51
;   JOB 2: MOVE (also dispatches editors) -> PANEL_MOVE_OR_EDITOR; manual 3-3-11, PDF 52
;   Mstart,end,destination              -> CMD_MOVE_PARAMETERS; Appendix 1, PDF 82
;   /                                   -> PANEL_MODE_RESTART; 4-4-7, PDF 67
;   JOB E SET / JOB F SET              -> ENTER_TERMINAL_MODE / ENTER_HOST_MODE; Appendix 1, PDF 82
;   X6=0 and tests 4/5 are extension paths; see ROM_ANALYSIS.md.
; Parameterized forms use ARGUMENT_HANDLERS rather than the default DW target.
; Thus bare X/M lead to COMMAND_ERROR, but Xn/Mstart,end,destination have handlers.
;------------------------------------------------------------------------------
COMMAND_HANDLERS:                                                 ; was CMDMAP
          DB        'W'
          DB        1
          DW        CMD_PROGRAM

          DB        'L'
          DB        2                                             ; argument-descriptor index 2 selects Ln; not a character count
          DW        CMD_LOAD_PROM

          DB        'E'
          DB        0
          DW        CMD_ERASE_CHECK

          DB        'C'
          DB        3
          DW        CMD_COMPARE_PROM

          DB        'D'
          DB        4
          DW        CMD_DUMP_BUFFER

          DB        'G'
          DB        0
          DW        CMD_BINARY_PUNCH

          DB        'S'
          DB        0
          DW        CMD_BINARY_READ

          DB        'A'
          DB        0
          DW        CMD_AUTO_PROGRAM

          DB        'X'
          DB        7
          DW        COMMAND_ERROR

          DB        'M'
          DB        8
          DW        COMMAND_ERROR

          DB        'B'
          DB        0
          DW        CMD_BLANK_BUFFER

          DB        'O'
          DB        0
          DW        CMD_INVERT_BUFFER

          DB        'P'
          DB        5
          DW        CMD_PUNCH_BUFFER

          DB        'R'
          DB        6
          DW        CMD_READ_TAPE

          DB        'W'
          DB        0
          DW        ENTER_TERMINAL_MODE

          DB        'W'
          DB        0
          DW        ENTER_HOST_MODE

          DB        '/'                                           ; return from Command- to Key- Mode
          DB        0
          DW        PANEL_MODE_RESTART

          DB        0FFH
          DB        0
          DW        TEST_MENU
;------------------------------------------------------------------------------
; ARGUMENT_HANDLERS: 3-byte entries = argument count, DW handler.
;------------------------------------------------------------------------------
ARGUMENT_HANDLERS:                                                ; was A0ED0
          DB        001H
          DW        CMD_PROM_EDITOR
          DB        001H
          DW        CMD_BUFFER_EDITOR
          DB        001H
          DW        CMD_COMPARE_TAPE_AT
          DB        002H
          DW        CMD_DUMP_RANGE
          DB        002H
          DW        CMD_PUNCH_RANGE
          DB        001H
          DW        CMD_READ_TAPE_AT
          DB        001H
          DW        CMD_PARAMETER_EDITOR
          DB        003H
          DW        CMD_MOVE_PARAMETERS
;------------------------------------------------------------------------------
; CMD_AUTO_PROGRAM
;   Set automatic flag, then erase-check/program/compare sequence.
; Command: A. Manual 4-3-5 (PDF pp. 59).
; Flow: sets auto flag, then falls through into CMD_PROGRAM.
;------------------------------------------------------------------------------
CMD_AUTO_PROGRAM:                                                 ; was PR0EE8
          STA       SHARED_INPUT_STATE
;------------------------------------------------------------------------------
; CMD_PROGRAM
;   Optional erase-check; checksum buffer before/after programming, then verify PROM.
; Command: W. Manual 4-3-4 (PDF pp. 58).
;------------------------------------------------------------------------------
CMD_PROGRAM:                                                      ; was PR0EEB
          LDA       SHARED_INPUT_STATE
          ORA       A
          JZ        PROGRAM_WITH_BUFFER_CHECKSUM
          CALL      CMD_ERASE_CHECK
          RC
          RNZ
          MVI       A, 080H
          STA       VOLTAGE_SELECT
          CALL      CLEAR_DISPLAY
          MVI       A, 093H
          STA       DISPLAY_COMMAND
PROGRAM_WITH_BUFFER_CHECKSUM:                                     ; was B18
          CALL      CHECKSUM_BUFFER
          PUSH      H
          LXI       H, TRANSFER_IRQ_INHIBIT
          DCR       M
          PUSH      H
          CALL      PROGRAM_PROM_RANGE
          POP       H
          INR       M
          EI
          JC        RETURN_POP_BC
          CALL      CHECKSUM_BUFFER
          POP       B
          MOV       A, L
          CMP       C
          JNZ       PROGRAM_BUFFER_CHANGED
          MOV       A, H
          CMP       B
          JZ        CMD_COMPARE_PROM
PROGRAM_BUFFER_CHANGED:                                           ; was B19
          MVI       D, 000H
;------------------------------------------------------------------------------
; WAIT_FAULT_ACK
;   Repeated error tone until JOB; resume current mode or panel mode according to D.
;------------------------------------------------------------------------------
WAIT_FAULT_ACK:                                                   ; was PR0F26
          CALL      ERROR_BEEP
          CALL      POLL_KEY_EVENT
          JNC       WAIT_FAULT_ACK
          MOV       A, D
          ORA       A
          JNZ       PANEL_MODE_RESTART
          JMP       DISPATCH_CURRENT_MODE
;------------------------------------------------------------------------------
; CHECKSUM_BUFFER
;   Sum selected buffer bytes into HL (16-bit); used to detect buffer changes while programming.
;------------------------------------------------------------------------------
CHECKSUM_BUFFER:                                                  ; was PR0F37
          CALL      LOAD_PROM_BUFFER_RANGE
          PUSH      H
          LXI       H, 0
          XTHL
BUFFER_CHECKSUM_LOOP:                                             ; was B20
          RST       RST_MEM_READ
          MOV       C, A
          MVI       B, 000H
          XTHL
          DAD       B
          XTHL
          INX       D
          INX       H
          LDA       PROM_SIZE_PAGES
          MOV       B, A
          MOV       A, D
          CMP       B
          JC        BUFFER_CHECKSUM_LOOP
          POP       H
          RET
;------------------------------------------------------------------------------
; PROGRAM_PROM_RANGE
;   Read desired bytes; skip matching/FF bytes when quick mode permits; program each address.
;------------------------------------------------------------------------------
PROGRAM_PROM_RANGE:                                               ; was PR0F53
          CALL      LOAD_PROM_BUFFER_RANGE
          XRA       A
          STA       DISPLAY_COMMAND
PROGRAM_ADDRESS_LOOP:                                             ; was B21
          CNZ       INCREMENT_DISPLAY_ADDRESS
          CALL      READ_BUFFER_AND_DISPLAY
          MOV       C, A
          CALL      READ_PROM_AND_DISPLAY
          CMP       C
          JZ        PROGRAM_CHECK_QUICK_MODE
          MOV       A, C
          INR       A
          JNZ       PROGRAM_CURRENT_BYTE
PROGRAM_CHECK_QUICK_MODE:                                         ; was B22
          LDA       STATUS_FLAGS
          RRC
          JC        PROGRAM_CHECK_ABORT
PROGRAM_CURRENT_BYTE:                                             ; was B23
          CALL      PROGRAM_PROM_BYTE
PROGRAM_CHECK_ABORT:                                              ; was B24
          CALL      POLL_KEY_EVENT
          JC        RESTORE_PROM_READ_STATE
          INX       D
          MOV       A, D
          CMP       B
          JC        PROGRAM_ADDRESS_LOOP
;------------------------------------------------------------------------------
; RESTORE_PROM_READ_STATE
;   Enable interrupts, apply control-table state 3, preserve A/flags.
;------------------------------------------------------------------------------
RESTORE_PROM_READ_STATE:                                          ; was PR0F83
          EI
          PUSH      PSW
          RST       RST_PROM_CONTROL
          DB        3
          POP       PSW
          RET
;------------------------------------------------------------------------------
; CMD_LOAD_PROM
; L-COMMAND (LOAD PROM INTO BUFFER)
;   Read selected PROM range into buffer, then fall through to comparison.
; Command: L. Manual 4-3-3 (PDF pp. 58).
; Flow: after loading the last byte, fall through into CMD_COMPARE_PROM.
;------------------------------------------------------------------------------
CMD_LOAD_PROM:                                                    ; was PR0F89
          CALL      READ_PROM_LOGIC_LEVELS
          RST       RST_MEM_WRITE
          INX       D
          INX       H
          MOV       A, D
          CMP       B
          JC        CMD_LOAD_PROM
;------------------------------------------------------------------------------
; CMD_COMPARE_PROM
; C COMMAND (COMPARE)
;   Compare PROM against buffer over voltage passes; report mismatches and buffer checksum.
; Command: C. Manual 4-3-2 (PDF pp. 57-58).
;------------------------------------------------------------------------------
CMD_COMPARE_PROM:                                                 ; was PR0F94
          LXI       H, TARGET_ACCESS_MODE
          DCR       M
          CALL      FIRST_VOLTAGE_PASS
COMPARE_VOLTAGE_PASS:                                             ; was B25
          CALL      BEGIN_VOLTAGE_PASS
COMPARE_ADDRESS_LOOP:                                             ; was B26
          CNZ       INCREMENT_DISPLAY_ADDRESS
          CALL      READ_BUFFER_AND_DISPLAY
          CALL      ADD_BUFFER_CHECKSUM
          CALL      READ_PROM_AND_DISPLAY
          JNZ       COMPARE_REPORT_MISMATCH
          PUSH      B
          MOV       B, A
          RST       RST_MEM_READ
          CMP       B
          MOV       A, B
          POP       B
COMPARE_REPORT_MISMATCH:                                          ; was B27
          CNZ       REPORT_COMPARE_ERROR
          EI
          RC
          INX       D
          MOV       A, D
          CMP       B
          JC        COMPARE_ADDRESS_LOOP
          LDA       VOLTAGE_SELECT
          SUI       040H
          JNC       COMPARE_VOLTAGE_PASS
          LHLD      SHARED_OFFSET_SUM
          LDA       OPERATING_MODE
          ORA       A
          JZ        COMPARE_SHOW_CHECKSUM
          DCR       A
          JNZ       FINISH_OPERATION
          CALL      TX_SPACE
          MOV       A, H
          CALL      TX_HEX_BYTE
          MOV       A, L
          CALL      TX_HEX_BYTE
          JMP       FINISH_OPERATION
COMPARE_SHOW_CHECKSUM:                                            ; was B28
          PUSH      H
          CALL      ENTER_PANEL_MODE
          POP       H
          LXI       D, DISPLAY_ADDRESS_HIGH
          XRA       A
          MOV       A, H
          CALL      STORE_DISPLAY_BYTE
          XRA       A
          MOV       A, L
          CALL      STORE_DISPLAY_BYTE
          JMP       PANEL_COMMAND_LOOP
;------------------------------------------------------------------------------
; FIRST_VOLTAGE_PASS
;   Start with 80H, or 40H when X7 bit 3 requests only two margins.
;------------------------------------------------------------------------------
FIRST_VOLTAGE_PASS:                                               ; was PR0FF7
          LDA       STATUS_FLAGS
          ANI       008H
          LDA       VOLTAGE_SELECT
          RZ
          SUI       040H
          RET
;------------------------------------------------------------------------------
; ADD_BUFFER_CHECKSUM
;   Add byte A to 16-bit running buffer checksum at SHARED_OFFSET_SUM.
; Caller reads the buffer, not the PROM. BEGIN_VOLTAGE_PASS resets the sum each pass.
; C/L/W report this sum; it equals the PROM sum only if comparison succeeds.
;------------------------------------------------------------------------------
ADD_BUFFER_CHECKSUM:                                              ; was PR1003
          PUSH      B
          PUSH      H
          MOV       C, A
          MVI       B, 000H
          LHLD      SHARED_OFFSET_SUM
          DAD       B
          SHLD      SHARED_OFFSET_SUM
          POP       H
;------------------------------------------------------------------------------
; RETURN_POP_BC
;   Shared stack-cleanup tail; not a standalone CALL entry.
;------------------------------------------------------------------------------
RETURN_POP_BC:                                                    ; was PR1010
          POP       B
          RET
;------------------------------------------------------------------------------
; CMD_ERASE_CHECK
; E COMMAND (ERASE-CHECK)
;   Check erased bytes over voltage passes; type 6 first performs electrical erase sequence.
; Command: E. Manual 4-3-1 (PDF pp. 57).
;------------------------------------------------------------------------------
CMD_ERASE_CHECK:                                                  ; was PR1012
          CALL      INIT_PROM_READ
          LDA       PROM_TYPE_INDEX
          CPI       006H
          JNZ       ERASE_CHECK_FIRST_VOLTAGE
          RST       RST_PROM_CONTROL
          DB        9
          RST       RST_PROM_CONTROL
          DB        0AH
          RST       RST_PROM_CONTROL
          DB        0BH
          MVI       A, 003H
          CALL      DELAY_A_LONG
          RST       RST_PROM_CONTROL
          DB        0AH
          RST       RST_PROM_CONTROL
          DB        9
          CALL      RESTORE_PROM_READ_STATE
ERASE_CHECK_FIRST_VOLTAGE:                                        ; was B29
          CALL      FIRST_VOLTAGE_PASS
ERASE_CHECK_VOLTAGE_PASS:                                         ; was B30
          CALL      BEGIN_VOLTAGE_PASS
ERASE_CHECK_ADDRESS_LOOP:                                         ; was B31
          CNZ       INCREMENT_DISPLAY_ADDRESS
          CALL      READ_PROM_AND_DISPLAY
          JNZ       ERASE_CHECK_REPORT_ERROR
          INR       A
ERASE_CHECK_REPORT_ERROR:                                         ; was B32
          CNZ       REPORT_COMPARE_ERROR
          EI
          RC
          INX       D
          MOV       A, D
          CMP       B
          JC        ERASE_CHECK_ADDRESS_LOOP
          LDA       VOLTAGE_SELECT
          SUI       040H
          JNC       ERASE_CHECK_VOLTAGE_PASS
          LDA       SHARED_INPUT_STATE
          ORA       A
          JZ        FINISH_OPERATION
;------------------------------------------------------------------------------
; GET_ERROR_STATUS
;   Load error count SHARED_ERRORS_SAVED_SP into HL; Z iff zero.
;------------------------------------------------------------------------------
GET_ERROR_STATUS:                                                 ; was PR1059
          LHLD      SHARED_ERRORS_SAVED_SP
          MOV       A, H
          ORA       L
          RET
;------------------------------------------------------------------------------
; READ_EDIT_TARGET
;   TARGET_ACCESS_MODE=0: buffer at DE; nonzero: PROM at DE.
;------------------------------------------------------------------------------
READ_EDIT_TARGET:                                                 ; was PR105F
          LDA       TARGET_ACCESS_MODE
          ORA       A
          JNZ       READ_PROM_LOGIC_LEVELS
          XCHG
          RST       RST_MEM_READ
          XCHG
          RET
;------------------------------------------------------------------------------
; READ_PROM_LOGIC_LEVELS
; Read a PROM byte through the analog multiplexer and two logic-level comparators.
;   Sample both comparators for all eight mux positions; A=data, NZ=invalid level.
;------------------------------------------------------------------------------
READ_PROM_LOGIC_LEVELS:                                           ; was PR106A
          PUSH      B
          PUSH      H
          LDA       PROM_PPI_MODE_CACHE
          CPI       090H
          MVI       A, 090H
          CNZ       PREPARE_AND_SET_PROM_PPI
          LDA       PROM_CONTROL_STATE
          INR       A
          CALL      APPLY_PROM_CONTROL
          CALL      WRITE_PROM_DATA_ADDRESS
          MVI       L, 0E0H                                       ; mux select bits 7..5 = 111: start at channel 7, step down by 20H
PROM_SAMPLE_BIT_LOOP:                                             ; was B33
          LDA       PROM_ADDRESS_MUX_CACHE
          ORA       L
          OUT       PORT_PROM_ADDRESS_MUX
          PUSH      PSW
          POP       PSW
          IN        PORT_SWITCHES_COMPARATORS                     ; Port C0H
          ANI       001H                                          ; isolate bit 0 = level comparator HI
          MOV       C, A
          IN        PORT_SWITCHES_COMPARATORS
          ANI       002H                                          ; isolate bit 1 = l.comp. LO
          RRC                                                     ; shift to bit 0
          CMP       C                                             ; Z iff both normalized comparator bits agree; NZ means invalid logic level
          JNZ       PROM_READ_RESTORE_CONTROL
          RRC
          MOV       A, H
          RAL
          MOV       H, A
          MOV       A, L
          SUI       020H
          MOV       L, A
          JNC       PROM_SAMPLE_BIT_LOOP
          XRA       A
PROM_READ_RESTORE_CONTROL:                                        ; was B34
          MOV       A, H
          PUSH      PSW
          LDA       PROM_CONTROL_STATE
          CALL      APPLY_PROM_CONTROL
          POP       PSW
          POP       H
          POP       B
          RET
;------------------------------------------------------------------------------
; WRITE_AND_VERIFY_EDIT
;   Write C via shared editor path; read back and beep on mismatch/invalid level.
;------------------------------------------------------------------------------
WRITE_AND_VERIFY_EDIT:                                            ; was PR10B0
          PUSH      B
          MOV       C, A
          CALL      WRITE_EDIT_TARGET
          CALL      READ_EDIT_TARGET
          JNZ       EDIT_VERIFY_REPORT_ERROR
          CMP       C
EDIT_VERIFY_REPORT_ERROR:                                         ; was B35
          CNZ       ERROR_BEEP
          POP       B
          RET
;------------------------------------------------------------------------------
; WRITE_EDIT_TARGET
;   Buffer: write C at DE; PROM: program only when X7 bit 0 set.
;------------------------------------------------------------------------------
WRITE_EDIT_TARGET:                                                ; was PR10C1
          LDA       TARGET_ACCESS_MODE
          ORA       A
          JNZ       EDIT_PROM_CHECK_WRITE_ENABLE
          MOV       A, C
          XCHG
          RST       RST_MEM_WRITE
          XCHG
          RET
EDIT_PROM_CHECK_WRITE_ENABLE:                                     ; was B36
          LDA       STATUS_FLAGS
          RRC
          RNC
          CALL      PROGRAM_PROM_BYTE
          JMP       RESTORE_PROM_READ_STATE
;------------------------------------------------------------------------------
; PROGRAM_PROM_BYTE
;   Drive C to PROM at DE; table states 7/8 generate programming pulse; refresh keys/display.
;------------------------------------------------------------------------------
PROGRAM_PROM_BYTE:                                                ; was PR10D8
          CALL      SET_PROM_PPI_OUTPUT
          RST       RST_PROM_CONTROL
          DB        7
          MOV       A, C
          CALL      WRITE_PROM_DATA_ADDRESS
          RST       RST_PROM_CONTROL
          DB        8
          LDA       PROM_TYPE_INDEX
          CPI       006H
          MVI       A, 045H
          JZ        PROGRAM_PULSE_DELAY
          MVI       A, 0AEH
PROGRAM_PULSE_DELAY:                                              ; was B37
          CALL      DELAY_A_SCANS
          RST       RST_PROM_CONTROL
          DB        7
          RST       RST_PROM_CONTROL
          DB        5
          PUSH      B
          PUSH      D
          PUSH      H
          CALL      BUILD_DISPLAY_SEGMENTS
          CALL      DECODE_KEY_EDGE
          POP       H
          POP       D
          POP       B
;------------------------------------------------------------------------------
; DELAY_SIX_SCANS
;   Set A=6 and fall through to scan delay loop.
;------------------------------------------------------------------------------
DELAY_SIX_SCANS:                                                  ; was PR1102
          MVI       A, 006H
;------------------------------------------------------------------------------
; DELAY_A_SCANS
;   Repeat three-column scan delay A times; A=0 means 256 iterations.
;------------------------------------------------------------------------------
DELAY_A_SCANS:                                                    ; was PR1104
          CALL      DELAY_SCAN_THREE
          DCR       A
          JNZ       DELAY_A_SCANS
          RET
;------------------------------------------------------------------------------
; SET_PROM_PPI_OUTPUT
;   Select PPI mode 80H (all output), with control-state preparation.
;------------------------------------------------------------------------------
SET_PROM_PPI_OUTPUT:                                              ; was PR110C
          MVI       A, 080H
;------------------------------------------------------------------------------
; PREPARE_AND_SET_PROM_PPI
;   Preserve requested mode A while preparing PROM control lines.
;------------------------------------------------------------------------------
PREPARE_AND_SET_PROM_PPI:                                         ; was PR110E
          PUSH      PSW
          CALL      PREPARE_PROM_BUS
          POP       PSW
;------------------------------------------------------------------------------
; WRITE_PROM_PPI_MODE
; Called from RESET_WORKSPACE_IO... with A=92H
;   Write A to port A3 and cache at PROM_PPI_MODE_CACHE (not port C3).
;------------------------------------------------------------------------------
WRITE_PROM_PPI_MODE:                                              ; was PR1113
          OUT       PORT_PROM_PPI_CONTROL
          STA       PROM_PPI_MODE_CACHE
          RET
;------------------------------------------------------------------------------
; PREPARE_PROM_BUS
;   For control states 0/1 initialize PROM read state and set control bit 3.
;------------------------------------------------------------------------------
PREPARE_PROM_BUS:                                                 ; was PR1119
          LDA       PROM_CONTROL_STATE
          SUI       002H
          RNC
          CALL      INIT_PROM_READ
          LDA       PROM_CONTROL_CACHE
          ORI       008H
          JMP       WRITE_PROM_CONTROL
;------------------------------------------------------------------------------
; WRITE_DISPLAY_SELECT
; Write display/key select control and cache the base value for scanning and beeping.
;   RESET_WORKSPACE_IO is the sole direct caller in this ROM.
;   The author's reason for keeping a separate subroutine is not documented.
;   Output A to port 6A and cache base select/control value at DISPLAY_SELECT_CACHE.
;------------------------------------------------------------------------------
WRITE_DISPLAY_SELECT:                                             ; was PR112A
          OUT       PORT_DISPLAY_KEY_BEEPER
          STA       DISPLAY_SELECT_CACHE
          RET
;------------------------------------------------------------------------------
; INIT_PROM_READ
;   Apply states 1/2 with settling delays, then restore read state 3.
;------------------------------------------------------------------------------
INIT_PROM_READ:                                                   ; was PR1130
          CALL      PROM_STATE_ONE_DELAY
          RST       RST_PROM_CONTROL
          DB        2
          CALL      DELAY_ONE_LONG
          JMP       RESTORE_PROM_READ_STATE
;------------------------------------------------------------------------------
; PROM_STATE_ONE_DELAY
;   Apply table state 1, then one long settling delay.
;------------------------------------------------------------------------------
PROM_STATE_ONE_DELAY:                                             ; was PR113B
          RST       RST_PROM_CONTROL
          DB        1
;------------------------------------------------------------------------------
; DELAY_ONE_LONG
;   One group of 256 scan-delay iterations.
;------------------------------------------------------------------------------
DELAY_ONE_LONG:                                                   ; was PR113D
          MVI       A, 001H
;------------------------------------------------------------------------------
; DELAY_A_LONG
;   A groups of 256 scan delays, preserving outer count.
;------------------------------------------------------------------------------
DELAY_A_LONG:                                                     ; was PR113F
          PUSH      PSW
          XRA       A
          CALL      DELAY_A_SCANS
          POP       PSW
          DCR       A
          JNZ       DELAY_A_LONG
          RET
;------------------------------------------------------------------------------
; WRITE_PROM_DATA_ADDRESS
;   Output A to data port; DE to address ports; retain cached upper mux bits.
;------------------------------------------------------------------------------
WRITE_PROM_DATA_ADDRESS:                                          ; was PR114A
          OUT       PORT_PROM_DATA
          MOV       A, E
          OUT       PORT_PROM_ADDRESS_LSB
          MOV       A, D
          ANI       01FH
          MOV       D, A
          LDA       PROM_ADDRESS_MUX_CACHE
          ANI       0E0H
          ORA       D
          OUT       PORT_PROM_ADDRESS_MUX
          STA       PROM_ADDRESS_MUX_CACHE
          RET
;------------------------------------------------------------------------------
; TEST_MENU
; Test menu: entered through panel JOB/- dispatch or when restarting an existing test mode.
; The FFH command-table entry supplies the panel target; serial FFH does not enter this menu.
; SERIAL_RX_ASCII strips bit 7 and discards DEL; FFH is also the serial table-search sentinel.
;   Panel test mode=3; repeatedly read and dispatch test number.
; Command: JOB - SET, then test number. Manual 3-3-10 (PDF pp. 48-51).
;------------------------------------------------------------------------------
TEST_MENU:                                                        ; was PR115F
          LXI       SP, STACK_TOP
          MVI       A, 003H
          CALL      SET_MODE_AND_RESET
          MVI       A, 012H
          STA       DISPLAY_COMMAND
          RST       RST_WAIT_KEY
          CALL      DISPATCH_TEST
          JMP       TEST_MENU
;------------------------------------------------------------------------------
; DISPATCH_TEST
;   Accept 0..5; >=6 error; JOB returns to panel mode.
;------------------------------------------------------------------------------
DISPATCH_TEST:                                                    ; was PR1173
          JC        PANEL_MODE_RESTART
          CPI       006H
          JNC       CLEAR_AND_ERROR_BEEP
          STA       DISPLAY_PROM_TYPE
          MOV       E, A
          CALL      DECODE_PROM_TYPE
          MVI       D, 000H
          LXI       H, TEST_HANDLERS
          DAD       D
          DAD       D
          MOV       E, M
          INX       H
          MOV       D, M
          PUSH      D
          RET
;------------------------------------------------------------------------------
; TEST_HANDLERS: 0 LEDs, 1 keys, 2 TX, 3 RX, 4 load/run RAM, 5 jump address.
;------------------------------------------------------------------------------
TEST_HANDLERS:                                                    ; was A118E
          DW        TEST_LED_SEGMENTS
          DW        TEST_KEY_CODES
          DW        TEST_SERIAL_TX
          DW        TEST_SERIAL_RX
          DW        TEST_LOAD_RUN_RAM
          DW        TEST_JUMP_ADDRESS
;------------------------------------------------------------------------------
; TEST_LED_SEGMENTS
;   Test 0: mode=4 bypasses character conversion; shift lit bit through segment RAM.
;------------------------------------------------------------------------------
TEST_LED_SEGMENTS:                                                ; was PR119A
          MVI       A, 004H
          STA       OPERATING_MODE
          LXI       H, DISPLAY_SEGMENTS
          MVI       M, 001H
          INX       H
          MVI       B, 007H
          XRA       A
          CALL      FILL_BYTES                                    ; fill B bytes at HL through HL+B-1 with A; advance HL, return B=0
LED_TEST_WAIT_KEY:                                                ; was B38
          CALL      DELAY_BC
          CALL      POLL_KEY_EVENT
          RC
          CNZ       WAIT_KEY
          RC
          LXI       H, DISPLAY_SEGMENTS
          MVI       B, 008H
LED_TEST_SHIFT_SEGMENTS:                                          ; was B39
          MOV       A, M
          RAL
          MOV       M, A
          INX       H
          DCR       B
          JNZ       LED_TEST_SHIFT_SEGMENTS
          JNC       LED_TEST_WAIT_KEY
          JMP       TEST_LED_SEGMENTS
;------------------------------------------------------------------------------
; TEST_KEY_CODES
;   Test 1: display decoded keys; same key twice returns.
;------------------------------------------------------------------------------
TEST_KEY_CODES:                                                   ; was PR11C9
          MVI       B, 010H
KEY_TEST_WAIT_KEY:                                                ; was B40
          RST       RST_WAIT_KEY
          CMP       B
          RZ
          MOV       B, A
          CALL      SHOW_BYTE_VALID
          JMP       KEY_TEST_WAIT_KEY
;------------------------------------------------------------------------------
; TEST_SERIAL_TX
;   Test 2: enter a byte on keys, send serially, repeat.
;------------------------------------------------------------------------------
TEST_SERIAL_TX:                                                   ; was PR11D5
          EI
          RST       RST_WAIT_KEY
          CALL      PANEL_READ_HEX_BYTE
          RC
          RNZ
          MOV       A, L
          CALL      SERIAL_TX
          CALL      DELAY_SIX_SCANS
          JMP       TEST_SERIAL_TX
;------------------------------------------------------------------------------
; TEST_SERIAL_RX
;   Test 3: raw serial receive, show byte, wait for key; JOB exits.
;------------------------------------------------------------------------------
TEST_SERIAL_RX:                                                   ; was PR11E6
          RST       RST_RX_RAW
          EI
          CALL      SHOW_BYTE_VALID
          RST       RST_WAIT_KEY
          JNC       TEST_SERIAL_RX
          RET
;------------------------------------------------------------------------------
; TEST_LOAD_RUN_RAM
;   Test 4: require X7 bit 7; copy 128 bytes 8080..80FF -> 6080..60FF, RET into 6080.
; UNDOCUMENTED in supplied base manual: tests 0..3 only (3-24..3-27).
;   X7 D7 is shown as Don't Care (3-22), but gates tests 4/5.
;   Source is buffer logical 0080H, not the beginning at logical 0000H.
;   PUSH D / RET leaves the test-dispatch return address for user code to RET.
;------------------------------------------------------------------------------
TEST_LOAD_RUN_RAM:                                                ; was PR11F0
          LDA       STATUS_FLAGS
          ANI       080H
          JZ        CLEAR_AND_ERROR_BEEP
          LXI       H, USER_CODE_BUFFER_SOURCE
          LXI       D, USER_CODE_RAM
          PUSH      D
          MVI       B, 128
USER_CODE_COPY_LOOP:                                              ; was B41
          RST       RST_MEM_READ
          STAX      D
          INX       H
          INX       D
          DCR       B
          JNZ       USER_CODE_COPY_LOOP
          RET
          RST       RST_WAIT_KEY
;------------------------------------------------------------------------------
; TEST_JUMP_ADDRESS
;   Test 5: require X7 bit 7; enter hex address via PANEL_READ_HEX_WORD; PCHL transfers control.
; UNDOCUMENTED: direct CPU address, not logical buffer address.
;   DRAM 8000..9FFF is accessed through RST helpers, not directly executable.
;   Intended Personality Module/development use is plausible, not proven.
;------------------------------------------------------------------------------
TEST_JUMP_ADDRESS:                                                ; was PR120B
          LDA       STATUS_FLAGS
          ANI       080H
          JZ        CLEAR_AND_ERROR_BEEP
          CALL      PANEL_READ_HEX_WORD
          RC
          PCHL
RX_BITS_AND_DATA_INIT:            EQU       00800H                ; D=8 received bits, E=0 initial data accumulator | was K0800
RECORD_COUNT_CHECKSUM_INIT:       EQU       01000H                ; B=16 maximum record bytes, C=0 checksum | was K1000
DISPLAY_TWO_BLANKS:               EQU       01010H                ; two blank display characters (10H each) | was K1010
DISPLAY_BLANKS_FIRST_DP:          EQU       01090H                ; blank pair with decimal point on first stored character | was K1090
BUFFER_BASE_ADDRESS:              EQU       08000H                ; firmware address base of the I/O-accessed DRAM buffer | was K8000
USER_CODE_BUFFER_SOURCE:          EQU       08080H                ; test 4 source: buffer base +0080H | was K8080
DISPLAY_BLANKS_SECOND_DP:         EQU       09010H                ; blank pair with decimal point on second stored character | was K9010
          END
