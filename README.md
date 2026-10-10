# Basic Components of a PLC

A Programmable Logic Controller (PLC) is an industrial computer that monitors inputs, makes decisions based on a stored program, and controls outputs. Its main components are:

## 1. Central Processing Unit (CPU)

The “brain” of the PLC. It:

Executes the user program in a repeating scan cycle (read inputs → execute logic → update outputs → housekeeping/communications)
Manages memory and communications
Performs diagnostics and self-checks
## 2. Power Supply

Converts incoming mains power (e.g., 120/240 VAC) to the low-voltage DC (typically 5 V and 24 V DC) needed by the CPU and modules. Some designs provide 24 V DC for powering field sensors.

## 3. Memory
System memory (ROM/flash): Stores the operating system/firmware
User program memory: Stores the control logic
Data memory (RAM): Holds I/O status, timers, counters, internal bits, and variables
Battery or non-volatile storage retains data through power loss

## 4. Input Modules

Interface between field devices and the CPU. They convert real-world signals into logic-level signals the CPU can process, and typically provide electrical isolation (often optocouplers) to protect the CPU from voltage spikes.

Digital (discrete) inputs: ON/OFF signals from

Pushbuttons and switches
Limit and proximity switches
Photoelectric sensors
Contact status from relays

Analog inputs: Continuously varying signals (4-20 mA, 0-10 V) from

Temperature sensors (thermocouples, RTDs)
Pressure and flow transmitters
Level sensors

Analog signals are converted by an ADC (analog-to-digital converter).

## 5. Output Modules

Take CPU decisions and drive field devices, again with isolation.

Digital outputs: ON/OFF control of

Motor starters and contactors
Solenoid valves
Indicator lamps, alarms, and relays

Output types include relay (AC/DC, slower, high current), transistor (DC, fast), and triac (AC, switching).

Analog outputs: Variable signals (4-20 mA, 0-10 V) via a DAC (digital-to-analog converter) to control

Variable frequency drives (speed reference)
Proportional valves
Analog meters
## 6. Programming Device

Used to create, download, monitor, and troubleshoot the program. It can be:

A PC/laptop running vendor software (e.g., TIA Portal, Studio 5000, CODESYS, GX Works)
A handheld programmer (older/simple systems)
An HMI for limited online edits or monitoring

Programs are written in languages defined by IEC 61131-3: Ladder Diagram (LD), Function Block Diagram (FBD), Structured Text (ST), Instruction List (IL), and Sequential Function Chart (SFC).

## 7. Communication Interfaces

Ports and modules for connecting to programming devices, HMIs/SCADA, other PLCs, and remote I/O, using protocols such as Ethernet/IP, Modbus, PROFINET, and PROFIBUS.

## 8. Rack / Backplane (modular PLCs)

A chassis with a backplane bus that holds the CPU, power supply, and I/O modules and carries data and power between them. Compact PLCs integrate these into a single unit.

# How they work together
    Field Inputs → [Input Module] → [CPU + Memory] → [Output Module] → Field Outputs
                                         ↑            
                                [Programming Device]

- Field Inputs: sensors, switches
- Field Outputs: motors, valves, lamps