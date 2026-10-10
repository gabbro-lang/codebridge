// codebridge-ble: the Bluetooth half of CodeBridge.
//
// It reads the iPhone notifications over ANCS and writes one line per notification,
// "app<TAB>title<SPACE>message", to the named pipe "codebridge" that the tray app serves.
//
// First run: the iPhone has to be bonded once (let it find the PC through the ANCS solicitation
// this program advertises, or connect with nRF Connect, and accept pairing). After that the bond
// is remembered: Windows keeps a connection to the iPhone open for us and the iPhone reconnects.
//
// Nothing about the notifications is logged; helper.log in LocalAppData/CodeBridge holds
// connection events only.
using System.IO.Pipes;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

static class P {
    static readonly Guid Ancs = Guid.Parse("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
    static readonly Guid ControlPoint = Guid.Parse("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
    static readonly Guid NotificationSource = Guid.Parse("9FBF120D-6301-42D9-8C58-25E699A21DBD");
    static readonly Guid DataSource = Guid.Parse("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");
    static readonly Guid MyService = Guid.Parse("8C0F0001-6D2A-4B6E-9E7B-3A5B1C2D4E5F");
    static readonly Guid MyChar = Guid.Parse("8C0F0002-6D2A-4B6E-9E7B-3A5B1C2D4E5F");

    static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeBridge", "helper.log");
    static readonly object LogLock = new();
    static void Log(string s) {
        lock (LogLock) {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + s + Environment.NewLine);
        }
        Console.WriteLine(s);
    }

    // what the tray app shows: "connected" while an iPhone has ANCS subscribed, else "waiting"
    static volatile string State = "waiting";
    static void SetState(string st) { State = st; Outbox.Add("!" + (char)9 + st); }

    // the pipe to the tray app
    static readonly System.Collections.Concurrent.BlockingCollection<string> Outbox = new();
    static void PipeWriter() {
        while (true) {
            try {
                using var pipe = new NamedPipeClientStream(".", "codebridge", PipeDirection.Out);
                pipe.Connect(2000);
                Log("connected to the tray app");
                using var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                w.WriteLine("!" + (char)9 + State);
                foreach (var line in Outbox.GetConsumingEnumerable()) w.WriteLine(line);
            } catch (Exception) { Thread.Sleep(1500); }   // the tray app is not running (yet) or went away
        }
    }

    static string Flat(string s) {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c < ' ' ? ' ' : c);   // tabs and newlines would break the framing
        return sb.ToString();
    }

    // one ANCS session per connected iPhone
    sealed class Session {
        public BluetoothLEDevice Dev;
        GattCharacteristic control;
        readonly object gate = new();
        List<byte> buf;                       // the response being assembled, null when idle
        public bool Ready;

        static byte[] Bytes(IBuffer b) { var a = new byte[b.Length]; DataReader.FromBuffer(b).ReadBytes(a); return a; }

        // a keyboard or mouse has no ANCS and that is fine; only a phone failing is worth reporting
        public bool Apple => Dev.Name != null && (Dev.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || Dev.Name.Contains("iPad", StringComparison.OrdinalIgnoreCase));
        public string Why = "";
        public bool Denied;                    // the iPhone has not allowed this PC to share notifications                // why Setup failed, for the log and the diagnostics hint

        async Task<bool> TrySetup() {
            var svc = await Dev.GetGattServicesForUuidAsync(Ancs, BluetoothCacheMode.Uncached);
            if (svc.Status != GattCommunicationStatus.Success) { Why = "GATT services: " + svc.Status + (svc.ProtocolError.HasValue ? " 0x" + svc.ProtocolError.Value.ToString("X2") : ""); return false; }
            if (svc.Services.Count == 0) { Why = "the iPhone shows no ANCS service (link not encrypted or stale pairing)"; return false; }
            var cr = await svc.Services[0].GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (cr.Status != GattCommunicationStatus.Success) {
                Why = "ANCS characteristics: " + cr.Status;
                Denied = cr.Status == GattCommunicationStatus.AccessDenied;       // iPhone: Settings > Bluetooth > (i) > Share System Notifications
                return false;
            }
            Denied = false;
            var chars = cr.Characteristics;
            control = chars.FirstOrDefault(c => c.Uuid == ControlPoint);
            var ns = chars.FirstOrDefault(c => c.Uuid == NotificationSource);
            var ds = chars.FirstOrDefault(c => c.Uuid == DataSource);
            if (control == null || ns == null || ds == null) { Why = "ANCS characteristics missing"; return false; }
            ds.ValueChanged += (s, a) => OnData(Bytes(a.CharacteristicValue));
            ns.ValueChanged += (s, a) => OnNotification(Bytes(a.CharacteristicValue));
            var r1 = await ds.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            var r2 = await ns.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            Ready = r1.Status == GattCommunicationStatus.Success && r2.Status == GattCommunicationStatus.Success;
            if (!Ready) Why = "subscribe: " + r1.Status + (r1.ProtocolError.HasValue ? " 0x" + r1.ProtocolError.Value.ToString("X2") : "") + " / " + r2.Status + (r2.ProtocolError.HasValue ? " 0x" + r2.ProtocolError.Value.ToString("X2") : "");
            return Ready;
        }

        public async Task<bool> Setup() {
            bool ok = await TrySetup();
            if (!ok && Apple && !Dev.DeviceInformation.Pairing.IsPaired) {
                Log("not paired for Low Energy, asking Windows to pair");
                var pr = await Dev.DeviceInformation.Pairing.PairAsync(DevicePairingProtectionLevel.Encryption);
                Log("PairAsync: " + pr.Status);
                ok = await TrySetup();
            }
            if (ok) Log("ANCS subscribed: True");
            else if (Apple && Why != LastWhy) { Log("ANCS not available: " + Why); LastWhy = Why; }
            return ok;
        }
        static string LastWhy = "";

        void OnNotification(byte[] p) {
            if (p.Length < 8 || p[0] != 0) return;            // only "added"
            if ((p[1] & 0x04) != 0) return;                   // pre-existing: it was there before we connected
            _ = Ask(BitConverter.ToUInt32(p, 4));
        }

        async Task Ask(uint uid) {
            // GetNotificationAttributes(uid): AppIdentifier, Title (max 64), Message (max 2000), little endian
            var w = new DataWriter { ByteOrder = ByteOrder.LittleEndian };
            w.WriteByte(0); w.WriteUInt32(uid);
            w.WriteByte(0);
            w.WriteByte(1); w.WriteUInt16(64);
            w.WriteByte(3); w.WriteUInt16(2000);
            lock (gate) { asked[uid] = DateTime.UtcNow; }
            try { await control.WriteValueAsync(w.DetachBuffer(), GattWriteOption.WriteWithResponse); }
            catch (Exception e) { Log("control point write failed: 0x" + e.HResult.ToString("X")); }
        }

        // Other apps on the PC (Phone Link) may use ANCS on the same link, and the Data Source then also
        // carries the answers to THEIR requests. Only a response that starts with a uid we asked for is
        // taken; everything else is ignored, and a half-received response is dropped after 3 seconds.
        readonly Dictionary<uint, DateTime> asked = new();
        DateTime bufSince;

        void OnData(byte[] chunk) {
            // [0][uid:4] then [attr:1 len:2 data]...; may come in several pieces
            lock (gate) {
                var now = DateTime.UtcNow;
                if (buf != null && (now - bufSince).TotalSeconds > 3) buf = null;
                foreach (var old in asked.Where(k => (now - k.Value).TotalSeconds > 30).Select(k => k.Key).ToList()) asked.Remove(old);
                if (buf == null) {
                    if (chunk.Length < 5 || chunk[0] != 0 || !asked.ContainsKey(BitConverter.ToUInt32(chunk, 1))) return;
                    buf = new List<byte>();
                    bufSince = now;
                }
                buf.AddRange(chunk);
                var p = buf.ToArray();
                uint uid = BitConverter.ToUInt32(p, 1);
                int pos = 5, seen = 0;
                string app = "", title = "", msg = "";
                while (pos + 3 <= p.Length) {
                    int id = p[pos], len = p[pos + 1] | (p[pos + 2] << 8);
                    if (pos + 3 + len > p.Length) return;           // wait for the rest
                    var text = Encoding.UTF8.GetString(p, pos + 3, len);
                    if (id == 0) { app = text; seen |= 1; } else if (id == 1) { title = text; seen |= 2; } else if (id == 3) { msg = text; seen |= 4; }
                    pos += 3 + len;
                }
                if (seen != 7) return;                              // not all three yet
                buf = null;
                asked.Remove(uid);
                Outbox.Add(Flat(app) + (char)9 + Flat(title) + " " + Flat(msg));
            }
        }
    }

    static readonly Dictionary<string, Session> Sessions = new();
    static readonly SemaphoreSlim Gate = new(1, 1);

    static async Task Attach(string id) {
        await Gate.WaitAsync();
        try {
            lock (Sessions) { if (Sessions.TryGetValue(id, out var old) && old.Ready) return; }
            var dev = await BluetoothLEDevice.FromIdAsync(id);
            if (dev == null) return;
            var s = new Session { Dev = dev };
            if (await s.Setup()) {
                lock (Sessions) { Sessions[id] = s; }
                SetState("connected");
                dev.ConnectionStatusChanged += (d, _) => {
                    Log(d.Name + ": " + d.ConnectionStatus);
                    if (d.ConnectionStatus == BluetoothConnectionStatus.Disconnected) { lock (Sessions) { Sessions.Remove(id); } SetState("waiting"); }
                    else _ = Attach(id);
                };
            } else { dev.Dispose(); bool any; lock (Sessions) { any = Sessions.Count > 0; } if (!any && (State == "waiting" || State.StartsWith("ancs")) && s.Apple) SetState(s.Denied ? "ancs-denied" : "ancs-failed"); }
        } catch (Exception e) { Log("attach failed: " + e.Message); }
        finally { Gate.Release(); }
    }

    // keep Windows connected to every bonded LE device and attach ANCS where there is one
    static async Task WatchBonded() {
        var keep = new List<GattSession>();
        var known = new HashSet<string>();
        while (true) {
            try {
                var sel = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                foreach (var info in await DeviceInformation.FindAllAsync(sel)) {
                    if (known.Add(info.Id)) {
                        var gs = await GattSession.FromDeviceIdAsync(BluetoothDeviceId.FromId(info.Id));
                        gs.MaintainConnection = true;
                        keep.Add(gs);
                        Log("watching bonded device: " + info.Name);
                    }
                    bool have; lock (Sessions) { have = Sessions.ContainsKey(info.Id); }
                    if (!have) await Attach(info.Id);
                }
            } catch (Exception e) { Log("bonded scan failed: " + e.Message); }
            await Task.Delay(15000);
        }
    }

    // is this PC able to do it at all?
    // null when Bluetooth is fine, otherwise a state the tray app explains to the user
    static async Task<string> Problem() {
        try {
            var ad = await BluetoothAdapter.GetDefaultAsync();
            if (ad == null) return "no-adapter";
            if (!ad.IsLowEnergySupported) return "no-le";
            if (!ad.IsPeripheralRoleSupported) return "no-peripheral";
            var radio = await ad.GetRadioAsync();
            if (radio == null || radio.State != RadioState.On) return "radio-off";
        } catch (Exception e) { Log("adapter check failed: " + e.Message); }
        return null;
    }

    static async Task HealthLoop() {
        while (true) {
            var problem = await Problem();
            bool bad = State == "no-adapter" || State == "no-le" || State == "no-peripheral" || State == "radio-off";
            if (problem != null && State != problem) { Log("bluetooth problem: " + problem); SetState(problem); }
            else if (problem == null && bad) {
                Log("bluetooth ok again");
                try { provider?.StopAdvertising(); publisher?.Stop(); } catch (Exception) { }
                _ = AdvertiseLoop();                              // the radio was off: advertise afresh
                SetState(Sessions.Count > 0 ? "connected" : "waiting");
            }
            await Task.Delay(4000);
        }
    }

    // be findable: a connectable GATT server plus the ANCS service-solicitation advertisement
    static GattServiceProvider provider;
    static BluetoothLEAdvertisementPublisher publisher;
    static async Task AdvertiseLoop() {
        while (!await Advertise()) await Task.Delay(10000);       // e.g. Bluetooth is off for now
    }

    static async Task<bool> Advertise() {
        var created = await GattServiceProvider.CreateAsync(MyService);
        if (created.Error != BluetoothError.Success) { Log("GATT server failed: " + created.Error); return false; }
        provider = created.ServiceProvider;
        var cp = new GattLocalCharacteristicParameters {
            CharacteristicProperties = GattCharacteristicProperties.Read,
            ReadProtectionLevel = GattProtectionLevel.EncryptionRequired,
            UserDescription = "codebridge"
        };
        var ch = await provider.Service.CreateCharacteristicAsync(MyChar, cp);
        ch.Characteristic.ReadRequested += async (s, a) => {
            var d = a.GetDeferral();
            var req = await a.GetRequestAsync();
            var w = new DataWriter(); w.WriteString("codebridge");
            req.RespondWithValue(w.DetachBuffer());
            Log("a device read the pairing characteristic");
            _ = Attach(a.Session.DeviceId.Id);
            d.Complete();
        };
        provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        publisher = new BluetoothLEAdvertisementPublisher();
        publisher.Advertisement.DataSections.Add(new BluetoothLEAdvertisementDataSection { DataType = 0x15, Data = Ancs.ToByteArray().AsBuffer() });
        publisher.Start();
        Log("advertising (GATT server + ANCS solicitation)");
        return true;
    }

    static async Task Main(string[] args) {
        if (Array.IndexOf(args, "--diagnose") >= 0) {
            var report = await D.Report();
            var file = Path.Combine(Path.GetTempPath(), "CodeBridge-Diagnose.txt");
            File.WriteAllText(file, report, new UTF8Encoding(true));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", file) { UseShellExecute = true });
            return;
        }
        Log("codebridge-ble started");
        // --parent <pid>: quit together with the tray app that started us
        int pi = Array.IndexOf(args, "--parent");
        if (pi >= 0 && pi + 1 < args.Length && int.TryParse(args[pi + 1], out int ppid)) {
            try {
                var parent = System.Diagnostics.Process.GetProcessById(ppid);
                _ = Task.Run(() => { parent.WaitForExit(); Environment.Exit(0); });
            } catch (Exception) { return; }          // the parent is already gone
        }
        // a second copy would fight over the Bluetooth link
        var once = new Mutex(true, "CodeBridge.Ble.Single", out bool first);
        if (!first) return;
        new Thread(PipeWriter) { IsBackground = true }.Start();
        _ = HealthLoop();
        _ = WatchBonded();
        _ = AdvertiseLoop();
        await Task.Delay(Timeout.Infinite);
    }
}
