using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// Read-only probe. Direct HID access is opt-in; never writes controller reports.
internal static class Probe
{
    internal static Action<IntPtr, byte[]> LabReport; internal static StreamWriter Log;
    internal static string Match = "";
    internal static int Seconds = 60;
    internal static bool AllReports;
    internal static BridgePreview Preview;
    internal static void Say(string s)
    {
        string line = DateTime.UtcNow.ToString("o") + " " + s;
        Console.WriteLine(line);
        if (Log != null) Log.WriteLine(line);
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            bool list = false, test = false, diagnose = false, hid = false;
            bool ipcTest = false;
            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i])
                {
                    case "--list": list = true; break;
                    case "--diagnose": diagnose = true; break;
                    case "--hid": hid = true; break;
                    case "--settings": if (Preview == null) Preview = new BridgePreview(); Preview.Configure(args[++i]); break;
                    case "--preview": if (Preview == null) Preview = new BridgePreview(); break;
                    case "--bridge": if (Preview == null) Preview = new BridgePreview(); Preview.EnableHelper(false); break;
                    case "--helper-preview": if (Preview == null) Preview = new BridgePreview(); Preview.EnableHelper(true); break;
                    case "--preview-target": if (Preview == null) Preview = new BridgePreview(); Preview.AddTarget(args[++i]); break;
                    case "--self-test": test = true; break;
                    case "--ipc-self-test": ipcTest = true; break;
                    case "--all-reports": AllReports = true; break;
                    case "--match": Match = args[++i]; break;
                    case "--seconds": Seconds = int.Parse(args[++i]); if (Seconds < 1) throw new ArgumentException("seconds must be positive"); break;
                    case "--log": Log = new StreamWriter(args[++i], false, new UTF8Encoding(false)); Log.AutoFlush = true; break;
                    case "--help": Console.WriteLine("SC2Probe [--list | --diagnose | --hid | --preview | --helper-preview | --bridge] [--preview-target absolute-exe-path] [--match path-substring] [--seconds 60] [--log file] [--all-reports] [--self-test | --ipc-self-test]"); return 0;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (test) { Decoder.Test(); MappingTests.Run(); return 0; }
            if (ipcTest) { BridgeClient.SelfTest(); return 0; }
            if (hid && Preview != null) throw new ArgumentException("Preview currently uses Raw Input only.");
            Say("INPUT CAPTURE mode=" + (hid ? "shared-HID" : "RawInput") + "; no Steam/controller settings changed. PID=" + Process.GetCurrentProcess().Id);
            Say("Steam process present=" + (Process.GetProcessesByName("steam").Length > 0) + " (does NOT prove Steam Input active)");
            Diagnostics.Run();
            if (diagnose) return 0;
            if (list) { Devices.Enumerate(true); return 0; }
            if (hid) return SharedHid.Run();
            if (Preview != null) Preview.Start();
            using (var window = new Observer()) Application.Run();
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        finally { if (Preview != null) Preview.Close(); if (Log != null) Log.Dispose(); }
    }
}

internal sealed class Device
{
    internal IntPtr Handle;
    internal uint Type, Vid, Pid;
    internal ushort Page, Usage;
    internal string Path;
    internal bool Selected { get { return Type == 2 && (Probe.Match.Length > 0 ? Path.IndexOf(Probe.Match, StringComparison.OrdinalIgnoreCase) >= 0 : Vid == 0x28de && Pid != 0x11ff); } }
    internal string Label { get { return Vid == 0x28de ? (Pid == 0x1302 ? "SC2 USB" : Pid == 0x1303 ? "SC2 Bluetooth LE" : Pid == 0x1304 || Pid == 0x1305 ? "SC2-family receiver (not proof of connected controller)" : Pid == 0x11ff ? "Steam virtual gamepad (excluded by default)" : "Valve candidate; confirm SC2 via path and state reports") : "other"; } }
    public override string ToString() { return string.Format("h=0x{0:X} type={1} VID={2:X4} PID={3:X4} TLC={4:X4}:{5:X4} selected={6} {7}\n  {8}", Handle.ToInt64(), Type, Vid, Pid, Page, Usage, Selected, Label, Path); }
}

internal static class Devices
{
    internal static Dictionary<IntPtr, Device> Enumerate(bool print)
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf(typeof(Native.DeviceList));
        if (Native.GetRawInputDeviceList(IntPtr.Zero, ref count, size) == uint.MaxValue) throw new Win32Exception();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            IntPtr p = Marshal.AllocHGlobal(checked((int)((count + 16) * size)));
            try
            {
                count += 16;
                uint actual = Native.GetRawInputDeviceList(p, ref count, size);
                if (actual == uint.MaxValue) { if (Marshal.GetLastWin32Error() == 122) continue; throw new Win32Exception(); }
                var result = new Dictionary<IntPtr, Device>();
                for (int i = 0; i < actual; i++)
                {
                    var entry = (Native.DeviceList)Marshal.PtrToStructure(IntPtr.Add(p, checked(i * (int)size)), typeof(Native.DeviceList));
                    Device d = Read(entry.Handle, entry.Type);
                    if (d != null) { result[d.Handle] = d; if (print) Probe.Say(d.ToString()); }
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(p); }
        }
        throw new Exception("Device list changed repeatedly; retry enumeration.");
    }
    internal static Device Read(IntPtr h, uint type)
    {
        uint chars = 0;
        if (Native.GetRawInputDeviceInfo(h, 0x20000007, IntPtr.Zero, ref chars) == uint.MaxValue) return null;
        IntPtr name = Marshal.AllocHGlobal(checked((int)(chars + 1) * 2));
        IntPtr info = Marshal.AllocHGlobal(32);
        try
        {
            if (Native.GetRawInputDeviceInfo(h, 0x20000007, name, ref chars) == uint.MaxValue) return null;
            var d = new Device { Handle = h, Type = type, Path = Marshal.PtrToStringUni(name) ?? "" };
            Marshal.WriteInt32(info, 32); uint bytes = 32;
            if (Native.GetRawInputDeviceInfo(h, 0x2000000b, info, ref bytes) == uint.MaxValue) return null;
            if (type == 2)
            {
                d.Vid = (uint)Marshal.ReadInt32(info, 8); d.Pid = (uint)Marshal.ReadInt32(info, 12);
                d.Page = (ushort)Marshal.ReadInt16(info, 20); d.Usage = (ushort)Marshal.ReadInt16(info, 22);
            }
            return d;
        }
        finally { Marshal.FreeHGlobal(name); Marshal.FreeHGlobal(info); }
    }
}

internal sealed class Observer : NativeWindow, IDisposable
{
    Dictionary<IntPtr, Device> devices;
    readonly HashSet<uint> registered = new HashSet<uint>();
    readonly Dictionary<string, byte[]> previous = new Dictionary<string, byte[]>();
    readonly Dictionary<IntPtr, long> counts = new Dictionary<IntPtr, long>();
    readonly Dictionary<string, uint> buttons = new Dictionary<string, uint>();
    readonly Timer timer = new Timer();
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    long reports, decoded;
    double nextStatus = 5;
    bool disposed;
    internal Observer()
    {
        // Message-only window never takes focus from the game.
        CreateHandle(new CreateParams { Caption = "SC2Probe", Parent = new IntPtr(-3) });
        Refresh(true);
        Probe.Say("Capture started for " + Probe.Seconds + " seconds; keep game/Steam foreground. No reports is INCONCLUSIVE.");
        timer.Interval = Probe.Preview == null ? 250 : 100;
        timer.Tick += delegate
        {
            if (Probe.Preview != null) Probe.Preview.Poll();
            if (elapsed.Elapsed.TotalSeconds >= nextStatus)
            {
                Probe.Say("STATUS seconds=" + (int)elapsed.Elapsed.TotalSeconds + " reports=" + reports + " decoded=" + decoded);
                nextStatus += 5;
                Refresh(false);
            }
            if (elapsed.Elapsed.TotalSeconds >= Probe.Seconds) Application.ExitThread();
        };
        timer.Start();
    }
    void Refresh(bool print)
    {
        devices = Devices.Enumerate(print);
        foreach (Device d in devices.Values)
        {
            if (!d.Selected) continue;
            uint key = ((uint)d.Page << 16) | d.Usage;
            if (registered.Contains(key)) continue;
            // Do NOT use EXINPUTSINK: it can suppress delivery while foreground Steam subscribes.
            var registration = new Native.Registration[] { new Native.Registration { Page = d.Page, Usage = d.Usage, Flags = 0x100 | 0x2000, Target = Handle } };
            if (!Native.RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf(typeof(Native.Registration))))
                Probe.Say("REGISTER FAILED TLC=" + key.ToString("X8") + " error=" + Marshal.GetLastWin32Error());
            else { registered.Add(key); Probe.Say("REGISTERED INPUTSINK TLC=" + key.ToString("X8")); }
        }
    }
    protected override void WndProc(ref Message m)
    {
        try
        {
            if (m.Msg == 0xff) Receive(m.LParam);
            else if (m.Msg == 0xfe) { previous.Clear(); buttons.Clear(); if (Probe.Preview != null) Probe.Preview.Reset("device-change"); Refresh(true); }
        }
        catch (Exception e) { Probe.Say("INPUT ERROR " + e.Message); }
        // DefWindowProc is required for foreground WM_INPUT cleanup.
        base.WndProc(ref m);
    }
    void Receive(IntPtr input)
    {
        uint length = 0; uint header = (uint)(8 + 2 * IntPtr.Size);
        if (Native.GetRawInputData(input, 0x10000003, IntPtr.Zero, ref length, header) == uint.MaxValue) throw new Win32Exception();
        if (length < header + 8 || length > 1024 * 1024) return;
        IntPtr p = Marshal.AllocHGlobal((int)length);
        try
        {
            uint got = Native.GetRawInputData(input, 0x10000003, p, ref length, header);
            if (got == uint.MaxValue) throw new Win32Exception();
            if (got < header + 8 || Marshal.ReadInt32(p) != 2) return;
            IntPtr h = Marshal.ReadIntPtr(p, 8);
            Device d;
            if (!devices.TryGetValue(h, out d) || !d.Selected) return;
            uint reportSize = (uint)Marshal.ReadInt32(p, (int)header), count = (uint)Marshal.ReadInt32(p, (int)header + 4);
            if (reportSize == 0 || (ulong)reportSize * count > got - header - 8) return;
            for (uint n = 0; n < count; n++)
            {
                byte[] data = new byte[reportSize];
                Marshal.Copy(IntPtr.Add(p, checked((int)(header + 8 + n * reportSize))), data, 0, data.Length);
                reports++; if (!counts.ContainsKey(h)) counts[h] = 0; counts[h]++;
                string key = h.ToInt64().ToString("X") + ":" + data[0].ToString("X2") + ":" + data.Length;
                byte[] old; previous.TryGetValue(key, out old);
                string delta = Decoder.Delta(old, data);
                previous[key] = data;
                uint mask = 0; string state = d.Vid == 0x28de && d.Pid >= 0x1302 && d.Pid <= 0x1305 ? Decoder.Decode(data, out mask) : null;
                if (state != null) decoded++; if (Probe.LabReport != null) { if (state != null) Probe.LabReport(h, data); continue; }
                if (Probe.Preview != null)
                {
                    if (state != null) Probe.Preview.Report(h, data);
                    continue;
                }
                if (!Probe.AllReports && delta == "same") continue;
                Probe.Say("REPORT h=" + h.ToInt64().ToString("X") + " n=" + counts[h] + " bytes=" + data.Length + " HEX=" + BitConverter.ToString(data) + " DELTA=" + delta);
                if (state != null)
                {
                    uint prior; bool hasPrior = buttons.TryGetValue(key, out prior); buttons[key] = mask;
                    Probe.Say("  " + state + " buttons=" + Decoder.Names(mask) + (hasPrior ? " pressed=" + Decoder.Names(mask & ~prior) + " released=" + Decoder.Names(prior & ~mask) : " baseline"));
                }
            }
        }
        finally { Marshal.FreeHGlobal(p); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Dispose();
        if (Probe.Preview != null) Probe.Preview.Reset("exit");
        foreach (uint key in registered)
        {
            Native.RegisterRawInputDevices(new Native.Registration[] { new Native.Registration { Page = (ushort)(key >> 16), Usage = (ushort)key, Flags = 1, Target = IntPtr.Zero } }, 1, (uint)Marshal.SizeOf(typeof(Native.Registration)));
        }
        DestroyHandle();
        Probe.Say("END reports=" + reports + " decoded=" + decoded + "; Steam Input continuity requires human verification.");
        foreach (var pair in counts) Probe.Say("DEVICE h=" + pair.Key.ToInt64().ToString("X") + " reports=" + pair.Value);
    }
}

internal static class Decoder
{
    // Independent C# implementation of the layouts documented by SDL (see SOURCES.md).
    static readonly string[] names = { "A", "B", "X", "Y", "QAM", "R3", "VIEW", "R4", "R5", "RB", "DOWN", "RIGHT", "LEFT", "UP", "MENU", "L3", "STEAM", "L4", "L5", "LB", "RStickTouch", "RPadTouch", "RPadClick", "RTClick", "LStickTouch", "LPadTouch", "LPadClick", "LTClick", "RGTouch", "LGTouch", "unknown30", "unknown31" };
    internal static string Names(uint mask)
    {
        var parts = new List<string>(); for (int b = 0; b < 32; b++) if ((mask & (1u << b)) != 0) parts.Add(names[b]);
        return parts.Count == 0 ? "-" : string.Join(",", parts.ToArray());
    }
    internal static string Decode(byte[] d, out uint buttons)
    {
        buttons = 0;
        // Report ID is byte 0, never search arbitrary payload bytes for an ID.
        if (d.Length < 46 || (d[0] != 0x42 && d[0] != 0x45 && d[0] != 0x47)) return null;
        buttons = BitConverter.ToUInt32(d, 2);
        int pad = d[0] == 0x47 ? 20 : 18;
        return string.Format("TRITON id={0:X2} seq={1} mask={2:X8} LT={3} RT={4} LStick=({5},{6}) RStick=({7},{8}) LPad=({9},{10}) pressure={11} RPad=({12},{13}) pressure={14}",
            d[0], d[1], buttons, BitConverter.ToInt16(d, 6), BitConverter.ToInt16(d, 8), BitConverter.ToInt16(d, 10), BitConverter.ToInt16(d, 12), BitConverter.ToInt16(d, 14), BitConverter.ToInt16(d, 16),
            BitConverter.ToInt16(d, pad), BitConverter.ToInt16(d, pad + 2), BitConverter.ToUInt16(d, pad + 4), BitConverter.ToInt16(d, pad + 6), BitConverter.ToInt16(d, pad + 8), BitConverter.ToUInt16(d, pad + 10));
    }
    internal static string Delta(byte[] old, byte[] data)
    {
        if (old == null || old.Length != data.Length) return "baseline";
        var s = new StringBuilder();
        for (int i = 0; i < data.Length; i++) if (old[i] != data[i]) s.AppendFormat("[{0}] {1:X2}>{2:X2}(xor {3:X2}) ", i, old[i], data[i], old[i] ^ data[i]);
        return s.Length == 0 ? "same" : s.ToString();
    }
    static void Check(bool b, string name) { if (!b) throw new Exception("SELF TEST FAILED: " + name); }
    internal static void Test()
    {
        foreach (byte id in new byte[] { 0x42, 0x45, 0x47 })
        {
            byte[] d = new byte[46]; d[0] = id; d[4] = 6; d[5] = 0x30;
            int pad = id == 0x47 ? 20 : 18;
            d[pad] = 0; d[pad + 1] = 0x80; d[pad + 6] = 0xff; d[pad + 7] = 0x7f;
            uint mask; string result = Decode(d, out mask);
            Check(result.Contains("LPad=(-32768,0)") && result.Contains("RPad=(32767,0)"), "signed pads " + id);
            Check(Names(mask) == "L4,L5,RGTouch,LGTouch", "back and grip masks");
            for (int n = 0; n < 46; n++) { byte[] shortReport = new byte[n]; if(n > 0) shortReport[0] = id; Check(Decode(shortReport, out mask) == null, "truncated " + n); }
        }
        uint ignored; Check(Decode(new byte[64], out ignored) == null, "unknown ID");
        Check(Delta(new byte[] { 0 }, new byte[] { 0x80 }).Contains("xor 80"), "bit delta");
        Check(Delta(new byte[] { 3 }, new byte[] { 3 }) == "same", "identical");
        Probe.Say("SELF TEST PASS: three layouts, signed pads, back/grip bits, truncation, unknown ID, deltas. Synthetic data only.");
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct DeviceList { internal IntPtr Handle; internal uint Type; }
    [StructLayout(LayoutKind.Sequential)] internal struct Registration { internal ushort Page, Usage; internal uint Flags; internal IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterRawInputDevices([In] Registration[] devices, uint count, uint size);
}
