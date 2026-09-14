using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal sealed class VirtualGamepad:IDisposable {
 [StructLayout(LayoutKind.Sequential,Pack=1)]internal struct Report{internal ushort Buttons;internal byte LT,RT;internal short LX,LY,RX,RY;}
 static readonly Dictionary<string,ushort> Flags=new Dictionary<string,ushort>{{"A",0x1000},{"B",0x2000},{"X",0x4000},{"Y",0x8000},{"LB",0x100},{"RB",0x200},{"VIEW",0x20},{"MENU",0x10},{"L3",0x40},{"R3",0x80},{"UP",1},{"DOWN",2},{"LEFT",4},{"RIGHT",8}};
 readonly HashSet<string> mapped=new HashSet<string>();readonly PadBook book;IntPtr client,target;Report physical;uint blocked;bool seeded;
 internal VirtualGamepad(PadBook book){this.book=book;try{client=vigem_alloc();if(client==IntPtr.Zero)throw new Exception(L.T("无法分配虚拟手柄客户端。"));Check(vigem_connect(client));target=vigem_target_x360_alloc();if(target==IntPtr.Zero)throw new Exception(L.T("无法分配虚拟手柄。"));Check(vigem_target_add(client,target));}catch{Dispose();throw;}Probe.Say("VIRTUAL X360 connected (selected SC2 input only)");}
 static void Check(uint result){if(result!=0x20000000)throw new Exception(L.T("虚拟手柄不可用（ViGEm 错误 0x")+result.ToString("X")+"）。");}
 internal void Button(string name,bool down){if(down)mapped.Add(name);else mapped.Remove(name);Send();}
 internal void Update(byte[] raw,uint bits){if(!seeded){blocked=bits;seeded=true;}blocked&=bits;physical=ConvertReport(book,raw,bits&~blocked);Send();}
 internal static Report ConvertReport(PadBook book,byte[] raw,uint bits){var r=new Report();var bindings=book.EffectiveBindings();foreach(var f in Flags){BindingAction a;if((bits&InputCatalog.Mask(f.Key))!=0 && (bindings.TryGetValue(f.Key,out a) && a.Kind=="gamepad" && a.Chord==f.Key))r.Buttons|=f.Value;}StickSources.Axes(book,raw,ref r);BindingAction t;if(bindings.TryGetValue("LT",out t) && t.Kind=="gamepad" && t.Chord=="LT")r.LT=Trigger(raw,6);if(bindings.TryGetValue("RT",out t) && t.Kind=="gamepad" && t.Chord=="RT")r.RT=Trigger(raw,8);PadSources.Axes(book,raw,bits,ref r);return r;}
 static byte Trigger(byte[] b,int i){return (byte)(Math.Max(0,(int)BitConverter.ToInt16(b,i))*255/32767);}
 void Send(){var r=physical;foreach(string name in mapped){ushort f;if(Flags.TryGetValue(name,out f))r.Buttons|=f;else if(name=="LT")r.LT=255;else if(name=="RT")r.RT=255;}Check(vigem_target_x360_update(client,target,r));}
 internal void Neutral(){physical=new Report();mapped.Clear();seeded=false;blocked=0;Send();}
 public void Dispose(){if(target!=IntPtr.Zero){if(client!=IntPtr.Zero){vigem_target_x360_update(client,target,new Report());vigem_target_remove(client,target);}vigem_target_free(target);target=IntPtr.Zero;}if(client!=IntPtr.Zero){vigem_disconnect(client);vigem_free(client);client=IntPtr.Zero;}}
 internal static void Test(){var menuBook=new PadBook{GamepadEnabled=true};var menuRaw=new byte[48];menuRaw[0]=0x45;if(ConvertReport(menuBook,menuRaw,1u<<6).Buttons!=0x10 || ConvertReport(menuBook,menuRaw,1u<<14).Buttons!=0x20)throw new Exception("Physical Menu/View reversed");var b=new PadBook{GamepadEnabled=true,Bindings=new Dictionary<string,BindingAction>{{"A",new BindingAction{Kind="keyboard",Chord="Space"}}}};var raw=new byte[48];raw[0]=0x45;Array.Copy(BitConverter.GetBytes((short)32767),0,raw,6,2);Array.Copy(BitConverter.GetBytes((short)-12345),0,raw,10,2);var r=ConvertReport(b,raw,3);if(r.Buttons!=0x2000||r.LT!=255||r.LX!=-12345)throw new Exception("Virtual mapping suppressed/baseline/axes failed");}
 [StructLayout(LayoutKind.Sequential)]struct State{internal uint Packet;internal Report Pad;}
 internal static void DeviceTest(){using(var form=new System.Windows.Forms.Form{Text=L.T("SC2 输出验证"),Width=420,Height=150,TopMost=true}){form.Show();form.Activate();System.Windows.Forms.Application.DoEvents();using(var pad=new VirtualGamepad(new PadBook())){uint index;Check(vigem_target_x360_get_user_index(pad.client,pad.target,out index));if(GetForegroundWindow()!=form.Handle)throw new Exception(L.T("输出验证窗口不在前台，已取消。"));pad.Button("A",true);State s;bool down=false;for(int i=0;i<20;i++){System.Threading.Thread.Sleep(25);if(XInputGetState(index,out s)==0 && (s.Pad.Buttons&0x1000)!=0){down=true;break;}}pad.Button("A",false);bool up=false;for(int i=0;i<20;i++){System.Threading.Thread.Sleep(25);if(XInputGetState(index,out s)==0 && s.Pad.Buttons==0){up=true;break;}}if(!down || !up)throw new Exception("XInput button readback failed");Probe.Say("VIRTUAL DEVICE PASS: XInput slot="+index+" A down/up verified; disposing device.");}form.Close();}}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("xinput1_4.dll")]static extern uint XInputGetState(uint index,out State state);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern uint vigem_target_x360_get_user_index(IntPtr c,IntPtr t,out uint index);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr vigem_alloc();
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern void vigem_free(IntPtr c);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern uint vigem_connect(IntPtr c);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern void vigem_disconnect(IntPtr c);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr vigem_target_x360_alloc();
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern void vigem_target_free(IntPtr t);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern uint vigem_target_add(IntPtr c,IntPtr t);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern uint vigem_target_remove(IntPtr c,IntPtr t);
 [DllImport("ViGEmClient.dll",CallingConvention=CallingConvention.Cdecl)]static extern uint vigem_target_x360_update(IntPtr c,IntPtr t,Report r);
}
