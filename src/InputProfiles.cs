using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

public sealed class BindingAction {
 public string Kind {get;set;} public string Chord {get;set;}
 public List<MacroStroke> Steps {get;set;} public int Repeat {get;set;} public bool WhileHeld {get;set;}
 public BindingAction(){Kind="none";Chord="";Steps=new List<MacroStroke>();Repeat=1;}
 public void Validate(){if(Kind!="none" && Kind!="keyboard" && Kind!="mouse" && Kind!="osk" && Kind!="gamepad" && Kind!="macro")throw new Exception("未知按键动作。");if(Kind=="gamepad" && !InputCatalog.Gamepad.Contains(Chord))throw new Exception("请选择手柄输出。");if(Kind=="keyboard")InputCatalog.Parse(Chord);if(Kind=="mouse" && !InputCatalog.Mouse.Contains(Chord))throw new Exception("请选择鼠标动作。");if(Kind=="macro"){if(Steps==null || Steps.Count<1 || Steps.Count>64 || Repeat<1 || Repeat>100)throw new Exception("宏需要 1–64 步，重复 1–100 次。");foreach(var s in Steps)s.Validate();if(Steps.Sum(s=>(long)s.HoldMs+s.WaitMs)*Repeat>600000)throw new Exception("单次宏总时长最多 10 分钟。");}}
 public override string ToString(){return Kind=="none"?"不映射":Kind=="osk"?"显示／隐藏屏幕键盘":Kind=="macro"?"宏 · "+Steps.Count+" 步 · "+(WhileHeld?"按住连发":Repeat+" 次"):Kind=="mouse"?InputCatalog.MouseLabel(Chord):Chord;}
}
public sealed class MacroStroke {
 public string Kind {get;set;} public string Chord {get;set;} public int HoldMs {get;set;} public int WaitMs {get;set;}
 public MacroStroke(){Kind="keyboard";Chord="Space";HoldMs=60;WaitMs=80;}
 public void Validate(){if(HoldMs<20 || HoldMs>10000 || WaitMs<20 || WaitMs>10000)throw new Exception("按住时间和间隔范围均为 20–10000 ms。");if(Kind=="gamepad" && !InputCatalog.Gamepad.Contains(Chord))throw new Exception("请选择手柄输出。");if(Kind=="keyboard")InputCatalog.Parse(Chord);else if(Kind=="gamepad"){if(!InputCatalog.Gamepad.Contains(Chord))throw new Exception("未知手柄输出。");}else if(Kind!="mouse" || !InputCatalog.Mouse.Contains(Chord))throw new Exception("宏步骤请选择键盘或鼠标动作。");}
}
public static class InputCatalog {
 public static readonly string[] Buttons={"A","B","X","Y","LB","RB","LT","RT","VIEW","MENU","L3","R3","UP","DOWN","LEFT","RIGHT","L4","L5","R4","R5","LG","RG","QAM"};
 static readonly int[] Bits={0,1,2,3,19,9,27,23,6,14,15,5,13,10,12,11,17,18,7,8,29,28,4};
 public static uint Mask(string button){int i=Array.IndexOf(Buttons,button);return i<0?0:1u<<Bits[i];}
 public static readonly string[] Gamepad={"A","B","X","Y","LB","RB","LT","RT","VIEW","MENU","L3","R3","UP","DOWN","LEFT","RIGHT"};
 public static readonly string[] Mouse={"left","right","middle","x1","x2","wheelUp","wheelDown"};
 public static string MouseLabel(string s){int i=Array.IndexOf(Mouse,s);return i<0?s:new[]{"鼠标左键","鼠标右键","鼠标中键","鼠标侧键 1","鼠标侧键 2","滚轮向上","滚轮向下"}[i];}
 public static ulong SourceMask(string button){if(button=="LPRESS")return 1UL<<26;if(button=="RPRESS")return 1UL<<22;string[] dirs={"LP_UP","LP_DOWN","LP_LEFT","LP_RIGHT","RP_UP","RP_DOWN","RP_LEFT","RP_RIGHT","LS_UP","LS_DOWN","LS_LEFT","LS_RIGHT","RS_UP","RS_DOWN","RS_LEFT","RS_RIGHT"};int n=Array.IndexOf(dirs,button);return n>=0?1UL<<(32+n):Mask(button);}
 public static string ButtonLabel(string b){var labels=new Dictionary<string,string>{{"LB","LB / L1 · 左肩键"},{"RB","RB / R1 · 右肩键"},{"LT","LT / L2 · 左扳机到底"},{"RT","RT / R2 · 右扳机到底"},{"L4","L4 · 左上背键"},{"L5","L5 · 左下背键"},{"R4","R4 · 右上背键"},{"R5","R5 · 右下背键"},{"LG","LG · 左握把触摸"},{"RG","RG · 右握把触摸"},{"L3","L3 · 左摇杆按下"},{"R3","R3 · 右摇杆按下"},{"UP","十字键 · 上"},{"DOWN","十字键 · 下"},{"LEFT","十字键 · 左"},{"RIGHT","十字键 · 右"},{"VIEW","视图 / 选择键"},{"MENU","菜单 / 开始键"},{"QAM","… · 快捷菜单键"},{"LPRESS","左触摸板 · 按压"},{"RPRESS","右触摸板 · 按压"}};string name;if(labels.TryGetValue(b,out name))return name;if(b.StartsWith("LS_") || b.StartsWith("RS_"))return (b.StartsWith("LS_")?"左摇杆":"右摇杆")+" · "+ButtonLabel(b.Substring(3));if(b.StartsWith("LP_") || b.StartsWith("RP_"))return (b.StartsWith("LP_")?"左触摸板":"右触摸板")+" · "+ButtonLabel(b.Substring(3));return b;}

 public static readonly Dictionary<string,int> Keys=MakeKeys();
 static Dictionary<string,int> MakeKeys(){var d=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"Ctrl",0xA2},{"Alt",0xA4},{"Shift",0xA0},{"Win",0x5B},{"RightCtrl",0xA3},{"RightAlt",0xA5},{"RightShift",0xA1},{"RightWin",0x5C},{"Space",32},{"Enter",13},{"Tab",9},{"Escape",27},{"Backspace",8},{"Delete",46},{"Insert",45},{"Home",36},{"End",35},{"PageUp",33},{"PageDown",34},{"Up",38},{"Down",40},{"Left",37},{"Right",39},{"CapsLock",20},{"Plus",187},{"Minus",189},{"Comma",188},{"Period",190},{"Slash",191},{"Backslash",220},{"Semicolon",186},{"Quote",222},{"LeftBracket",219},{"RightBracket",221},{"Backtick",192}};for(int i=65;i<=90;i++)d[((char)i).ToString()]=i;for(int i=48;i<=57;i++)d[((char)i).ToString()]=i;for(int i=1;i<=24;i++)d["F"+i]=111+i;for(int i=0;i<=9;i++)d["Num"+i]=96+i;d["NumMultiply"]=106;d["NumAdd"]=107;d["NumSubtract"]=109;d["NumDecimal"]=110;d["NumDivide"]=111;d["NumLock"]=144;d["PrintScreen"]=44;d["ScrollLock"]=145;d["Pause"]=19;return d;}
 public static int[] Parse(string text){if(string.IsNullOrWhiteSpace(text))throw new Exception("请选择按键，例如 Space 或 Win+D。");var keys=new List<int>();foreach(string token in text.Split('+')){int k;if(!Keys.TryGetValue(token.Trim(),out k))throw new Exception("无法识别按键："+token+"。可用 Win、Ctrl、Alt、Shift、A–Z、F1–F24 等。");if(!keys.Contains(k))keys.Add(k);}if(keys.Count>8)throw new Exception("组合键最多 8 键。");return keys.OrderBy(k=>IsModifier(k)?0:1).ToArray();}
 public static bool IsModifier(int k){return k==0x5B || k==0x5C || (k>=0xA0 && k<=0xA5);}
}
public sealed class AppAssignment {
 public string Path {get;set;} public string PresetId {get;set;} public string AppliedName {get;set;} public PadBook Applied {get;set;}
 public override string ToString(){return System.IO.Path.GetFileNameWithoutExtension(Path);}
}
public sealed class RuntimeProfiles {
 public List<AppAssignment> Apps {get;set;}
 public RuntimeProfiles(){Apps=new List<AppAssignment>();}
 public PadBook Select(string path,PadBook fallback){var a=Apps.FirstOrDefault(x=>string.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase));return a==null || a.Applied==null?fallback:a.Applied;}
 public void Validate(){if(Apps==null || Apps.Count>200)throw new Exception("应用配置最多 200 个。");var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var a in Apps){if(a==null || !System.IO.Path.IsPathRooted(a.Path??"") || a.Path.IndexOfAny(new[]{'\r','\n'})>=0 || !a.Path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || !paths.Add(a.Path))throw new Exception("应用路径无效或重复。");if(a.Applied!=null)a.Applied.Validate();}}
 public static RuntimeProfiles Load(string p){if(new FileInfo(p).Length>8388608)throw new Exception("应用配置过大。");var b=new JavaScriptSerializer{MaxJsonLength=8388608}.Deserialize<RuntimeProfiles>(File.ReadAllText(p));if(b==null)throw new Exception("应用配置为空。");b.Validate();return b;}
}
