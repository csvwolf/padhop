using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
internal sealed partial class PadHop {
 string CaptureTool(string key){string file=Path.Combine(AppPaths.Data,"capture-tools.json");if(!File.Exists(file))throw new Exception(L.T("录制需要可选的 Python 与微软蓝牙分析工具。请先运行安装包中的 configure-capture.ps1，详见 docs/capture.md。"));var d=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(file));string path;if(!d.TryGetValue(key,out path)||!Path.IsPathRooted(path)||!File.Exists(path))throw new Exception(L.T("采集工具路径不可用，请重新配置：")+key);return path;}
}
