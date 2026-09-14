using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Diagnostics
{
    internal static bool? Elevated(int pid)
    {
        IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
        if (process == IntPtr.Zero) return null;
        try { if (!OpenProcessToken(process, 8, out token)) return null; return ReadInt(token, 20) != 0; }
        catch { return null; }
        finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }
    internal static void Run()
    {
        using (Process self = Process.GetCurrentProcess()) Describe(self, "PROBE");
        foreach (Process p in Process.GetProcessesByName("steam")) using (p) Describe(p, "STEAM");
        Probe.Say("DESKTOP=" + ObjectName(GetThreadDesktop(GetCurrentThreadId())) + " WINDOWSTATION=" + ObjectName(GetProcessWindowStation()));
        Probe.Say("Diagnostics are observations only; unknown/access-denied is NOT non-elevated. No automatic elevation.");
    }
    static void Describe(Process p, string label)
    {
        IntPtr process = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            process = OpenProcess(0x1000, false, p.Id); // QUERY_LIMITED_INFORMATION only
            if (process == IntPtr.Zero) throw new Win32Exception();
            if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception();
            Probe.Say(label + " pid=" + p.Id + " session=" + p.SessionId + " elevated=" + ReadInt(token, 20) + " integrity=" + Integrity(token) + " restricted=" + IsTokenRestricted(token));
        }
        catch (Exception e) { Probe.Say(label + " pid=" + p.Id + " token=UNKNOWN " + e.Message); }
        finally { if (token != IntPtr.Zero) CloseHandle(token); if (process != IntPtr.Zero) CloseHandle(process); }
    }
    static int ReadInt(IntPtr token, int cls)
    {
        IntPtr p = Marshal.AllocHGlobal(4);
        try { int needed; if (!GetTokenInformation(token, cls, p, 4, out needed)) throw new Win32Exception(); return Marshal.ReadInt32(p); }
        finally { Marshal.FreeHGlobal(p); }
    }
    static string Integrity(IntPtr token)
    {
        int size; GetTokenInformation(token, 25, IntPtr.Zero, 0, out size);
        if (size <= 0) throw new Win32Exception();
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, 25, p, size, out size)) throw new Win32Exception();
            IntPtr sid = Marshal.ReadIntPtr(p);
            byte n = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            uint rid = (uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(n - 1)));
            return "0x" + rid.ToString("X") + (rid < 0x1000 ? "(Untrusted)" : rid < 0x2000 ? "(Low)" : rid < 0x3000 ? "(Medium)" : rid < 0x4000 ? "(High)" : "(System+)");
        }
        finally { Marshal.FreeHGlobal(p); }
    }
    static string ObjectName(IntPtr h)
    {
        var text = new StringBuilder(512); int needed;
        return GetUserObjectInformation(h, 2, text, text.Capacity * 2, out needed) ? text.ToString() : "UNKNOWN error=" + Marshal.GetLastWin32Error();
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr data, int size, out int needed);
    [DllImport("advapi32.dll")] static extern bool IsTokenRestricted(IntPtr token);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder data, int size, out int needed);
}
