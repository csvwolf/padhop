using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

internal sealed partial class PadHop
{
 StackPanel signingHost;
 TextBlock updateStatus;
 Button checkUpdate,installUpdate;
 CheckBox autoUpdate;
 bool checkingUpdate;
 DateTime nextUpdateCheck=DateTime.MinValue;
 UpdateRelease pendingUpdate;
 string pendingInstaller;
 static string ProductVersion {get{return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(PadHop).Assembly,typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;}}
 StackPanel AboutCard(string title){var p=new StackPanel();p.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});Get<StackPanel>("AboutPage").Children.Add(new Border{Style=(Style)window.FindResource("Card"),Child=p});return p;}
 Button AboutButton(Panel host,string text,Action action){var b=new Button{Content=text,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,10,0)};b.Click+=delegate{Guard(action);};host.Children.Add(b);return b;}
 void BuildAbout(bool live){
  var brand=AboutCard("PadHop · 跃控");
  brand.Children.Add(new TextBlock{Text="让 Steam Controller 2，在更多地方好用。",Margin=new Thickness(0,0,0,8)});
  brand.Children.Add(new TextBlock{Text="版本 "+ProductVersion+"  ·  实验版通道  ·  MIT 开源",Style=(Style)window.FindResource("Caption")});
  var links=new WrapPanel{Margin=new Thickness(0,16,0,0)};brand.Children.Add(links);
  AboutButton(links,"GitHub ↗",delegate{OpenExternal("https://github.com/csvwolf/padhop");});
  AboutButton(links,"作者博客 ↗",delegate{OpenExternal("https://www.codesky.me/");});
  AboutButton(links,"微博 ↗",delegate{OpenExternal("https://www.weibo.com/dreamit");});
  var updates=AboutCard("软件更新");
  updateStatus=new TextBlock{Text="尚未检查更新。更新来源：GitHub 官方项目 Release。",Margin=new Thickness(0,0,0,12)};updates.Children.Add(updateStatus);
  var actions=new WrapPanel();updates.Children.Add(actions);
  checkUpdate=AboutButton(actions,"检查更新",delegate{CheckUpdates(false);});
  installUpdate=AboutButton(actions,"安装更新",InstallUpdate);installUpdate.Visibility=Visibility.Collapsed;
  autoUpdate=new CheckBox{Content="自动更新（检查并下载，安装前确认）",IsChecked=File.Exists(Path.Combine(AppPaths.Data,"automatic-updates.enabled"))};updates.Children.Add(autoUpdate);
  autoUpdate.Checked+=delegate{if(live){AtomicWrite(Path.Combine(AppPaths.Data,"automatic-updates.enabled"),"enabled");nextUpdateCheck=DateTime.MinValue;CheckUpdates(true);}};
  autoUpdate.Unchecked+=delegate{if(live){string f=Path.Combine(AppPaths.Data,"automatic-updates.enabled");if(File.Exists(f))File.Delete(f);updateStatus.Text="已关闭自动更新。可以手动检查；不会自动安装。";}};
  updates.Children.Add(new TextBlock{Text="开启后，运行期间每天检查一次。安装需要管理员确认，自签选项由安装向导确认。",Style=(Style)window.FindResource("Caption")});
  signingHost=AboutCard("本机签名");
  signingHost.Children.Add(new TextBlock{Text="未启用本机自签。需要操作管理员窗口时，可重新运行 install.exe 勾选该组件。",Style=(Style)window.FindResource("Caption")});
  var diagnostics=AboutCard("诊断与日志");var logs=new WrapPanel();diagnostics.Children.Add(logs);
  AboutButton(logs,"打开日志文件夹",delegate{string dir=Path.Combine(AppPaths.Data,"logs");Directory.CreateDirectory(dir);Process.Start(new ProcessStartInfo("explorer.exe",Quote(dir)){UseShellExecute=true});});
  AboutButton(logs,"打开最近日志",delegate{var files=Directory.GetFiles(AppPaths.Data,"*.txt",SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(Path.Combine(AppPaths.Data,"logs"),"*.txt")).Concat(Directory.GetFiles(Path.Combine(AppPaths.Data,"logs"),"*.log")).Select(f=>new FileInfo(f)).OrderByDescending(f=>f.LastWriteTimeUtc).ToList();if(files.Count==0){Notice("还没有日志。复现问题后再打开。");return;}Process.Start(new ProcessStartInfo("notepad.exe",Quote(files[0].FullName)){UseShellExecute=true});});
  diagnostics.Children.Add(new TextBlock{Text="日志可能包含应用路径和设备标识，分享前可以先查看；不会自动上传。",Style=(Style)window.FindResource("Caption")});
  if(live)window.Loaded+=delegate{PollUpdates();};
 }
 static void OpenExternal(string url){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
 void PollUpdates(){if(autoUpdate!=null && autoUpdate.IsChecked==true && DateTime.UtcNow>=nextUpdateCheck)CheckUpdates(true);}
 async void CheckUpdates(bool automatic){
  if(checkingUpdate)return;checkingUpdate=true;nextUpdateCheck=DateTime.UtcNow.AddDays(1);checkUpdate.IsEnabled=false;
  try{
   updateStatus.Text="正在检查更新…";
   var release=await Task.Run(()=>UpdateRelease.FindNewer(ProductVersion));
   if(release==null){updateStatus.Text="当前已是最新版本（"+ProductVersion+"）。";return;}
   if(automatic && autoUpdate.IsChecked!=true)return;
   updateStatus.Text="发现 "+release.Version+"，正在下载并校验…";
   string path=await Task.Run(()=>release.Download(Path.Combine(AppPaths.Data,"updates")));
   pendingUpdate=release;pendingInstaller=path;installUpdate.Visibility=Visibility.Visible;
   updateStatus.Text="新版 "+release.Version+" 已下载并通过 SHA-256 校验。点击安装更新继续。";
   if(automatic && tray!=null)tray.ShowBalloonTip(6000,"PadHop 更新已就绪","打开「关于」页安装新版 "+release.Version+"。",System.Windows.Forms.ToolTipIcon.Info);
  }catch(Exception e){updateStatus.Text="更新未完成："+e.Message+"。可稍后重试或从 GitHub 下载。";}
  finally{checkingUpdate=false;checkUpdate.IsEnabled=true;}
 }
 void InstallUpdate(){
  if(pendingUpdate==null || pendingInstaller==null)return;
  if(recorder!=null)throw new Exception("请先完成录制，再安装更新。");
  pendingUpdate.Verify(pendingInstaller);
  if(MessageBox.Show(window,"安装更新将退出 PadHop，保留已保存的配置。安装向导会让你确认组件和本机自签选项。继续？","安装更新",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
  Save();
  try{using(var p=Process.Start(new ProcessStartInfo(pendingInstaller){UseShellExecute=true,Verb="runas"})){};}
  catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode==1223){Notice("已取消安装更新。");return;}throw;}
  Stop();exiting=true;window.Close();
 }
}
