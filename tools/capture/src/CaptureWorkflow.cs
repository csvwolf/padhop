using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class CapturedWave
{
    internal byte[] Packet;
    internal string Description,Reason;
    internal int SuggestedInterval;
    internal string Phase;
    internal bool Supported {get{return Reason==null;}}
    internal static CapturedWave Read(IDictionary<string,object> item)
    {
        var wave=new CapturedWave();string hex=Convert.ToString(item["payload_hex"]);
        if(hex.Length>256 || hex.Length%2!=0)throw new Exception("Invalid payload length");
        byte report=Convert.ToByte(Convert.ToString(item["report_id"]).Replace("0x",""),16);
        byte[] p=new byte[hex.Length/2+1];p[0]=report;for(int i=1;i<p.Length;i++)p[i]=Convert.ToByte(hex.Substring((i-1)*2,2),16);
        if(p.Length<2 || p[1]!=(report==0x81?1:0))throw new Exception("Expected report-specific left trackpad report");
        int length=report==0x81?8:report==0x82?4:report==0x83?10:report==0x84?9:report==0x85?4:0;
        if(length==0 || p.Length<length)throw new Exception("Unknown or truncated report");
        for(int i=length;i<p.Length;i++)if(p[i]!=0)throw new Exception("Nonzero trailing bytes");
        Array.Resize(ref p,length);p[1]=(byte)(report==0x81?0:1);wave.Packet=p;
        if(report==0x81){int on=BitConverter.ToUInt16(p,2),off=BitConverter.ToUInt16(p,4),count=BitConverter.ToUInt16(p,6);
            if(on<1 || on>5000 || off>5000 || count<1 || count>100 || ((long)on+off)*count>80000)wave.Reason="脉冲超出本测试器的短反馈范围";
            wave.Description="脉冲：通电 "+on+" μs / 间隔 "+off+" μs / "+count+" 次";
        }else if(report==0x82){if((p[2]!=1 && p[2]!=2) || (sbyte)p[3]<-30 || (sbyte)p[3]>-6)wave.Reason="内置指令或增益暂不支持应用";wave.Description="内置 "+(p[2]==1?"点振":p[2]==2?"点击":"指令 "+p[2])+" / "+(sbyte)p[3]+" dB";
        }else if(report==0x83){int hz=BitConverter.ToUInt16(p,3),ms=BitConverter.ToUInt16(p,5);if(hz<40 || hz>500 || ms<1 || ms>80 || (sbyte)p[2]<-30 || (sbyte)p[2]>-6 || BitConverter.ToUInt16(p,7)!=0 || p[9]!=0)wave.Reason="音调参数或调制暂不支持应用";wave.Description="音调："+hz+" Hz / "+ms+" ms / "+(sbyte)p[2]+" dB";
        }else {wave.Reason="此类扫频/脚本尚未验证回放；保留采集数据";wave.Description=report==0x84?"扫频":"固件脚本";}
        if(item.ContainsKey("suggested_interval_ms"))wave.SuggestedInterval=Convert.ToInt32(item["suggested_interval_ms"]);
        string phase=item.ContainsKey("phase")?Convert.ToString(item["phase"]):"unclassified";
        wave.Phase=phase;
        phase=phase=="slow_slide"?"慢滑":phase=="fast_slide"?"快滑":phase=="press_release"?"按压/松开":phase=="idle" || phase=="idle_end"?"静止":"未归类";
        wave.Description=phase+" · "+wave.Description+" · "+Convert.ToString(item["count"])+" 条"+(wave.Supported?"":"（仅查看）");return wave;
    }
    public override string ToString(){return Description;}
    internal static void Test(){var item=new Dictionary<string,object>{{"payload_hex","00f1a0000500000000"},{"report_id","0x83"},{"count",3}};var w=Read(item);if(!w.Supported || w.Packet[1]!=1 || w.Packet[5]!=5)throw new Exception("Capture tone import");item["payload_hex"]="0003f1";item["report_id"]="0x82";if(Read(item).Supported)throw new Exception("Unsupported command imported");item["payload_hex"]="016400c8000300";item["report_id"]="0x81";if(!Read(item).Supported)throw new Exception("Pulse import");item["payload_hex"]="006400c8000300";bool reject=false;try{Read(item);}catch{reject=true;}if(!reject)throw new Exception("Right capture accepted as left");}
}

internal sealed partial class LiveLab
{
    TabControl pages;
    Capture capturePage;
    readonly ListBox captured=new ListBox();
    readonly Label captureStatus=new Label();
    readonly CheckBox useRhythm=new CheckBox(),showIdle=new CheckBox();
    readonly List<CapturedWave> allCaptured=new List<CapturedWave>();
    readonly Button applyMotion=new Button(),applyClick=new Button(),undo=new Button(),pulseEdit=new Button();
    bool analyzing;
    byte[] motionPulse,clickPulse;
    Action restore;
    void BuildWorkflow()
    {
        pages=new TabControl{Dock=DockStyle.Fill};var experience=new TabPage("右板体验与微调");var recording=new TabPage("左板采集 → 应用");
        experience.AutoScroll=true;recording.AutoScroll=true;
        Control[] existing=new Control[Controls.Count];Controls.CopyTo(existing,0);Controls.Clear();experience.Controls.AddRange(existing);
        pages.TabPages.Add(experience);pages.TabPages.Add(recording);Controls.Add(pages);
        capturePage=new Capture{TopLevel=false,FormBorderStyle=FormBorderStyle.None};capturePage.SetBounds(0,0,900,275);recording.Controls.Add(capturePage);capturePage.Show();
        capturePage.Beginning=delegate{if(analyzing)throw new Exception("请等待上一次分析结束。");Stop();captured.Items.Clear();allCaptured.Clear();applyMotion.Enabled=applyClick.Enabled=pulseEdit.Enabled=false;};
        capturePage.SessionCompleted=async delegate(string folder){await AnalyzeSession(folder);};
        captureStatus.SetBounds(18,285,875,65);captureStatus.Text="采集结束后自动解析。成功后选一项，应用到右板滑动或点击，再回体验页微调。";recording.Controls.Add(captureStatus);
        captured.SetBounds(18,355,875,170);recording.Controls.Add(captured);
        applyMotion.Text="应用并体验右板滑动";applyClick.Text="应用并体验右板点击";undo.Text="恢复应用前";
        applyMotion.SetBounds(18,540,210,38);applyClick.SetBounds(245,540,210,38);undo.SetBounds(472,540,170,38);
        applyMotion.Enabled=applyClick.Enabled=undo.Enabled=false;recording.Controls.AddRange(new Control[]{applyMotion,applyClick,undo});
        useRhythm.Text="同时采用采集的间隔（估计值；移动距离保留原设置）";useRhythm.SetBounds(18,590,800,32);recording.Controls.Add(useRhythm);
        pulseEdit.Text="微调所选脉冲";pulseEdit.SetBounds(660,540,230,38);pulseEdit.Enabled=false;recording.Controls.Add(pulseEdit);
        showIdle.Text="显示静止 / 准备 / 未归类记录";showIdle.SetBounds(290,638,560,36);recording.Controls.Add(showIdle);showIdle.CheckedChanged+=delegate{FilterCandidates();};
        var reopen=new Button{Text="打开已有采集结果"};reopen.SetBounds(18,638,240,36);recording.Controls.Add(reopen);
        reopen.Click+=delegate{if(analyzing || capturePage.IsBusy)return;using(var dialog=new OpenFileDialog{Filter="采集结果|haptics.json"})if(dialog.ShowDialog(this)==DialogResult.OK)try{LoadResults(dialog.FileName);}catch(Exception e){captureStatus.Text=e.Message;}};
        captured.SelectedIndexChanged+=delegate{var w=captured.SelectedItem as CapturedWave;applyMotion.Enabled=applyClick.Enabled=w!=null && w.Supported;pulseEdit.Enabled=w!=null && w.Supported && w.Packet[0]==0x81;};
        applyMotion.Click+=delegate{ApplyCaptured(false);};applyClick.Click+=delegate{ApplyCaptured(true);};
        undo.Click+=delegate{if(restore!=null){Stop();restore();ResetInput();pages.SelectedIndex=0;status.Text="已恢复应用前的设置。";restore=null;undo.Enabled=false;}};
        pulseEdit.Click+=delegate{EditPulse();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(capturePage.IsBusy){e.Cancel=true;capturePage.RequestStop();pages.SelectedIndex=1;}else if(analyzing){e.Cancel=true;captureStatus.Text="正在分析，完成后可关闭。";}};
    }
    async Task AnalyzeSession(string folder)
    {
        if(!File.Exists(Path.Combine(folder,"bluetooth.etl")) || !File.Exists(Path.Combine(folder,"left-input.csv"))){captureStatus.Text="采集文件不完整，无法应用。";return;}
        analyzing=true;applyMotion.Enabled=applyClick.Enabled=false;captured.Items.Clear();captureStatus.Text="采集结束，正在自动识别 SC2 的震动通道并解析…";
        try {
            string root=AppDomain.CurrentDomain.BaseDirectory,python=File.ReadAllText(Path.Combine(root,"tools","python-path.txt")).Trim();
            if(!File.Exists(python))throw new Exception("分析运行环境缺失，尚不能自动解析。");
            await Task.Run(delegate{var psi=new ProcessStartInfo(python,"\""+Path.Combine(root,"auto_capture.py")+"\" \""+folder+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};using(var process=Process.Start(psi)){if(!process.WaitForExit(120000)){process.Kill();throw new Exception("分析超时，原始日志已保留。");}}});
            LoadResults(Path.Combine(folder,"haptics.json"));
        }catch(Exception e){captureStatus.Text="未能取得可应用的结果："+e.Message;}
        finally{analyzing=false;}
    }
    void LoadResults(string path)
    {
        if(new FileInfo(path).Length>16*1024*1024)throw new Exception("采集结果过大，原始日志保留，需单独分析。");
        var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};var data=serializer.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
        captured.Items.Clear();allCaptured.Clear();applyMotion.Enabled=applyClick.Enabled=false;pulseEdit.Enabled=false;
        if(data==null || !data.ContainsKey("status") || Convert.ToString(data["status"])!="decoded"){captureStatus.Text=data!=null && data.ContainsKey("message")?Convert.ToString(data["message"]):"还没有确认的左板震动数据。";return;}
        var list=data["candidates"] as object[];if(list==null || list.Length>100)throw new Exception("采集结果格式不正确。");
        foreach(object value in list)allCaptured.Add(CapturedWave.Read((IDictionary<string,object>)value));
        FilterCandidates();
        captureStatus.Text+="\n应用脉冲后，实际参数显示在体验页下方；Hz / dB 不适用于该脉冲。";
        if(captured.Items.Count>0)captured.SelectedIndex=0;
    }
    void FilterCandidates(){captured.Items.Clear();foreach(var w in allCaptured)if(showIdle.Checked || w.Phase=="slow_slide" || w.Phase=="fast_slide" || w.Phase=="press_release")captured.Items.Add(w);if(captured.Items.Count>0)captured.SelectedIndex=0;captureStatus.Text="显示 "+captured.Items.Count+" / "+allCaptured.Count+" 组；默认隐藏静止、准备及未归类记录，原始数据仍保留。";}
    byte[] CapturedPulse(bool isClick){byte[] bytes=isClick?clickPulse:motionPulse;if(bytes==null)throw new Exception("尚未应用采集脉冲，请先选其他方案。");return (byte[])bytes.Clone();}
    void ApplyCaptured(bool isClick)
    {
        if(capturePage.IsBusy || analyzing)return;
        var wave=captured.SelectedItem as CapturedWave;if(wave==null || !wave.Supported)return;
        Stop();
        int mi=motion.SelectedIndex,ci=click.SelectedIndex;decimal mg=motionGain.Value,cg=clickGain.Value,mh=motionHz.Value,ch=clickHz.Value,mm=motionMs.Value,cm=clickMs.Value,iv=interval.Value,sp=spacing.Value;bool rel=release.Checked;byte[] mp=motionPulse,cp=clickPulse;
        restore=delegate{motionPulse=mp;clickPulse=cp;applyingPreset=true;try{motion.SelectedIndex=mi;click.SelectedIndex=ci;motionGain.Value=mg;clickGain.Value=cg;motionHz.Value=mh;clickHz.Value=ch;motionMs.Value=mm;clickMs.Value=cm;interval.Value=iv;spacing.Value=sp;release.Checked=rel;}finally{applyingPreset=false;}RefreshCapturedControls(false);RefreshCapturedControls(true);};undo.Enabled=true;
        byte[] p=wave.Packet;ComboBox choice=isClick?click:motion;NumericUpDown gain=isClick?clickGain:motionGain,hz=isClick?clickHz:motionHz,ms=isClick?clickMs:motionMs;
        applyingPreset=true;
        try {
            if(p[0]==0x81){if(isClick)clickPulse=(byte[])p.Clone();else motionPulse=(byte[])p.Clone();choice.SelectedIndex=11;}
            else if(p[0]==0x82){choice.SelectedIndex=p[2];gain.Value=(sbyte)p[3];}
            else {choice.SelectedIndex=10;gain.Value=(sbyte)p[2];hz.Value=BitConverter.ToUInt16(p,3);ms.Value=BitConverter.ToUInt16(p,5);}
            if(!isClick && useRhythm.Checked && wave.SuggestedInterval>=30 && wave.SuggestedInterval<=200)interval.Value=wave.SuggestedInterval;
            if(isClick)release.Checked=false; // One observed template does not establish a release waveform.
        }finally{applyingPreset=false;}
        RefreshCapturedControls(isClick);ResetInput();pages.SelectedIndex=0;Start();
        if(active)status.Text="已应用并开始右板"+(isClick?"点击":"滑动")+"体验。先抬手再操作；可微调，也可在采集页恢复。";
    }
    void RefreshCapturedControls(bool isClick){int n=(isClick?click:motion).SelectedIndex;(isClick?clickHz:motionHz).Enabled=(isClick?clickMs:motionMs).Enabled=n>=3 && n<=10;(isClick?clickGain:motionGain).Enabled=n!=11;}
    void EditPulse()
    {
        var w=captured.SelectedItem as CapturedWave;if(w==null || w.Packet[0]!=0x81)return;
        using(var f=new Form{Text="脉冲细调（仍需点击应用）",ClientSize=new Size(410,200),StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}){
            var values=new NumericUpDown[3];string[] names={"通电时长 μs","关闭间隔 μs","重复次数"};
            for(int i=0;i<3;i++){var l=new Label{Text=names[i],Left=18,Top=20+i*40,Width=180};var n=new NumericUpDown{Left=205,Top=18+i*40,Minimum=i==1?0:1,Maximum=i==2?100:5000,Value=BitConverter.ToUInt16(w.Packet,2+i*2)};values[i]=n;f.Controls.Add(l);f.Controls.Add(n);}
            var ok=new Button{Text="保存候选",Left=205,Top=150,Width=130};f.Controls.Add(ok);ok.Click+=delegate{if((values[0].Value+values[1].Value)*values[2].Value>80000){MessageBox.Show(f,"总时长需不超过 80 毫秒。");return;}byte[] p=(byte[])w.Packet.Clone();for(int i=0;i<3;i++)Array.Copy(BitConverter.GetBytes((ushort)values[i].Value),0,p,2+i*2,2);var edited=new CapturedWave{Packet=p,Description="自定义脉冲 · "+values[0].Value+" / "+values[1].Value+" μs × "+values[2].Value,SuggestedInterval=w.SuggestedInterval};captured.Items.Add(edited);captured.SelectedItem=edited;f.Close();};f.ShowDialog(this);
        }
    }
}
