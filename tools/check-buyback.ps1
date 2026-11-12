param([string]$GameRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
$compiler = Get-XiaomanCompiler
$package = Join-Path $projectRoot 'package\XiaomanVisitor'
$runtime = Get-XiaomanNet8Runtime
$checks = Join-Path $projectRoot '.dev\buyback\checks'
New-Item -ItemType Directory -Path $checks -Force | Out-Null
$checker = Join-Path $checks 'CheckBuybackTest.dll'
$checkArgs = @('/nologo','/target:exe','/nostdlib+',('/out:' + $checker))
foreach ($ref in Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object { $_.Name -notlike '*.Native.dll' }) { $checkArgs += '/reference:' + $ref.FullName }
foreach ($ref in @('netstandard.dll','mscorlib.dll')) { $checkArgs += '/reference:' + (Join-Path $runtime.FullName $ref) }
foreach ($ref in @('Assembly-CSharp.dll','0Harmony.dll','UnityEngine.CoreModule.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.TextRenderingModule.dll')) { $checkArgs += '/reference:' + (Join-Path $managed $ref) }
$checkArgs += Join-Path $PSScriptRoot 'buyback-test\CheckBuybackTest.cs'
& $compiler @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal checker compilation failed' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checks 'CheckBuybackTest.runtimeconfig.json') -Encoding utf8
$nativeCheckArgs = @($checker, $managed, $package)
dotnet @nativeCheckArgs
if ($LASTEXITCODE -ne 0) { throw 'Native rehearsal validation failed' }
