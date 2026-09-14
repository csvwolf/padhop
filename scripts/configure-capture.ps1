param([Parameter(Mandatory=$true)][string]$Python,[Parameter(Mandatory=$true)][string]$Parser)
$ErrorActionPreference='Stop'
$Python=(Resolve-Path -LiteralPath $Python).Path
$Parser=(Resolve-Path -LiteralPath $Parser).Path
if([IO.Path]::GetExtension($Python) -ne '.exe' -or [IO.Path]::GetFileName($Parser) -ne 'BTETLParse.exe'){throw 'Supply Python.exe and BTETLParse.exe paths'}
$s=Get-AuthenticodeSignature $Parser
if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'Microsoft'){throw 'The Bluetooth parser must have a valid Microsoft signature'}
& $Python -c 'import sys; assert sys.version_info >= (3,9)'
if($LASTEXITCODE){throw 'Python 3.9 or newer is required'}
$folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PadHop'
New-Item -ItemType Directory -Force $folder | Out-Null
@{Python=$Python;Parser=$Parser} | ConvertTo-Json | Set-Content (Join-Path $folder 'capture-tools.json') -Encoding UTF8
'Optional capture tools configured for this user. No binary was redistributed or trusted certificate installed.'
