$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$projectRoot=Split-Path -Parent $PSScriptRoot
$checkRoot=Join-Path $projectRoot '.dev/voice-flow'
New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
# Compile the production wait methods unchanged against a small deterministic
# dialogue/audio scheduler. Native compatibility is checked separately.
$source=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Runtime/XiaomanVoice.cs') -Raw
$start=$source.IndexOf('        internal void WrapFlow(')
$end=$source.IndexOf('        internal void Shutdown()', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production voice wait methods.' }
$methods=$source.Substring($start,$end-$start)
$generated='using System; using System.Collections; using System.Collections.Generic; namespace XiaomanVisitor { public partial class XiaomanVoicePlayer {'+$methods+'} }'
$generatedPath=Join-Path $checkRoot 'VoiceWaitMethods.cs'
Set-Content -LiteralPath $generatedPath -Value $generated -Encoding utf8
# Exercise the actual menu guard as well, without starting Unity or a save.
$menuSource=Get-Content -LiteralPath (Join-Path $projectRoot 'src/Runtime/XiaomanTavern.cs') -Raw
$menuStart=$menuSource.IndexOf('        private Action TavernMenuAction(')
$menuEnd=$menuSource.IndexOf('        private void CloseTavernMenu()', $menuStart)
if ($menuStart -lt 0 -or $menuEnd -le $menuStart) { throw 'Cannot locate production menu callback guard.' }
$menuGenerated='using System; namespace XiaomanVisitor { public sealed partial class XiaomanMod {'+$menuSource.Substring($menuStart,$menuEnd-$menuStart)+'} }'
$menuPath=Join-Path $checkRoot 'MenuActionMethods.cs'
Set-Content -LiteralPath $menuPath -Value $menuGenerated -Encoding utf8
$runtime=Get-XiaomanNet8Runtime
$references=@(Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object Name -NotLike '*.Native.dll')
$references+=Get-Item -LiteralPath (Join-Path $runtime.FullName 'netstandard.dll'),(Join-Path $runtime.FullName 'mscorlib.dll')
$output=Join-Path $checkRoot 'VoiceFlowChecks.dll'
$compileArgs=@('/nologo','/target:exe','/nostdlib+',('/out:'+$output))
foreach ($reference in $references) { $compileArgs+='/reference:'+$reference.FullName }
$compileArgs+=$generatedPath
$compileArgs+=$menuPath
$compileArgs+=Join-Path $projectRoot 'tests/voice-flow/VoiceFlowHarness.cs'
$compileArgs+=Join-Path $projectRoot 'tests/voice-flow/MenuFlowHarness.cs'
& (Get-XiaomanCompiler) @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Voice flow regression compilation failed.' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checkRoot 'VoiceFlowChecks.runtimeconfig.json') -Encoding utf8
dotnet $output
if ($LASTEXITCODE -ne 0) { throw 'Voice flow regression failed.' }
