# Getting started, step by step

This chapter walks you from installing the program to your first QSO: the station profile, CAT
rig control (with the FT-710 as the example), audio for the digital modes, and the LoTW, QRZ.com
and other accounts. You only do this once; after that, everything is remembered from one session
to the next.

The screenshots in this help were taken with **test data** (that is why the «DATOS DE PRUEBA» —
TEST DATA — badge shows at the bottom left): the QSOs, the cluster spots and the radio are made
up. The screenshots show the program in Spanish; in English the buttons carry the names used in
this chapter.

## Step 1 · Install

1. Run `CuadernoNodisla-Instalador-<version>.exe`.
2. The installer asks whether to install **just for your user** (no administrator rights needed;
   it goes to `%LocalAppData%\Programs\Cuaderno NODISLA`) or **for all users** (Program Files,
   needs administrator rights). Either is fine.
3. When it finishes there is a shortcut in the Start menu, with the logbook icon.

You do not need to install .NET or anything else: the program carries everything it needs.

**Your data does not live in the program folder** but in `%AppData%\CuadernoNodisla\`: the log
(`cuaderno.sqlite`, a single file), the settings, the QSL and award templates and the log file.
Installing, updating or uninstalling never touches that folder.

For the radio, Windows needs its drivers:

- **FT-710 CAT**: the Silicon Labs *CP210x* driver (the one from Yaesu). It creates two serial
  ports: *Enhanced COM Port* and *Standard COM Port*.
- **Audio**: nothing needed; the radio's USB codec shows up by itself as “USB Audio Device”.
- **FT-710 spectrum scope** (optional): the FTDI *D2XX* driver. See step 4.

## Step 2 · First start: the station profile

The first time, before letting you touch anything, the program asks for a **station profile**:
the call sign you transmit with and where you operate from. Without it the log cannot be exported
to ADIF or used to apply for awards, so there is no way to skip it.

![Welcome window asking for the station profile (sample data)](../capturas/ayuda/primer-uso-perfil.png)

- **Station call sign** — the only required field.
- **Locator** — optional, but without it there are no distances, bearings or propagation.
- **Operator name** and **Town / city** — optional.
- **Profile name** — to tell it apart if you ever create more (Home, Portable…).

Press **Create profile and start**. “Exit the program” closes without creating anything.

You can have several profiles and choose the active one in the **Station profile** drop-down in
the header. QSOs are always logged with the active profile.

### Bringing in your previous log

If the log is empty, the program says so and offers to **import an ADIF file** (the backup from
Log4OM, from another program or from LoTW). **It never imports anything by itself**: you decide
with “Import an ADIF…” or “Start from scratch”. Duplicates **are merged, never discarded**,
and when it finishes you get a report with the confirmations that were recovered. You can import
later from **Settings › Log (ADIF)**.

## Step 3 · Getting to know the window

![The main window: the navigation bar at the top and the Operate shack](../capturas/ayuda/operar.png)

At the top, the header with the active profile, the light or dark theme and the UTC and local
clock, with the solar strip below it. Underneath, **a single bar**:

- **Operate** — Shack, Digital, Satellites and Net control.
- **Log** — QSOs and Map.
- **QSL** — QSL card, Labels and Award designer.
- **Awards** — tracking of the awards you have and the ones still to go.
- On the right, **Help** (or `F1`) and **Settings**.

When you choose a group, its pages appear in the same bar, right after it. At the bottom, the
status bar with a badge for each service (green up to date, amber waiting, red with a problem),
the number of QSOs and the text size control (`−`, `+`, `100 %`).

## Step 4 · Settings › Radio (CAT), with the FT-710

Open **Settings** (at the right of the bar) and the **Radio (CAT)** section.

![Settings › Radio (CAT) with the FT-710: native CAT, Yaesu, FT-710 and 115,200 (screenshot with a simulated rig: on your PC, choose the Enhanced COM Port under “Serial port”)](../capturas/ayuda/primer-uso-equipo-cat.png)

1. **Control method**: *Native CAT (Yaesu and ICOM)*. It is the only one that gives you filters,
   noise controls, meters, memories and the drawn front panel. (Hamlib *rigctld* and *OmniRig*
   are also available, for rigs that are not on the list.)
2. **Manufacturer**: *Yaesu*. **Model**: *FT-710*. If you are not sure, leave automatic
   detection on: the program asks for the identification on each port.
3. **Serial port**: the FT-710's **Enhanced COM Port** (in Device Manager, *Silicon Labs Dual
   CP210x USB to UART Bridge: Enhanced COM Port*; the one Yaesu calls CAT-1). The *Standard* port
   (CAT-2) is for PTT and keying: do not use it for CAT. If you do not know the number, tick
   **Detect automatically which port the radio is on**: the port is found by what the radio
   answers, never by its number.
4. **Baud rate**: **115,200**, and the same on the radio: **MENU › OPERATION SETTING › GENERAL ›
   CAT-1 RATE = 115200 bps**. The rig sets the speed, not the cable; if they do not match, it
   does not answer.
5. **PTT**: *Via CAT*. Leave the **maximum transmit time** at 180 s (hard limit 600 s): the
   watchdog releases the PTT whatever happens.
6. Press **Test**: it opens the port, reads the identification, frequency and mode, and closes.
   **It never transmits.** If the radio answers, press **Apply**.

Then, in **Operate › Shack**, press **Connect**: the FT-710 front panel appears with its two VFOs.
**Opening the program never connects to the radio by itself**: connecting is always a button.

### The FT-710 spectrum on the front panel display

The front panel's scope shows **the radio's own spectrum**, not one computed here. For that:

- On the radio: **MENU › OPERATION SETTING › GENERAL › SCU-LAN10 = ON**. You do not need to own
  the SCU-LAN10: that setting is what opens the spectrum output over USB.
- On the PC: the **FTDI D2XX** driver (the FT-710 has an FTDI FT4222 bridge inside).

If something is missing, the scope says so in words (“library missing”, “device not found”, “no
frames arriving: check SCU-LAN10”). The rest of the program works the same without it.

### Other radios

Besides the FT-710 there are the **Yaesu** FTDX10, FTDX101D/MP, FT-991/991A, FT-891, FTDX3000,
FTDX5000, FTDX1200 and the older-CAT rigs (FT-817, FT-818, FT-857, FT-897), and the **ICOM**
IC-7300, IC-7610, IC-705, IC-7100, IC-9700 and IC-7851 (via CI-V, with their address). All except
the FT-710 are implemented **from the manual, without testing on the radio**, and the program
warns you about it. See [Equipos](15-equipos.md) (Radios).

## Step 5 · Settings › Audio & digital

![Settings › Audio & digital: input and output of the rig's codec](../capturas/ayuda/primer-uso-audio.png)

1. **Input (from the radio)** and **Output (to the radio)**: the radio's USB codec. It is marked
   **RIG** and listed first: it is recognised by its USB identifier, not by its name. On the
   FT-710 they are *Microphone (USB Audio Device)* and *Speakers (USB Audio Device)*.
2. **Samples per second**: 48,000.
3. **Test level** with the radio receiving band noise: a good level is between **0.20 and
   0.60**. Above 0.90 the codec clips; turn it down on the radio (on the FT-710, the USB audio
   output level in the menu) until it shows green.
4. The **Transmitting and logging** tick boxes come set correctly from the factory: ask before
   transmitting, do not remember the permission to transmit between sessions, and log only the
   complete QSO. Press **Save**.

For the digital modes, the radio must be in **DATA-U** (or DATA-L below 10 MHz), and for phone
through the PC, with the modulation source set to USB: see
[Fonía por el PC](12-fonia.md) (Phone through the PC).

**The PC clock matters**: FT8 does not forgive more than one second of error. In
**Operate › Digital** the clock is at the very top, with **Measure** and **Set the clock**. See
[Digital](03-digital.md).

## Step 6 · Settings › Accounts & services

![Settings › Accounts & services: one card per service](../capturas/ayuda/primer-uso-cuentas.png)

**One card per service**, each with its status at the top right (green *Configured*, amber
*Incomplete*, grey *Not configured*):

1. **QRZ.com** — user name and password (fills in the other station's name, QTH and locator) and
   the *QRZ logbook key* to upload QSOs (QRZ › Logbook › Settings › API).
2. **LoTW** — first install **TQSL** with your certificate. Enter the **TQSL station location**
   (the name you gave it in TQSL, not your call sign), the LoTW password and the certificate
   passphrase, and press **Save account**. The card says clearly what is missing.
3. **eQSL.cc**, **Club Log** (with its API key) and **HamQTH** (fallback for QRZ).
4. **DX cluster** and **E-mail (SMTP)** take you to their own sections with **Configure…**.

Passwords are stored **encrypted with your Windows account** and are never shown again. Once the
accounts are set up, in **Uploads & QRZ** you tick which services receive each new QSO. Details in
[Configuración](09-ajustes.md) (Settings).

## Step 7 · Ready to operate

- In **Operate › Shack**, **Connect** the rig and the DX cluster.
- Type a call sign: the portrait tells you whether it brings anything new (new country, band or
  mode).
- **Enter** logs the QSO; **Esc** clears; **F7** shows more fields.

![The built-in help, open at this chapter](../capturas/ayuda/primer-uso-ayuda.png)

And whenever in doubt: **F1** opens the help at the chapter for the page you are on. From Help
itself you can **search**, **report a bug** and **check for updates**.

## Three rules that apply to the whole program

- **Nothing connects by itself.** The rig, the DX cluster and the modem start disconnected.
- **Nothing transmits by itself.** Transmitting, keying the PTT or calling CQ are always explicit
  buttons, with **RELEASE PTT** always at hand.
- **Credentials are encrypted** and never shown again.
