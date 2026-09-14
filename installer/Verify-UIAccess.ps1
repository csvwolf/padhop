$ErrorActionPreference='Stop'
$publishers=@()
foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
 $sig=Get-AuthenticodeSignature (Join-Path $PSScriptRoot $name)
 if($sig.Status -ne 'Valid'){exit 1}
 $publishers+=$sig.SignerCertificate.Thumbprint
}
if(@($publishers | Select-Object -Unique).Count -ne 1){exit 2}
exit 0
