using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

internal sealed partial class PadHop
{
 TextBlock signingMessage;
 Button renewSigning;
 DateTime nextSigningCheck=DateTime.MinValue;
 string remindedCertificate;
 void BuildSigningMaintenance(bool live){
  if(!live)return;
  var row=new StackPanel{Margin=new Thickness(0,0,0,16)};
  signingMessage=new TextBlock{TextWrapping=TextWrapping.Wrap};
  renewSigning=new Button{Content=L.T("续期本机签名"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
  renewSigning.ToolTip=L.T("重新生成一年有效期的本机证书，签署程序并清理旧证书。需要一次管理员确认；完成后重新打开 Talaria，配置保留。");
  renewSigning.Click+=delegate{Guard(RenewSigning);};
  row.Children.Add(signingMessage);row.Children.Add(renewSigning);
  if(File.Exists(Path.Combine(Root,".local-signing","state.json")))signingHost.Children.RemoveAt(1);
  signingHost.Children.Add(row);
  row.Visibility=Visibility.Collapsed;
  window.Loaded+=delegate{nextSigningCheck=DateTime.MinValue;CheckSigningExpiry();};
  CheckSigningExpiry();
 }
 internal static bool SigningDue(DateTimeOffset expiry,DateTimeOffset now){return expiry<=now.AddDays(30);}
 void CheckSigningExpiry(){
  if(signingMessage==null || DateTime.UtcNow<nextSigningCheck)return;
  nextSigningCheck=DateTime.UtcNow.AddHours(6);
  var row=(StackPanel)signingMessage.Parent;
  string file=Path.Combine(Root,".local-signing","state.json");
  if(!File.Exists(file)){row.Visibility=Visibility.Collapsed;return;}
  row.Visibility=Visibility.Visible;
  try{
   var state=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
   DateTimeOffset expiry;
   if(Convert.ToString(state["Product"])!="PadHop" || Convert.ToString(state["Status"])!="Enabled" || !DateTimeOffset.TryParse(Convert.ToString(state["Expires"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out expiry))throw new Exception();
   bool expired=expiry<=DateTimeOffset.Now,due=SigningDue(expiry,DateTimeOffset.Now);
   signingMessage.Text=(expired?L.T("本机签名已到期，请续期后使用管理员窗口操作。"):due?L.T("本机签名即将到期，请安排续期。"):L.T("本机签名有效。"))+L.T(" 到期：")+expiry.ToLocalTime().ToString("yyyy-MM-dd")+L.T("。续期需要管理员确认，并会退出 Talaria；配置保留。");
   renewSigning.IsEnabled=true;
   string thumb=Convert.ToString(state["Thumbprint"]);
   if(due)Get<TextBlock>("CapabilityHint").Text=L.T("本机签名")+(expired?L.T("已到期"):L.T("即将到期"))+L.T("，请在「关于」页续期。");
   if(due && tray!=null && remindedCertificate!=thumb){remindedCertificate=thumb;tray.ShowBalloonTip(8000,L.T("Talaria · 签名续期"),expired?L.T("本机签名已到期，请打开关于页续期。"):L.T("本机签名将在 30 天内到期，可在关于页一键续期。"),System.Windows.Forms.ToolTipIcon.Info);}
  }catch{signingMessage.Text=L.T("本机签名记录异常。请重新运行 install.exe 修复；个人配置保留。");renewSigning.IsEnabled=false;}
 }
 void RenewSigning(){
  if(recorder!=null)throw new Exception(L.T("请先完成录制，再续期。"));
  // Save through the same path as a normal exit before requesting elevation.
  Save();
  string script=Path.Combine(Root,"Renew-LocalSigning.ps1");
  if(!File.Exists(script))throw new Exception(L.T("续期组件缺失，请重新运行 install.exe。"));
  try{
   using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -File "+Quote(script)+" -WaitForPid "+Process.GetCurrentProcess().Id){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})){}
  }catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode==1223){Notice(L.T("已取消续期，当前签名未改变。"));return;}throw;}
  Stop();exiting=true;window.Close();
 }
}
