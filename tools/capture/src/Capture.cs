using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class Capture : Form
{
    readonly Button full=new Button(), raw=new Button(), stop=new Button();
    readonly Label info=new Label();
    readonly Timer timer=new Timer();
    internal Action<string> SessionCompleted; internal Action Beginning; internal bool IsBusy { get { return csv!=null || waiting || finishing; } } internal void RequestStop(){Finish();}
    StreamWriter csv;
    string folder;
    string targetDevicePath;
    System.Threading.CancellationTokenSource calibrationCancel;
    IntPtr device;
    bool tracing, registered, waiting, finishing, sawDone;
    DateTime deadline;
    long samples;
    int durationSeconds=60;
    internal bool RecordRight; internal string ResultFile; internal string SelectedPath;
    readonly Stopwatch elapsed=new Stopwatch();
    uint previous;
    bool seeded;
    [DllImport("kernel32.dll")] static extern void GetSystemTimePreciseAsFileTime(out long time);
    internal static string TraceArguments(string script,string session){return "-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -SessionDirectory \""+session+"\" -FullPacket";}
    [STAThread] static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--self-test") { Decoder.Test();return 0; }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var form=new Capture();if((args.Length==3 || args.Length==4) && args[0]=="--record"){form.SelectedPath=args.Length==4?args[3]:null;form.RecordRight=args[1]=="right";if(args[1]!="right" && args[1]!="left")throw new Exception("Invalid source pad");form.ResultFile=Path.GetFullPath(args[2]);form.ShowInTaskbar=false;form.Opacity=0;form.SessionCompleted=delegate(string session){File.WriteAllText(form.ResultFile,session);form.Dispose();Application.ExitThread();};form.Shown+=delegate{form.Begin(true);};}Application.Run(form);return 0;
    }
    internal Capture()
    {
        Text=L.T("SC2 · 左板事件采集");ClientSize=new Size(760,270);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;
        var hint=new Label{Text=L.T("保留 Steam 左触摸板配置；采集时暂停其他震动测试工具。\n无需重连。准备阶段会发几下轻微右板反馈，自动核对通道；不发送键鼠输入。")};hint.SetBounds(18,15,720,60);Controls.Add(hint);
        full.Text=L.T("完整采集（60 秒）");full.SetBounds(18,90,225,40);Controls.Add(full);
        raw.Text=L.T("先检查采集通路（8 秒）");raw.SetBounds(260,90,225,40);Controls.Add(raw);
        stop.Text=L.T("停止并保存");stop.SetBounds(502,90,225,40);stop.Enabled=false;Controls.Add(stop);
        info.SetBounds(18,150,720,110);info.Text=L.T("完整采集会临时开启 HID 内容记录，可能包含蓝牙密钥和其他设备数据；仅存本机，结束恢复记录开关。无需关闭或重连手柄。");Controls.Add(info);
        full.Click+=delegate{Begin(true);};raw.Click+=delegate{Begin(true,8);};stop.Click+=delegate{Finish();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(csv!=null || waiting || finishing){e.Cancel=true;Finish();}};
        timer.Interval=200;timer.Tick+=delegate{Poll();};timer.Start();
    }
    internal void Begin(bool withTrace,int seconds=60)
    {
        durationSeconds=seconds;
        try {
            if(Beginning!=null)Beginning();
            int count=0; string targetPath="";foreach(Device d in Devices.Enumerate(false).Values)if(d.Vid==0x28de && d.Pid==0x1303 && d.Page==0xff00 && d.Usage==1 && (SelectedPath==null || string.Equals(SelectedPath,d.Path,StringComparison.OrdinalIgnoreCase))){device=d.Handle;targetPath=d.Path;count++;}
            if(count!=1)throw new Exception(L.T("需要连接一个 SC2 蓝牙控制器，当前匹配 ")+count+L.T(" 个。"));
            targetDevicePath=targetPath;
            folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PadHop","captures",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(folder);
            var mac=System.Text.RegularExpressions.Regex.Match(targetPath,@"_([0-9a-fA-F]{12})&Col",System.Text.RegularExpressions.RegexOptions.IgnoreCase); File.WriteAllText(Path.Combine(folder,"target.json"),"{\"bluetooth_mac\":\""+(mac.Success?mac.Groups[1].Value.ToLowerInvariant():"")+"\"}");
            string targetFile=Path.Combine(folder,"target.json");string targetText=File.ReadAllText(targetFile);File.WriteAllText(targetFile,targetText.Substring(0,targetText.Length-1)+",\"capture_side\":\""+(RecordRight?"right":"left")+"\"}");
            if(ResultFile!=null)File.WriteAllText(ResultFile+".pending",folder);
            tracing=withTrace;full.Enabled=raw.Enabled=false;stop.Enabled=true;samples=0;seeded=false;sawDone=false;
            if(withTrace){
                var psi=new ProcessStartInfo("powershell.exe",TraceArguments(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Record-Bluetooth.ps1"),folder));
                psi.UseShellExecute=true;psi.Verb="runas";psi.WindowStyle=ProcessWindowStyle.Hidden;
                Process.Start(psi);waiting=true;deadline=DateTime.UtcNow.AddSeconds(45);info.Text=L.T("等待蓝牙采集启动。确认管理员提示后，请按窗口提示操作。");
            }else OpenRaw();
        }catch(Exception e){info.Text=L.T("未开始：")+e.Message;full.Enabled=raw.Enabled=true;stop.Enabled=false;if(ResultFile!=null){File.WriteAllText(ResultFile+".error",e.ToString());Dispose();Application.ExitThread();}}
    }
    void OpenRaw()
    {
        var r=new Native.Registration[]{new Native.Registration{Page=0xff00,Usage=1,Flags=0x100,Target=Handle}};
        if(!Native.RegisterRawInputDevices(r,1,(uint)Marshal.SizeOf(typeof(Native.Registration))))throw new Win32Exception();registered=true;
        csv=new StreamWriter(Path.Combine(folder,"left-input.csv"));csv.WriteLine("unix_seconds,qpc_ticks,elapsed_ms,phase,touch,click_bit,x,y,pressure,touch_changed,click_changed,report_hex");
        File.WriteAllText(Path.Combine(folder,"input-clock.txt"),"QPC frequency="+Stopwatch.Frequency+"\r\nTimestamps are host arrival times, not controller hardware times.\r\n");
        waiting=false;elapsed.Restart();
        if(tracing){calibrationCancel=new System.Threading.CancellationTokenSource();var cancel=calibrationCancel.Token;string session=folder,path=targetDevicePath;System.Threading.Tasks.Task.Run(delegate{ChannelCalibration.Run(session,path,cancel);});}
    }
    string Phase(){double s=elapsed.Elapsed.TotalSeconds;return s<10?"prepare":s<15?"idle":s<30?"slow_slide":s<45?"fast_slide":s<55?"press_release":"idle_end";}
    void Poll()
    {
        if(ResultFile!=null){File.WriteAllText(ResultFile+".progress",info.Text.Replace("left trackpad",RecordRight?"right trackpad":"left trackpad").Replace("left pad",RecordRight?"right pad":"left pad").Replace(L.T("左触摸板"),RecordRight?L.T("右触摸板"):L.T("左触摸板")).Replace(L.T("左板"),RecordRight?L.T("右板"):L.T("左板")));if(File.Exists(ResultFile+".stop"))Finish();}
        try {
            if(waiting){if(File.Exists(Path.Combine(folder,"trace.error.txt")))throw new Exception(File.ReadAllText(Path.Combine(folder,"trace.error.txt")));
                if(File.Exists(Path.Combine(folder,"trace.ready")))OpenRaw();else if(DateTime.UtcNow>deadline)throw new Exception(L.T("等待启动超时；已请求结束采集。"));}
            if(csv!=null){csv.Flush();
                if(tracing && File.Exists(Path.Combine(folder,"trace.done"))){sawDone=true;Finish();return;}
                if(elapsed.Elapsed.TotalSeconds>=durationSeconds){Finish();return;}
                string p=Phase();string hint=p=="prepare"?L.T("自动核对通道中：右板可能轻震几下，请先松手等待，不用重连。"):p=="idle" || p=="idle_end"?L.T("请松开左板，保持静止。"):p=="slow_slide"?L.T("请缓慢滑动左触摸板。"):p=="fast_slide"?L.T("请快速滑动左触摸板。"):L.T("请反复按下、松开左触摸板。");
                info.Text=hint+L.T("\n剩余 ")+Math.Max(0,durationSeconds-(int)elapsed.Elapsed.TotalSeconds)+L.T(" 秒；已记录 ")+samples+L.T(" 条输入。");
            }
            if(finishing && File.Exists(Path.Combine(folder,"trace.done"))){finishing=false; if(SessionCompleted!=null)SessionCompleted(folder);full.Enabled=raw.Enabled=true;stop.Enabled=false;info.Text=L.T("已保存：")+folder+"\n"+(File.Exists(Path.Combine(folder,"trace.error.txt"))?L.T("蓝牙采集失败，查看 trace.error.txt；输入记录仍保留。"):sawDone?L.T("蓝牙记录提前结束；输入同步停止，请检查 trace.log。"):L.T("下一步解析蓝牙日志；成功保存不代表已捕获震动载荷。"));}
            else if(finishing && DateTime.UtcNow>deadline){finishing=false;full.Enabled=raw.Enabled=true;stop.Enabled=false;info.Text=L.T("蓝牙记录收尾未确认。请查看 trace.log 和 trace-instance.txt；输入文件已经保存。");}
        }catch(Exception e){Finish();info.Text=L.T("采集停止：")+e.Message+L.T("\n文件：")+folder;}
    }
    void Finish()
    {
        if(folder==null || finishing)return;
        if(calibrationCancel!=null){calibrationCancel.Cancel();calibrationCancel=null;}
        if(registered){Native.RegisterRawInputDevices(new Native.Registration[]{new Native.Registration{Page=0xff00,Usage=1,Flags=1,Target=IntPtr.Zero}},1,(uint)Marshal.SizeOf(typeof(Native.Registration)));registered=false;}
        if(csv!=null){csv.Dispose();csv=null;}waiting=false;elapsed.Stop();
        if(tracing){File.WriteAllText(Path.Combine(folder,"stop.request"),"stop");finishing=true;deadline=DateTime.UtcNow.AddMinutes(2);info.Text=L.T("正在停止蓝牙记录并保存…");}
        else {full.Enabled=raw.Enabled=true;stop.Enabled=false;info.Text=L.T("输入已保存：")+folder+L.T("\n仅输入模式不包含 Steam 发出的震动指令。");}
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0xff && csv!=null)try{Receive(m.LParam);}catch(Exception e){Finish();info.Text=L.T("输入错误：")+e.Message;}
        base.WndProc(ref m);
    }
    void Receive(IntPtr input)
    {
        long utc;GetSystemTimePreciseAsFileTime(out utc);long qpc=Stopwatch.GetTimestamp();
        uint size=0,header=(uint)(8+2*IntPtr.Size);if(Native.GetRawInputData(input,0x10000003,IntPtr.Zero,ref size,header)==uint.MaxValue)throw new Win32Exception();
        if(size<header+8 || size>1048576)return;IntPtr p=Marshal.AllocHGlobal((int)size);
        try{uint got=Native.GetRawInputData(input,0x10000003,p,ref size,header);if(got==uint.MaxValue)throw new Win32Exception();if(got<header+8 || Marshal.ReadInt32(p)!=2)return;
            IntPtr h=Marshal.ReadIntPtr(p,8);if(h!=device){Device d=Devices.Read(h,2);if(d==null || d.Vid!=0x28de || d.Pid!=0x1303 || d.Page!=0xff00 || d.Usage!=1 || (SelectedPath!=null && !string.Equals(SelectedPath,d.Path,StringComparison.OrdinalIgnoreCase)))return;device=h;seeded=false;}
            uint n=(uint)Marshal.ReadInt32(p,(int)header+4),len=(uint)Marshal.ReadInt32(p,(int)header);if(len==0 || (ulong)n*len>got-header-8)return;
            for(uint i=0;i<n;i++){byte[] b=new byte[len];Marshal.Copy(IntPtr.Add(p,checked((int)(header+8+i*len))),b,0,b.Length);uint bits;if(Decoder.Decode(b,out bits)==null)continue;int offset=(b[0]==0x47?20:18)+(RecordRight?6:0);int touchBit=RecordRight?21:25,clickBit=RecordRight?22:26;
                string stamp=((utc-116444736000000000L)/10000000m).ToString("F7",CultureInfo.InvariantCulture);
                csv.WriteLine(string.Join(",",new string[]{stamp,qpc.ToString(),elapsed.ElapsedMilliseconds.ToString(),Phase(),((bits>>touchBit)&1).ToString(),((bits>>clickBit)&1).ToString(),BitConverter.ToInt16(b,offset).ToString(),BitConverter.ToInt16(b,offset+2).ToString(),BitConverter.ToUInt16(b,offset+4).ToString(),seeded?(((bits^previous)>>touchBit)&1).ToString():"",seeded?(((bits^previous)>>clickBit)&1).ToString():"",BitConverter.ToString(b)}));previous=bits;seeded=true;samples++;}
        }finally{Marshal.FreeHGlobal(p);}
    }
}
