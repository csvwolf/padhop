# Real trust-store tests are restricted to an expendable GitHub-hosted Windows runner.
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Run only on a disposable GitHub Actions Windows runner.'}
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$app=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PadHop'
if(Test-Path $app){throw 'Refusing to test against an existing installation.'}
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){throw 'Disposable runner must be an administrator.'}
$setup=Join-Path $root 'dist\install.exe'
function RunInstall([string]$tasks,[bool]$accept,[bool]$expectSuccess){
 $args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/COMPONENTS=app',('/TASKS='+$tasks),('/LOG="'+(Join-Path $env:RUNNER_TEMP 'padhop-install.log')+'"'))
 if($accept){$args+='/ACCEPTLOCALTRUST=YES'}
 Write-Host ('Testing installer tasks='+$tasks+' accept='+$accept)
 $p=Start-Process $setup -ArgumentList $args -WindowStyle Hidden -PassThru
 if(!$p.WaitForExit(90000)){Get-Content (Join-Path $env:RUNNER_TEMP 'padhop-install.log') -Tail 40 -ErrorAction SilentlyContinue;Get-Content (Join-Path $app 'local-signing-last.log') -Tail 40 -ErrorAction SilentlyContinue;throw 'Installer process exceeded 90 seconds'}
 Write-Host ('Installer exit='+$p.ExitCode)
 if(($p.ExitCode -eq 0) -ne $expectSuccess){
  Get-Content (Join-Path $env:RUNNER_TEMP 'padhop-install.log') -Tail 35 -ErrorAction SilentlyContinue
  Get-Content (Join-Path $app 'local-signing-last.log') -Tail 45 -ErrorAction SilentlyContinue
  throw ('Unexpected installer result: '+$p.ExitCode)
 }
}
function CheckSigned {
 if((Get-Content (Join-Path $app 'input-mode.txt') -Raw).Trim() -ne 'local-uiaccess'){throw 'Local UIAccess was not activated'}
 $state=Get-Content (Join-Path $app '.local-signing\state.json') -Raw | ConvertFrom-Json
 if(!(Test-Path ('Cert:\LocalMachine\Root\'+$state.Thumbprint))){throw 'Local trust certificate missing'}
 if(Test-Path ('Cert:\CurrentUser\My\'+$state.Thumbprint)){throw 'Signing private-key certificate was retained'}
 foreach($n in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $s=Get-AuthenticodeSignature (Join-Path $app $n)
  if($s.Status -ne 'Valid' -or $s.SignerCertificate.Thumbprint -ne $state.Thumbprint){throw 'Invalid local binary signature'}
 }
 $p=Start-Process (Join-Path $app 'PadHop.Input.exe') -ArgumentList '--check-uiaccess' -WindowStyle Hidden -PassThru
 try {
  if(!$p.WaitForExit(15000)){throw 'Token check timed out'}
  if($p.ExitCode){throw 'Windows did not grant TokenUIAccess'}
 } finally {$p.Dispose()}
 return $state.Thumbprint
}
RunInstall 'localuiaccess' $false $false
if(Test-Path (Join-Path $app 'PadHop.exe')){throw 'Installation proceeded without explicit trust consent'}
RunInstall '' $false $true
if((Get-Content (Join-Path $app 'input-mode.txt') -Raw).Trim() -ne 'standard'){throw 'Default install modified trust mode'}
RunInstall 'localuiaccess' $true $true
$first=CheckSigned
RunInstall '' $false $true
if(Test-Path ('Cert:\LocalMachine\Root\'+$first)){throw 'Upgrade retained old local trust'}
if(Test-Path (Join-Path $app '.local-signing\state.json')){throw 'Upgrade retained old signing state'}
RunInstall 'localuiaccess' $true $true
$second=CheckSigned
if($first -eq $second){throw 'Local signing reused an old key'}
$p=Start-Process (Join-Path $app 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(60000)){throw 'Uninstall process timed out'}
if($p.ExitCode){throw 'Native uninstall failed'}
if(Test-Path ('Cert:\LocalMachine\Root\'+$second)){throw 'Uninstall retained local trust'}
if(Test-Path (Join-Path $app 'PadHop.exe')){throw 'Uninstall retained executable'}
'PASS: explicit consent required, local signing, TokenUIAccess, private-key certificate removal, upgrade restore, fresh key, uninstall trust cleanup.'
