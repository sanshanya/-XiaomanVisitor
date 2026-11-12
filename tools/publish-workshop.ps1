param(
    [ValidateSet('check','inspect','content','publish','metadata','gallery','verify')][string]$Action='check',
    [string]$GameRoot,
    [string]$ExpectedSteamId='',
    [ValidateSet('schinese','english','japanese')][string]$Language='schinese'
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot=Resolve-XiaomanGameRoot $GameRoot
$projectRoot=Split-Path -Parent $PSScriptRoot
$output=Join-Path $PSScriptRoot 'workshop-uploader'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$managed=Join-Path $GameRoot 'Probably Stolen_Data\Managed'
foreach ($name in @('com.rlabrecque.steamworks.net.dll','Newtonsoft.Json.dll')) {
    Copy-Item -LiteralPath (Join-Path $managed $name) -Destination (Join-Path $output $name) -Force
}
Copy-Item -LiteralPath (Join-Path $GameRoot 'Probably Stolen_Data\Plugins\x86_64\steam_api64.dll') -Destination (Join-Path $output 'steam_api64.dll') -Force
$runtime=Get-XiaomanNet8Runtime
$references=@(Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object Name -NotLike '*.Native.dll')
$references+=Get-Item -LiteralPath (Join-Path $runtime.FullName 'netstandard.dll'),(Join-Path $runtime.FullName 'mscorlib.dll')
$compileArgs=@('/noconfig','/nologo','/target:exe','/platform:x64','/nostdlib+','/langversion:latest',('/out:'+(Join-Path $output 'WorkshopPublisher.dll')))
foreach ($reference in $references) { $compileArgs+='/reference:'+$reference.FullName }
foreach ($name in @('com.rlabrecque.steamworks.net.dll','Newtonsoft.Json.dll')) { $compileArgs+='/reference:'+(Join-Path $output $name) }
$compileArgs+=Join-Path $PSScriptRoot 'WorkshopPublisher.cs'
& (Get-XiaomanCompiler) @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Workshop uploader compilation failed.' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'WorkshopPublisher.runtimeconfig.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $output 'steam_appid.txt') -Value '4348910' -Encoding ascii
$publisherArguments=@($Action,$projectRoot,$ExpectedSteamId,$Language)
Push-Location -LiteralPath $output
try { dotnet (Join-Path $output 'WorkshopPublisher.dll') @publisherArguments; $publisherExitCode=$LASTEXITCODE }
finally { Pop-Location }
if ($publisherExitCode -ne 0) { throw ('Workshop uploader exited with code '+$publisherExitCode) }
