$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$deps=Join-Path $root '.deps'
New-Item -ItemType Directory -Force $deps | Out-Null
$expected='00303F99E1968D523FBD08A7C691D60D145B7335059EC593947841D124C10875'
$dll=Join-Path $deps 'ViGEmClient.dll'
if((Test-Path $dll) -and (Get-FileHash $dll).Hash -eq $expected){return}
$pkg=Join-Path $deps 'vigem-client-1.21.256.zip'
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/nefarius.vigem.client/1.21.256/nefarius.vigem.client.1.21.256.nupkg' -OutFile $pkg
$extract=Join-Path $deps ('extract-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive $pkg $extract
$assembly=Get-ChildItem $extract -Filter Nefarius.ViGEm.Client.dll -Recurse | Where-Object {$_.FullName -match 'netstandard2.0'} | Select-Object -First 1
if(!$assembly){throw 'Expected package assembly not found'}
$a=[Reflection.Assembly]::LoadFile($assembly.FullName)
$stream=$a.GetManifestResourceStream('costura64.vigemclient.dll')
if(!$stream){throw 'Expected native library resource not found'}
$file=[IO.File]::Create($dll)
try{$stream.CopyTo($file)}finally{$file.Dispose();$stream.Dispose()}
if((Get-FileHash $dll).Hash -ne $expected){throw 'Native library checksum mismatch; do not use this dependency'}
'Verified ViGEmClient 1.21.256 native x64 dependency.'
