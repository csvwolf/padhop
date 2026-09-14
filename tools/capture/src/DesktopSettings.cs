using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

// Shared by the ordinary reader and UIAccess helper. Loaded once for each session.
internal sealed class DesktopSettings
{
    internal double Speed = 0.02, SmoothMs = 12, Friction = 8, ScrollUnits = 2000;
    internal bool Inertia = true, Global, SteamDesktopMuted;
    internal bool PressureClick;
    internal bool RightHaptics=true, MotionHaptics=true, LeftScroll=false;
    internal double HapticGain=-12;
    internal double PressThreshold = 3500, ReleaseThreshold = 2200, ClickStableMs = 60, DragScale = 0.65, VerticalFriction = 1, Acceleration = 0;
    internal readonly List<string> Allow = new List<string>();
    internal readonly List<string> Exclude = new List<string>();
    internal static DesktopSettings Load(string file)
    {
        var s = new DesktopSettings();
        foreach (string raw in File.ReadAllLines(file))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
            int split = line.IndexOf('='); if (split < 1) throw new Exception("Invalid settings line");
            string k = line.Substring(0, split), v = line.Substring(split + 1).Trim();
            switch (k)
            {
                case "motionHaptics": s.MotionHaptics=bool.Parse(v); break;
                case "rightHaptics": s.RightHaptics=bool.Parse(v); break;
                case "leftScroll": s.LeftScroll=bool.Parse(v); break;
                case "hapticGain": s.HapticGain=Number(v,-24,-6); break;
                case "speed": s.Speed = Number(v, 0.001, 0.1); break;
                case "pressureClick": s.PressureClick = bool.Parse(v); break;
                case "pressThreshold": s.PressThreshold = Number(v, 500, 32767); break;
                case "releaseThreshold": s.ReleaseThreshold = Number(v, 0, 32766); break;
                case "clickStableMs": s.ClickStableMs = Number(v, 0, 150); break;
                case "dragScale": s.DragScale = Number(v, 0.1, 1); break;
                case "verticalFriction": s.VerticalFriction = Number(v, 0.25, 4); break;
                case "acceleration": s.Acceleration = Number(v, 0, 2); break;
                case "smoothMs": s.SmoothMs = Number(v, 0, 100); break;
                case "friction": s.Friction = Number(v, 1, 30); break;
                case "scrollUnits": s.ScrollUnits = Number(v, 200, 20000); break;
                case "inertia": s.Inertia = bool.Parse(v); break;
                case "global": s.Global = bool.Parse(v); break;
                case "steamDesktopMuted": s.SteamDesktopMuted = bool.Parse(v); break;
                case "allow": s.Allow.Add(Absolute(v)); break;
                case "exclude": s.Exclude.Add(Absolute(v)); break;
                default: throw new Exception("Unknown setting: " + k);
            }
        }
        if (s.ReleaseThreshold >= s.PressThreshold) throw new Exception("Release threshold must be below press threshold");
        return s;
    }
    static string Absolute(string v) { if (!Path.IsPathRooted(v) || v.Contains("\n")) throw new Exception("Expected absolute application path"); return Path.GetFullPath(v); }
    static double Number(string v, double lo, double hi) { double n = double.Parse(v, CultureInfo.InvariantCulture); if (double.IsNaN(n) || double.IsInfinity(n) || n < lo || n > hi) throw new Exception("Setting out of bounds"); return n; }
    internal bool Allows(string path, IntPtr window)
    {
        if (path == null || window == IntPtr.Zero) return false;
        // Steam normal UI is also excluded until Big Picture/overlay detection is verified.
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name == "steam.exe" || name == "steamwebhelper.exe" || name == "gameoverlayui.exe" || name == "consent.exe" || name == "lockapp.exe") return false;
        foreach (string p in Allow) if (string.Equals(path, p, StringComparison.OrdinalIgnoreCase)) return true;
        if (!Global) return false;
        foreach (string p in Exclude)
            if (string.Equals(path, p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        // Full-screen applications are an extra conservative fallback, not a game detector.
        RECT r; var m = new MONITORINFO(); m.size = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetWindowRect(window, out r) || !GetMonitorInfo(MonitorFromWindow(window, 2), ref m)) return false;
        if (r.left <= m.monitor.left && r.top <= m.monitor.top && r.right >= m.monitor.right && r.bottom >= m.monitor.bottom && name != "explorer.exe") return false;
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] struct RECT { internal int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { internal int size; internal RECT monitor, work; internal uint flags; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr w, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO info);
}
