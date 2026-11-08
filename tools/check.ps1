param([string]$GameRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
$compiler = Get-XiaomanCompiler
$checkRoot = Join-Path $projectRoot '.dev\checks'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
$checker = Join-Path $checkRoot 'CheckMod.dll'
$runtime = Get-XiaomanNet8Runtime
$refs = @(Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object { $_.Name -notlike '*.Native.dll' })
$refs += Get-Item -LiteralPath (Join-Path $runtime.FullName 'netstandard.dll'),(Join-Path $runtime.FullName 'mscorlib.dll')
$argsForCompiler = @('/nologo','/target:exe','/nostdlib+',('/out:' + $checker))
foreach ($reference in $refs) { $argsForCompiler += '/reference:' + $reference.FullName }
$argsForCompiler += '/reference:' + (Join-Path $managed 'Assembly-CSharp.dll')
$argsForCompiler += '/reference:' + (Join-Path $managed 'UnityEngine.CoreModule.dll')
$argsForCompiler += '/reference:' + (Join-Path $managed 'Unity.2D.PixelPerfect.dll')
$argsForCompiler += '/reference:' + (Join-Path $managed '0Harmony.dll')
$argsForCompiler += '/reference:' + (Join-Path $managed 'Newtonsoft.Json.dll')
$argsForCompiler += '/reference:' + (Join-Path $projectRoot 'package\XiaomanVisitor\XiaomanVisitor.dll')
$argsForCompiler += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' -File | Sort-Object FullName | Select-Object -ExpandProperty FullName)
& $compiler @argsForCompiler
if ($LASTEXITCODE -ne 0) { throw 'Checker compilation failed.' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'CheckMod.runtimeconfig.json') -Encoding utf8
dotnet $checker $managed (Join-Path $projectRoot 'package\XiaomanVisitor')
if ($LASTEXITCODE -ne 0) { throw 'Native compatibility or runtime regression checks failed. See the preceding output.' }
