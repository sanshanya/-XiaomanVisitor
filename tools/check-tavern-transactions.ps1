param([string]$GameRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot=Resolve-XiaomanGameRoot $GameRoot
$projectRoot=Split-Path -Parent $PSScriptRoot
$checkRoot=Join-Path $projectRoot '.dev/tavern-transactions'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
$runtime=Get-XiaomanNet8Runtime
$refs=@(Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object Name -NotLike '*.Native.dll')
$refs+=Get-Item -LiteralPath (Join-Path $runtime.FullName 'netstandard.dll'),(Join-Path $runtime.FullName 'mscorlib.dll')
$output=Join-Path $checkRoot 'TavernTransactions.dll'
$compile=@('/nologo','/target:exe','/nostdlib+','/debug:portable','/nowarn:0436',('/out:'+$output))
foreach ($ref in $refs) { $compile+='/reference:'+$ref.FullName }
$compile+='/reference:'+(Join-Path $GameRoot 'Probably Stolen_Data/Managed/Newtonsoft.Json.dll')
$compile+='/reference:'+(Join-Path $projectRoot 'package/XiaomanVisitor/XiaomanVisitor.dll')
# Real Domain assembly and the entire production adapter, with only native I/O
# stubbed. This catches purchase/recovery sequencing, not a rewritten algorithm.
$compile+=Join-Path $projectRoot 'src/Runtime/XiaomanTavernTransactions.cs'
$compile+=Join-Path $projectRoot 'tests/tavern-transactions/TavernTransactionsHarness.cs'
& (Get-XiaomanCompiler) @compile
if ($LASTEXITCODE -ne 0) { throw 'Tavern transaction regression compilation failed.' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'TavernTransactions.runtimeconfig.json') -Encoding utf8
dotnet $output (Join-Path $projectRoot 'package/XiaomanVisitor') (Join-Path $GameRoot 'Probably Stolen_Data/Managed')
if ($LASTEXITCODE -ne 0) { throw 'Tavern transaction regression failed.' }
