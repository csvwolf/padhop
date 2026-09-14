$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.deps\setup'
$dest=Join-Path $root '.deps\inno'
New-Item -ItemType Directory -Force $cache | Out-Null
$setup=Join-Path $cache 'inno.exe'
if(!(Test-Path $setup)){Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $setup}
if((Get-FileHash $setup).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'){throw 'Inno Setup checksum mismatch.'}
$sig=Get-AuthenticodeSignature $setup
if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Pyrsys*'){throw 'Inno Setup publisher signature invalid.'}
$p=Start-Process $setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="'+$dest+'"'),'/NOICONS','/TASKS=' -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode){throw 'Compiler installation failed.'}
'Inno Setup compiler installed in '+$dest
