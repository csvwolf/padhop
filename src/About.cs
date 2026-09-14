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
 Button checkUpdate,downloadUpdate,installUpdate;
 CheckBox autoUpdate;
 bool checkingUpdate;
 DateTime nextUpdateCheck=DateTime.MinValue;
 UpdateRelease pendingUpdate;
 string pendingInstaller;
 static string ProductVersion {get{return ((AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(PadHop).Assembly,typeof(AssemblyInformationalVersionAttribute))).InformationalVersion;}}
 StackPanel AboutCard(string title){var p=new StackPanel();p.Children.Add(new TextBlock{Text=title,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});Get<StackPanel>("AboutPage").Children.Add(new Border{Style=(Style)window.FindResource("Card"),Child=p});return p;}
 Button AboutButton(Panel host,string text,Action action){var b=new Button{Content=text,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,10,0)};b.Click+=delegate{Guard(action);};host.Children.Add(b);return b;}
 void BuildAbout(bool live){
  var brand=AboutCard(L.T("PadHop · 跃控"));
  brand.Children.Add(new TextBlock{Text=L.T("让 Steam Controller 2，在更多地方好用。"),Margin=new Thickness(0,0,0,8)});
  brand.Children.Add(new TextBlock{Text=L.T("版本 ")+ProductVersion+L.T("  ·  实验版通道  ·  MIT 开源"),Style=(Style)window.FindResource("Caption")});
  var language=new ComboBox{Width=220,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0),ToolTip="Language / 语言 · Restart to apply / 重启后生效"};
  language.Items.Add("System / 跟随系统");language.Items.Add("简体中文");language.Items.Add("English");
  string preference=File.Exists(L.PreferencePath)?File.ReadAllText(L.PreferencePath).Trim():"auto";
  language.SelectedIndex=preference=="zh-CN"?1:preference=="en"?2:0;
  language.SelectionChanged+=delegate{if(!live)return;AtomicWrite(L.PreferencePath,language.SelectedIndex==1?"zh-CN":language.SelectedIndex==2?"en":"auto");Notice(language.SelectedIndex==1?"语言已保存，重新打开 PadHop 后生效。":"Language saved. Reopen PadHop to apply.");};
  brand.Children.Add(language);
  var links=new WrapPanel{Margin=new Thickness(0,16,0,0)};brand.Children.Add(links);
  AboutButton(links,"GitHub ↗",delegate{OpenExternal("https://github.com/csvwolf/padhop");});
  AboutButton(links,L.T("作者博客 ↗"),delegate{OpenExternal("https://www.codesky.me/");});
  AboutButton(links,L.T("微博 ↗"),delegate{OpenExternal("https://www.weibo.com/dreamit");});
  var updates=AboutCard(L.T("软件更新"));
  updateStatus=new TextBlock{Text=L.T("尚未检查更新。更新来源：GitHub 官方项目 Release。"),Margin=new Thickness(0,0,0,12)};updates.Children.Add(updateStatus);
  var actions=new WrapPanel();updates.Children.Add(actions);
  checkUpdate=AboutButton(actions,L.T("检查更新"),delegate{CheckUpdates(false);});
  downloadUpdate=AboutButton(actions,L.T("下载更新"),delegate{DownloadUpdate();});downloadUpdate.Visibility=Visibility.Collapsed;
  installUpdate=AboutButton(actions,L.T("安装更新"),InstallUpdate);installUpdate.Visibility=Visibility.Collapsed;
  autoUpdate=new CheckBox{Content=L.T("自动检查更新并提示"),IsChecked=File.Exists(Path.Combine(AppPaths.Data,"automatic-update-check.enabled"))};updates.Children.Add(autoUpdate);
  autoUpdate.Checked+=delegate{if(live){AtomicWrite(Path.Combine(AppPaths.Data,"automatic-update-check.enabled"),"enabled");nextUpdateCheck=DateTime.MinValue;CheckUpdates(true);}};
  autoUpdate.Unchecked+=delegate{if(live){string f=Path.Combine(AppPaths.Data,"automatic-update-check.enabled");if(File.Exists(f))File.Delete(f);updateStatus.Text=L.T("已关闭自动检查。仍可手动检查更新。");}};
  updates.Children.Add(new TextBlock{Text=L.T("开启后，启动时及运行期间每 24 小时检查。只提示，不自动下载或安装；下载和安装分别由你点击。"),Style=(Style)window.FindResource("Caption")});
  signingHost=AboutCard(L.T("本机签名"));
  signingHost.Children.Add(new TextBlock{Text=L.T("未启用本机自签。需要操作管理员窗口时，可重新运行 install.exe 勾选该组件。"),Style=(Style)window.FindResource("Caption")});
  var diagnostics=AboutCard(L.T("诊断与日志"));var logs=new WrapPanel();diagnostics.Children.Add(logs);
  AboutButton(logs,L.T("打开日志文件夹"),delegate{string dir=Path.Combine(AppPaths.Data,"logs");Directory.CreateDirectory(dir);Process.Start(new ProcessStartInfo("explorer.exe",Quote(dir)){UseShellExecute=true});});
  AboutButton(logs,L.T("打开最近日志"),delegate{var files=Directory.GetFiles(AppPaths.Data,"*.txt",SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(Path.Combine(AppPaths.Data,"logs"),"*.txt")).Concat(Directory.GetFiles(Path.Combine(AppPaths.Data,"logs"),"*.log")).Select(f=>new FileInfo(f)).OrderByDescending(f=>f.LastWriteTimeUtc).ToList();if(files.Count==0){Notice(L.T("还没有日志。复现问题后再打开。"));return;}Process.Start(new ProcessStartInfo("notepad.exe",Quote(files[0].FullName)){UseShellExecute=true});});
  diagnostics.Children.Add(new TextBlock{Text=L.T("日志可能包含应用路径和设备标识，分享前可以先查看；不会自动上传。"),Style=(Style)window.FindResource("Caption")});
  if(live)window.Loaded+=delegate{PollUpdates();};
 }
 static void OpenExternal(string url){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
 void PollUpdates(){if(autoUpdate!=null && autoUpdate.IsChecked==true && DateTime.UtcNow>=nextUpdateCheck)CheckUpdates(true);}
 async void CheckUpdates(bool automatic){
  if(checkingUpdate)return;checkingUpdate=true;nextUpdateCheck=DateTime.UtcNow.AddDays(1);checkUpdate.IsEnabled=false;
  try{
   updateStatus.Text=L.T("正在检查更新…");
   var release=await Task.Run(()=>UpdateRelease.FindNewer(ProductVersion));
   if(release==null){updateStatus.Text=L.T("当前已是最新版本（")+ProductVersion+"）。";return;}
   if(automatic && autoUpdate.IsChecked!=true)return;
   if(pendingUpdate==null || pendingUpdate.Version!=release.Version){pendingInstaller=null;installUpdate.Visibility=Visibility.Collapsed;}
   pendingUpdate=release;downloadUpdate.Visibility=Visibility.Visible;
   updateStatus.Text=L.T("发现新版 ")+release.Version+L.T("。点击“下载更新”才会下载。");
   if(automatic && tray!=null)tray.ShowBalloonTip(6000,L.T("PadHop 发现新版"),L.T("新版 ")+release.Version+L.T(" 已发布，可在「关于」页查看；尚未下载。"),System.Windows.Forms.ToolTipIcon.Info);
  }catch(Exception e){updateStatus.Text=L.T("更新未完成：")+e.Message+L.T("。可稍后重试或从 GitHub 下载。");}
  finally{checkingUpdate=false;checkUpdate.IsEnabled=true;}
 }
 async void DownloadUpdate(){
  if(pendingUpdate==null || checkingUpdate)return;
  checkingUpdate=true;checkUpdate.IsEnabled=false;downloadUpdate.IsEnabled=false;installUpdate.IsEnabled=false;
  try{
   var release=pendingUpdate;updateStatus.Text=L.T("正在下载 ")+release.Version+L.T(" 并校验…");
   pendingInstaller=await Task.Run(()=>release.Download(Path.Combine(AppPaths.Data,"updates")));
   installUpdate.Visibility=Visibility.Visible;
   updateStatus.Text=L.T("新版 ")+release.Version+L.T(" 已下载并通过 SHA-256 校验。由你点击“安装更新”继续。");
  }catch(Exception e){updateStatus.Text=L.T("下载未完成：")+e.Message+L.T("。可重试或从 GitHub 下载。");}
  finally{checkingUpdate=false;checkUpdate.IsEnabled=true;downloadUpdate.IsEnabled=true;installUpdate.IsEnabled=true;}
 }
 void InstallUpdate(){
  if(pendingUpdate==null || pendingInstaller==null)return;
  if(recorder!=null)throw new Exception(L.T("请先完成录制，再安装更新。"));
  pendingUpdate.Verify(pendingInstaller);
  if(MessageBox.Show(window,L.T("安装更新将退出 PadHop，保留已保存的配置。安装向导会让你确认组件和本机自签选项。继续？"),L.T("安装更新"),MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
  Save();
  try{using(var p=Process.Start(new ProcessStartInfo(pendingInstaller){UseShellExecute=true,Verb="runas"})){};}
  catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode==1223){Notice(L.T("已取消安装更新。"));return;}throw;}
  Stop();exiting=true;window.Close();
 }
}
