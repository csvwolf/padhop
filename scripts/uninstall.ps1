param([switch]$RemoveUserData)
$ErrorActionPreference='Stop'
function Assert-NoLinks([string]$Path){
 $check=[IO.Path]::GetFullPath($Path)
 while($check){
  if(Test-Path -LiteralPath $check){if((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Unexpected filesystem link: '+$check)}}
  $parent=Split-Path $check -Parent
  if($parent -eq $check){break};$check=$parent
 }
}

$root=[IO.Path]::GetFullPath($PSScriptRoot)
$allowed=@((Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PadHop'),(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PadHop'))
if($allowed -notcontains $root){throw 'Only installed PadHop directories may be uninstalled.'}
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if($root -eq $allowed[0] -and !$admin){throw 'Run uninstall.ps1 as administrator to uninstall the UIAccess installation.'}
if((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected link'}
Assert-NoLinks $root
if(Get-Process PadHop,PadHop.Engine,PadHop.Input -ErrorAction SilentlyContinue){throw 'Exit PadHop first.'}
$manifest=Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
foreach($f in $manifest.Files){
 $p=[IO.Path]::GetFullPath((Join-Path $root $f.Path))
 if(!$p.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe uninstall path'}
 Assert-NoLinks $p
 if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p -Force}
}
Remove-Item -LiteralPath (Join-Path $root 'manifest.json') -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) 'PadHop.lnk') -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PadHop' -ErrorAction SilentlyContinue
if($RemoveUserData){
 $data=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PadHop'))
 Assert-NoLinks $data
 if((Split-Path $data -Leaf) -ne 'PadHop'){throw 'Unexpected data path'}
 if(Test-Path $data){if(Get-ChildItem -LiteralPath $data -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }){throw 'Data contains links; remove manually'}}
 if(Test-Path -LiteralPath $data){Remove-Item -LiteralPath $data -Recurse -Force}
}
'Uninstalled application files. Configuration is retained unless -RemoveUserData was specified. Update backups, if present, remain in the install directory.'
