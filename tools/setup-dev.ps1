param([string]$GameRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$runtime = Get-XiaomanNet8Runtime
$toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dev-toolchain.json') -Raw | ConvertFrom-Json
$uv = Get-Command uv -ErrorAction Stop
Get-Command git -ErrorAction Stop | Out-Null
& git lfs version
if ($LASTEXITCODE -ne 0) { throw 'Install Git LFS before initializing the development environment.' }

$venv = Join-Path $projectRoot '.venv'
$python = Join-Path $venv 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    & $uv.Source venv --python $toolchain.python $venv
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the pinned Python environment.' }
}
& $python -c 'import sys; assert sys.version_info[:2] == (3, 12), "Python 3.12 is required"'
if ($LASTEXITCODE -ne 0) { throw 'The existing virtual environment uses a different Python version.' }
& $uv.Source pip sync --python $python --link-mode copy --require-hashes (Join-Path $projectRoot 'requirements.lock')
if ($LASTEXITCODE -ne 0) { throw 'Python dependency restoration failed.' }

$compiler = Join-Path $PSScriptRoot 'compilers\tasks\net472\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $downloads = Join-Path $projectRoot '.dev\downloads'
    New-Item -ItemType Directory -Path $downloads -Force | Out-Null
    $archive = Join-Path $downloads ('roslyn-' + $toolchain.compiler.version + '.zip')
    if (-not (Test-Path -LiteralPath $archive)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $toolchain.compiler.url -OutFile $archive -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $toolchain.compiler.archive_sha256) {
        throw 'Compiler archive hash mismatch. Do not use this download.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $PSScriptRoot 'compilers') -Force
}
Get-XiaomanCompiler | Out-Null
$localRoot = Join-Path $projectRoot '.dev'
New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
@{game_root=$GameRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $localRoot 'local.json') -Encoding utf8
@{
    python=(& $python -c 'import platform; print(platform.python_version())')
    dotnet_runtime=$runtime.Name
    compiler=$toolchain.compiler.version
    game_assembly_sha256=(Get-FileHash -LiteralPath (Join-Path $GameRoot 'Probably Stolen_Data\Managed\Assembly-CSharp.dll')).Hash
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $localRoot 'environment.json') -Encoding utf8
if (Test-Path -LiteralPath (Join-Path $projectRoot '.git')) {
    & git -C $projectRoot lfs install --local
    if ($LASTEXITCODE -ne 0) { throw 'Git LFS hook setup failed.' }
}
Write-Output 'Development environment ready. Run tools/verify-dev.ps1 to build and validate. No game installation or Steam upload was performed.'
