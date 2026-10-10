# CodeBridge

Gets verification codes (SMS, iMessage, e-mail, banking apps) from your **iPhone** onto your **Windows PC**,
without an app on the phone. A small popup shows the code; press **Ctrl+Alt+V** to type it into the field
you are in, or click **Copy**.

It reads the iPhone's notifications over Bluetooth LE using Apple's own notification service (ANCS), the
same way a smartwatch does.

## Requirements

- Windows 10 (2004) or 11 with a Bluetooth 4.0+ adapter
- An iPhone with Bluetooth on

## Start

1. Unzip, run `codebridge.exe`. A tray icon appears; it starts the Bluetooth part (`codebridge-ble.exe`)
   itself. Right-click the icon for **Start with Windows**.
2. **Pair once.** On the iPhone open *Settings > Bluetooth*; the PC appears in the list of devices (its name is
   your PC's name). Tap it and accept the pairing request on the phone and on the PC. When the iPhone asks to
   share system notifications, choose **Allow**.
3. After that it reconnects by itself whenever the phone is in range.

For the full text of a notification, set the iPhone's *Settings > Notifications > Show Previews* to
**Always**.

## What it does with a code

- The popup (never takes the keyboard focus) shows the code and the app it came from, for 20 seconds.
- **Ctrl+Alt+V** types the code with simulated key presses, so it works in fields that block pasting.
- **Copy** keeps the code out of the Windows clipboard history and the cloud clipboard, and the clipboard is
  emptied after 60 seconds.
- The notification text is not stored. Only the code and the app name are kept in memory, for at most
  60 seconds. The log (`%LOCALAPPDATA%\CodeBridge\helper.log`) holds connection events only.

## What counts as a code

4 to 8 digits near a word like *code, TAN, OTP, PIN, verification, Bestätigung, Passwort, login*. Phone
numbers, amounts, times and dates are skipped. `123 456` and `123-456` count as one code.

## If it does not pair

The setup window shows what is wrong:

- **Bluetooth is turned off:** turn it on in Windows *Settings > Bluetooth & devices*.
- **No Low Energy support / no peripheral mode:** CodeBridge needs a Bluetooth 4.0+ adapter that can *advertise*
  as a Low Energy device. Many built-in adapters can, but some older or cheap USB adapters, and some drivers,
  cannot. Updating the Bluetooth driver from the PC maker sometimes helps; otherwise a recent USB adapter
  (Bluetooth 5.x, Intel or Realtek chip) is the usual fix.
- Phone Link working does not prove this: it uses classic Bluetooth, not Low Energy.

### The window stays yellow ("Waiting for the iPhone") although the phone is connected

Right-click the tray icon and choose **Run diagnostics**. After about half a minute it opens a report that
checks the adapter, the drivers, the paired devices and the phone's notification service, and ends with what
to do. The usual cause is an old pairing, for example when the iPhone was connected as a normal Bluetooth
device first. Fix: on the iPhone open *Settings > Bluetooth*, tap the (i) next to this PC and choose
*Forget This Device*; in Windows open *Settings > Bluetooth & devices* and remove the iPhone; then pair again
from the iPhone's Bluetooth list while CodeBridge is running, and tap **Allow** when it asks to share system
notifications. The report contains device names but no notification text.

If the report says **AccessDenied**, the pairing is fine and only a permission is missing: on the iPhone open
*Settings > Bluetooth*, tap the (i) next to this PC and turn on **Share System Notifications**. The window then
turns green within about 15 seconds. (When the iPhone was first paired through normal Bluetooth or Phone Link,
this switch is often off.)

## Limits

- iOS shortens some notifications (e-mail especially) to a preview. A code that is not in the preview
  cannot be seen.
- Only one iPhone, and notifications that arrive while the phone is out of range are not replayed.
- Notifications are read from the phone only; nothing is sent to it.

## Privacy

CodeBridge does not connect to the internet and has no telemetry. A notification is read from the iPhone over
Bluetooth, searched for a code in memory, and wiped; only the code and the app name are kept, for at most
60 seconds, and the clipboard copy is cleared after 60 seconds. Nothing about a notification is written to
disk. The log file holds connection events only.

## Code signing policy

<!-- Enable this line once SignPath Foundation has approved the project:
Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).
-->

Release binaries are built from this repository by GitHub Actions (`.github/workflows/build.yml`). Only
builds made there are published. The maintainer is the only person who approves a release.

## License

MIT, see [LICENSE](LICENSE). The Bluetooth helper bundles the .NET runtime (MIT).

## Build from source

Needs the [Gabbro](https://github.com/gabbro-lang/gabbro) compiler (version 0.2.0 or newer) and the .NET 10 SDK.

```
gabbro build                                   :: tray app -> out\codebridge.exe, and the detector tests
gabbro build test
cd helper && dotnet publish -c Release -o ..\dist   :: Bluetooth helper -> dist\codebridge-ble.exe
```

Put both `.exe` files in one folder.
