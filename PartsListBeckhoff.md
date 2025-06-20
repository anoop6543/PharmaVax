# Pharmaceutical Process Control System - Parts List

This document provides a detailed list of hardware components required for the pharmaceutical viral vector manufacturing process automation system, using Beckhoff automation technology as the primary control platform.

## Table of Contents
1. [Controllers and Computing Hardware](#controllers-and-computing-hardware)
2. [I/O Modules](#io-modules)
3. [Communication and Networking Hardware](#communication-and-networking-hardware)
4. [Motion Control Hardware](#motion-control-hardware)
5. [Safety Systems](#safety-systems)
6. [Power Supplies and Infrastructure](#power-supplies-and-infrastructure)
7. [Third-Party Field Devices](#third-party-field-devices)
8. [Software and Licenses](#software-and-licenses)
9. [Spare Parts](#spare-parts)

## Controllers and Computing Hardware

### Main Control System

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Embedded PC | Beckhoff | CX2062-0100 | 6 | High-performance multi-core industrial PC, Intel® Core™ i7, 2.1 GHz, 8 cores, 16 GB RAM, 512 GB SSD | Primary controllers for Upstream, Downstream, Fill-Finish areas |
| Embedded PC | Beckhoff | CX2042-0100 | 6 | Mid-range embedded PC with Intel® Core™ i7, 1.7 GHz, 4 cores, 8 GB RAM, 256 GB SSD | Secondary controllers for unit operations |
| Compact PC | Beckhoff | C6030-0060 | 3 | Ultra-compact Industrial PC, Intel® Core™ i7, 3.6 GHz, 6 cores, 32 GB RAM, 1 TB SSD | Data collection and batch management servers |
| Panel PC | Beckhoff | CP3919-0000 | 15 | 19" multi-touch panel PC with Intel® Core™ i5, 16 GB RAM, 256 GB SSD | HMIs for local control stations |
| Uninterruptible Power Supply | APC | SRT3000RMXLI | 6 | 3000VA rack-mounted UPS with network management card | Power backup for critical controllers |

### Redundancy and Fault Tolerance

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Controller Redundancy License | Beckhoff | TF1100 | 6 | EtherCAT redundancy for critical systems | Redundant control for critical process areas |
| Redundant Ethernet Ports | Beckhoff | CX2500-0060 | 12 | Ethernet port multiplier for redundant networks | Networking redundancy |
| Redundant Storage | Beckhoff | CX2900-0192 | 12 | CFast cards for RAID implementation | Data storage redundancy |

## I/O Modules

### Digital Input Modules

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Digital Input 24V | Beckhoff | EL1809 | 60 | 16-channel digital input, 24V DC, 3ms filter | General purpose digital inputs |
| Digital Input Fast | Beckhoff | EL1819 | 20 | 16-channel digital input, 24V DC, 10?s filter | High-speed interlocks and critical signals |
| Digital Input with Diagnostics | Beckhoff | EL1819-0010 | 20 | 16-channel digital input with broken wire detection | Critical monitoring points |
| Potential-Free Inputs | Beckhoff | EL1104 | 15 | 4-channel potential-free input, 24V DC | External system interfaces |

### Digital Output Modules

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Digital Output 24V | Beckhoff | EL2809 | 40 | 16-channel digital output, 24V DC, 0.5A | General purpose outputs |
| Digital Output 2A | Beckhoff | EL2819 | 15 | 16-channel digital output, 24V DC, 2A | Valve and solenoid control |
| Digital Output with Diagnostics | Beckhoff | EL2819-0010 | 15 | 16-channel digital output with diagnostics | Critical control outputs |
| Relay Outputs | Beckhoff | EL2624 | 10 | 4-channel relay output, 125V AC/30V DC, 1A | Isolation for external systems |

### Analog Input Modules

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Analog Input 4-20mA | Beckhoff | EL3048 | 50 | 8-channel analog input, 4-20mA, 16-bit | Transmitter inputs |
| Analog Input 0-10V | Beckhoff | EL3064 | 20 | 4-channel analog input, 0-10V, 12-bit | General voltage inputs |
| HART-capable Analog Input | Beckhoff | EL3054 | 15 | 4-channel analog input, 4-20mA, HART | Smart transmitter integration |
| High-precision Analog Input | Beckhoff | EL3602 | 10 | 2-channel analog input, ±10V, 24-bit | High-precision measurements |

### Analog Output Modules

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Analog Output 4-20mA | Beckhoff | EL4028 | 20 | 8-channel analog output, 4-20mA, 16-bit | Control valve positioning |
| Analog Output 0-10V | Beckhoff | EL4024 | 10 | 4-channel analog output, 0-10V, 12-bit | Speed references |
| High-precision Analog Output | Beckhoff | EL4732 | 5 | 2-channel analog output, ±10V, 16-bit, oversampling | Critical process control |

### Special Function Modules

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| RTD Input | Beckhoff | EL3204 | 15 | 4-channel RTD input, PT100, PT1000 | Temperature measurement |
| Thermocouple Input | Beckhoff | EL3314 | 10 | 4-channel thermocouple input, types J, K, L, B, E, N, R, S, T, U | High-temperature monitoring |
| Incremental Encoder Interface | Beckhoff | EL5101 | 15 | 1-channel incremental encoder interface, RS422/TTL | Position feedback |
| Load Cell Interface | Beckhoff | EL3356 | 6 | 1-channel resistive bridge, strain gauge | Weight measurement |
| Serial Interface | Beckhoff | EL6001 | 10 | 1-channel serial interface, RS232 | Legacy equipment integration |
| Serial Interface | Beckhoff | EL6021 | 8 | 1-channel serial interface, RS485 | Multi-drop serial networks |

### Remote I/O Assemblies

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| EtherCAT Coupler | Beckhoff | EK1100 | 30 | EtherCAT coupler for remote I/O islands | Remote I/O distribution |
| EtherCAT Junction | Beckhoff | EK1122 | 15 | 2-port EtherCAT junction | Network branching |
| EtherCAT Extension | Beckhoff | EK1521-0010 | 10 | Fiber optic extension, single-mode | Long-distance connections |
| Bus Terminal Power Supply | Beckhoff | EL9410 | 30 | Power supply terminal for E-bus | Remote I/O power distribution |

## Communication and Networking Hardware

### EtherCAT Network

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| EtherCAT Network Card | Beckhoff | FC1100 | 6 | PCI Express EtherCAT master network card | Additional EtherCAT segments |
| EtherCAT Junction | Beckhoff | EK1122 | 15 | 2-port EtherCAT junction | Network topology management |
| EtherCAT Media Converter | Beckhoff | CU2508 | 8 | 8-port EtherCAT junction with fiber option | Long-distance EtherCAT |
| EtherCAT Diagnotic Interface | Beckhoff | EL6070 | 6 | License key terminal for TwinCAT | System activation |

### Industrial Ethernet Infrastructure

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Managed Ethernet Switch | Hirschmann | RSPE30-08/08T3VTTEHH | 10 | 16-port managed gigabit switch with IGMP snooping, VLAN, redundancy | Backbone infrastructure |
| Fiber Optic Switch | Hirschmann | MACH1000-08/08PoEPowerSupply | 5 | 16-port managed switch with 8 fiber ports | Long-distance connections |
| Ethernet Media Converter | Hirschmann | OZD Genius 1300 | 10 | Ethernet to fiber media converter | Network extensions |
| Firewall/Router | Phoenix Contact | FL MGUARD RS4000 TX/TX VPN | 3 | Industrial firewall with VPN functionality | Security zones |

### Fieldbus Integration

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| PROFIBUS Master | Beckhoff | EL6731 | 3 | PROFIBUS DP master terminal | Legacy PROFIBUS integration |
| Foundation Fieldbus Master | Beckhoff | EL6224 | 3 | Foundation Fieldbus H1 master terminal | Process instrument integration |
| IO-Link Master | Beckhoff | EL6224 | 10 | IO-Link master terminal, 4 channels | Smart sensor integration |
| ASi Master | Beckhoff | EL6201 | 4 | AS-Interface master terminal | Simple field devices |

## Motion Control Hardware

### Servo Drives and Motors

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Servo Drive | Beckhoff | AX5118 | 8 | Single-channel servo drive, 18A | Fill pump control |
| Servo Motor | Beckhoff | AM8053-wJyz | 8 | Stainless steel servo motor, 2.0 kW | Fill pumps |
| Servo Drive | Beckhoff | AX5106 | 4 | Single-channel servo drive, 6A | Capping station |
| Servo Motor | Beckhoff | AM8043-wJyz | 4 | Servo motor, 1.0 kW | Capping heads |
| Servo Drive | Beckhoff | AX5203 | 3 | Dual-channel servo drive, 2×3A | Conveyor systems |
| Servo Motor | Beckhoff | AM8031-wJyz | 6 | Servo motor, 400W | Conveyor positioning |

### Motion Controllers

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| XTS System | Beckhoff | AT2000-0250 | 1 | Linear transport system, 4m track | Fill-line container transport |
| XPlanar System | Beckhoff | AT900x | 1 | Planar motor system, 2×3m area | Inspection station |
| NC Axes License | Beckhoff | TF5000 | 30 | NC PTP Axes license | Software motion control |

### Variable Frequency Drives

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| VFD Small | Danfoss | FC-280P-2K2 | 45 | 2.2kW VFD with EtherCAT interface | Small pumps and mixers |
| VFD Medium | Danfoss | FC-280P-7K5 | 15 | 7.5kW VFD with EtherCAT interface | Medium-sized agitators |
| VFD Large | Danfoss | FC-280P-22K | 8 | 22kW VFD with EtherCAT interface | Bioreactor agitators |
| VFD High Power | Danfoss | FC-280P-55K | 4 | 55kW VFD with EtherCAT interface | Centrifuge drives |

## Safety Systems

### Safety Controllers

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| TwinSAFE PLC | Beckhoff | EL6910 | 4 | TwinSAFE logic terminal | Safety control |
| TwinSAFE Group | Beckhoff | EL1904 | 15 | 4-channel digital input, TwinSAFE, 24V DC | Safety inputs |
| TwinSAFE Group | Beckhoff | EL2904 | 10 | 4-channel digital output, TwinSAFE, 24V DC, 0.5A | Safety outputs |
| TwinSAFE Drive | Beckhoff | AX5805 | 15 | TwinSAFE drive option card | Safe motion |

### Safety Components

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| E-Stop Button | Pilz | PIT es3.1 | 40 | Emergency stop button with LED indicator | Emergency shutdown |
| Safety Light Curtain | SICK | deTec4 Core | 10 | Type 4 safety light curtain, 900mm height | Area protection |
| Safety Scanner | SICK | microScan3 Core | 6 | Safety laser scanner, 5.5m range | Area monitoring |
| Safety Relay | Pilz | PNOZ s30 | 8 | Speed and standstill monitor | Motion safety |

## Power Supplies and Infrastructure

### Power Supplies

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| 24VDC Power Supply | PULS | QS20.241 | 25 | 24V DC, 20A, 480W power supply | I/O and field device power |
| 24VDC Redundant Power Supply | PULS | QS20.241-A2 | 15 | Redundant 24V DC, 2×20A, 960W | Critical system power |
| UPS Module | PULS | UB20.241 | 10 | DC UPS module, 24V, 20A | Short-term power backup |
| Battery Module | PULS | UC10.241 | 10 | Battery module for DC UPS | Power backup |

### Enclosures and Cabinets

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Control Cabinet | Rittal | VX SE 5866.500 | 20 | Freestanding enclosure, 1800×800×600mm | Main control cabinets |
| Remote I/O Cabinet | Rittal | AE 1380.500 | 30 | Wall-mounted enclosure, 600×800×300mm | Remote I/O stations |
| Stainless Steel Cabinet | Rittal | HD 1302.600 | 10 | Hygienic design, 760×760×300mm | Process area cabinets |
| Terminal Boxes | Rittal | KL 1517.010 | 50 | Small terminal box, 200×300×120mm | Field junction boxes |

## Third-Party Field Devices

### Process Instruments

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| pH Transmitter | Mettler Toledo | M400 | 16 | pH/ORP transmitter with Modbus | pH monitoring |
| DO Sensor | Mettler Toledo | InPro 6850i | 12 | Optical dissolved oxygen sensor | Bioreactor DO control |
| Pressure Transmitter | Endress+Hauser | Cerabar PMC71 | 48 | Digital pressure transmitter, HART | Process pressure measurement |
| Flow Meter | Endress+Hauser | Promag H 300 | 30 | Electromagnetic flowmeter, hygienic | Liquid flow measurement |
| Temperature Transmitter | Endress+Hauser | iTEMP TMT82 | 108 | HART temperature transmitter | Temperature monitoring |

### Process Valves

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Control Valve | Samson | Type 3241 | 45 | Pneumatic control valve, sanitary design | Flow control |
| Digital Valve Controller | Samson | TROVIS 3730-3 | 45 | Digital valve positioner, HART | Valve control |
| On/Off Valve | GEA | VARIVENT Type F | 120 | Pneumatic on/off valve | Process isolation |
| Valve Terminal | Festo | VTSA | 25 | Valve terminal, 8 stations | Pneumatic valve control |

### Analytical Instruments

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| TOC Analyzer | GE Analytical | Sievers M9 | 4 | TOC analyzer, 0.03-50,000 ppb | Water purity monitoring |
| Particle Counter | Particle Measuring Systems | Lasair III 110 | 8 | Particle counter, 0.1-5.0?m | Environmental monitoring |
| Conductivity Analyzer | Mettler Toledo | M800 | 24 | Conductivity transmitter, multi-channel | Conductivity measurement |
| UV Detector | Thermo Scientific | Dionex Ultimate 3000 | 6 | UV/Vis detector, 190-800nm | Process monitoring |

## Software and Licenses

### TwinCAT Software

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| TwinCAT 3 XAE | Beckhoff | TC1000 | 6 | Engineering environment license | Development platform |
| TwinCAT 3 PLC | Beckhoff | TC1200 | 15 | PLC runtime license | Control logic |
| TwinCAT 3 NC | Beckhoff | TC1250 | 5 | NC runtime license | Motion control |
| TwinCAT 3 CNC | Beckhoff | TC1270 | 2 | CNC runtime license | Complex motion |

### TwinCAT Extensions

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| TwinCAT HMI | Beckhoff | TF2000 | 15 | HMI server license | User interface |
| TwinCAT Analytics | Beckhoff | TF3500 | 1 | Analytics workbench | Process analysis |
| TwinCAT Database Server | Beckhoff | TF6420 | 3 | Database connectivity | Data logging |
| TwinCAT IoT | Beckhoff | TF6701 | 1 | MQTT connectivity | IoT integration |

### SCADA and Batch Software

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| SCADA System | COPA-DATA | zenon Supervisor | 3 | SCADA system, redundant | Process visualization |
| Batch System | COPA-DATA | zenon Batch Control | 1 | Batch management software | Recipe management |
| Historian | COPA-DATA | zenon Historian | 1 | Historical data collection | Process data storage |
| Electronic Batch Records | Werum | PAS-X | 1 | Electronic batch record system | Compliance documentation |

## Spare Parts

### Critical Spares

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Embedded PC | Beckhoff | CX2062-0100 | 1 | High-performance controller | Critical system backup |
| I/O Module Set | Beckhoff | Various | 1 lot | Assortment of critical I/O modules | Rapid repair |
| Power Supply | PULS | QS20.241 | 5 | 24V DC power supply | Power system backup |
| Network Switch | Hirschmann | RSPE30-08/08T3VTTEHH | 2 | Managed switch | Network backup |

### Maintenance Spares

| Item | Manufacturer | Model | Quantity | Description | Application Area |
|------|--------------|-------|----------|-------------|------------------|
| Terminal Blocks | Phoenix Contact | Various | 200 | Assorted terminal blocks | Cabinet wiring |
| Fuses | Various | Various | 100 | Assorted fuses | Circuit protection |
| Cable Glands | Various | Various | 100 | Assorted cable glands | Cabinet penetrations |
| Network Connectors | Various | Various | 50 | RJ45 and fiber connectors | Network maintenance |