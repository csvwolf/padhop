param([string]$Iscc,[string]$TestAppId)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$Iscc){$Iscc=Join-Path $root '.deps\inno\ISCC.exe'}
if(!(Test-Path $Iscc)){throw 'Install Inno Setup 6.7.3, then pass -Iscc <ISCC.exe>.'}
$mode=(Get-Content (Join-Path $root 'bin\input-mode.txt') -Raw).Trim()
if($mode -notin @('standard','uiaccess')){throw 'Build PadHop first.'}
if($mode -eq 'uiaccess'){
 $publishers=@()
 foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $signature=Get-AuthenticodeSignature (Join-Path $root ('bin\'+$name))
  if($signature.Status -ne 'Valid'){throw 'UIAccess release requires trusted executable signatures.'}
  $publishers+=$signature.SignerCertificate.Thumbprint
 }
 if(@($publishers | Select-Object -Unique).Count -ne 1){throw 'Mixed UIAccess publishers.'}
}
if(Test-Path (Join-Path $root 'bin\capture\tools\BTETLParse.exe')){throw 'Do not redistribute the optional Microsoft parser.'}
$driver=Join-Path $root '.deps\setup\ViGEmBus.exe'
New-Item -ItemType Directory -Force (Split-Path $driver) | Out-Null
$hash='89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A'
if(!(Test-Path $driver)){
 Invoke-WebRequest 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe' -OutFile $driver
}
if((Get-FileHash $driver).Hash -ne $hash){throw 'Driver installer hash mismatch.'}
$sig=Get-AuthenticodeSignature $driver
if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Nefarius*'){throw 'Driver publisher signature invalid.'}
$version=(Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION'}
$options=@('/Qp',('/DMode='+$mode),('/DProductVersion='+$version))
if($TestAppId){if($TestAppId -notmatch '^PadHopTest-[a-zA-Z0-9-]+$'){throw 'Invalid test app identity'};$options+=('/DAppIdentifier='+$TestAppId)}
& $Iscc $options (Join-Path $root 'installer\PadHop.iss')
if($LASTEXITCODE){throw 'Installer compilation failed.'}
$exe=Join-Path $root 'dist\install.exe'
'Built '+$exe+' ('+$mode+'). SHA256: '+(Get-FileHash $exe).Hash
