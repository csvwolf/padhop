using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// Only the experimentally verified right-pad tick. No features, settings or rumble.
internal sealed class LiveFeedback : IDisposable
{
    readonly System.Collections.Generic.HashSet<byte> supportedIds=new System.Collections.Generic.HashSet<byte>(); readonly SafeFileHandle file;
    readonly int length;
    readonly Thread worker;
    readonly object gate=new object();
    readonly AutoResetEvent wake=new AutoResetEvent(false);
    bool stopping, queued, queuedClick;
    byte[] packet;
    IntPtr window;
    long queuedAt;
    internal string Error;
    internal int Sent;
    internal LiveFeedback(IntPtr rawDevice)
    {
        Device d=Devices.Read(rawDevice,2);
        if(d==null || d.Vid!=0x28de || d.Pid!=0x1303 || d.Page!=0xff00 || d.Usage!=1)throw new Exception("Haptics currently verified only on SC2 BLE FF00:1");
        file=CreateFile(d.Path,0x40000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        try {
            if(file.IsInvalid)throw new Win32Exception();
            IntPtr data; if(!HidD_GetPreparsedData(file,out data))throw new Win32Exception();
            bool supported=false;
            try {
                IntPtr caps=Marshal.AllocHGlobal(64);
                try {
                    if(HidP_GetCaps(data,caps)!=0x110000)throw new Exception("HID caps failed");
                    length=(ushort)Marshal.ReadInt16(caps,6);
                    for(int kind=0;kind<2;kind++) {
                        ushort count=(ushort)Marshal.ReadInt16(caps,kind==0?54:52); if(count==0)continue;
                        IntPtr entries=Marshal.AllocHGlobal(count*72);
                        try {
                            int status=kind==0?HidP_GetValueCaps(1,entries,ref count,data):HidP_GetButtonCaps(1,entries,ref count,data);
                            if(status!=0x110000)throw new Exception("Output caps failed");
                            for(int i=0;i<count;i++){byte id=Marshal.ReadByte(entries,i*72+2); if(id==0x81 || id==0x82 || id==0x83) supportedIds.Add(id); if(supportedIds.Contains(0x82) && supportedIds.Contains(0x83))supported=true;}
                        } finally { Marshal.FreeHGlobal(entries); }
                    }
                } finally { Marshal.FreeHGlobal(caps); }
            } finally { HidD_FreePreparsedData(data); }
            if(!supported || length<4 || length>128)throw new Exception("No supported 0x82 output report");
            worker=new Thread(Run); worker.IsBackground=true; worker.Start();
        } catch { file.Dispose(); wake.Dispose(); throw; }
    }
    internal void Queue(byte[] bytes,IntPtr foreground,bool click) {
        lock(gate) { if(stopping || (queued && queuedClick && !click))return; queuedClick=click; packet=(byte[])bytes.Clone(); window=foreground; queuedAt=System.Diagnostics.Stopwatch.GetTimestamp(); queued=true; wake.Set(); }
    }
    internal void Clear() { lock(gate)queued=false; }
    internal void ClearMotion() { lock(gate)if(!queuedClick)queued=false; }
    void Run() {
        try {
            while(true) {
                wake.WaitOne(100);
                byte[] bytes; IntPtr wanted; long at;
                lock(gate) { if(stopping)break; if(!queued)continue; bytes=packet; wanted=window; at=queuedAt; queued=false; }
                double age=(System.Diagnostics.Stopwatch.GetTimestamp()-at)/(double)System.Diagnostics.Stopwatch.Frequency;
                if(age>0.025 || GetForegroundWindow()!=wanted || (GetAsyncKeyState(0x7b)&0x8000)!=0)continue;
                if(bytes.Length<2 || bytes[1]!=(bytes[0]==0x81?0:1) || !supportedIds.Contains(bytes[0]) || bytes.Length>length)throw new Exception("Unsupported waveform"); byte[] padded=new byte[length]; Array.Copy(bytes,padded,bytes.Length); WriteOnce(padded); Interlocked.Increment(ref Sent);
            }
        } catch(Exception e) { Error=e.Message; lock(gate)stopping=true; }
        finally { file.Dispose(); }
    }
    internal static byte[] Packet(int length,int gain) {
        if(length<4 || length>128 || gain < -42 || gain > -6)throw new ArgumentException("Haptic bounds");
        byte[] b=new byte[length]; b[0]=0x82; b[1]=1; b[2]=1; b[3]=unchecked((byte)gain); return b;
    }
    public void Dispose() { lock(gate) { stopping=true; queued=false; wake.Set(); } if(worker.Join(1200))wake.Dispose(); }
    void WriteOnce(byte[] bytes) {
        IntPtr buffer=Marshal.AllocHGlobal(bytes.Length), ov=Marshal.AllocHGlobal(32), signal=CreateEvent(IntPtr.Zero,true,false,null); bool pending=false;
        try {
            if(signal==IntPtr.Zero)throw new Win32Exception(); Marshal.Copy(bytes,0,buffer,bytes.Length);
            for(int i=0;i<32;i++)Marshal.WriteByte(ov,i,0); Marshal.WriteIntPtr(ov,24,signal);
            bool immediate=WriteFile(file,buffer,(uint)bytes.Length,IntPtr.Zero,ov); int error=immediate?0:Marshal.GetLastWin32Error();
            if(!immediate && error!=997)throw new Win32Exception(error); pending=true;
            if(WaitForSingleObject(signal,500)!=0)throw new Exception("Haptic output timeout; disabled without retry");
            uint written; bool ok=GetOverlappedResult(file,ov,out written,false); pending=false;
            if(!ok)throw new Win32Exception(); if(written!=bytes.Length)throw new Exception("Short haptic write");
        } finally {
            if(pending){CancelIoEx(file,ov); uint n; GetOverlappedResult(file,ov,out n,true);}
            if(signal!=IntPtr.Zero)CloseHandle(signal); Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer);
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sec,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_GetPreparsedData(SafeFileHandle file,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,IntPtr caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort length,IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type,IntPtr caps,ref ushort length,IntPtr data);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteFile(SafeFileHandle file,IntPtr buffer,uint length,IntPtr written,IntPtr ov);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr ov,out uint written,bool wait);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle file,IntPtr ov);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateEvent(IntPtr sec,bool manual,bool initial,string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint time);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}
