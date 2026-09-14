using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Preview by default. Optional helper owns OS input; parent remains ordinary privilege.
internal sealed class BridgePreview
{
    readonly HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly StickSources stickSources=new StickSources();readonly PadSources padSources=new PadSources();BindingEngine bindings;PadBook globalBook,currentBook;RuntimeProfiles appProfiles;readonly MouseOwnership ownership=new MouseOwnership();
    MappingEngine engine = new MappingEngine(); DualPads dual;
    DesktopSettings settings; string settingsPath;
    internal void Configure(string path) { settingsPath = Path.GetFullPath(path); settings = DesktopSettings.Load(settingsPath); Probe.DevicePath=settings.DevicePath; engine = new MappingEngine(settings); if(settings.PadProfiles!=null){var book=PadBook.Load(settings.PadProfiles);globalBook=book;SetBook(book);} if(settings.AppProfiles!=null)appProfiles=RuntimeProfiles.Load(settings.AppProfiles); }
    void SetBook(PadBook book){if(dual!=null)dual.Dispose();currentBook=book;dual=new DualPads(book);bindings=new BindingEngine(book);}
    readonly Stopwatch clock = Stopwatch.StartNew();
    IntPtr window, device;
    uint pid;
    bool active;
    long lastReport;
    string target = "unknown";
    BridgeClient client;VirtualGamepad gamepad;
    bool useHelper, helperPreview, failed;
    long heartbeat;
    TouchFeedback feedback; IntPtr feedbackDevice; bool feedbackFailed, touchingRight;
    readonly FeedbackCadence cadence=new FeedbackCadence();
    internal void EnableHelper(bool dry) { useHelper = true; helperPreview = dry; }
    internal void Start() { if (useHelper) client = new BridgeClient(targets, helperPreview, settingsPath); }
    internal void Close() { if(gamepad!=null){gamepad.Dispose();gamepad=null;} if(dual!=null){dual.Dispose();dual=null;} if(feedback!=null) { Probe.Say("HAPTICS ticks="+feedback.Sent+" error="+(feedback.Error??"none")); feedback.Dispose(); feedback=null; } if (client != null) { client.Dispose(); client = null; } }
    internal void AddTarget(string path)
    {
        if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new ArgumentException("preview-target must be an existing absolute EXE path");
        targets.Add(Path.GetFullPath(path));
    }
    internal void Reset(string reason,bool detach=true)
    {
        stickSources.Reset();padSources.Reset();if(bindings!=null)Emit(ownership.Mix(bindings.Reset(),1),reason);touchingRight=false; cadence.Reset(); if(feedback!=null)feedback.Clear();
        Emit(ownership.Mix(dual==null?engine.Reset():dual.Reset(),0), reason);ownership.Clear();
        Transmit("Reset");
        if(gamepad!=null){if(detach){gamepad.Dispose();gamepad=null;}else gamepad.Neutral();}
        device = IntPtr.Zero;
    }
    internal void Poll()
    {
        IntPtr current = GetForegroundWindow(); uint currentPid;
        GetWindowThreadProcessId(current, out currentPid);
        bool permitted = !failed && current != IntPtr.Zero && (settings == null ? targets.Contains(ProcessPath(currentPid) ?? "") : settings.Allows(ProcessPath(currentPid), current));
        if (current != window || currentPid != pid || permitted != active)
        {
            Reset("foreground-change",currentPid!=pid || !permitted);
            window = current; pid = currentPid; target = ProcessPath(currentPid);
            if(globalBook!=null){var chosen=appProfiles==null?globalBook:appProfiles.Select(target,globalBook);if(!object.ReferenceEquals(chosen,currentBook))SetBook(chosen);}
            active = permitted;
            Probe.Say("MAPPING foreground=" + (target ?? "unknown") + " active=" + active + " output=" + (useHelper && !helperPreview ? "UIAccess mouse helper" : "PREVIEW ONLY"));
        }
        if (device != IntPtr.Zero && clock.ElapsedMilliseconds - lastReport > 500) Reset("input-timeout",false);
        if(active && device!=IntPtr.Zero && bindings!=null)Emit(ownership.Mix(bindings.Tick(clock.ElapsedMilliseconds),1),"macro");
        if (active && device != IntPtr.Zero && clock.ElapsedMilliseconds - heartbeat >= 100)
        {
            heartbeat = clock.ElapsedMilliseconds; Transmit("Heartbeat");
        }
    }
    internal void Report(IntPtr handle, byte[] bytes)
    {
        Poll();
        if (!active) return;
        // Lock to first reporting controller until focus changes or a 500 ms gap.
        if (device != IntPtr.Zero && device != handle) return;
        device = handle; lastReport = clock.ElapsedMilliseconds;
        uint bits; bool decoded=Decoder.Decode(bytes,out bits)!=null;touchingRight=decoded && (bits&0x200000)!=0;
        if(decoded && currentBook!=null && currentBook.VirtualOutputEnabled && currentBook.NeedsVirtualGamepad && useHelper && !helperPreview){if(gamepad==null)gamepad=new VirtualGamepad(currentBook);gamepad.Update(bytes,bits);}
        Emit(ownership.Mix(dual==null?engine.Step(bytes):dual.Step(bytes,handle,window,useHelper && !helperPreview),0), "mapped");
        if(decoded && bindings!=null){Emit(stickSources.Mouse(currentBook,bytes,clock.ElapsedMilliseconds),"stick");ulong sources=dual==null?bits:padSources.Read(currentBook,bytes,bits,dual.Pressed(0),dual.Pressed(1));Emit(ownership.Mix(bindings.Step(sources,clock.ElapsedMilliseconds),1),"button");}
    }
    void Emit(List<string> actions, string reason)
    {
        foreach (string action in actions)
        {
            Probe.Say("MAPPING target=" + target + " " + action + " reason=" + reason + " helper=" + (client != null));
            Transmit(action);
        }
    }
    void Transmit(string action)
    {
        if(action.StartsWith("GameDown button=") || action.StartsWith("GameUp button=")){if(gamepad!=null)gamepad.Button(action.Substring(action.IndexOf('=')+1),action.StartsWith("GameDown"));return;}
        if (client == null) return;
        try {
            client.Send(action, window, pid);
            if(dual==null && settings!=null && settings.RightHaptics && !helperPreview && !feedbackFailed && active && device!=IntPtr.Zero) {
                bool click=touchingRight && (action=="LeftDown" || action=="LeftUp");
                double moved=0;
                // Movement haptics removed: only the previous press/release feedback remains.
                if((click || moved>0) && cadence.Tick(clock.ElapsedMilliseconds,touchingRight,click,moved)) {
                    try {
                        if(feedback==null) { feedback=new TouchFeedback(device); feedbackDevice=device; Probe.Say("HAPTICS click-only; previous press/release timing; movement disabled"); }
                        if(feedback.Error!=null)throw new Exception(feedback.Error);
                        if(feedbackDevice==device)feedback.Queue((int)settings.HapticGain-(click?0:6),window);
                    } catch(Exception e) { feedbackFailed=true; Probe.Say("HAPTICS DISABLED "+e.Message+"; mouse continues"); if(feedback!=null){feedback.Dispose();feedback=null;} }
                }
            }
        }
        catch (Exception e)
        {
            failed = true; active = false; Close(); engine.Reset(); device = IntPtr.Zero;
            Probe.FatalError=L.T("输入辅助程序连接断开：")+e.Message;
            Probe.Say("HELPER STOPPED " + e.Message + "; restart required, no automatic reconnect.");
        }
    }
    internal static string ProcessPath(uint id)
    {
        if (id == 0) return null;
        IntPtr process = OpenProcess(0x1000, false, id);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new StringBuilder(32768); uint size = (uint)path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
        }
        finally { CloseHandle(process); }
    }
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}

internal sealed class MappingEngine
{
    readonly DesktopSettings cfg;
    readonly Stopwatch time = Stopwatch.StartNew();
    double previousTime, vx, vy, stableUntil;
    bool seeded, leftTouch, rightTouch, leftDown, rightDown, leftBlocked, rightBlocked;
    int leftY, rightX, rightY;
    double residualX, residualY, wheel;
    internal MappingEngine() : this(new DesktopSettings { SmoothMs=0, Inertia=false, ClickStableMs=0, DragScale=1 }) {}
    internal MappingEngine(DesktopSettings settings) { cfg=settings; }
    internal List<string> Reset()
    {
        var actions=new List<string>();
        if(leftDown) actions.Add("LeftUp"); if(rightDown) actions.Add("RightUp");
        seeded=leftTouch=rightTouch=leftDown=rightDown=leftBlocked=rightBlocked=false;
        residualX=residualY=wheel=vx=vy=previousTime=stableUntil=0;
        return actions;
    }
    internal List<string> Step(byte[] d) { return StepAt(d,time.Elapsed.TotalSeconds); }
    internal List<string> StepAt(byte[] d,double now)
    {
        var actions=new List<string>(); uint bits;
        if(Decoder.Decode(d,out bits)==null) return actions;
        double dt=!seeded ? 0.0075 : Math.Max(0.001,Math.Min(0.05,now-previousTime)); previousTime=now;
        bool lt=(bits&0x02000000)!=0, rt=(bits&0x00200000)!=0;
        int pad=d[0]==0x47?20:18;
        int ly=BitConverter.ToInt16(d,pad+2), rx=BitConverter.ToInt16(d,pad+6), ry=BitConverter.ToInt16(d,pad+8);
        int pressure=BitConverter.ToUInt16(d,pad+10);
        int click=cfg.PressureClick ? (rt ? pressure : 0) : ((bits&0x00400000)!=0 ? 16000 : 0);
        double press=cfg.PressureClick?cfg.PressThreshold:12000, release=cfg.PressureClick?cfg.ReleaseThreshold:6000;
        if(cfg.HardwarePadClick && (bits&0x00400000)!=0)click=Math.Max(click,(int)Math.Ceiling(press));
        int trigger=BitConverter.ToInt16(d,6);
        if(!seeded) { leftBlocked=click>release; rightBlocked=trigger>6000; }
        bool wasDown=leftDown;
        Trigger(click,press,release,ref leftBlocked,ref leftDown,"Left",actions);
        Trigger(trigger,12000,6000,ref rightBlocked,ref rightDown,"Right",actions);
        if(wasDown!=leftDown) { stableUntil=now+cfg.ClickStableMs/1000; ClearMotion(); }
        if(rt && !rightTouch) ClearMotion(); // Touching a spinning trackball brakes it without a jump.
        if(seeded && rt && rightTouch)
        {
            if(now<stableUntil || wasDown!=leftDown) ClearMotion();
            else {
                double dx=(rx-rightX)*cfg.Speed, dy=-(ry-rightY)*cfg.Speed;
                double gain=1+cfg.Acceleration*Math.Min(1,Math.Sqrt(dx*dx+dy*dy)/dt/1500);
                double scale=leftDown?cfg.DragScale:1;
                double alpha=cfg.SmoothMs==0?1:1-Math.Exp(-dt/(cfg.SmoothMs/1000));
                vx+=(dx*gain*scale/dt-vx)*alpha; vy+=(dy*gain*scale/dt-vy)*alpha;
                Move(cfg.SmoothMs==0 ? dx*gain*scale : vx*dt, cfg.SmoothMs==0 ? dy*gain*scale : vy*dt,actions);
            }
        }
        if(!rt) {
            if(cfg.Inertia && !leftDown && !rightDown && now>=stableUntil && Math.Abs(vx)+Math.Abs(vy)>5) {
                double fy=cfg.Friction*cfg.VerticalFriction;
                double decayX=Math.Exp(-cfg.Friction*dt), decayY=Math.Exp(-fy*dt);
                Move(vx*(1-decayX)/cfg.Friction,vy*(1-decayY)/fy,actions);
                vx*=decayX; vy*=decayY;
            } else ClearMotion();
        }
        if(cfg.LeftScroll && seeded && lt && leftTouch) {
            wheel+=(ly-leftY)/cfg.ScrollUnits;
            int ticks=Math.Max(-34,Math.Min(34,(int)wheel)); wheel-=(int)wheel;
            if(ticks!=0)actions.Add("Wheel delta="+ticks*120);
        }
        if(!lt)wheel=0;
        seeded=true; leftTouch=lt; rightTouch=rt; leftY=ly; rightX=rx; rightY=ry;
        return actions;
    }
    void ClearMotion() { residualX=residualY=vx=vy=0; }
    void Move(double dx,double dy,List<string> actions) {
        residualX+=dx; residualY+=dy;
        int x=Math.Max(-2048,Math.Min(2048,(int)residualX)), y=Math.Max(-2048,Math.Min(2048,(int)residualY));
        residualX-=(int)residualX; residualY-=(int)residualY; // Drop oversize excess, never replay a backlog.
        if(x!=0 || y!=0)actions.Add("Move dx="+x+" dy="+y);
    }
    static void Trigger(int value,double press,double release,ref bool blocked,ref bool down,string name,List<string> actions) {
        if(blocked) { if(value<=release)blocked=false; else return; }
        if(!down && value>=press) { down=true; actions.Add(name+"Down"); }
        else if(down && value<=release) { down=false; actions.Add(name+"Up"); }
    }
}

internal static class MappingTests
{
    static byte[] Report(uint buttons, short rt, short x, short y)
    {
        byte[] d = new byte[46]; d[0] = 0x45;
        Array.Copy(BitConverter.GetBytes(buttons), 0, d, 2, 4);
        Array.Copy(BitConverter.GetBytes(rt), 0, d, 8, 2);
        Array.Copy(BitConverter.GetBytes(x), 0, d, 24, 2);
        Array.Copy(BitConverter.GetBytes(y), 0, d, 26, 2);
        return d;
    }
    static byte[] Pressure(ushort p, short x, bool touch) {
        byte[] d=Report(touch?0x200000u:0u,0,x,0);
        Array.Copy(BitConverter.GetBytes(p),0,d,28,2); return d;
    }
    static void Check(bool pass, string name) { if (!pass) throw new Exception("MAPPING TEST FAILED " + name); }
    internal static void Run()
    {
        var e = new MappingEngine();
        Check(e.Step(Report(0x600000, 20000, 1000, 1000)).Count == 0, "pad held on entry suppressed");
        Check(e.Step(Report(0x600000, 20000, 1100, 900)).Contains("Move dx=2 dy=2"), "relative motion");
        Check(e.Step(Report(0x200000, 20000, 1100, 900)).Count == 0, "release unblocks; RT has no left click mapping");
        Check(e.Step(Report(0x600000, 0, 1100, 900)).Contains("LeftDown"), "pad click press");
        Check(e.Step(Report(0x600000, 0, 1100, 900)).Count == 0, "held click no repeat");
        Check(e.Reset().Contains("LeftUp"), "focus loss releases held output");
        Check(e.Reset().Count == 0, "idempotent release");
        Check(e.Step(Report(0x200000, 0, 30000, 0)).Count == 0, "reentry no jump");
        e.Step(Report(0, 0, 0, 0));
        Check(e.Step(Report(0x200000, 0, -30000, 0)).Count == 0, "retouch no jump");
        Check(e.Step(Report(0x600000, 0, -30000, 0)).Contains("LeftDown"), "second click");
        Check(e.Step(Report(0x200000, 0, -30000, 0)).Contains("LeftUp"), "pad release");
        Check(e.Step(Report(0x200000, 30000, -30000, 0)).Count == 0, "RT never clicks");
        byte[] wire = MouseWire.Pack(3, 0, 0, 123, 456);
        Check(MouseWire.Valid(wire), "valid click frame");
        Check(!MouseWire.Valid(MouseWire.Pack(1, 2049, 0, 123, 456)), "oversize motion rejected");
        Check(MouseWire.Valid(MouseWire.Pack(2, 1, 0, 123, 456)) && !MouseWire.Valid(MouseWire.Pack(2,4081,0,123,456)), "fine wheel bounded");
        Check(MouseWire.Valid(MouseWire.Pack(8,0,0,123,456)) && !MouseWire.Valid(MouseWire.Pack(8,1,0,123,456)), "fixed keyboard action rejects payload");
        Check(!MouseWire.Valid(MouseWire.Pack(99, 0, 0, 123, 456)), "unknown operation rejected");
        Check(!MouseWire.Fresh(wire, unchecked(BitConverter.ToInt32(wire, 12) + 251)), "stale frame rejected");
        Check(MouseWire.Fresh(wire, unchecked(BitConverter.ToInt32(wire, 12) + 250)), "fresh frame boundary");
        var cfg = new DesktopSettings { Speed = 0.04, SmoothMs = 0, Inertia = true, Friction = 8 };
        var motion = new MappingEngine(cfg);
        motion.StepAt(Report(0x200000, 0, 0, 0), 1);
        Check(motion.StepAt(Report(0x200000, 0, 1000, 0), 1.01).Contains("Move dx=40 dy=0"), "configured speed");
        Check(motion.StepAt(Report(0, 0, 1000, 0), 1.02).Count > 0, "inertia after release");
        motion.Reset();
        Check(motion.StepAt(Report(0, 0, 0, 0), 1.03).Count == 0, "reset cancels inertia");
        Check(motion.StepAt(Report(0x200000, 0, -30000, 0), 1.04).Count == 0, "retouch cancels inertia");
        cfg.Allow.Add(@"C:\Apps\editor.exe");
        cfg.Allow.Add(@"C:\Steam\steamwebhelper.exe");
        Check(cfg.Allows(@"C:\Apps\EDITOR.exe", new IntPtr(1)), "explicit case-insensitive app");
        Check(!cfg.Allows(@"C:\Steam\steamwebhelper.exe", new IntPtr(1)), "Steam always excluded even explicit");
        Check(!cfg.Allows(@"C:\Games\game.exe", new IntPtr(1)), "unknown app excluded by default");
        cfg.Global = true; cfg.Exclude.Add(@"C:\Games");
        Check(!cfg.Allows(@"C:\Games\Sub\game.exe", new IntPtr(1)), "global game folder excluded");
        Check(!cfg.Allows(null, new IntPtr(1)), "unreadable process excluded");
        var pc=new DesktopSettings { PressureClick=true, PressThreshold=3500, ReleaseThreshold=2200, SmoothMs=0, ClickStableMs=60, Inertia=true, DragScale=1 };
        var pe=new MappingEngine(pc);
        pe.StepAt(Pressure(0,0,true),2);
        Check(pe.StepAt(Pressure(3500,500,true),2.01).Contains("LeftDown"),"pressure threshold press");
        Check(!pe.StepAt(Pressure(3000,800,true),2.02).Exists(delegate(string a){return a.StartsWith("Move") || a=="LeftUp" || a=="LeftDown";}),"hysteresis and click drift suppressed");
        Check(pe.StepAt(Pressure(3000,1000,true),2.10).Exists(delegate(string a){return a.StartsWith("Move");}),"held pressure drag resumes");
        Check(pe.StepAt(Pressure(2200,1000,true),2.11).Contains("LeftUp"),"release threshold");
        Check(!pe.StepAt(Pressure(2700,1000,true),2.12).Contains("LeftDown"),"pressure bounce no reclick");
        pe.StepAt(Pressure(4000,1000,true),2.20);
        Check(pe.StepAt(Pressure(4000,1000,false),2.21).Contains("LeftUp"),"lost touch releases pressure click");
        pe.Reset();
        Check(!pe.StepAt(Pressure(5000,0,true),3).Contains("LeftDown"),"held pressure on entry blocked");
        pe.StepAt(Pressure(0,0,true),3.01);
        Check(pe.StepAt(Pressure(4000,0,true),3.02).Contains("LeftDown"),"release rearms pressure click");
        var smoothTrack=new MappingEngine(new DesktopSettings{SmoothMs=0,Inertia=true});
        smoothTrack.StepAt(Report(0x200000,0,0,0),4);
        smoothTrack.StepAt(Report(0x200000,0,1000,0),4.01);
        Check(smoothTrack.StepAt(Report(0,0,0,0),4.02).Count>0,"trackball spins");
        Check(smoothTrack.StepAt(Report(0x200000,0,-25000,0),4.03).Count==0,"retouch stops active spin immediately");
        Check(smoothTrack.StepAt(Report(0,0,0,0),4.04).Count==0,"brake does not resume spin");
        var fc=new FeedbackCadence();
        Check(!fc.Tick(0,false,false,50),"haptics idle/inertia without touch silent");
        Check(fc.Tick(1,true,true,0),"click feedback");
        Check(!fc.Tick(10,true,true,0),"feedback rate limit");
        Check(fc.Tick(51,true,false,15),"movement feedback after interval");
        Check(!fc.Tick(101,true,false,0),"stationary finger silent");
        byte[] pulse=TouchFeedback.Packet(64,-12);
        Check(pulse[0]==0x82 && pulse[1]==1 && pulse[2]==1 && pulse[3]==244 && pulse[63]==0,"verified right tick encoding");
        bool rejected=false; try{TouchFeedback.Packet(64,0);}catch(ArgumentException){rejected=true;}
        Check(rejected,"haptic gain bounded");
        byte[] scroll=Report(0x2000000,0,0,0);
        var noWheel=new MappingEngine(new DesktopSettings{LeftScroll=false});
        noWheel.StepAt(scroll,10); Array.Copy(BitConverter.GetBytes((short)10000),0,scroll,20,2);
        Check(!noWheel.StepAt(scroll,10.01).Exists(delegate(string a){return a.StartsWith("Wheel");}),"Steam-owned left pad not duplicated");
        var yesWheel=new MappingEngine(new DesktopSettings{LeftScroll=true});
        yesWheel.StepAt(scroll,10); Array.Copy(BitConverter.GetBytes((short)12000),0,scroll,20,2);
        Check(yesWheel.StepAt(scroll,10.01).Contains("Wheel delta=120"),"optional left scroll");
        Probe.Say("MAPPING TEST PASS: activation, held-trigger suppression, motion, hysteresis, reset, retouch. No input sent.");
    }
}
