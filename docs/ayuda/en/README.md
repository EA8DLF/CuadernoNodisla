# Cuaderno NODISLA Help

This is the program's help, written for a radio amateur sitting in front of the screen who wants
to know what each thing does. It is not a field-by-field reference manual: it is what you need to
operate without tripping up.

Cuaderno NODISLA is a logbook for radio amateurs: it controls your rig via CAT, runs the digital
modes with its own modem, follows the DX cluster and your awards, designs and sends QSL cards and
awards, and keeps everything in a single file, `%AppData%\CuadernoNodisla\cuaderno.sqlite`, easy to
copy and to move to another computer.

> Only some chapters of this help have been translated into English so far. The others open in
> Spanish, with a notice at the top.

**First time?** Start with [Getting started, step by step](01-primer-uso.md).

## How the window is organised

At the top there is a single bar with four entries grouped by use and, at the far right, Help and
Settings. When you choose an entry, its pages appear in the same bar, right after it; when you go
back to it, you return to the page you last saw.

| Entry | Pages |
|---|---|
| **Operate** | Shack (`Ctrl` `1`) · Digital (`Ctrl` `2`) · Satellites (`Ctrl` `7`) · Net control (`Ctrl` `9`) |
| **Log** | QSOs (`Ctrl` `3`) · Map (`Ctrl` `4`) |
| **QSL** | QSL card (`Ctrl` `8`) · Labels · Award designer (`Ctrl` `Shift` `5`) |
| **Awards** | Award tracking (`Ctrl` `5`) |
| **Help** | This help (`F1` opens the chapter for the page you are on) |
| **Settings** | Accounts, radio, audio, phone, cluster, e-mail, log and updates (`Ctrl` `6`) |

## Contents

1. [Getting started, step by step](01-primer-uso.md) — installing, the station profile, CAT rig
   control with the FT-710, audio and online accounts.
2. [Operar](02-operar.md) (Operating) — the shack: the front panel, the new QSO entry, the call
   sign portrait, the DX cluster and the bandmap.
3. [Digital](03-digital.md) — the built-in modem: FT8, FT4, WSPR, JT65, JT9, Q65, MSK144, FST4 and
   FST4W; the clock, the waterfall and the automatic sequence.
4. [Contactos](04-cuaderno.md) (QSOs) — the log grid: searching, editing and merging duplicates.
5. [Diplomas](05-diplomas.md) (Awards) — NEW, BAND and MODE, the confirmation paths and when a
   figure is not final.
6. [Mapa](06-mapa.md) (Map) — QSOs, spots, grey line and paths.
7. [Satélites](07-satelites.md) (Satellites) — the next pass, live azimuth and elevation, and
   Doppler.
8. [QSL: etiquetas del buró](08-impresion-qsl.md) (QSL bureau labels) — filter, Avery templates
   and PDF.
9. [Configuración](09-ajustes.md) (Settings) — every section.
10. [Atajos de teclado](10-atajos-de-teclado.md) (Keyboard shortcuts) — everything you can do
    without a mouse.
11. [Ronda de control](11-ronda-de-control.md) (Net control) — running a net and logging the
    check-ins.
12. [Fonía por el PC](12-fonia.md) (Phone through the PC) — listening to the radio on the PC and
    talking through its microphone.
13. [Tarjeta QSL propia](13-tarjeta-qsl.md) (Your own QSL card) — designing it and sending it by
    e-mail; eQSL.
14. [Diseñador de diplomas](14-disenador-de-diplomas.md) (Award designer) — your own awards and
    certificates, with numbering, PDF, e-mail and history.
15. [Equipos](15-equipos.md) (Radios) — the FT-710, the other Yaesu and ICOM rigs, the front
    panel, the spectrum scope and MULTI.
16. [Ayuda, actualizaciones y fallos](16-ayuda-actualizaciones-y-fallos.md) (Help, updates and
    bugs) — the help, the new-version notice, reporting a bug and “About”.

## Before anything else

Three things that apply to the whole program:

- **Nothing connects by itself.** The rig, the DX cluster and the modem start disconnected.
  Connecting is always a button the operator presses.
- **Nothing transmits by itself.** Sending a digital message, keying the PTT or calling CQ are
  always explicit actions, with **RELEASE PTT** always at hand.
- **Credentials are encrypted.** LoTW, eQSL, Club Log, QRZ… are stored with your Windows account's
  data protection, never in a readable file, and are never shown again once entered.
