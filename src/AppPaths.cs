using System;
using System.IO;
using System.Linq;
internal static class AppPaths {
 internal static readonly string Install=AppDomain.CurrentDomain.BaseDirectory;
 internal static string Data;
 internal static void Initialize(bool isolated){Data=isolated?Path.Combine(Path.GetTempPath(),"PadHop-tests-"+Guid.NewGuid().ToString("N")):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PadHop");Directory.CreateDirectory(Data);Directory.CreateDirectory(Path.Combine(Data,"logs"));if(!isolated)PruneLogs();}
 internal static void PruneLogs(){var files=new DirectoryInfo(Path.Combine(Data,"logs")).GetFiles().OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();long bytes=0;foreach(var f in files){bytes+=f.Length;if(f.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7) || bytes>20*1024*1024)try{if((f.Attributes&FileAttributes.ReparsePoint)==0)f.Delete();}catch(IOException){}}}
 internal static string Engine{get{return Path.Combine(Install,"PadHop.Engine.exe");}}
 internal static string Capture{get{return Path.Combine(Install,"capture");}}
}
