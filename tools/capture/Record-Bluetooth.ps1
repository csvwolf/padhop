param([Parameter(Mandatory=$true)][string]$SessionDirectory,[switch]$FullPacket,[switch]$ValidateOnly)
$ErrorActionPreference='Stop'
$sessionRoot=[IO.Path]::GetFullPath($SessionDirectory)
$allowed=[IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PadHop\captures'))+[IO.Path]::DirectorySeparatorChar
if(-not $sessionRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Session must be inside this tool captures directory.'}
if(-not (Test-Path -LiteralPath $sessionRoot -PathType Container)){throw 'Session directory does not exist.'}
if($ValidateOnly){if(-not $FullPacket){throw 'FullPacket argument missing'};Write-Output 'PASS: FullPacket received; no capture or settings changes performed.';return}
$instance='SC2Left_'+[Guid]::NewGuid().ToString('N')
$started=$false
$registryKey=$null
$backup=@()
$recoveryKey="HKLM:\SOFTWARE\PadHopCaptureRecovery"
$restored=$true
$journalCreated=$false
$mutex=$null
$ownsMutex=$false
$traceLog=Join-Path $sessionRoot 'trace.log'
try {
    if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator rights required for Bluetooth WPR recording.'}
    if(-not $FullPacket){throw 'This older capture UI does not enable complete HID logging. Use the current PadHop capture UI.'}
    $mutex=New-Object System.Threading.Mutex($false,'Local\SC2FullHciCapture')
    $ownsMutex=$mutex.WaitOne(0)
    if(-not $ownsMutex){throw 'Another SC2 full-packet capture is active.'}
    if(Test-Path $recoveryKey){throw 'Unfinished capture recovery exists. Run Recover-Capture.ps1 as administrator first.'}
    # A named instance is essential: never cancel or stop another recording.
    Set-Content -LiteralPath (Join-Path $sessionRoot 'trace-instance.txt') -Value $instance -Encoding ASCII
    if(Test-Path -LiteralPath (Join-Path $sessionRoot 'stop.request')){throw 'Capture was canceled before startup.'}
    # Full HCI contents are required: normal ETW drops HID payloads. No pairing/debug-key mode is changed.
    $registryKey=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters',$true)
    if($null -eq $registryKey){throw 'Bluetooth diagnostic registry key is unavailable.'}
    $wanted=@{MaxEtwBytes=1024;EtwLogSensitiveData=1;EtwDropLargeEvents=0}
    foreach($name in $wanted.Keys){
        $present=$registryKey.GetValueNames() -contains $name
        $backup+= [pscustomobject]@{Name=$name;Present=$present;Kind=$(if($present){$registryKey.GetValueKind($name).ToString()}else{'DWord'});Value=$(if($present){$registryKey.GetValue($name)}else{$null});Temporary=$wanted[$name]}
    }
    $backup | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $sessionRoot 'logging-settings-backup.json') -Encoding UTF8
    if(@($backup | Where-Object {$_.Kind -ne 'DWord'}).Count){throw 'Unsupported existing diagnostic value type; settings unchanged.'}
    New-Item $recoveryKey -Force | Out-Null
    New-ItemProperty $recoveryKey State -Value ($backup | ConvertTo-Json -Depth 4 -Compress) -Force | Out-Null
    $journalCreated=$true
    foreach($entry in $backup){$registryKey.SetValue($entry.Name,$entry.Temporary,[Microsoft.Win32.RegistryValueKind]::DWord)}
    & (Join-Path $PSScriptRoot 'tools\SC2LoggingRefresh.exe') 2>&1 | Out-File $traceLog -Append
    if($LASTEXITCODE -ne 0){throw 'Bluetooth driver did not reload logging settings; capture was not started.'}
    & wpr.exe -start ((Join-Path $PSScriptRoot 'BluetoothStack.wprp')+'!BluetoothStack') -filemode -instancename $instance 2>&1 | Out-File $traceLog -Append
    if($LASTEXITCODE -ne 0){throw ('WPR start failed: '+$LASTEXITCODE)}
    $started=$true
    Set-Content -LiteralPath (Join-Path $sessionRoot 'trace.ready') -Value ([DateTime]::UtcNow.ToString('o'))
    $end=[DateTime]::UtcNow.AddSeconds(90)
    while([DateTime]::UtcNow -lt $end -and -not (Test-Path -LiteralPath (Join-Path $sessionRoot 'stop.request'))){Start-Sleep -Milliseconds 200}
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $sessionRoot 'trace.error.txt')
} finally {
  try {
    if($started){
        & wpr.exe -stop (Join-Path $sessionRoot 'bluetooth.etl') -instancename $instance 2>&1 | Out-File $traceLog -Append
        if($LASTEXITCODE -ne 0){
            Add-Content -LiteralPath (Join-Path $sessionRoot 'trace.error.txt') -Value ('WPR stop failed: '+$LASTEXITCODE+'; canceling only '+$instance)
            & wpr.exe -cancel -instancename $instance 2>&1 | Out-File $traceLog -Append
        }
    }
  } finally {
    if($null -ne $registryKey){
        foreach($entry in $backup){
            try {
                # Do not overwrite a different value installed by another diagnostic tool meanwhile.
                if($registryKey.GetValue($entry.Name) -eq $entry.Temporary){
                    if($entry.Present){$registryKey.SetValue($entry.Name,$entry.Value,([Microsoft.Win32.RegistryValueKind][Enum]::Parse([Microsoft.Win32.RegistryValueKind],$entry.Kind)))}
                    else{$registryKey.DeleteValue($entry.Name,$false)}
                }
            }catch{$restored=$false;Add-Content -LiteralPath (Join-Path $sessionRoot 'trace.error.txt') -Value ('Restore failed for '+$entry.Name+': '+$_)}
        }
        $registryKey.Dispose()
        try {
            & (Join-Path $PSScriptRoot 'tools\SC2LoggingRefresh.exe') 2>&1 | Out-File $traceLog -Append
            if($LASTEXITCODE -ne 0){throw 'Driver refresh returned a failure.'}
        } catch {$restored=$false;Add-Content -LiteralPath (Join-Path $sessionRoot 'trace.error.txt') -Value ('Registry restoration attempted, but driver refresh failed; verify runtime logging state: '+$_)}
    }
    if($journalCreated -and $restored){Remove-Item -LiteralPath $recoveryKey -ErrorAction SilentlyContinue}
    if($ownsMutex){$mutex.ReleaseMutex()}
    if($null -ne $mutex){$mutex.Dispose()}
    Set-Content -LiteralPath (Join-Path $sessionRoot 'trace.done') -Value ([DateTime]::UtcNow.ToString('o'))
  }
}
