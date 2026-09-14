using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
internal sealed partial class PadHop {
 const string ActivationName="Local\\PadHop.Activate.v1";
 EventWaitHandle activation;RegisteredWaitHandle activationWait;HwndSource shellSource;
 static readonly uint TaskbarCreated=RegisterWindowMessage("TaskbarCreated");
 void StartActivation(){activation=new EventWaitHandle(false,EventResetMode.AutoReset,ActivationName);activationWait=ThreadPool.RegisterWaitForSingleObject(activation,delegate(object state,bool timedOut){window.Dispatcher.BeginInvoke(new Action(Show));},null,-1,false);}
 void StopActivation(){if(activationWait!=null)activationWait.Unregister(null);if(activation!=null)activation.Dispose();if(shellSource!=null)shellSource.RemoveHook(ShellMessage);}
 static void WakeExisting(){for(int i=0;i<50;i++){try{using(var signal=EventWaitHandle.OpenExisting(ActivationName)){foreach(var p in Process.GetProcessesByName("PadHop")){using(p){try{if(p.Id!=Process.GetCurrentProcess().Id && p.SessionId==Process.GetCurrentProcess().SessionId && string.Equals(p.MainModule.FileName,Path.Combine(Root,"PadHop.exe"),StringComparison.OrdinalIgnoreCase))AllowSetForegroundWindow((uint)p.Id);}catch{}}}signal.Set();return;}}catch(WaitHandleCannotBeOpenedException){Thread.Sleep(100);}}throw new Exception("已有进程未响应窗口唤起，请退出旧版本后重试。");}
 void InstallShellHook(){shellSource=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);if(shellSource!=null)shellSource.AddHook(ShellMessage);}
 IntPtr ShellMessage(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled){if((uint)msg==TaskbarCreated && tray!=null){window.Dispatcher.BeginInvoke(new Action(delegate{tray.Visible=false;tray.Visible=true;}));}return IntPtr.Zero;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint pid);
}
