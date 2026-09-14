using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

// SteamlessController command protocol (MIT); see THIRD-PARTY-NOTICES.
// Only selected SC2 BLE, only while our virtual output is active and Steam absent.
// No flash writes, virtual-device hiding or global keyboard hooks.
internal sealed class StandaloneInputLease:IDisposable {
 readonly string path;readonly Thread worker;readonly ManualResetEvent stop=new ManualResetEvent(false);
 internal volatile int State; // 0 waiting, 1 disabled, 2 Steam owns device, -1 failed
 internal static bool SteamRunning(){foreach(var p in Process.GetProcessesByName("steam")){p.Dispose();return true;}return false;}
 internal StandaloneInputLease(IntPtr raw){var d=Devices.Read(raw,2);if(d==null || !DeviceGate.IsSc2(d.Type,d.Vid,d.Pid,d.Page,d.Usage))throw new Exception("Standalone lease requires selected SC2 BLE");path=d.Path;worker=new Thread(Run){IsBackground=true};worker.Start();}
 internal static byte[] Command(int length,byte id,byte command){if(length<9 || length>128 || (id!=1 && id!=2))throw new ArgumentException("Unsupported command report");var b=new byte[length];b[0]=id;b[1]=command;if(command==0x87){b[2]=6;b[3]=8;b[6]=7;}else if(command!=0x81 && command!=0x85 && command!=0x8e)throw new ArgumentException("Unsupported command");return b;}
 void Run(){SafeFileHandle file=null;bool changed=false;byte id=0;int length=0;try{
  if(SteamRunning()){State=2;return;}
  file=CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);if(file.IsInvalid)throw new Win32Exception();
  IntPtr data;if(!HidD_GetPreparsedData(file,out data))throw new Win32Exception();
  try{IntPtr caps=Marshal.AllocHGlobal(64);try{if(HidP_GetCaps(data,caps)!=0x110000)throw new Exception("Feature caps failed");length=(ushort)Marshal.ReadInt16(caps,8);for(int kind=0;kind<2;kind++){ushort count=(ushort)Marshal.ReadInt16(caps,kind==0?60:58);if(count==0)continue;IntPtr entries=Marshal.AllocHGlobal(count*72);try{int status=kind==0?HidP_GetValueCaps(2,entries,ref count,data):HidP_GetButtonCaps(2,entries,ref count,data);if(status!=0x110000)throw new Exception("Feature IDs unavailable");for(int i=0;i<count;i++){byte candidate=Marshal.ReadByte(entries,i*72+2);if(candidate==1 || (candidate==2 && id==0))id=candidate;}}finally{Marshal.FreeHGlobal(entries);}}}finally{Marshal.FreeHGlobal(caps);}}finally{HidD_FreePreparsedData(data);}
  Command(length,id,0x81); // validate before any change
  while(!stop.WaitOne(0)){
   if(SteamRunning()){State=2;break;}
   changed=true;Write(file,Command(length,id,0x81));Write(file,Command(length,id,0x87));State=1;
   if(stop.WaitOne(1000))break;
  }
 }catch(Exception){State=-1;Probe.Say("STANDALONE_FAILED");}
 finally{
  // Steam owns its own settings once running; never overwrite them with defaults.
  if(changed && file!=null && !file.IsInvalid && !SteamRunning())try{Write(file,Command(length,id,0x85));Write(file,Command(length,id,0x8e));}catch(Exception){State=-1;Probe.Say("STANDALONE_RESTORE_FAILED");}
  if(file!=null)file.Dispose();
 }}
 static void Write(SafeFileHandle file,byte[] bytes){if(!HidD_SetFeature(file,bytes,bytes.Length))throw new Win32Exception();}
 public void Dispose(){stop.Set();if(worker.Join(1500))stop.Dispose();}
 internal static void Test(){var c=Command(64,1,0x87);if(c[0]!=1||c[1]!=0x87||c[2]!=6||c[3]!=8||c[4]!=0||c[5]!=0||c[6]!=7||c[7]!=0||c[8]!=0)throw new Exception("Raw pad mode payload");if(Command(64,1,0x81)[2]!=0 || Command(64,2,0x85)[1]!=0x85)throw new Exception("Mapping command framing");bool denied=false;try{Command(64,0x82,0x81);}catch(ArgumentException){denied=true;}if(!denied)throw new Exception("Haptics report confused with feature command");}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string p,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("hid.dll",SetLastError=true)]static extern bool HidD_GetPreparsedData(SafeFileHandle f,out IntPtr p);
 [DllImport("hid.dll")]static extern bool HidD_FreePreparsedData(IntPtr p);
 [DllImport("hid.dll")]static extern int HidP_GetCaps(IntPtr p,IntPtr caps);
 [DllImport("hid.dll")]static extern int HidP_GetValueCaps(int t,IntPtr caps,ref ushort count,IntPtr p);
 [DllImport("hid.dll")]static extern int HidP_GetButtonCaps(int t,IntPtr caps,ref ushort count,IntPtr p);
 [DllImport("hid.dll",SetLastError=true)]static extern bool HidD_SetFeature(SafeFileHandle f,byte[] b,int length);
}
