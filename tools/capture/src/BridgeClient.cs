using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

internal sealed class BridgeClient : IDisposable
{
    readonly NamedPipeServerStream pipe;
    readonly Process helper;
    string helperSettings;
    internal BridgeClient(IEnumerable<string> targets, bool preview, string settingsPath = null)
    {
        var paths = new List<string>(targets);
        if (!preview)
            foreach (Process steam in Process.GetProcessesByName("steam"))
                using (steam) if (Diagnostics.Elevated(steam.Id) != false) throw new Exception("Actual bridge requires Steam non-elevated; unknown also refuses. Preview remains available.");
        if (settingsPath != null) {
            var lines=new List<string>();
            foreach(string line in File.ReadAllLines(settingsPath)) {
                string k=line.Split('=')[0].Trim();
                if(k=="global" || k=="steamDesktopMuted" || k=="allow" || k=="exclude")lines.Add(line);
            }
            helperSettings=Path.Combine(Path.GetDirectoryName(settingsPath),"helper-"+Guid.NewGuid().ToString("N")+".ini");
            File.WriteAllLines(helperSettings,lines.ToArray());
            paths.Add("@settings:"+helperSettings);
        }
        if (paths.Count == 0) throw new ArgumentException("Helper requires at least one --preview-target absolute EXE path.");
        string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, preview ? "SC2MouseHelper.Preview.exe" : "SC2MouseHelper.exe");
        if (!File.Exists(file)) throw new FileNotFoundException("Build/install helper first", file);
        string name = "SC2Mouse-" + Guid.NewGuid().ToString("N");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        using (WindowsIdentity user = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(user.User, PipeAccessRights.FullControl, AccessControlType.Allow));
#if NET8_0_OR_GREATER
        pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
#else
        pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
#endif
        try
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", paths.ToArray())));
            helper = Process.Start(new ProcessStartInfo(file, name + " " + Process.GetCurrentProcess().Id + " " + encoded) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            IAsyncResult connect = pipe.BeginWaitForConnection(null, null);
            using (var ready = connect.AsyncWaitHandle)
            {
                if (!ready.WaitOne(10000)) throw new TimeoutException("Helper did not connect; check signing/install and helper log.");
                pipe.EndWaitForConnection(connect);
            }
            uint client;
            if (helper == null || !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out client) || client != (uint)helper.Id)
                throw new InvalidOperationException("Unexpected pipe client process.");
            Probe.Say("HELPER CONNECTED preview=" + preview + " pid=" + client);
        }
        catch { pipe.Dispose(); if (helper != null) helper.Dispose(); throw; }
    }
    internal void Send(string action, IntPtr window, uint pid)
    {
        int kind, x = 0, y = 0;
        switch (action)
        {
            case "Reset": kind = 0; break;
            case "Heartbeat": kind = 7; break;
            case "LeftDown": kind = 3; break;
            case "LeftUp": kind = 4; break;
            case "RightDown": kind = 5; break;
            case "RightUp": kind = 6; break;
            default:
                string[] pieces = action.Split(' ');
                if (pieces.Length == 3 && pieces[0] == "Move") { kind = 1; x = int.Parse(pieces[1].Substring(3)); y = int.Parse(pieces[2].Substring(3)); }
                else if (pieces.Length == 2 && pieces[0] == "Wheel") { kind = 2; x = int.Parse(pieces[1].Substring(6)); }
                else throw new ArgumentException("Unknown mouse action");
                break;
        }
        byte[] frame = MouseWire.Pack(kind, x, y, window.ToInt64(), pid);
        if (!MouseWire.Valid(frame)) throw new ArgumentException("Mouse action exceeds protocol limits");
        // Bounded write: a stalled helper must not stall the raw-input thread indefinitely.
        IAsyncResult write = pipe.BeginWrite(frame, 0, frame.Length, null, null);
        using (var ready = write.AsyncWaitHandle)
        {
            if (!ready.WaitOne(100)) { pipe.Dispose(); throw new TimeoutException("Helper write timeout"); }
            pipe.EndWrite(write);
        }
    }
    public void Dispose() { pipe.Dispose(); if (helper != null) helper.Dispose(); }
    internal static void SelfTest()
    {
        uint pid; IntPtr window = GetForegroundWindow(); GetWindowThreadProcessId(window, out pid);
        string target = BridgePreview.ProcessPath(pid);
        if (target == null) throw new Exception("IPC self-test needs readable foreground process on the normal desktop");
        var client = new BridgeClient(new string[] { target }, true);
        int helperPid = client.helper.Id;
        using (Process child = client.helper)
        {
            try
            {
                client.Send("Move dx=3 dy=2", window, pid);
                client.Send("LeftDown", window, pid);
                client.Send("LeftUp", window, pid);
                client.Send("RightDown", window, pid);
                client.Send("RightUp", window, pid);
                client.Send("Wheel delta=120", window, pid);
                client.Send("LeftDown", window, pid);
                System.Threading.Thread.Sleep(800); // Exercise helper watchdog; preview executable only.
            }
            finally { client.pipe.Dispose(); }
            if (!child.WaitForExit(3000)) throw new Exception("Preview helper failed to exit after pipe close");
            if (child.ExitCode != 0) throw new Exception("Preview helper exit=" + child.ExitCode);
        }
        string evidence = File.ReadAllText(Path.Combine(Path.GetTempPath(), "SC2MouseHelper-" + helperPid + ".log"));
        foreach (string expected in new string[] { "mouse=1 ", "mouse=2 ", "mouse=4 ", "mouse=8 ", "mouse=16 ", "mouse=2048 " })
            if (!evidence.Contains(expected)) throw new Exception("Missing preview dispatch " + expected + "; foreground may have changed");
        if (evidence.Split(new string[] { "mouse=4 " }, StringSplitOptions.None).Length - 1 != 2) throw new Exception("Expected explicit and watchdog left releases");
        Probe.Say("IPC SELF TEST PASS: preview-only move, clicks, wheel, timeout release and disconnect exit. No input sent. Helper log=" + helperPid);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError=true)] static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
}
