# Shared local configuration. No credentials or game binaries belong in Git.
function Resolve-XiaomanGameRoot {
    param([string]$GameRoot)
    $configPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.dev\local.json'
    if (-not $GameRoot -and (Test-Path -LiteralPath $configPath)) {
        $GameRoot = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).game_root
    }
    if (-not $GameRoot) { $GameRoot = 'E:\app\steam\steamapps\common\Probably Stolen' }
    $GameRoot = [IO.Path]::GetFullPath($GameRoot)
    $assembly = Join-Path $GameRoot 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assembly)) {
        throw 'Probably Stolen was not found. Run tools/setup-dev.ps1 -GameRoot <installation directory>.'
    }
    return $GameRoot
}

function Get-XiaomanCompiler {
    $compiler = Join-Path $PSScriptRoot 'compilers\tasks\net472\csc.exe'
    $toolchain = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dev-toolchain.json') -Raw | ConvertFrom-Json
    if (-not (Test-Path -LiteralPath $compiler)) {
        throw 'Pinned compiler is missing. Run tools/setup-dev.ps1.'
    }
    if ((Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne $toolchain.compiler.csc_sha256) {
        throw 'Compiler hash differs from dev-toolchain.json. Restore the pinned compiler before building.'
    }
    return $compiler
}

function Get-XiaomanNet8Runtime {
    $dotnet = Get-Command dotnet -ErrorAction Stop
    $dotnetRoot = Split-Path -Parent $dotnet.Source
    $runtimeRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'
    $runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory |
        Where-Object { $_.Name -match '^8\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $runtime) { throw '.NET 8 x64 runtime is required; the full SDK is optional.' }
    return $runtime
}
