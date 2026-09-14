using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed partial class LiveLab : Form
{
    readonly ComboBox motion=new ComboBox(), click=new ComboBox(), mode=new ComboBox();
    readonly NumericUpDown motionGain=new NumericUpDown(), clickGain=new NumericUpDown(), interval=new NumericUpDown(), spacing=new NumericUpDown();
    readonly NumericUpDown motionHz=new NumericUpDown(), motionMs=new NumericUpDown(), clickHz=new NumericUpDown(), clickMs=new NumericUpDown();
    bool applyingPreset;
    readonly CheckBox release=new CheckBox();
    readonly Button start=new Button(), save=new Button();
    readonly Label status=new Label(),effective=new Label();
    readonly TestSurface surface=new TestSurface();
    readonly Timer timer=new Timer();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly GestureGate cadence=new GestureGate();
    Observer observer;
    LiveFeedback feedback;
    MappingEngine mapping;
    IntPtr device;
    long lastReport;
    bool active, priorTouch, seeded;
    int px,py;
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [STAThread] static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--self-test") {
            try { Waves.Test(); ChannelCalibration.Test(); CapturedWave.Test(); GestureGate.Test(); MappingTests.Run(); TestEffectiveConfig(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-self-test.txt"),"PASS: waveform encoding, movement cadence, stationary silence, pressure hysteresis, click stabilization, mapping regression.");return 0; }
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-self-test.txt"),e.ToString());return 1;}
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && (args[0]=="--render-ui" || args[0]=="--render-config")) {
            using(var f=new LiveLab()){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-2000,-2000);f.Show();f.pages.SelectedIndex=args[0]=="--render-config"?0:1;if(args[0]=="--render-config"){f.motionPulse=new byte[]{0x81,0,0x90,1,0,0,1,0};f.motion.SelectedIndex=11;f.UpdateEffective();}Application.DoEvents();f.PerformLayout();using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(0,0,f.Width,f.Height));bitmap.Save(args[1]);}}
            return 0;
        }
        bool created;using(var singleton=new System.Threading.Mutex(true,"Local\\SC2HapticWorkbench",out created)){
            if(!created){MessageBox.Show("采集修复版已经打开，请使用现有窗口。","SC2 v8");return 0;}
            try{var form=new LiveLab();if(args.Length==1 && args[0]=="--path-check")form.Shown+=delegate{form.pages.SelectedIndex=1;form.BeginInvoke(new Action(delegate{form.capturePage.Begin(true,8);}));};if(args.Length==2 && args[0]=="--open-capture")form.Shown+=delegate{form.LoadResults(args[1]);};Application.Run(form);}finally{singleton.ReleaseMutex();}
        }return 0;
    }
    LiveLab()
    {
        Text="SC2 · 采集修复版 v8（当前参数与导出）"; ClientSize=new Size(940,770); Font=new Font("Microsoft YaHei UI",10);
        StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
        AddLabel("选方案后点“开始实操”。右板滑动 / 按压；左板可滚动测试内容。切走窗口即停止。",18,14,900);
        SetupCombo(motion,18,74);SetupCombo(click,470,74);
        AddLabel("滑动反馈",18,46,200);AddLabel("点击反馈",470,46,200);
        SetupNumber(motionGain,18,137,-30,-6,-15);SetupNumber(clickGain,470,137,-30,-6,-12);
        AddLabel("强度 dB",18,111,130);AddLabel("强度 dB",470,111,130);
        AddLabel("频率 Hz",165,111,130);SetupNumber(motionHz,165,137,40,500,160);
        AddLabel("每次时长 ms",312,111,140);SetupNumber(motionMs,312,137,1,80,20);
        AddLabel("频率 Hz",617,111,130);SetupNumber(clickHz,617,137,40,500,160);
        AddLabel("每次时长 ms",764,111,140);SetupNumber(clickMs,764,137,1,80,20);
        AddLabel("两次最短间隔 ms",18,179,190);SetupNumber(interval,18,206,30,200,60);
        AddLabel("移动距离（像素）",220,179,190);SetupNumber(spacing,220,206,2,60,12);
        release.Text="松开也反馈（较轻）";release.Checked=true;release.SetBounds(470,206,270,30);Controls.Add(release);
        mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Items.AddRange(new string[]{"右板移动光标 + 点击", "右板上下滚动 + 点击"});mode.SelectedIndex=0;mode.SetBounds(18,268,290,30);Controls.Add(mode);
        start.Text="开始实操";start.SetBounds(330,263,180,38);Controls.Add(start);start.Click+=delegate{if(active)Stop();else Start();};
        save.Text="保存这组设置";save.SetBounds(530,263,180,38);Controls.Add(save);save.Click+=delegate{Save();};
        effective.SetBounds(18,306,900,52);Controls.Add(effective);
        surface.SetBounds(18,361,904,308);Controls.Add(surface);
        status.SetBounds(18,687,904,70);status.Text="未开始。默认滑动：160 Hz / 20 ms / -15 dB；频率与时长可直接输入。";Controls.Add(status);
        motion.SelectedIndexChanged+=delegate{ApplyPreset(false);};click.SelectedIndexChanged+=delegate{ApplyPreset(true);};
        mode.SelectedIndexChanged+=delegate{ResetInput();};
        motionHz.ValueChanged+=delegate{Custom(false);};motionMs.ValueChanged+=delegate{Custom(false);};
        clickHz.ValueChanged+=delegate{Custom(true);};clickMs.ValueChanged+=delegate{Custom(true);};
        motion.SelectedIndex=3;ApplyPreset(true);
        foreach(NumericUpDown n in new NumericUpDown[]{motionGain,clickGain,interval,spacing}) n.ValueChanged+=delegate{ResetInput();};
        release.CheckedChanged+=delegate{ResetInput();};
        Deactivate+=delegate{if(active){ResetInput();status.Text="已暂停：切回本窗口后继续，抬手再操作。";}};
        FormClosed+=delegate{Stop();timer.Dispose();};
        timer.Interval=100;timer.Tick+=delegate{
            if(!active)return;
            if((GetAsyncKeyState(0x7b)&0x8000)!=0){Stop();return;}
            if(feedback!=null && feedback.Error!=null){string error=feedback.Error;Stop();status.Text="输出失败，已停止："+error;return;}
            if(clock.ElapsedMilliseconds-lastReport>500){ResetInput();status.Text="等待控制器输入；滑动右板即可。";}
        };timer.Start(); BuildWorkflow();
    }
    void AddLabel(string text,int x,int y,int w){var l=new Label{Text=text};l.SetBounds(x,y,w,27);Controls.Add(l);}
    void SetupCombo(ComboBox c,int x,int y){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Items.Add("关闭反馈");c.Items.AddRange(Waves.Names);c.Items.AddRange(new string[]{"G · 160 Hz，5 毫秒","H · 160 Hz，10 毫秒","I · 160 Hz，15 毫秒","自定义音调","采集脉冲"});c.SelectedIndex=1;c.SetBounds(x,y,430,30);Controls.Add(c);}
    void ApplyPreset(bool isClick)
    {
        if(applyingPreset)return;
        ComboBox choice=isClick?click:motion;NumericUpDown hz=isClick?clickHz:motionHz,ms=isClick?clickMs:motionMs;
        int n=choice.SelectedIndex;applyingPreset=true;
        try{if(n==11 && (isClick?clickPulse:motionPulse)==null){choice.SelectedIndex=0;n=0;status.Text="请先采集并应用一个脉冲方案。";}hz.Enabled=ms.Enabled=n>=3 && n<=10;(isClick?clickGain:motionGain).Enabled=n!=11;
            if(n>=3 && n<=9){hz.Value=n==4 || n==6?240:n==5?320:160;ms.Value=n==6?40:n==7?5:n==8?10:n==9?15:20;}
        }finally{applyingPreset=false;}
        ResetInput();
    }
    void Custom(bool isClick){if(applyingPreset)return;applyingPreset=true;try{(isClick?click:motion).SelectedIndex=10;}finally{applyingPreset=false;}ResetInput();}
    void SetupNumber(NumericUpDown n,int x,int y,int min,int max,int value){n.Minimum=min;n.Maximum=max;n.Value=value;n.SetBounds(x,y,120,30);Controls.Add(n);}
    void Start()
    {
        if(capturePage!=null && (capturePage.IsBusy || analyzing)){status.Text="请先结束左板采集和分析，再体验右板。";return;}
        try {
            var found=new List<Device>();foreach(Device d in Devices.Enumerate(false).Values)if(d.Vid==0x28de && d.Pid==0x1303 && d.Page==0xff00 && d.Usage==1)found.Add(d);
            if(found.Count!=1)throw new Exception("需要连接一个 SC2 蓝牙控制器；当前匹配 "+found.Count+" 个。");
            device=found[0].Handle;feedback=new LiveFeedback(device);
            mapping=new MappingEngine(new DesktopSettings{Speed=0.02,SmoothMs=12,Inertia=true,Friction=8,PressureClick=true,PressThreshold=3500,ReleaseThreshold=2200,ClickStableMs=60,LeftScroll=true});
            Probe.Seconds=int.MaxValue;Probe.LabReport=Report;observer=new Observer();active=true;lastReport=clock.ElapsedMilliseconds;
            ResetInput();start.Text="停止实操";status.Text="已开始。先抬手，再滑动 / 按压右板；随时更换方案。";
        }catch(Exception e){Stop();status.Text="未开始："+e.Message;}
    }
    void Stop(){active=false;Probe.LabReport=null;if(observer!=null){observer.Dispose();observer=null;}if(feedback!=null){feedback.Dispose();feedback=null;}ResetInput();start.Text="开始实操";status.Text="已停止。设置可保存，下次继续比较。";}
    void ResetInput(){if(feedback!=null)feedback.Clear();if(mapping!=null)mapping.Reset();priorTouch=false;seeded=false;cadence.Reset();surface.Down=false;surface.Invalidate();UpdateEffective();}
    void Emit(bool isClick, bool up)
    {
        int index=(isClick?click:motion).SelectedIndex-1;if(index<0)return;
        int db=(int)(isClick?clickGain:motionGain).Value;if(up)db=Math.Max(-30,db-6);
        byte[] packet=index==10 ? CapturedPulse(isClick) : index<2?Waves.Packet(index,db):Waves.Tone((int)(isClick?clickHz:motionHz).Value,(int)(isClick?clickMs:motionMs).Value,db);
        feedback.Queue(packet,Handle,isClick);
    }
    void Report(IntPtr source,byte[] bytes)
    {
        if(!active || source!=device || GetForegroundWindow()!=Handle)return;
        uint bits;if(Decoder.Decode(bytes,out bits)==null)return;
        int offset=bytes[0]==0x47?26:24;if(bytes.Length<offset+6)return;
        lastReport=clock.ElapsedMilliseconds;
        bool touch=(bits&0x200000)!=0;
        int x=BitConverter.ToInt16(bytes,offset),y=BitConverter.ToInt16(bytes,offset+2),pressure=BitConverter.ToUInt16(bytes,offset+4);
        double moved=seeded && priorTouch && touch ? Math.Sqrt((double)(x-px)*(x-px)+(double)(y-py)*(y-py))*0.02:0;
        px=x;py=y;priorTouch=touch;seeded=true;
        bool edge=false;
        foreach(string action in mapping.Step(bytes)) {
            if(action=="LeftDown"){surface.Down=true;surface.ClickAtCursor();edge=true;feedback.Clear();Emit(true,false);}
            else if(action=="LeftUp"){surface.Down=false;edge=true;feedback.Clear();if(release.Checked)Emit(true,true);}
            else if(action.StartsWith("Move dx=")) {
                string[] parts=action.Split(' ');int dx=int.Parse(parts[1].Substring(3)),dy=int.Parse(parts[2].Substring(3));
                if(mode.SelectedIndex==0){surface.X=Math.Max(0,Math.Min(surface.Width-1,surface.X+dx));surface.Y=Math.Max(0,Math.Min(surface.Height-1,surface.Y+dy));}
                else surface.Scroll=Math.Max(0,Math.Min(1000,surface.Scroll+dy));
            }else if(action.StartsWith("Wheel delta="))surface.Scroll=Math.Max(0,Math.Min(1000,surface.Scroll-int.Parse(action.Substring(12))/3));
        }
        if(edge){cadence.Reset();}
        else if(!touch || moved<0.4 || surface.Down){cadence.ResetDistance();feedback.ClearMotion();}
        else if(cadence.Step(clock.ElapsedMilliseconds,moved,(int)interval.Value,(int)spacing.Value))Emit(false,false);
        surface.Invalidate();status.Text="实操中 · 右板压力 "+pressure+" / 点击阈值 3500 · 已点击 "+surface.Clicks+" 次 · 已发送反馈 "+feedback.Sent+" 次\n滑动和点击方案可随时切换；切换后先抬手再试。";
    }
    static void TestEffectiveConfig(){using(var form=new LiveLab()){form.motionPulse=new byte[]{0x81,0,0x90,1,0,0,1,0};form.motion.SelectedIndex=11;form.UpdateEffective();if(!form.effective.Text.Contains("400 μs") || form.EffectivePacket(false)[1]!=0)throw new Exception("Applied pulse visibility");form.motion.SelectedIndex=0;if(form.EffectivePacket(false)!=null)throw new Exception("Disabled feedback export");}}
    byte[] EffectivePacket(bool isClick){int index=(isClick?click:motion).SelectedIndex-1;if(index<0)return null;int db=(int)(isClick?clickGain:motionGain).Value;return index==10?CapturedPulse(isClick):index<2?Waves.Packet(index,db):Waves.Tone((int)(isClick?clickHz:motionHz).Value,(int)(isClick?clickMs:motionMs).Value,db);}
    static string Describe(byte[] p){if(p==null)return "关闭";if(p[0]==0x81)return "脉冲：通电 "+BitConverter.ToUInt16(p,2)+" μs，间隔 "+BitConverter.ToUInt16(p,4)+" μs，重复 "+BitConverter.ToUInt16(p,6)+" 次（不使用 Hz / dB）";if(p[0]==0x82)return "内置指令 "+p[2]+"，"+(sbyte)p[3]+" dB（不使用 Hz / 时长）";return "音调："+BitConverter.ToUInt16(p,3)+" Hz，"+BitConverter.ToUInt16(p,5)+" ms，"+(sbyte)p[2]+" dB";}
    void UpdateEffective(){if(motion.Items.Count==0 || click.Items.Count==0)return;effective.Text="当前滑动 · "+Describe(EffectivePacket(false))+"\n当前点击 · "+Describe(EffectivePacket(true));}
    void Save()
    {
        try{
            byte[] mp=EffectivePacket(false),cp=EffectivePacket(true);
            string root=AppDomain.CurrentDomain.BaseDirectory;
            var data=new {version=1,saved_at=DateTime.Now.ToString("o"),motion=new {description=Describe(mp),packet_hex=mp==null?null:BitConverter.ToString(mp)},click=new {description=Describe(cp),packet_hex=cp==null?null:BitConverter.ToString(cp)},interval_ms=interval.Value,spacing_px=spacing.Value,release_feedback=release.Checked,mode=mode.Text};
            File.WriteAllText(Path.Combine(root,"当前触觉配置.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data));
            File.WriteAllText(Path.Combine(root,"实操选择.txt"),effective.Text+"\r\n滑动最短间隔："+interval.Value+" ms\r\n滑动触发距离："+spacing.Value+" px\r\n松开反馈："+release.Checked+"\r\n滑动报文："+(mp==null?"关闭":BitConverter.ToString(mp))+"\r\n点击报文："+(cp==null?"关闭":BitConverter.ToString(cp)));
            status.Text="已保存：当前触觉配置.json 和 实操选择.txt（实际生效参数，不含灰色旧值）。";
        }catch(Exception e){status.Text="保存失败："+e.Message;}
    }
}

internal sealed class GestureGate
{
    long last=-1000;double distance;
    internal void Reset(){last=-1000;distance=0;}
    internal void ResetDistance(){distance=0;}
    internal bool Step(long now,double moved,int interval,int spacing){if(moved<=0){distance=0;return false;}distance+=moved;if(now-last<interval || distance<spacing)return false;last=now;distance=0;return true;}
    internal static void Test(){var g=new GestureGate();if(!g.Step(0,12,60,12) || g.Step(10,40,60,12) || g.Step(70,0,60,12) || g.Step(80,1,60,12))throw new Exception("Cadence stationary/limit");g.ResetDistance();if(g.Step(100,1,60,12))throw new Exception("Lift clears backlog");}
}
internal sealed class TestSurface : Control
{
    internal int X=400,Y=130,Scroll,Clicks;
    internal bool Down;
    readonly HashSet<int> selected=new HashSet<int>();
    internal TestSurface(){DoubleBuffered=true;BackColor=Color.FromArgb(245,247,250);}
    internal void ClickAtCursor(){Clicks++;int row=(Y+Scroll)/65;if(selected.Contains(row))selected.Remove(row);else selected.Add(row);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);for(int row=0;row<22;row++){int y=row*65-Scroll;if(y<-65 || y>Height)continue;e.Graphics.FillRectangle(selected.Contains(row)?Brushes.LightGreen:Brushes.White,12,y+5,Width-24,55);e.Graphics.DrawString("测试卡片 "+(row+1)+"    滑动查看 · 按压选中",Font,Brushes.DimGray,25,y+22);}e.Graphics.FillEllipse(Down?Brushes.OrangeRed:Brushes.DodgerBlue,X-9,Y-9,18,18);e.Graphics.DrawEllipse(Pens.White,X-9,Y-9,18,18);}
}
