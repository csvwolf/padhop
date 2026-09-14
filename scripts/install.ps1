param([switch]$ValidateOnly,[string]$TestRoot)
$ErrorActionPreference='Stop'
function Assert-NoLinks([string]$Path){
 $check=[IO.Path]::GetFullPath($Path)
 while($check){
  if(Test-Path -LiteralPath $check){if((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Unexpected filesystem link: '+$check)}}
  $parent=Split-Path $check -Parent
  if($parent -eq $check){break};$check=$parent
 }
}

$packageRoot=$PSScriptRoot
$manifest=Get-Content (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
if($manifest.Product -ne 'PadHop' -or $manifest.Mode -notin @('standard','uiaccess')){throw 'Invalid package manifest'}
$files=@($manifest.Files)
foreach($f in $files){
 if([IO.Path]::IsPathRooted($f.Path) -or $f.Path -match '(^|[\\/])\.\.([\\/]|$)'){throw 'Unsafe package path'}
 $path=[IO.Path]::GetFullPath((Join-Path $packageRoot $f.Path))
 if(!$path.StartsWith($packageRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escaped root'}
 if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Links are not package files'}
 Assert-NoLinks $path
 if((Get-FileHash -LiteralPath $path).Hash -ne $f.Sha256){throw ('File verification failed: '+$f.Path)}
}
if($manifest.Mode -eq 'uiaccess'){
 $thumbprints=@()
 foreach($exe in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $signature=Get-AuthenticodeSignature (Join-Path $packageRoot $exe)
  if($signature.Status -ne 'Valid'){throw 'UIAccess release requires valid publisher signatures; no certificate will be imported'}
  $thumbprints+=$signature.SignerCertificate.Thumbprint
 }
 if(@($thumbprints | Select-Object -Unique).Count -ne 1){throw 'Mixed executable publishers'}
}
if($ValidateOnly){'PASS: package paths, hashes and applicable signatures verified.';return}
if(Get-Process PadHop,PadHop.Engine,PadHop.Input -ErrorAction SilentlyContinue){throw 'Exit PadHop before installing or upgrading.'}
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if($manifest.Mode -eq 'uiaccess' -and !$admin){throw 'Run this installer as administrator for UIAccess installation.'}
$base=if($manifest.Mode -eq 'uiaccess'){[Environment]::GetFolderPath('ProgramFiles')}else{Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'}
$dest=[IO.Path]::GetFullPath((Join-Path $base 'PadHop'))
if(Test-Path $dest){if((Get-Item -LiteralPath $dest).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Installation folder must not be a link'}}
Assert-NoLinks $dest
if($TestRoot){if($manifest.Mode -ne 'standard'){throw 'TestRoot only supports standard packages'};$dest=[IO.Path]::GetFullPath((Join-Path $TestRoot 'PadHop'));if(Test-Path $dest){throw 'Test destination must be new'};Assert-NoLinks $dest}
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PadHop'
$old=if(!$TestRoot){Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue}
if($old -and $old.InstallLocation -ne $dest){throw 'Uninstall the other installation mode first; user data is retained.'}
New-Item -ItemType Directory -Force $dest | Out-Null
$backup=Join-Path $dest ('.backup-'+(Get-Date -Format yyyyMMddHHmmss))
New-Item -ItemType Directory $backup | Out-Null
$written=@()
try {
 foreach($f in $files){
  $target=Join-Path $dest $f.Path
  Assert-NoLinks $target
  $relativeParent=Split-Path $f.Path -Parent
  if($relativeParent){New-Item -ItemType Directory -Force (Join-Path $dest $relativeParent),(Join-Path $backup $relativeParent) | Out-Null}
  if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination (Join-Path $backup $f.Path)}
  Copy-Item -LiteralPath (Join-Path $packageRoot $f.Path) -Destination $target -Force
  $written+=$f.Path
 }
 Copy-Item (Join-Path $packageRoot 'manifest.json') $dest -Force
 if($TestRoot){'PASS: isolated installation files copied and verified.';return}
 $shell=New-Object -ComObject WScript.Shell
 $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'PadHop.lnk'))
 $shortcut.TargetPath=Join-Path $dest 'PadHop.exe';$shortcut.WorkingDirectory=$dest;$shortcut.Save()
 New-Item -Path $uninstallKey -Force | Out-Null
 New-ItemProperty $uninstallKey DisplayName -Value 'PadHop' -Force | Out-Null
 New-ItemProperty $uninstallKey DisplayVersion -Value $manifest.Version -Force | Out-Null
 New-ItemProperty $uninstallKey InstallLocation -Value $dest -Force | Out-Null
 $cmd='powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $dest 'uninstall.ps1')+'"'
 New-ItemProperty $uninstallKey UninstallString -Value $cmd -Force | Out-Null
} catch {
 foreach($relative in $written){$previous=Join-Path $backup $relative;$target=Join-Path $dest $relative;if(Test-Path -LiteralPath $previous){Copy-Item -LiteralPath $previous -Destination $target -Force}else{Remove-Item -LiteralPath $target -Force}}
 throw
}
'Installed PadHop to '+$dest+'. User data was not modified. Launch from Start menu.'
