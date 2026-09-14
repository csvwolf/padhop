param(
 [ValidateSet('Enable','Renew','Disable','RemoveTrust','Status','SelfTest')][string]$Action='Status',
 [ValidateSet('auto','en','zh-CN')][string]$Language='auto',
 [switch]$AcceptLocalTrust
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Language.ps1')
$files=@('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe','input-mode.txt')
function NoLinks([string]$path){
 $p=[IO.Path]::GetFullPath($path)
 while($p){
  if(Test-Path -LiteralPath $p){if((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw (T '路径不能包含符号链接或目录联接。')}}
  $parent=Split-Path $p -Parent;if($parent -eq $p){break};$p=$parent
 }
}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function StatePath([string]$root){Join-Path $root '.local-signing\state.json'}
function SaveState([string]$root,$state){
 $path=StatePath $root;$tmp=$path+'.tmp'
 $state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tmp -Encoding UTF8
 Move-Item -LiteralPath $tmp -Destination $path -Force
}
function LoadState([string]$root){
 $path=StatePath $root;NoLinks $path
 if(!(Test-Path -LiteralPath $path)){return $null}
 $s=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 if($s.Product -ne 'PadHop' -or $s.Thumbprint -notmatch '^[A-Fa-f0-9]{40}$' -or $s.Subject -notmatch '^CN=PadHop Local Only [a-f0-9-]{36}$'){throw (T '本机签名记录无效；未修改证书。')}
 return $s
}
function RemoveOwnTrust($state){
 $path='Cert:\LocalMachine\Root\'+$state.Thumbprint
 if(Test-Path $path){
  $cert=Get-Item $path
  if($cert.Subject -ne $state.Subject){throw (T '证书身份与本机签名记录不一致。')}
  Remove-Item -LiteralPath $path
 }
}
function RestoreFiles([string]$root,$state){
 # Preflight all files before replacing any of them; never restore across an unknown upgrade.
 foreach($name in $files){
  $original=Join-Path $root ('.local-signing\original\'+$name);$current=Join-Path $root $name
  NoLinks $original;NoLinks $current
  $expected=$state.Original.$name
  if(!$expected -or (Hash $original) -ne $expected){throw ((T '原始备份校验失败：')+$name)}
  if(Test-Path $current){$h=Hash $current;if($h -ne $expected -and $h -ne $state.Signed.$name){throw ((T '程序已被其他版本修改，拒绝覆盖：')+$name+(T '。可用 RemoveTrust 撤销证书后重装。'))}}
 }
 foreach($name in $files){Copy-Item -LiteralPath (Join-Path $root ('.local-signing\original\'+$name)) -Destination (Join-Path $root $name) -Force}
}
function CleanState([string]$root){
 foreach($name in $files){
  foreach($sub in @('original','staged')){
   $p=Join-Path $root ('.local-signing\'+$sub+'\'+$name);NoLinks $p
   if(Test-Path -LiteralPath $p){Remove-Item -LiteralPath $p -Force}
  }
 }
 foreach($name in @('state.json','state.json.tmp')){$p=Join-Path $root ('.local-signing\'+$name);NoLinks $p;if(Test-Path $p){Remove-Item -LiteralPath $p -Force}}
 # Remove only empty, fixed directories. Unknown files are retained.
 foreach($sub in @('.local-signing\original','.local-signing\staged','.local-signing')){
  $p=Join-Path $root $sub;NoLinks $p
  if((Test-Path $p) -and !(Get-ChildItem -LiteralPath $p -Force)){Remove-Item -LiteralPath $p}
 }
}
function VerifyPayload([string]$root){
 $manifest=Join-Path $root 'local-signing\payload.json';NoLinks $manifest
 $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
 if($m.Product -ne 'PadHop' -or $m.Version -ne (Get-Item (Join-Path $root 'PadHop.exe')).VersionInfo.ProductVersion){throw (T '自签组件与安装版本不一致，请重装同一版本。')}
 foreach($name in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $source=if($name -eq 'PadHop.Input.exe'){Join-Path $root 'local-signing\PadHop.Input.UIAccess.exe'}else{Join-Path $root $name}
  NoLinks $source
  if((Hash $source) -ne $m.Hashes.$name){throw ((T '自签前文件校验失败：')+$name)}
 }
}
function AssertProtected([string]$root){
 NoLinks $root
 $allowed=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
 $write=[Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
 $items=@(Get-Item -LiteralPath $root)+@(Get-ChildItem -LiteralPath $root -Recurse -Force)
 foreach($item in $items){
  NoLinks $item.FullName
  $acl=Get-Acl -LiteralPath $item.FullName
  if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $allowed){throw (T '安装目录所有者不受保护，请重新安装到 Program Files。')}
  foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
   if($rule.AccessControlType -eq 'Allow' -and !($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and ($rule.FileSystemRights -band $write) -and $rule.IdentityReference.Value -notin $allowed){throw (T '安装文件允许普通账户写入，拒绝启用 UIAccess。')}
  }
 }
}
if($Action -eq 'SelfTest'){
 $root=Join-Path ([IO.Path]::GetTempPath()) ('PadHop-signing-test-'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Force (Join-Path $root '.local-signing\original') | Out-Null
 $s=@{Original=@{};Signed=@{}}
 try {
  foreach($n in $files){$o=Join-Path $root ('.local-signing\original\'+$n);$c=Join-Path $root $n;Set-Content $o 'original';Set-Content $c 'signed';$s.Original[$n]=Hash $o;$s.Signed[$n]=Hash $c}
  Set-Content (Join-Path $root 'PadHop.Engine.exe') 'unrelated upgrade'
  $rejected=$false;try{RestoreFiles $root $s}catch{$rejected=$true}
  if(!$rejected -or (Hash (Join-Path $root 'PadHop.exe')) -ne $s.Signed['PadHop.exe']){throw 'Restore preflight did not protect unrelated files'}
  Set-Content (Join-Path $root 'PadHop.Engine.exe') 'signed'
  RestoreFiles $root $s
  foreach($n in $files){if((Hash (Join-Path $root $n)) -ne $s.Original[$n]){throw 'Restore mismatch'}}
  Set-Content (Join-Path $root '.local-signing\original\PadHop.exe') 'corrupted'
  $rejected=$false;try{RestoreFiles $root $s}catch{$rejected=$true}
  if(!$rejected){throw 'Corrupt backup accepted'}
  'PASS: local-signing restore, unknown-upgrade protection, corrupt-backup rejection. No certificates or input changed.'
 }finally{CleanState $root;foreach($n in $files){Remove-Item -LiteralPath (Join-Path $root $n) -Force};Remove-Item -LiteralPath $root}
 return
}
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$expected=Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'PadHop'
if($root -ne $expected){throw (T '请先运行 install.exe，再使用 Program Files\PadHop 内的本机自签脚本。')}
NoLinks $root
$state=LoadState $root
if($Action -eq 'Status'){
 if($state){(T '本机签名记录：')+$state.Thumbprint;(T '到期时间：')+$state.Expires;(T '状态：')+$state.Status}else{(T '尚未启用本机自签。')}
 return
}
$admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){throw (T '请以管理员身份运行 PowerShell，再执行此脚本。')}
if(Get-Process PadHop,PadHop.Engine,PadHop.Input -ErrorAction SilentlyContinue){throw (T '请先从托盘退出 Talaria。')}
AssertProtected $root
$transcript=Join-Path $root 'local-signing-last.log';NoLinks $transcript
Start-Transcript -LiteralPath $transcript -Force | Out-Null
try {
if($Action -eq 'Renew'){
 if(!$AcceptLocalTrust){throw (T '续期需要明确同意本机信任变更（-AcceptLocalTrust）。')}
 if(!$state -or $state.Status -ne 'Enabled'){throw (T '没有可续期的本机签名，请重新运行安装程序。')}
 RestoreFiles $root $state
 RemoveOwnTrust $state
 CleanState $root
 $state=$null
}
if($Action -in @('Disable','RemoveTrust')){
 if(!$state){(T '没有需要撤销的本机签名。');return}
 if($Action -eq 'Disable'){RestoreFiles $root $state}
 RemoveOwnTrust $state
 CleanState $root
 (T '已移除本次本机信任。')+$(if($Action -eq 'Disable'){(T '已恢复自签前文件。')}else{(T '未覆盖程序；请重新安装标准版后使用。')})
 return
}
if($state){throw (T '已经存在本机签名或未完成记录。请先执行 -Action Disable，再重新启用。')}
if((Get-Content (Join-Path $root 'input-mode.txt') -Raw).Trim() -ne 'standard'){throw (T '仅对标准版启用本机自签。')}
VerifyPayload $root
if(!$AcceptLocalTrust){
 Write-Host (T '将向本机（所有用户）的受信任根证书库添加一张仅用于代码签名的本机证书。仅签署 Talaria 的三个程序；私钥不可导出并在本次操作后删除。证书一年后到期，届时需重新签名。不会修改 UAC、Secure Boot 或 Steam 权限。')
 if((Read-Host (T '明确同意此信任变更请输入 YES')) -cne 'YES'){throw (T '用户取消，未修改证书。')}
}
$cert=$null;$state=$null
try {
 foreach($sub in @('original','staged')){New-Item -ItemType Directory -Force (Join-Path $root ('.local-signing\'+$sub)) | Out-Null}
 $original=@{};$signed=@{}
 foreach($n in $files){Copy-Item -LiteralPath (Join-Path $root $n) -Destination (Join-Path $root ('.local-signing\original\'+$n)) -Force;$original[$n]=Hash (Join-Path $root $n)}
 foreach($n in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $source=if($n -eq 'PadHop.Input.exe'){Join-Path $root 'local-signing\PadHop.Input.UIAccess.exe'}else{Join-Path $root $n}
  Copy-Item -LiteralPath $source -Destination (Join-Path $root ('.local-signing\staged\'+$n)) -Force
 }
 $subject='CN=PadHop Local Only '+[guid]::NewGuid().ToString()
 $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName (T 'Talaria 本机自签（非公共发行证书）') -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
 $state=@{Product='PadHop';Thumbprint=$cert.Thumbprint;Subject=$subject;Expires=$cert.NotAfter.ToString('o');Original=$original;Signed=$signed;Status='Preparing'}
 SaveState $root $state
 $store=New-Object Security.Cryptography.X509Certificates.X509Store('Root','LocalMachine')
 try{$store.Open('ReadWrite');$public=New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (,$cert.RawData);$store.Add($public)}finally{$store.Close()}
 foreach($n in @('PadHop.exe','PadHop.Engine.exe','PadHop.Input.exe')){
  $p=Join-Path $root ('.local-signing\staged\'+$n)
  $sig=Set-AuthenticodeSignature -FilePath $p -Certificate $cert -HashAlgorithm SHA256
  if($sig.Status -ne 'Valid'){throw ((T '签名验证失败：')+$n+' '+$sig.Status)}
  $signed[$n]=Hash $p
 }
 $mode=Join-Path $root '.local-signing\staged\input-mode.txt';Set-Content $mode 'local-uiaccess' -Encoding ASCII;$signed['input-mode.txt']=Hash $mode
 SaveState $root $state
 foreach($n in $files){Copy-Item -LiteralPath (Join-Path $root ('.local-signing\staged\'+$n)) -Destination (Join-Path $root $n) -Force}
 $probe=Start-Process (Join-Path $root 'PadHop.Input.exe') -ArgumentList '--check-uiaccess' -WindowStyle Hidden -PassThru
 try {
  if(!$probe.WaitForExit(15000)){$probe.Kill();throw (T 'UIAccess 检测超时。')}
  if($probe.ExitCode -ne 0){throw (T 'Windows 未授予 UIAccess；可能被设备策略限制。')}
 } finally {$probe.Dispose()}
 $state.Status='Enabled';SaveState $root $state
 (T '已启用本机 UIAccess。证书指纹：')+$cert.Thumbprint
}catch{
 $failure=$_
 if($state){
  try{try{RestoreFiles $root $state}finally{RemoveOwnTrust $state};CleanState $root}catch{Write-Warning ((T '自动还原未完成，请保留 .local-signing 并执行 Disable 或 RemoveTrust：')+$_)}
 }
 throw $failure
}finally{
 if($cert){
  try{Remove-Item -LiteralPath ('Cert:\CurrentUser\My\'+$cert.Thumbprint) -DeleteKey -ErrorAction Stop}
  catch{if($state){RemoveOwnTrust $state;try{RestoreFiles $root $state;CleanState $root}catch{Write-Warning (T '证书信任已撤销，但文件还原未完成。')}};throw (T '无法删除本次签名私钥，已撤销本机信任；请检查当前用户证书库。')}
 }
}

} finally {Stop-Transcript | Out-Null}
