// codebridge-ble --diagnose: checks everything CodeBridge depends on and writes a report that says what is
// wrong. It reads the Bluetooth set-up of this PC and the state of a paired iPhone; it does not read any
// notification, and it changes nothing (apart from trying to subscribe to ANCS, as the helper does).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

static class D {
    static readonly Guid Ancs = Guid.Parse("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
    static readonly Guid NotificationSource = Guid.Parse("9FBF120D-6301-42D9-8C58-25E699A21DBD");
    static readonly Guid DataSource = Guid.Parse("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");
    static readonly Guid MyService = Guid.Parse("8C0F0001-6D2A-4B6E-9E7B-3A5B1C2D4E5F");

    static readonly StringBuilder R = new();
    static readonly List<string> Findings = new();
    static void L(string s = "") { R.AppendLine(s); }
    static void H(string s) { L(); L("== " + s + " =="); }
    static void Finding(string s) { Findings.Add(s); }

    static string Run(string file, string args) {
        try {
            var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } return "(timed out)"; }
            return o.Result.Trim();
        } catch (Exception e) { return "(failed: " + e.Message + ")"; }
    }
    static string Ps(string cmd) => Run("powershell.exe", "-NoProfile -NonInteractive -Command \"" + cmd + "\"");

    public static async Task<string> Report() {
        L("CodeBridge diagnostics  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        L("Windows: " + RuntimeInformation.OSDescription + "  (" + Environment.OSVersion.Version + ")");
        var drivers = Task.Run(() => Ps("Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceClass -eq 'BLUETOOTH' -and $_.DeviceName -match 'Adapter|Radio|Wireless|Intel|Realtek|Qualcomm|MediaTek|Broadcom|CSR|Atheros' } | Select-Object DeviceName,Manufacturer,DriverVersion,DriverDate | Format-Table -AutoSize | Out-String -Width 220"));
        var services = Task.Run(() => Ps("Get-Service | Where-Object { $_.Name -like 'bth*' -or $_.Name -like 'BluetoothUserService*' -or $_.Name -eq 'BTAGService' } | Format-Table Name,Status,StartType -AutoSize | Out-String -Width 200"));
        var others = Task.Run(() => Ps("Get-Process | Where-Object { $_.Name -match 'YourPhone|PhoneExperience|nRF|Bluetooth' } | Select-Object Name,Id | Format-Table -AutoSize | Out-String"));
        var bthserv = Task.Run(() => Run("sc.exe", "query bthserv"));

        // adapter
        H("Bluetooth adapter");
        bool adapterOk = false;
        try {
            var ad = await BluetoothAdapter.GetDefaultAsync();
            if (ad == null) { L("No Bluetooth adapter found."); Finding("This PC has no usable Bluetooth adapter (or the driver is broken)."); }
            else {
                L("Address: " + ad.BluetoothAddress.ToString("X12"));
                L("Classic Bluetooth:                 " + ad.IsClassicSupported);
                L("Low Energy:                        " + ad.IsLowEnergySupported);
                L("Central role (PC reads the phone): " + ad.IsCentralRoleSupported);
                L("Peripheral role (PC is found):     " + ad.IsPeripheralRoleSupported);
                L("LE secure connections:             " + ad.AreLowEnergySecureConnectionsSupported);
                L("Advertisement offload:             " + ad.IsAdvertisementOffloadSupported);
                if (!ad.IsLowEnergySupported) Finding("The adapter has no Bluetooth Low Energy. CodeBridge cannot work with it; use a Bluetooth 4.0+ adapter.");
                else if (!ad.IsPeripheralRoleSupported) Finding("The adapter cannot act as a Low Energy peripheral, so an iPhone cannot pair with this PC. Update the Bluetooth driver from the PC maker, or use another adapter.");
                else adapterOk = true;
                if (!ad.IsCentralRoleSupported) Finding("The adapter does not support the central role; CodeBridge cannot read notifications.");
                var radio = await ad.GetRadioAsync();
                L("Radio state: " + (radio == null ? "unknown" : radio.State.ToString()));
                if (radio == null || radio.State != RadioState.On) { Finding("Bluetooth is turned off. Turn it on in Windows Settings > Bluetooth & devices."); adapterOk = false; }
            }
        } catch (Exception e) { L("Adapter check failed: " + e.Message); }

        H("Bluetooth drivers");
        L(await drivers);
        H("Bluetooth services");
        var bs = await bthserv;
        L(bs);
        L(await services);
        if (bs.Contains("STOPPED")) Finding("The Windows Bluetooth Support Service (bthserv) is stopped. Start it in services.msc.");

        H("Other apps that use the phone");
        L(await others);

        // paired devices
        H("Paired Low Energy devices");
        var le = new List<DeviceInformation>();
        try {
            foreach (var i in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true))) {
                le.Add(i);
                L($"{i.Name}  [{i.Id}]");
            }
        } catch (Exception e) { L("failed: " + e.Message); }
        if (le.Count == 0) L("(none)");
        H("Paired classic Bluetooth devices");
        var classic = new List<DeviceInformation>();
        try {
            foreach (var i in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true))) {
                classic.Add(i);
                L($"{i.Name}  [{i.Id}]");
            }
        } catch (Exception e) { L("failed: " + e.Message); }
        if (classic.Count == 0) L("(none)");
        H("Low Energy devices that are connected but NOT paired");
        try {
            foreach (var i in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false))) {
                if (!string.IsNullOrEmpty(i.Name)) L($"{i.Name}  [{i.Id}]");
            }
        } catch (Exception e) { L("failed: " + e.Message); }

        var phones = le.Where(d => d.Name != null && d.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)).ToList();
        bool classicPhone = classic.Any(d => d.Name != null && d.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase));

        // the iPhone over Low Energy
        bool ancsOk = false;
        if (phones.Count == 0) {
            if (classicPhone) Finding("An iPhone is paired as a classic Bluetooth device, but not for Low Energy. CodeBridge needs the Low Energy pairing. On the iPhone: Settings > Bluetooth > (i) next to this PC > Forget This Device. On the PC: Settings > Bluetooth & devices > remove the iPhone. Then pair again from the iPhone's Bluetooth list while CodeBridge is running.");
            else Finding("No iPhone is paired with this PC for Low Energy. Open CodeBridge, then on the iPhone go to Settings > Bluetooth and tap this PC.");
        }
        foreach (var info in phones) {
            H("iPhone over Low Energy: " + info.Name);
            try {
                using var dev = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (dev == null) { L("FromIdAsync returned null (device not available)."); Finding("Windows cannot open the iPhone's Low Energy entry. Remove it in Windows Settings and pair again."); continue; }
                L("Address: " + dev.BluetoothAddress.ToString("X12") + "  type=" + dev.BluetoothAddressType);
                L("Connection: " + dev.ConnectionStatus);
                L("Paired: " + dev.DeviceInformation.Pairing.IsPaired + "   protection: " + dev.DeviceInformation.Pairing.ProtectionLevel);
                var svcs = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                L("GATT services: status=" + svcs.Status + (svcs.ProtocolError.HasValue ? " protocolError=0x" + svcs.ProtocolError.Value.ToString("X2") : "") + "  count=" + svcs.Services.Count);
                foreach (var s in svcs.Services) L("   service " + s.Uuid + (s.Uuid == Ancs ? "   (ANCS)" : ""));
                if (svcs.Status != GattCommunicationStatus.Success) {
                    Finding("The iPhone's Low Energy services cannot be read (" + svcs.Status + "). The phone may be asleep or out of range; unlock it and run this again. If it stays like this, the stored pairing is stale: forget the PC on the iPhone and the iPhone on the PC, then pair again.");
                    continue;
                }
                var ancs = svcs.Services.FirstOrDefault(x => x.Uuid == Ancs);
                if (ancs == null) {
                    Finding("Windows sees the iPhone but not its notification service (ANCS). This happens when the Low Energy link is not encrypted with a valid pairing, usually after the phone was paired as a normal Bluetooth device first. Fix: forget the PC on the iPhone (Settings > Bluetooth > (i) > Forget This Device), remove the iPhone in Windows Settings, then pair again from the iPhone's Bluetooth list with CodeBridge running. Allow 'Share System Notifications' when asked.");
                    continue;
                }
                var cr = await ancs.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                L("ANCS characteristics: status=" + cr.Status + (cr.ProtocolError.HasValue ? " protocolError=0x" + cr.ProtocolError.Value.ToString("X2") : "") + "  count=" + cr.Characteristics.Count);
                foreach (var c in cr.Characteristics) L("   " + c.Uuid + "  " + c.CharacteristicProperties);
                if (cr.Status == GattCommunicationStatus.AccessDenied) {
                    Finding("The iPhone refuses access to its notification service (AccessDenied). The pairing itself is fine; the iPhone has not allowed this PC to read notifications. On the iPhone open Settings > Bluetooth, tap the (i) next to this PC (the entry named like your PC or 'iPhone' in the list of devices it knows) and turn on 'Share System Notifications'. If that switch is not there, forget the device on the iPhone, remove the iPhone in Windows Settings, pair again from the iPhone's Bluetooth list with CodeBridge running, and tap Allow when it asks.");
                    continue;
                }
                bool all = true;
                foreach (var g in new[] { DataSource, NotificationSource }) {
                    var ch = cr.Characteristics.FirstOrDefault(c => c.Uuid == g);
                    if (ch == null) { L("   missing characteristic " + g); all = false; continue; }
                    var w = await ch.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    L("   subscribe " + (g == DataSource ? "Data Source" : "Notification Source") + ": " + w.Status + (w.ProtocolError.HasValue ? " protocolError=0x" + w.ProtocolError.Value.ToString("X2") : ""));
                    if (w.Status != GattCommunicationStatus.Success) all = false;
                }
                if (all) { ancsOk = true; L("ANCS works from this PC."); }
                else Finding("ANCS is there but subscribing to it failed (see above). Most often the pairing is stale or 'Share System Notifications' was not allowed: on the iPhone forget the PC, in Windows remove the iPhone, pair again, and tap Allow when the iPhone asks to share system notifications.");
            } catch (Exception e) { L("failed: " + e.GetType().Name + ": " + e.Message); }
        }

        // can this PC be found?
        H("This PC as a Low Energy device");
        if (adapterOk) {
            try {
                var created = await GattServiceProvider.CreateAsync(MyService);
                L("GATT server: " + created.Error);
                if (created.Error == BluetoothError.Success) {
                    try { created.ServiceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true }); L("Advertising started: yes"); created.ServiceProvider.StopAdvertising(); }
                    catch (Exception e) { L("Advertising failed: " + e.Message); Finding("The adapter refused to advertise (" + e.Message + ")."); }
                } else Finding("The Windows GATT server could not be created (" + created.Error + "), so an iPhone cannot pair with this PC.");
            } catch (Exception e) { L("failed: " + e.Message); }
        } else L("(skipped: the adapter check above failed)");

        // verdict
        H("RESULT");
        if (ancsOk && Findings.Count == 0) L("Everything needed is in place: the iPhone shares its notifications with this PC.");
        else if (Findings.Count == 0) L("No problem was found, but the iPhone could not be tested. Unlock the phone, keep it near the PC and run this again.");
        else { int n = 1; foreach (var f in Findings) L((n++) + ". " + f); }
        L();
        L("Send this file when asking for help. It contains device names and Bluetooth addresses, but no notification text.");
        return R.ToString();
    }
}
