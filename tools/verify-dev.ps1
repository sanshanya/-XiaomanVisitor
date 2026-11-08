param([string]$GameRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Run tools/setup-dev.ps1 first.' }
Get-XiaomanCompiler | Out-Null
Get-XiaomanNet8Runtime | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -GameRoot $GameRoot
& (Join-Path $PSScriptRoot 'check.ps1') -GameRoot $GameRoot
foreach ($name in @('check_assets.py','check_portrait_detail.py','check_handwriting.py','check_voice_audio.py','test_voice_pipeline.py','prepare_tavern_scene.py')) {
    & $python (Join-Path $PSScriptRoot $name)
    if ($LASTEXITCODE -ne 0) { throw ('Validation failed: ' + $name) }
}
Write-Output 'PASS: build, native scanner, story rules, assets, handwriting, portraits and offline voice checks. No installation or upload performed.'
