using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class UpdateRelease
{
 internal string Version,Url,Digest;
 internal long Size;
 internal static UpdateRelease Parse(string json,string current){
  var releases=new JavaScriptSerializer().DeserializeObject(json) as object[];
  if(releases==null)throw new Exception(L.T("更新信息格式无效"));
  System.Version newest=System.Version.Parse(current);UpdateRelease selected=null;
  foreach(var item in releases){
   var release=item as Dictionary<string,object>;if(release==null || Convert.ToBoolean(release["draft"]))continue;
   string tag=Convert.ToString(release["tag_name"]);if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$"))continue;
   System.Version version;if(!System.Version.TryParse(tag.Substring(1),out version) || version<=newest)continue;
   foreach(var value in (IEnumerable)release["assets"]){var asset=value as Dictionary<string,object>;if(asset==null || Convert.ToString(asset["name"])!="install.exe")continue;
    string url=Convert.ToString(asset["browser_download_url"]),digest=asset.ContainsKey("digest")?Convert.ToString(asset["digest"]):"";
    long size=Convert.ToInt64(asset["size"]);
    if(url!="https://github.com/csvwolf/padhop/releases/download/"+tag+"/install.exe" || !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$") || size<1 || size>134217728)continue;
    selected=new UpdateRelease{Version=tag.Substring(1),Url=url,Digest=digest.Substring(7),Size=size};newest=version;
   }
  }
  return selected;
 }
 internal static UpdateRelease FindNewer(string current){return Parse(Encoding.UTF8.GetString(Fetch("https://api.github.com/repos/csvwolf/padhop/releases?per_page=30",2097152)),current);}
 static byte[] Fetch(string url,int limit){
  ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="PadHop-Updater";request.Timeout=30000;request.ReadWriteTimeout=30000;
  using(var response=(HttpWebResponse)request.GetResponse()){
   if(response.ResponseUri.Scheme!="https" || response.ContentLength>limit)throw new Exception(L.T("更新响应不符合要求"));
   using(var stream=response.GetResponseStream())using(var result=new MemoryStream()){
    var buffer=new byte[65536];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(result.Length+count>limit)throw new Exception(L.T("更新文件超过大小限制"));result.Write(buffer,0,count);}return result.ToArray();
   }
  }
 }
 internal string Download(string folder){
  Directory.CreateDirectory(folder);string path=Path.Combine(folder,"install-"+Version+".exe");
  byte[] bytes=Fetch(Url,(int)Size);if(bytes.LongLength!=Size)throw new Exception(L.T("更新文件下载不完整"));
  using(var hash=SHA256.Create()){if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=Digest.ToLowerInvariant())throw new Exception(L.T("更新文件校验失败"));}
  File.WriteAllBytes(path,bytes);return path;
 }
 internal void Verify(string path){if(new FileInfo(path).Length!=Size)throw new Exception(L.T("更新文件已改变，请重新检查更新"));using(var stream=File.OpenRead(path))using(var hash=SHA256.Create()){if(!string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",""),Digest,StringComparison.OrdinalIgnoreCase))throw new Exception(L.T("更新文件已改变，请重新检查更新"));}}
 internal static void Test(){
  string asset="{\"draft\":false,\"tag_name\":\"v9.0.0\",\"assets\":[{\"name\":\"install.exe\",\"size\":12,\"digest\":\"sha256:"+new string('a',64)+"\",\"browser_download_url\":\"https://github.com/csvwolf/padhop/releases/download/v9.0.0/install.exe\"}]}";
  if(Parse("["+asset+"]","1.0.0")==null || Parse("["+asset+"]","9.0.0")!=null || Parse("["+asset.Replace("github.com/csvwolf","github.com/other")+"]","1.0.0")!=null || Parse("["+asset.Replace("sha256:","invalid:")+"]","1.0.0")!=null)throw new Exception("Update provenance/version test");
 }
}
