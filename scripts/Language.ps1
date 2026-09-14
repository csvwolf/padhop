# Shared display-text localization. Only a fixed language code is read from user settings.
$resolvedLanguage=if($Language -in @('en','zh-CN')){$Language}else{'auto'}
if($resolvedLanguage -eq 'auto'){
 $preference=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PadHop\ui-language.txt'
 if(Test-Path -LiteralPath $preference){$saved=(Get-Content -LiteralPath $preference -Raw).Trim();if($saved -in @('en','zh-CN')){$resolvedLanguage=$saved}}
}
if($resolvedLanguage -eq 'auto'){$resolvedLanguage=if([Globalization.CultureInfo]::CurrentUICulture.Name.StartsWith('zh')){'zh-CN'}else{'en'}}
$catalogPath=Join-Path $PSScriptRoot 'languages\en.json'
if(!(Test-Path -LiteralPath $catalogPath)){$catalogPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'languages\en.json'}
$translationMap=Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
function T([string]$text){if($resolvedLanguage -eq 'en'){$translated=$translationMap.$text;if($null -ne $translated){return $translated}};return $text}
