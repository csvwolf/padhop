param([Parameter(Mandatory=$true)][string]$Python)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach($script in Get-ChildItem $root -Recurse -Filter '*.ps1' | Where-Object {$_.FullName -notmatch '[\\/](bin|dist|\.deps)[\\/]'}){
 $tokens=$null;$errors=$null
 [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName,[ref]$tokens,[ref]$errors)
 if($errors){throw ($script.FullName+': '+($errors -join ', '))}
}
foreach($name in @('auto_capture.py','clean_capture.py')){& $Python -X utf8 (Join-Path $root ('tools\capture\'+$name)) --self-test;if($LASTEXITCODE){throw 'Python test failed'}}
& $Python -X utf8 (Join-Path $root 'tools\capture\analyze.py') self-test
if($LASTEXITCODE){throw 'Analysis tests failed'}
'PASS: PowerShell syntax and Python capture tests.'

& (Join-Path $root 'scripts\Local-Signing.ps1') -Action SelfTest
