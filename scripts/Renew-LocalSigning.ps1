#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][int]$WaitForPid,[ValidateSet('auto','en','zh-CN')][string]$Language='auto')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Language.ps1')
try {
 $expected=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PadHop'
 if($PSScriptRoot -ne $expected){throw (T '请使用已安装的 Talaria 续期。')}
 $p=Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
 if($p){try{
  if($p.Path -ne (Join-Path $expected 'PadHop.exe')){throw (T '续期请求进程不匹配。')}
  if(!$p.WaitForExit(30000)){throw (T 'Talaria 未退出，请退出后重新运行安装程序续期。')}
 }finally{$p.Dispose()}}
 & (Join-Path $PSScriptRoot 'Local-Signing.ps1') -Action Renew -AcceptLocalTrust
 $message=(T '本机签名已续期，旧证书和本次私钥已删除。请重新打开 Talaria；个人配置已保留。')
}catch{$message=(T '续期未完成：')+$_+(T '。请重新运行 install.exe 并勾选本机自签进行修复。')}
Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show($message,(T 'Talaria · 本机签名续期')) | Out-Null
