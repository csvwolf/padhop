param([switch]$UiAccess,[string]$SigningThumbprint)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$bin=Join-Path $root 'bin'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Force $bin,(Join-Path $bin 'source'),(Join-Path $bin 'assets'),(Join-Path $bin 'capture') | Out-Null
& (Join-Path $PSScriptRoot 'restore.ps1')
Copy-Item (Join-Path $root '.deps\ViGEmClient.dll') $bin -Force
Copy-Item (Join-Path $root 'src\Main.xaml') (Join-Path $bin 'source') -Force
$version=(Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION'}
$metadataDir=Join-Path $root '.deps\build'
New-Item -ItemType Directory -Force $metadataDir | Out-Null
$metadata=Join-Path $metadataDir 'AssemblyInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("Talaria")]
[assembly: AssemblyProduct("Talaria")]
[assembly: AssemblyCompany("Talaria contributors")]
[assembly: AssemblyCopyright("Copyright 2026 Talaria contributors")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
"@ | Set-Content $metadata -Encoding UTF8
$xaml=Join-Path $bin 'source\Main.xaml'
[IO.File]::WriteAllText($xaml,([IO.File]::ReadAllText($xaml) -replace '实验版 · \d+\.\d+\.\d+',('实验版 · '+$version)),[Text.UTF8Encoding]::new($true))
Copy-Item (Join-Path $root 'assets\app.png'),(Join-Path $root 'assets\app.ico') (Join-Path $bin 'assets') -Force
$wpf=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object {'/r:'+(Join-Path $framework ('WPF\'+$_))}
$localization=@((Join-Path $root 'src\Localization.cs'))
$translationSource=Join-Path $metadataDir 'Translations.cs'
$catalog=Get-Content (Join-Path $root 'languages\en.json') -Raw | ConvertFrom-Json
$entries=@($catalog.PSObject.Properties | ForEach-Object { '{'+(ConvertTo-Json -InputObject $_.Name -Compress)+','+(ConvertTo-Json -InputObject ([string]$_.Value) -Compress)+'}' })
[IO.File]::WriteAllText($translationSource,('using System.Collections.Generic; internal static partial class L {static readonly Dictionary<string,string> English=new Dictionary<string,string>{'+($entries -join ',')+'};}'),[Text.UTF8Encoding]::new($true))
$localization+=$translationSource
$front=Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
& $csc /nologo /target:winexe /main:PadHop /platform:x64 /warnaserror+ /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $wpf ('/win32icon:'+(Join-Path $root 'assets\app.ico')) ('/out:'+(Join-Path $bin 'PadHop.exe')) $metadata $translationSource $front
if($LASTEXITCODE){throw 'UI build failed'}
$engine=Get-ChildItem (Join-Path $root 'src\engine') -Filter '*.cs' | ForEach-Object FullName
$shared=@('PadProfiles.cs','InputProfiles.cs','InputLayout.cs') | ForEach-Object {Join-Path $root ('src\'+$_)}
& $csc /nologo /target:exe /platform:x64 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ('/out:'+(Join-Path $bin 'PadHop.Engine.exe')) $metadata $localization $engine $shared
if($LASTEXITCODE){throw 'Engine build failed'}
$options=if($UiAccess){@('/win32manifest:'+(Join-Path $root 'src\helper\uiaccess.manifest'))}else{@('/define:STANDARD')}
& $csc /nologo /target:winexe /platform:x64 /warnaserror+ $options ('/out:'+(Join-Path $bin 'PadHop.Input.exe')) $metadata (Join-Path $root 'src\helper\Helper.cs') (Join-Path $root 'src\engine\MouseWire.cs') (Join-Path $root 'src\engine\DesktopSettings.cs')
if($LASTEXITCODE){throw 'Input build failed'}
if($UiAccess){
 if(!$SigningThumbprint){throw 'UIAccess requires an explicitly supplied signing certificate. No test root is installed.'}
 $cert=Get-Item ('Cert:\CurrentUser\My\'+$SigningThumbprint)
 foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $sig=Set-AuthenticodeSignature (Join-Path $bin $name) -Certificate $cert -HashAlgorithm SHA256
  if($sig.Status -ne 'Valid'){throw 'Signature validation failed'}
 }
}
$mode=if($UiAccess){'uiaccess'}else{'standard'}
Set-Content (Join-Path $bin 'input-mode.txt') $mode -Encoding ASCII
$capture=Get-ChildItem (Join-Path $root 'tools\capture\src') -Filter '*.cs' | ForEach-Object FullName
& $csc /nologo /target:winexe /platform:x64 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /main:Capture ('/out:'+(Join-Path $bin 'capture\SC2CaptureWorker.exe')) $metadata $localization $capture
if($LASTEXITCODE){throw 'Capture build failed'}
New-Item -ItemType Directory -Force (Join-Path $bin 'capture\tools') | Out-Null
& $csc /nologo /target:exe /platform:x64 /warnaserror+ ('/out:'+(Join-Path $bin 'capture\tools\SC2LoggingRefresh.exe')) $metadata (Join-Path $root 'tools\capture\src\LoggingRefresh.cs')
if($LASTEXITCODE){throw 'Capture refresh build failed'}
Copy-Item (Join-Path $root 'tools\capture\*.py'),(Join-Path $root 'tools\capture\Record-Bluetooth.ps1'),(Join-Path $root 'tools\capture\Recover-Capture.ps1'),(Join-Path $root 'tools\capture\BluetoothStack.wprp') (Join-Path $bin 'capture') -Force
& (Join-Path $bin 'PadHop.Engine.exe') --self-test
if($LASTEXITCODE){throw 'Engine tests failed'}
foreach($locale in @('zh-CN','en')){
 $t=Start-Process (Join-Path $bin 'PadHop.exe') -ArgumentList '--self-test',('--language='+$locale) -PassThru -WindowStyle Hidden
 try{if(!$t.WaitForExit(30000) -or $t.ExitCode){throw ('UI tests failed: '+$locale)}}finally{$t.Dispose()}
}
'Build and synthetic tests passed: '+$bin+' ('+$mode+')'

# Optional local UIAccess payload stays unsigned until the end user explicitly opts in.
$localPayload=Join-Path $bin 'local-signing'
New-Item -ItemType Directory -Force $localPayload | Out-Null
$oldPayload=Join-Path $localPayload 'PadHop.Input.exe'
if(Test-Path -LiteralPath $oldPayload){Remove-Item -LiteralPath $oldPayload -Force}
& $csc /nologo /target:winexe /platform:x64 /warnaserror+ ('/win32manifest:'+(Join-Path $root 'src\helper\uiaccess.manifest')) ('/out:'+(Join-Path $localPayload 'PadHop.Input.UIAccess.exe')) $metadata (Join-Path $root 'src\helper\Helper.cs') (Join-Path $root 'src\engine\MouseWire.cs') (Join-Path $root 'src\engine\DesktopSettings.cs')
if($LASTEXITCODE){throw 'Optional UIAccess payload build failed'}
$hashes=@{}
foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
 $path=if($name -eq 'PadHop.Input.exe'){Join-Path $localPayload 'PadHop.Input.UIAccess.exe'}else{Join-Path $bin $name}
 $hashes[$name]=(Get-FileHash $path).Hash
}
@{Product='PadHop';Version=$version;Hashes=$hashes} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $localPayload 'payload.json') -Encoding UTF8
Copy-Item (Join-Path $root 'scripts\Local-Signing.ps1') $bin -Force
Copy-Item (Join-Path $root 'scripts\Renew-LocalSigning.ps1') $bin -Force

Copy-Item (Join-Path $root 'scripts\Language.ps1') $bin -Force
Copy-Item (Join-Path $root 'languages') $bin -Recurse -Force
