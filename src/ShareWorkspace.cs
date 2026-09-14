using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
internal sealed partial class PadHop {
 static FullState ShareState(PadBook input,string name){var b=Clone(input);foreach(var p in new[]{b.Left,b.Right}){p.PresetId=null;p.Motion.Source=p.PressWave.Source=p.ReleaseWave.Source="Shared preset";}var preset=new FullPreset{Id="shared",Name=name,Pads=b};return new FullState{Presets=new List<FullPreset>{preset},EditingId="shared",GlobalEditingId="shared",Applied=null,AppliedId=null,AppliedName=null,Apps=new List<AppAssignment>(),Drafts=new Dictionary<string,PadBook>()};}
 void SharePreset(){ReadEditor();string name=AskName(L.T("分享配置名称（公开可见）"),L.T("分享配置"));if(name==null)return;ValidatePresetName(name);var d=new Microsoft.Win32.SaveFileDialog{Filter=L.T("PadHop 配置|*.json"),FileName="PadHop-preset.json"};if(d.ShowDialog(window)==true){AtomicWrite(d.FileName,new JavaScriptSerializer().Serialize(ShareState(padBook,name)));Notice(L.T("已导出按键与触觉配置，不含应用关联、设备选择和采集来源路径。"));}}
 static void TestShare(){var p=new PadBook();p.Left.Motion.Source="C:\\Users\\private\\capture";var json=new JavaScriptSerializer().Serialize(ShareState(p,"public"));if(json.Contains("private") || json.Contains("capture"))throw new Exception("Shared preset leaked source metadata");}
}
