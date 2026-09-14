using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class SharedHid
{
    internal static int Run()
    {
        var candidates = new List<Device>();
        foreach (Device d in Devices.Enumerate(true).Values)
            if (d.Selected && d.Vid == 0x28de && d.Pid >= 0x1302 && d.Pid <= 0x1305 && d.Page == 0xff00 && d.Usage == 1) candidates.Add(d);
        if (candidates.Count != 1)
        {
            Probe.Say("HID requires exactly ONE SC2 FF00:0001 collection; found=" + candidates.Count + ". Use --match with a unique path substring. No device opened.");
            return 2;
        }
        Device target = candidates[0];
        // Share write permits Steam to write; this handle itself requests ONLY read access.
        using (SafeFileHandle file = CreateFile(target.Path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
        {
            if (file.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                Probe.Say("HID OPEN FAILED error=" + error + " " + new Win32Exception(error).Message + "; no exclusive/write-access fallback.");
                return 3;
            }
            Probe.Say("HID OPEN OK access=GENERIC_READ share=READ|WRITE overlapped=true; opening alone does NOT prove reports or Steam continuity.");
            IntPtr preparsed;
            if (!HidD_GetPreparsedData(file, out preparsed)) throw new Win32Exception();
            int size;
            IntPtr caps = Marshal.AllocHGlobal(64);
            try
            {
                int status = HidP_GetCaps(preparsed, caps);
                if (status != 0x110000) throw new Exception("HidP_GetCaps status=0x" + status.ToString("X"));
                size = (ushort)Marshal.ReadInt16(caps, 4);
            }
            finally { Marshal.FreeHGlobal(caps); HidD_FreePreparsedData(preparsed); }
            if (size < 1 || size > 65535) throw new Exception("Invalid input report size " + size);
            Probe.Say("HID InputReportByteLength=" + size);
            Capture(file, size);
            return 0;
        }
    }
    static void Capture(SafeFileHandle file, int size)
    {
        IntPtr buffer = Marshal.AllocHGlobal(size);
        int ovSize = IntPtr.Size == 8 ? 32 : 20;
        IntPtr ov = Marshal.AllocHGlobal(ovSize);
        IntPtr signal = CreateEvent(IntPtr.Zero, true, false, null);
        bool pending = false;
        long reports = 0, decoded = 0;
        var previous = new Dictionary<byte, byte[]>();
        var masks = new Dictionary<byte, uint>();
        var watch = Stopwatch.StartNew(); double next = 5;
        try
        {
            if (signal == IntPtr.Zero) throw new Win32Exception();
            for (int i = 0; i < ovSize; i++) Marshal.WriteByte(ov, i, 0);
            Marshal.WriteIntPtr(ov, IntPtr.Size == 8 ? 24 : 16, signal);
            while (watch.Elapsed.TotalSeconds < Probe.Seconds)
            {
                if (!pending)
                {
                    if (!ResetEvent(signal)) throw new Win32Exception();
                    bool immediate = ReadFile(file, buffer, (uint)size, IntPtr.Zero, ov);
                    int error = immediate ? 0 : Marshal.GetLastWin32Error();
                    if (!immediate && error != 997) throw new Win32Exception(error);
                    pending = true;
                }
                uint wait = WaitForSingleObject(signal, 100);
                if (wait == 0)
                {
                    uint got;
                    bool ok = GetOverlappedResult(file, ov, out got, false);
                    int error = ok ? 0 : Marshal.GetLastWin32Error();
                    // Event signaled: this operation has completed, including error completion.
                    pending = false;
                    if (!ok) throw new Win32Exception(error);
                    if (got == 0 || got > size) throw new Exception("Invalid read length " + got);
                    byte[] data = new byte[got]; Marshal.Copy(buffer, data, 0, (int)got);
                    reports++;
                    byte[] old; previous.TryGetValue(data[0], out old);
                    string delta = Decoder.Delta(old, data); previous[data[0]] = data;
                    uint mask; string state = Decoder.Decode(data, out mask); if (state != null) decoded++;
                    if (Probe.AllReports || delta != "same")
                    {
                        Probe.Say("HID REPORT n=" + reports + " bytes=" + got + " HEX=" + BitConverter.ToString(data) + " DELTA=" + delta);
                        if (state != null)
                        {
                            uint prior; bool known = masks.TryGetValue(data[0], out prior); masks[data[0]] = mask;
                            Probe.Say(state + " buttons=" + Decoder.Names(mask) + (known ? " pressed=" + Decoder.Names(mask & ~prior) + " released=" + Decoder.Names(prior & ~mask) : " baseline"));
                        }
                    }
                }
                else if (wait != 258) throw new Win32Exception();
                if (watch.Elapsed.TotalSeconds >= next)
                {
                    Probe.Say("HID STATUS seconds=" + (int)watch.Elapsed.TotalSeconds + " reports=" + reports + " decoded=" + decoded); next += 5;
                }
            }
        }
        finally
        {
            // Cancellation is not completion: drain before releasing OVERLAPPED/buffer/event.
            if (pending)
            {
                CancelIoEx(file, ov); uint ignored;
                GetOverlappedResult(file, ov, out ignored, true);
            }
            if (signal != IntPtr.Zero) CloseHandle(signal);
            Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer);
            Probe.Say("HID END reports=" + reports + " decoded=" + decoded + "; Steam Input continuity requires human verification.");
        }
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle file, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, IntPtr caps);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint count, IntPtr read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint count, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateEvent(IntPtr security, bool manual, bool initial, string name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ResetEvent(IntPtr signal);
    [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr signal, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
