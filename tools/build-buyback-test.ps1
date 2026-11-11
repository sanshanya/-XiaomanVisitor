param([string]$GameRoot, [switch]$Install)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
$compiler = Get-XiaomanCompiler
$python = Join-Path $projectRoot '.venv\Scripts\python.exe'
& $python (Join-Path $PSScriptRoot 'prepare_buyback.py') --check
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal inputs are stale' }
& $python -m unittest discover -s (Join-Path $projectRoot 'tests') -p test_buyback.py
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal contract checks failed' }
$version = & $python (Join-Path $PSScriptRoot 'prepare_buyback.py') --version
if ($LASTEXITCODE -ne 0) { throw 'Cannot read rehearsal version' }
$output = Join-Path $projectRoot ('release\buyback-test-' + $version)
$stage = Join-Path $output 'XiaomanBuybackTest'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& $python (Join-Path $PSScriptRoot 'prepare_buyback.py') --check --stage $stage
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal packaging failed' }
$compileArgs = @('/nologo','/target:library','/nostdlib+','/langversion:latest','/optimize+','/deterministic+','/nowarn:0649',
    ('/out:' + (Join-Path $stage 'XiaomanBuybackTest.dll')))
foreach ($ref in @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','0Harmony.dll',
    'UnityEngine.CoreModule.dll','UnityEngine.AudioModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.JSONSerializeModule.dll',
    'UnityEngine.UI.dll','UnityEngine.UIModule.dll','UnityEngine.TextRenderingModule.dll','Unity.TextMeshPro.dll',
    'Febucci.TextAnimator.Runtime.dll','Febucci.TextAnimator.TMP.Runtime.dll')) { $compileArgs += '/reference:' + (Join-Path $managed $ref) }
$compileArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'side-stories\permanent-buyback') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal compilation failed' }

$runtime = Get-XiaomanNet8Runtime
$checks = Join-Path $projectRoot '.dev\buyback\checks'
New-Item -ItemType Directory -Path $checks -Force | Out-Null
$checker = Join-Path $checks 'CheckBuybackTest.dll'
$checkArgs = @('/nologo','/target:exe','/nostdlib+',('/out:' + $checker))
foreach ($ref in Get-ChildItem -LiteralPath $runtime.FullName -Filter 'System.*.dll' -File | Where-Object { $_.Name -notlike '*.Native.dll' }) { $checkArgs += '/reference:' + $ref.FullName }
foreach ($ref in @('netstandard.dll','mscorlib.dll')) { $checkArgs += '/reference:' + (Join-Path $runtime.FullName $ref) }
foreach ($ref in @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll')) { $checkArgs += '/reference:' + (Join-Path $managed $ref) }
$checkArgs += Join-Path $PSScriptRoot 'buyback-test\CheckBuybackTest.cs'
& $compiler @checkArgs
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal checker compilation failed' }
@{runtimeOptions=@{tfm='net8.0';framework=@{name='Microsoft.NETCore.App';version=$runtime.Name}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $checks 'CheckBuybackTest.runtimeconfig.json') -Encoding utf8
dotnet $checker $managed $stage
if ($LASTEXITCODE -ne 0) { throw 'Native rehearsal validation failed' }
& $python (Join-Path $PSScriptRoot 'prepare_buyback.py') --check --stage $stage --zip
if ($LASTEXITCODE -ne 0) { throw 'Rehearsal ZIP validation failed' }

if ($Install) {
    $mods = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
    $destination = [IO.Path]::GetFullPath((Join-Path $mods 'XiaomanBuybackTest'))
    if ([IO.Path]::GetDirectoryName($destination) -ne $mods -or [IO.Path]::GetFileName($destination) -ne 'XiaomanBuybackTest') { throw 'Invalid isolated install path' }
    if (Test-Path -LiteralPath $destination) {
        $manifestPath = Join-Path $destination 'manifest.xml'
        if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Refusing to overwrite an unknown directory' }
        $previous = [xml](Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8)
        if ($previous.Mod.ID -ne 'sansm.xiaoman.buyback_test') { throw 'Refusing to overwrite a different mod' }
        $installedDll = Join-Path $destination 'XiaomanBuybackTest.dll'
        $gameProcess = Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue
        if ($gameProcess -and (Test-Path -LiteralPath $installedDll) -and
            (Get-FileHash -LiteralPath $installedDll).Hash -ne (Get-FileHash -LiteralPath (Join-Path $stage 'XiaomanBuybackTest.dll')).Hash) {
            throw 'Save and exit the game before replacing the rehearsal DLL; checked ZIP is ready'
        }
        $backup = Join-Path $projectRoot ('.dev\install-backups\buyback-test\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        Copy-Item -LiteralPath $destination -Destination $backup -Recurse
        foreach ($file in Get-ChildItem -LiteralPath $destination -Recurse -File) {
            $relative = $file.FullName.Substring($destination.Length + 1)
            if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup ('XiaomanBuybackTest\' + $relative))).Hash) { throw 'Rehearsal backup mismatch' }
        }
        Write-Output ('Backed up previous rehearsal: ' + $backup)
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
        if (-not $target.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid install file path' }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Installed rehearsal file mismatch' }
    }
    $installed = [xml](Get-Content -LiteralPath (Join-Path $destination 'manifest.xml') -Raw -Encoding UTF8)
    if ($installed.Mod.ModVersion -ne $version -or (Get-ChildItem -LiteralPath $destination -File -Recurse).Count -ne $files.Count) { throw 'Installed rehearsal differs from checked package' }
    Write-Output ('Installed independent rehearsal ' + $version + ': ' + $destination + ' (' + $files.Count + ' files verified)')
}
Write-Output ('Test ZIP: ' + (Join-Path $output ('XiaomanBuybackTest-' + $version + '.zip')))
