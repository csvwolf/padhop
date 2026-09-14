$ErrorActionPreference='Stop'
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){throw 'Run recovery as administrator.'}
$journal='HKLM:\SOFTWARE\PadHopCaptureRecovery'
if(!(Test-Path $journal)){'No pending recovery.';return}
$entries=@((Get-ItemProperty $journal).State | ConvertFrom-Json)
$wanted=@{MaxEtwBytes=1024;EtwLogSensitiveData=1;EtwDropLargeEvents=0}
if($entries.Count -ne 3 -or @($entries.Name | Select-Object -Unique).Count -ne 3){throw 'Invalid recovery journal'}
foreach($e in $entries){if(!$wanted.ContainsKey($e.Name) -or $e.Temporary -ne $wanted[$e.Name] -or $e.Kind -ne 'DWord'){throw 'Unsupported recovery entry; inspect manually.'}}
$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters',$true)
try {
 foreach($e in $entries){
  $current=$key.GetValue($e.Name)
  if($current -eq $e.Temporary){if($e.Present){$key.SetValue($e.Name,[int]$e.Value,[Microsoft.Win32.RegistryValueKind]::DWord)}else{$key.DeleteValue($e.Name,$false)}}
 }
} finally {$key.Dispose()}
& (Join-Path $PSScriptRoot 'tools\SC2LoggingRefresh.exe')
if($LASTEXITCODE){throw 'Refresh failed; journal retained. Reboot and retry recovery.'}
Remove-Item -LiteralPath $journal
'Recovered diagnostic settings; unrelated changes were preserved.'
