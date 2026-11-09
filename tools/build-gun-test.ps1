param([string]$GameRoot, [switch]$Install)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$gunRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
$compiler = Get-XiaomanCompiler
$version = ([xml](Get-Content -LiteralPath (Join-Path $gunRoot 'package\XiaomanVisitor\manifest.xml') -Raw -Encoding UTF8)).Mod.ModVersion
$output = Join-Path $gunRoot ('release\gun-test-' + $version)
$stage = Join-Path $output 'XiaomanGunTest'
if (Test-Path -LiteralPath (Join-Path $stage 'manifest.xml')) {
    $previous = [xml](Get-Content -LiteralPath (Join-Path $stage 'manifest.xml') -Raw -Encoding UTF8)
    if ($previous.Mod.ID -ne 'sansm.xiaoman.gun_test') { throw 'Unexpected existing test package' }
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$refs = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','0Harmony.dll',
    'UnityEngine.CoreModule.dll','UnityEngine.AudioModule.dll','UnityEngine.InputLegacyModule.dll','Febucci.TextAnimator.Runtime.dll',
    'Febucci.TextAnimator.TMP.Runtime.dll','Unity.TextMeshPro.dll','UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.ImageConversionModule.dll','Unity.2D.PixelPerfect.dll')
$sources = @('src\Domain\GunReaction.cs','src\Domain\PortraitTiming.cs','src\Domain\PortraitDetailRules.cs',
    'src\Runtime\XiaomanPortraitRuntime.cs','src\Runtime\XiaomanPortraitAssets.cs','src\Runtime\XiaomanPortraitDetail.cs',
    'src\Runtime\XiaomanNativeRendering.cs','src\Runtime\XiaomanGunReaction.cs','src\Runtime\XiaomanGunPortraits.cs',
    'tools\gun-test\XiaomanGunTestMod.cs')
$compileArgs = @('/nologo','/target:library','/nostdlib+','/langversion:latest','/optimize+','/deterministic+',('/out:' + (Join-Path $stage 'XiaomanGunTest.dll')))
foreach ($ref in $refs) { $compileArgs += '/reference:' + (Join-Path $managed $ref) }
foreach ($source in $sources) { $compileArgs += Join-Path $gunRoot $source }
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Independent gun-test compilation failed' }
$python = Join-Path $gunRoot '.venv\Scripts\python.exe'
& $python (Join-Path $PSScriptRoot 'gun-test\package_gun_test.py') --stage $stage
if ($LASTEXITCODE -ne 0) { throw 'Independent test asset packaging failed' }

$runtime = Get-XiaomanNet8Runtime
$checks = Join-Path $gunRoot '.dev\gun-test-checks'
New-Item -ItemType Directory -Path $checks -Force | Out-Null
$checker = Join-Path $checks 'CheckGunTest.dll'
$checkArgs = @('/nologo','/target:exe','/nostdlib+',('/out:' + $checker))
foreach ($ref in Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object { $_.Name -notlike '*.Native.dll' }) { $checkArgs += '/reference:' + $ref.FullName }
foreach ($ref in @('netstandard.dll','mscorlib.dll')) { $checkArgs += '/reference:' + (Join-Path $runtime.FullName $ref) }
$checkArgs += '/reference:' + (Join-Path $managed 'Assembly-CSharp.dll')
$checkArgs += '/reference:' + (Join-Path $managed 'UnityEngine.CoreModule.dll')
$checkArgs += Join-Path $PSScriptRoot 'gun-test\CheckGunTest.cs'
& $compiler @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'Test-package checker compilation failed' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checks 'CheckGunTest.runtimeconfig.json') -Encoding utf8
dotnet $checker $managed $stage
if ($LASTEXITCODE -ne 0) { throw 'Native independent-mod validation failed' }
& $python (Join-Path $PSScriptRoot 'gun-test\package_gun_test.py') --stage $stage --zip
if ($LASTEXITCODE -ne 0) { throw 'Test ZIP failed validation' }

if ($Install) {
    $destination = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods\XiaomanGunTest'))
    $mods = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
    if ([IO.Path]::GetDirectoryName($destination) -ne $mods -or [IO.Path]::GetFileName($destination) -ne 'XiaomanGunTest') { throw 'Unexpected test install path' }
    if (Test-Path -LiteralPath (Join-Path $destination 'manifest.xml')) {
        $installed = [xml](Get-Content -LiteralPath (Join-Path $destination 'manifest.xml') -Raw -Encoding UTF8)
        if ($installed.Mod.ID -ne 'sansm.xiaoman.gun_test') { throw 'Refusing to overwrite another mod' }
        if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) {
            $oldDll = Join-Path $destination 'XiaomanGunTest.dll'
            $newDll = Join-Path $stage 'XiaomanGunTest.dll'
            if (-not (Test-Path -LiteralPath $oldDll) -or (Get-FileHash -LiteralPath $oldDll).Hash -ne (Get-FileHash -LiteralPath $newDll).Hash) {
                throw 'Save and exit before replacing a changed test DLL'
            }
        }
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
        if (-not $target.StartsWith($destination + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test package path' }
        if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath $target).Hash) { continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Installed test file mismatch' }
    }
    Write-Output ('Installed independent test only: ' + $destination)
    if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) { Write-Output 'Only changed assets were replaced. Restart the game to load them.' }
}
Write-Output ('Test ZIP: ' + (Join-Path $output ('XiaomanGunTest-' + $version + '.zip')))
