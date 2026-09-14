using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Waves
{
    internal static readonly string[] Names = {
        "A · 短点振（已验证的基准）", "B · 内置点击（待实测）",
        "C · 160 Hz，20 毫秒", "D · 240 Hz，20 毫秒",
        "E · 320 Hz，20 毫秒", "F · 240 Hz，40 毫秒" };
    internal static byte[] Packet(int index, int gain)
    {
        if(index<0 || index>=Names.Length || gain < -30 || gain > -6)throw new ArgumentOutOfRangeException();
        if(index<2)return new byte[]{0x82,1,(byte)(index+1),unchecked((byte)gain)};
        int frequency=index==2?160:index==4?320:240;
        return new byte[]{0x83,1,unchecked((byte)gain),(byte)(frequency&255),(byte)(frequency>>8),
            (byte)(index==5?40:20),0,0,0,0};
    }
    internal static byte[] Tone(int frequency,int duration,int gain)
    {
        if(frequency<40 || frequency>500 || duration<1 || duration>80 || gain < -30 || gain > -6)throw new ArgumentOutOfRangeException();
        return new byte[]{0x83,1,unchecked((byte)gain),(byte)(frequency&255),(byte)(frequency>>8),(byte)duration,0,0,0,0};
    }
    internal static void Test()
    {
        for(int i=0;i<6;i++)for(int g=-30;g<=-6;g++) {
            byte[] p=Packet(i,g);
            if(p[1]!=1 || (sbyte)p[i<2?3:2]!=g)throw new Exception("Side/gain");
            if(i>=2 && (p.Length!=10 || p[5]>40 || p[5]==0 || p[6]!=0 || p[9]!=0))throw new Exception("Duration");
        }
        if(BitConverter.ToString(Packet(0,-12))!="82-01-01-F4")throw new Exception("Reference regression");
        if(BitConverter.ToString(Packet(4,-24))!="83-01-E8-40-01-14-00-00-00-00")throw new Exception("Tone encoding");
        bool rejected=false; try{Packet(6,-12);}catch(ArgumentOutOfRangeException){rejected=true;}
        if(!rejected)throw new Exception("Bounds");
        if(BitConverter.ToString(Tone(160,5,-15))!="83-01-F1-A0-00-05-00-00-00-00")throw new Exception("Custom tone encoding");
        if(BitConverter.ToString(Tone(500,80,-6))!="83-01-FA-F4-01-50-00-00-00-00")throw new Exception("Custom upper bounds");
        foreach(int duration in new int[]{0,81}){rejected=false;try{Tone(160,duration,-15);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new Exception("Duration bounds");}
        foreach(int hz in new int[]{39,501}){rejected=false;try{Tone(hz,20,-15);}catch(ArgumentOutOfRangeException){rejected=true;}if(!rejected)throw new Exception("Frequency bounds");}
    }
}

internal sealed class HapticLab : Form
{
    readonly ListBox choices=new ListBox();
    readonly TrackBar gain=new TrackBar();
    readonly Label strength=new Label(), status=new Label();
    readonly Button one=new Button(), all=new Button(), stop=new Button(), save=new Button();
    CancellationTokenSource cancel;
    bool closing;
    [STAThread] static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--self-test") {
            try{Waves.Test(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: packet encoding, right side, gain bounds, finite tone durations, reference regression.");return 0;}
            catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),e.ToString());return 1;}
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new HapticLab()); return 0;
    }
    HapticLab()
    {
        Text="SC2 · 右触摸板波形对比"; ClientSize=new Size(660,485);
        Font=new Font("Microsoft YaHei UI",10); FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        var hint=new Label{Text="把手指轻放在右触摸板上。每项只振一下，先找喜欢的质感。\n点击后留 2 秒准备；整轮每项间隔 3 秒。",AutoSize=false,Location=new Point(20,16),Size=new Size(620,54)};
        choices.SetBounds(20,80,620,170); choices.Items.AddRange(Waves.Names); choices.SelectedIndex=0;
        strength.SetBounds(20,265,620,25);
        gain.SetBounds(20,295,620,45); gain.Minimum=-30; gain.Maximum=-6; gain.Value=-12; gain.TickFrequency=6;
        gain.ValueChanged+=delegate{strength.Text="强度："+gain.Value+" dB（向右更强；只影响试听）";};
        strength.Text="强度：-12 dB（向右更强；只影响试听）";
        one.Text="试听所选"; all.Text="依次试听 A–F"; stop.Text="停止"; save.Text="保存所选";
        one.SetBounds(20,352,140,38); all.SetBounds(175,352,155,38); stop.SetBounds(345,352,130,38); save.SetBounds(490,352,150,38);
        stop.Enabled=false;
        status.SetBounds(20,405,620,65); status.Text="尚未发送。Steam 可以保持运行。\n这是单次波形对比；滑动跟手感还需要选定波形后单独调校。";
        Controls.AddRange(new Control[]{hint,choices,strength,gain,one,all,stop,save,status});
        one.Click+=async delegate{await Play(false);}; all.Click+=async delegate{await Play(true);};
        stop.Click+=delegate{if(cancel!=null)cancel.Cancel();};
        save.Click+=delegate {
            try {
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"我的选择.txt");
                File.WriteAllText(path,DateTime.Now.ToString("s")+Environment.NewLine+Waves.Names[choices.SelectedIndex]+Environment.NewLine+"gain_db="+gain.Value+Environment.NewLine+"packet="+BitConverter.ToString(Waves.Packet(choices.SelectedIndex,gain.Value)));
                status.Text="已保存："+path;
            }catch(Exception e){status.Text="保存失败："+e.Message;}
        };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(cancel!=null){e.Cancel=true;closing=true;cancel.Cancel();status.Text="正在停止…";}
        };
    }
    async Task Play(bool batch)
    {
        if(cancel!=null)return;
        int selected=choices.SelectedIndex, level=gain.Value;
        cancel=new CancellationTokenSource(); var token=cancel.Token;
        one.Enabled=all.Enabled=save.Enabled=choices.Enabled=gain.Enabled=false;stop.Enabled=true;
        try {
            int first=batch?0:selected, last=batch?5:selected;
            for(int i=first;i<=last;i++) {
                choices.SelectedIndex=i;
                status.Text="准备："+Waves.Names[i]+" · "+level+" dB";
                await Task.Delay(i==first?2000:3000,token);
                byte[] packet=Waves.Packet(i,level);
                await Task.Run(delegate{token.ThrowIfCancellationRequested();HapticTransport.Send(packet);},token);
                status.Text="已发送："+Waves.Names[i];
                await Task.Delay(250,token);
            }
            status.Text="试听结束。选中喜欢的一项，点“保存所选”。\n没感觉的候选可直接跳过，发送成功不代表设备一定实现该波形。";
        }catch(OperationCanceledException){status.Text="已停止后续试听。";}
        catch(Exception e){status.Text="测试已停止："+e.Message;}
        finally {
            cancel.Dispose();cancel=null;
            one.Enabled=all.Enabled=save.Enabled=choices.Enabled=gain.Enabled=true;stop.Enabled=false;
            if(closing)Close();
        }
    }
}
