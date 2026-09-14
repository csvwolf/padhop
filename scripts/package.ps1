$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$bin=Join-Path $root 'bin'
$dist=Join-Path $root 'dist'
$mode=(Get-Content (Join-Path $bin 'input-mode.txt') -Raw).Trim()
$folder=Join-Path $dist ('PadHop-0.2.0-'+$mode+'-'+(Get-Date -Format yyyyMMddHHmmss))
New-Item -ItemType Directory -Force $folder | Out-Null
foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe','ViGEmClient.dll','input-mode.txt','source','assets','capture')){Copy-Item -LiteralPath (Join-Path $bin $name) -Destination $folder -Recurse}
# Never redistribute user-selected Microsoft parser or machine-local tool paths.
$parser=Join-Path $folder 'capture\tools\BTETLParse.exe'
if(Test-Path -LiteralPath $parser){throw 'External parser must not be redistributed'}
foreach($name in @('LICENSE','THIRD-PARTY-NOTICES.txt','README.md','licenses','docs')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination $folder -Recurse}
Copy-Item (Join-Path $PSScriptRoot 'install.ps1'),(Join-Path $PSScriptRoot 'uninstall.ps1'),(Join-Path $PSScriptRoot 'configure-capture.ps1') $folder
Set-Content (Join-Path $folder 'Install.cmd') '@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"' -Encoding ASCII
$files=Get-ChildItem $folder -Recurse -File | ForEach-Object {[ordered]@{Path=$_.FullName.Substring($folder.Length+1);Sha256=(Get-FileHash $_.FullName).Hash}}
[ordered]@{Product='PadHop';Version='0.2.0';Mode=$mode;Files=@($files)} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $folder 'manifest.json') -Encoding UTF8
& (Join-Path $folder 'install.ps1') -ValidateOnly
$zip=$folder+'.zip';Compress-Archive -Path (Join-Path $folder '*') -DestinationPath $zip
(Get-FileHash $zip).Hash+'  '+(Split-Path $zip -Leaf) | Set-Content ($zip+'.sha256') -Encoding ASCII
$zip
