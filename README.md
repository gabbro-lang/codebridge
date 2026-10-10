# CodeBridge

CodeBridge shows the verification codes from your iPhone on your Windows PC. It works for SMS, iMessage,
mail and banking apps. You do not need an app on the phone.

When a code arrives, a small popup shows it. Press **Ctrl+Alt+V** to type the code into the field you are
in, or click **Copy**.

It reads the notifications over Bluetooth Low Energy with Apple's notification service (ANCS). Smartwatches
use the same service.

## What you need

- Windows 10 (version 2004 or newer) or Windows 11
- A Bluetooth adapter with Low Energy support (Bluetooth 4.0 or newer)
- An iPhone

## Install and pair

1. Run `CodeBridge-Setup.exe`, or unzip `CodeBridge.zip` and start `codebridge.exe`. A tray icon appears.
   CodeBridge starts its Bluetooth helper (`codebridge-ble.exe`) by itself.
2. On the iPhone open Settings, then Bluetooth. Your PC is in the list. Tap it and accept the pairing
   request on the phone and on the PC.
3. When the iPhone asks if it may share system notifications, tap **Allow**.
4. The setup window turns green and says "iPhone connected". From then on it reconnects by itself.

To get the whole text of a message, set Settings, Notifications, Show Previews to **Always** on the iPhone.

## How it works

- The popup does not take the keyboard focus. It stays for 20 seconds.
- **Ctrl+Alt+V** types the code with simulated key presses. This also works in fields that block pasting.
- **Copy** keeps the code out of the Windows clipboard history and the cloud clipboard. The clipboard is
  cleared after 60 seconds.
- The tray menu has "Start with Windows", "Send test code", "Run diagnostics" and "Quit".

A code is 4 to 8 digits next to a word like code, TAN, OTP, PIN, verification, Bestätigung or Passwort
(English, German, French, Spanish, Portuguese, Italian and Dutch). `123 456` and `123-456` count as one code.
A code with letters and digits, like `K7M2P9`, is found when it follows a strong word such as "code".

These are ignored: phone numbers, amounts, times, dates, and numbers that belong to an order, parcel,
invoice or balance ("Order confirmed 123456", "tracking code 90817265"). Short words only count as whole
words, so "codebridge" or "spinning" do not trigger it, and discount codes ("Use code SAVE20 for 15% off")
are skipped.

## If it does not work

The setup window shows the state. Right-click the tray icon and choose **Run diagnostics** to get a report.
It takes about half a minute and ends with what to do. The report has device names in it, but no
notification text.

**Bluetooth is turned off.** Turn it on in Windows Settings, Bluetooth & devices.

**The adapter has no Low Energy or no peripheral mode.** Your PC must be able to advertise as a Low Energy
device, or the iPhone cannot pair with it. Many built-in adapters can. Some old or cheap USB adapters and
some drivers cannot. A newer driver from the PC maker may help. If not, use a current USB adapter
(Bluetooth 5, Intel or Realtek). Phone Link working does not prove anything here, because it uses classic
Bluetooth.

**It says "Waiting for the iPhone" but the phone is connected.** The pairing is probably old, for example
from when the iPhone was first connected as a normal Bluetooth device. Remove it on both sides and pair
again. On the iPhone: Settings, Bluetooth, tap the (i) next to the PC, then Forget This Device. On the
PC: Settings, Bluetooth & devices, remove the iPhone. Then pair again from the iPhone while CodeBridge is
running.

**The report says AccessDenied.** The pairing is fine, but the iPhone has not allowed notifications for
this PC. On the iPhone: Settings, Bluetooth, tap the (i) next to the PC, and turn on **Share System
Notifications**. The window turns green after a few seconds. This switch is often off when the phone was
first paired through normal Bluetooth or Phone Link.

## Limits

- iOS cuts some notifications to a short preview, mail especially. A code that is not in the preview
  cannot be read.
- One iPhone at a time.
- Notifications that arrive while the phone is out of range are not delivered later.
- CodeBridge only reads from the phone. It sends nothing to it.

## Privacy

CodeBridge does not use the internet and has no telemetry. This program will not transfer any information to other networked systems. A notification is read over Bluetooth and
searched for a code in memory. Only the code and the name of the app are kept, for at most 60 seconds.
The notification text is not stored, and the clipboard copy is cleared after 60 seconds. The log file
(`%LOCALAPPDATA%\CodeBridge\helper.log`) has connection events only.

## Code signing policy

<!-- Enable this line when SignPath Foundation has approved the project:
Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).
-->

Release files are built from this repository by GitHub Actions (`.github/workflows/build.yml`). Only
builds made there are published. Every release is approved by hand before it is signed.

Team roles:

- Author (commits to the repository): [Christian Brendlin](https://github.com/gabbro-lang)
- Reviewer (reviews changes from others): Christian Brendlin
- Approver (approves each signing request): Christian Brendlin

Everyone with access uses multi-factor authentication.

## License

MIT. See [LICENSE](LICENSE). The Bluetooth helper includes the .NET runtime (MIT).

## Build from source

You need the [Gabbro](https://github.com/gabbro-lang/gabbro) compiler (0.2.1 or newer) and the .NET 10 SDK.

```
gabbro build
gabbro build test
cd helper
dotnet publish -c Release -o ../dist
```

`gabbro build` makes `out/codebridge.exe` and the detector test program. `gabbro build test` runs the
detector tests. The helper ends up in `dist/codebridge-ble.exe`. Put both `.exe` files in one folder.
