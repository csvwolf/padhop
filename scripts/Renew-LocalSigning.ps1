#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][int]$WaitForPid)
$ErrorActionPreference='Stop'
try {
 $expected=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PadHop'
 if($PSScriptRoot -ne $expected){throw '请使用已安装的 PadHop 续期。'}
 $p=Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
 if($p){try{
  if($p.Path -ne (Join-Path $expected 'PadHop.exe')){throw '续期请求进程不匹配。'}
  if(!$p.WaitForExit(30000)){throw 'PadHop 未退出，请退出后重新运行安装程序续期。'}
 }finally{$p.Dispose()}}
 & (Join-Path $PSScriptRoot 'Local-Signing.ps1') -Action Renew -AcceptLocalTrust
 $message='本机签名已续期，旧证书和本次私钥已删除。请重新打开 PadHop；个人配置已保留。'
}catch{$message='续期未完成：'+$_+'。请重新运行 install.exe 并勾选本机自签进行修复。'}
Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show($message,'PadHop · 本机签名续期') | Out-Null
