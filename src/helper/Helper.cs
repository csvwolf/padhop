using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

internal static class MouseHelper
{
#if PREVIEW
    const bool Preview = true;
#else
    const bool Preview = false;
#endif
    static bool left, right;static readonly Dictionary<int,INPUT> keyPackets=new Dictionary<int,INPUT>();static readonly HashSet<int> heldKeys=new HashSet<int>();static readonly HashSet<int> heldMouse=new HashSet<int>();
    static IntPtr heldWindow;
    static uint heldPid;
    static StreamWriter log;
    static void Note(string s) { if(log.BaseStream.Length<262144)log.WriteLine(DateTime.UtcNow.ToString("o") + " " + s.Split(' ')[0]); }
    static int Main(string[] args)
    {
        string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PadHop", "logs"); Directory.CreateDirectory(logDirectory);
        string logPath = Path.Combine(logDirectory, "input-" + Process.GetCurrentProcess().Id + ".log");
        using (log = new StreamWriter(logPath))
        {
            log.AutoFlush = true;
            try
            {
                if (args.Length != 3) throw new ArgumentException("Expected pipe, parent PID, target-list");
#if !PREVIEW && !STANDARD
                if (!HasUIAccess()) throw new Exception("TokenUIAccess is false. Refusing actual input.");
#endif
                uint parent = uint.Parse(args[1]);
                string[] paths = Encoding.UTF8.GetString(Convert.FromBase64String(args[2])).Split('\n');
                DesktopSettings settings = null;
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    if (path.StartsWith("@settings:")) { settings = DesktopSettings.Load(path.Substring(10)); continue; }
                    if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new ArgumentException("Invalid target");
                    allowed.Add(Path.GetFullPath(path));
                }
                if ((allowed.Count == 0 && settings == null) || allowed.Count > 16) throw new ArgumentException("Target count out of bounds");
                using (var pipe = new NamedPipeClientStream(".", args[0], PipeDirection.In, PipeOptions.Asynchronous))
                {
                    pipe.Connect(10000);
                    uint server;
                    if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out server) || server != parent) throw new Exception("Unexpected pipe server");
                    string expected = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PadHop.Engine.exe");
                    if (!string.Equals(ImagePath(server), expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Server must be sibling PadHop.Engine.exe");
                    using (Process p = Process.GetProcessById((int)server))
                    using (Process self = Process.GetCurrentProcess())
                        if (p.SessionId != self.SessionId) throw new Exception("Session mismatch");
                    Note("CONNECTED preview=" + Preview + "; F12 stops helper; targets=" + allowed.Count);
                    byte[] frame = new byte[MouseWire.Size]; int offset = 0;
                    Task<int> pending = pipe.ReadAsync(frame, 0, frame.Length);
                    var elapsed = Stopwatch.StartNew(); long received = 0, steamCheck = -1000;
                    while (true)
                    {
                        if (settings != null && !settings.SteamDesktopMuted && elapsed.ElapsedMilliseconds - steamCheck >= 250) {
                            steamCheck = elapsed.ElapsedMilliseconds;
                            bool running = false; foreach (Process steam in Process.GetProcessesByName("steam")) { using (steam) running = true; }
                            if (running) { Note("Steam desktop not acknowledged muted; stop to avoid duplicate output"); break; }
                        }
                        if (!heldKeys.Contains(0x7b) && (GetAsyncKeyState(0x7b) & 0x8000) != 0) { Note("F12 stop"); break; }
                        uint foregroundPid; IntPtr foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out foregroundPid);
                        if ((left || right || heldKeys.Count>0 || heldMouse.Count>0) && (foreground != heldWindow || foregroundPid != heldPid || elapsed.ElapsedMilliseconds - received > 500)) Release();
                        if (!pending.Wait(20)) continue;
                        int got = pending.Result;
                        if (got == 0) break;
                        offset += got;
                        if (offset == frame.Length)
                        {
                            if (!MouseWire.Valid(frame)) throw new Exception("Invalid mouse frame");
                            if (!MouseWire.Fresh(frame, Environment.TickCount)) { Release(); Note("Stale frame rejected"); }
                            else
                            {
                                int kind = BitConverter.ToInt32(frame, 0);
                                if (kind == 0) Release();
                                else
                                {
                                    IntPtr wanted = new IntPtr(BitConverter.ToInt64(frame, 16)); uint wantedPid = BitConverter.ToUInt32(frame, 24);
                                    // Re-read foreground immediately before dispatch, independently of parent.
                                    foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out foregroundPid);
                                    if (wanted == foreground && wantedPid == foregroundPid && (settings == null ? allowed.Contains(ImagePath(foregroundPid) ?? "") : settings.Allows(ImagePath(foregroundPid), foreground)) && GetForegroundWindow() == foreground)
                                    {
                                        received = elapsed.ElapsedMilliseconds;
#if STANDARD
                                        if (IsElevatedTarget(foregroundPid)) { Release(); offset = 0; pending = pipe.ReadAsync(frame, 0, frame.Length); continue; }
#endif
#if !PREVIEW && !STANDARD
                                        if (settings == null && !IsElevatedTarget(foregroundPid)) { Release(); offset = 0; pending = pipe.ReadAsync(frame, 0, frame.Length); continue; }
#endif
                                        Dispatch(kind, BitConverter.ToInt32(frame, 4), BitConverter.ToInt32(frame, 8));
                                        heldWindow = foreground; heldPid = foregroundPid;
                                    }
                                    else { Release(); Note("Foreground mismatch rejected"); }
                                }
                            }
                            offset = 0;
                        }
                        pending = pipe.ReadAsync(frame, offset, frame.Length - offset);
                    }
                }
                return 0;
            }
            catch (Exception e) { Note("ERROR " + e.Message); return 1; }
            finally { Release(); Note("END"); }
        }
    }
    static void Dispatch(int k, int x, int y)
    {
        switch(k)
        {
            case 8: ToggleKeyboard(); break;
            case 9: if(!heldKeys.Contains(x)){SendKey(x,false);heldKeys.Add(x);}break;
            case 10: if(heldKeys.Contains(x)){SendKey(x,true);heldKeys.Remove(x);}break;
            case 11: if(!heldMouse.Contains(x)){SendExtraMouse(x,false);heldMouse.Add(x);}break;
            case 12: if(heldMouse.Contains(x)){SendExtraMouse(x,true);heldMouse.Remove(x);}break;
            case 1: Send(1, x, y, 0); break;
            case 2: Send(0x800, 0, 0, x); break;
            case 3: if (!left) { Send(2, 0, 0, 0); left = true; } break;
            case 4: if (left) { Send(4, 0, 0, 0); left = false; } break;
            case 5: if (!right) { Send(8, 0, 0, 0); right = true; } break;
            case 6: if (right) { Send(16, 0, 0, 0); right = false; } break;
        }
    }
    static long keyboardAt=-2000;static readonly Stopwatch keyboardClock=Stopwatch.StartNew();
    static void ToggleKeyboard(){
#if PREVIEW
        Note("PREVIEW ONLY ToggleKeyboard");
#else
        if(keyboardClock.ElapsedMilliseconds-keyboardAt<800)return;
        keyboardAt=keyboardClock.ElapsedMilliseconds;
        string osk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"osk.exe");
        IntPtr w=FindWindow("OSKMainClass",null);uint id;GetWindowThreadProcessId(w,out id);
        if(w!=IntPtr.Zero && string.Equals(ImagePath(id),osk,StringComparison.OrdinalIgnoreCase)){
            if(!PostMessage(w,0x0010,IntPtr.Zero,IntPtr.Zero))throw new Exception("Cannot close system keyboard");
            Note("KEYBOARD close requested");
        }else{using(var p=Process.Start(new ProcessStartInfo(osk){UseShellExecute=true})){}Note("KEYBOARD open requested");}
#endif
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(IntPtr w,uint msg,IntPtr a,IntPtr b);
    static void Release()
    {
        var releases=new List<int>(heldKeys);releases.Sort(delegate(int a,int b){return IsModifier(a).CompareTo(IsModifier(b));});foreach(int key in releases){try{SendKey(key,true);heldKeys.Remove(key);}catch(Exception e){Note("KEY RELEASE FAILED "+e.Message);}}
        foreach(int button in new List<int>(heldMouse)){try{SendExtraMouse(button,true);heldMouse.Remove(button);}catch(Exception e){Note("MOUSE RELEASE FAILED "+e.Message);}}
        // Retry release at final cleanup if an earlier attempt failed.
        if (left) { try { Send(4, 0, 0, 0); left = false; } catch (Exception e) { Note("RELEASE FAILED " + e.Message); } }
        if (right) { try { Send(16, 0, 0, 0); right = false; } catch (Exception e) { Note("RELEASE FAILED " + e.Message); } }
    }
    static void Send(uint flags, int x, int y, int data)
    {
#if PREVIEW
        Note("PREVIEW ONLY mouse=" + flags + " x=" + x + " y=" + y + " data=" + data);
#else
        INPUT input = new INPUT { type = 0, dx = x, dy = y, mouseData = unchecked((uint)data), flags = flags };
        if (SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1) throw new Exception("SendInput failed error=" + Marshal.GetLastWin32Error());
#endif
    }
    static void SendExtraMouse(int button,bool up){Send(button==3?(up?0x40u:0x20u):(up?0x100u:0x80u),0,0,button==3?0:button-3);}
    static bool IsModifier(int k){return k==91 || k==92 || (k>=160 && k<=165);}
    static void SendKey(int vk,bool up){
#if PREVIEW
        Note("PREVIEW ONLY key="+vk+" up="+up);
#else
        uint pid;uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out pid);
        uint scan=MapVirtualKeyEx((uint)vk,4,GetKeyboardLayout(thread));
        // Scan codes support games; E0 distinguishes Win/right modifiers/navigation keys.
        INPUT input=new INPUT{type=1,keyVk=(ushort)(scan==0?vk:0),keyScan=(ushort)(scan&255),keyFlags=(up?2u:0u)|(scan==0?0u:8u)|((scan&0xFF00)==0xE000?1u:0u)};
        INPUT held;if(up && keyPackets.TryGetValue(vk,out held)){input=held;input.keyFlags|=2;}
        if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("Keyboard SendInput failed error="+Marshal.GetLastWin32Error());
        if(up)keyPackets.Remove(vk);else keyPackets[vk]=input;
#endif
    }
    [DllImport("user32.dll")]static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint MapVirtualKeyEx(uint code,uint type,IntPtr layout);
    static bool HasUIAccess()
    {
        IntPtr token;
        using (Process p = Process.GetCurrentProcess())
        {
            if (!OpenProcessToken(p.Handle, 8, out token)) return false;
            try { int value, needed; return GetTokenInformation(token, 26, out value, 4, out needed) && value != 0; }
            finally { CloseHandle(token); }
        }
    }
    static string ImagePath(uint pid)
    {
        return ReadImagePath(pid);
    }
    static bool IsElevatedTarget(uint pid)
    {
        IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, 8, out token)) return false;
            int value, needed;
            return GetTokenInformation(token, 20, out value, 4, out needed) && value != 0;
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }
    static string ReadImagePath(uint pid)
    {
        IntPtr p = OpenProcess(0x1000, false, pid); if (p == IntPtr.Zero) return null;
        try { var b = new StringBuilder(32768); uint size = (uint)b.Capacity; return QueryFullProcessImageName(p, 0, b, ref size) ? b.ToString() : null; }
        finally { CloseHandle(p); }
    }
    [StructLayout(LayoutKind.Explicit, Size=40)] struct INPUT
    {
        [FieldOffset(0)] internal uint type;
        [FieldOffset(8)] internal ushort keyVk;
        [FieldOffset(10)] internal ushort keyScan;
        [FieldOffset(12)] internal uint keyFlags;
        [FieldOffset(8)] internal int dx;
        [FieldOffset(12)] internal int dy;
        [FieldOffset(16)] internal uint mouseData;
        [FieldOffset(20)] internal uint flags;
    }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int cls, out int data, int size, out int needed);
}
