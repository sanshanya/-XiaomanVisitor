param(
    [string]$GameRoot,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dev-environment.ps1')
$GameRoot = Resolve-XiaomanGameRoot $GameRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameRoot 'Probably Stolen_Data\Managed'
$compiler = Get-XiaomanCompiler
$package = Join-Path $projectRoot 'package\XiaomanVisitor'
$refs = @('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','0Harmony.dll','Newtonsoft.Json.dll',
    'UnityEngine.CoreModule.dll','UnityEngine.AudioModule.dll','UnityEngine.InputLegacyModule.dll','Febucci.TextAnimator.Runtime.dll',
    'Febucci.TextAnimator.TMP.Runtime.dll','Unity.TextMeshPro.dll','UnityEngine.UI.dll','UnityEngine.ImageConversionModule.dll')
$compilerArgs = @('/nologo','/target:library','/nostdlib+','/langversion:latest','/optimize+','/deterministic+',
    ('/out:' + (Join-Path $package 'XiaomanVisitor.dll')))
foreach ($name in $refs) { $compilerArgs += '/reference:' + (Join-Path $managed $name) }
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed.' }
Write-Output ('Built ' + (Join-Path $package 'XiaomanVisitor.dll'))
if ($Install) {
    $installPath = Join-Path $GameRoot 'Mods\XiaomanVisitor'
    $sourceDll = Join-Path $package 'XiaomanVisitor.dll'
    $installedDll = Join-Path $installPath 'XiaomanVisitor.dll'
    if ((Test-Path -LiteralPath $installedDll) -and
        ((Get-FileHash -LiteralPath $sourceDll).Hash -ne (Get-FileHash -LiteralPath $installedDll).Hash)) {
        $gameExe = Join-Path $GameRoot 'Probably Stolen.exe'
        $runningGame = Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $gameExe }
        if ($runningGame) {
            throw 'The running game has the mod DLL loaded. Save and exit Probably Stolen before installing this update.'
        }
    }
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    foreach ($fileName in @('manifest.xml','XiaomanVisitor.dll','preview.png')) {
        $sourceFile = Join-Path $package $fileName
        $installedFile = Join-Path $installPath $fileName
        if ((Test-Path -LiteralPath $installedFile) -and
            ((Get-FileHash -LiteralPath $sourceFile).Hash -eq (Get-FileHash -LiteralPath $installedFile).Hash)) {
            continue
        }
        Copy-Item -LiteralPath $sourceFile -Destination $installedFile -Force
    }
    foreach ($folder in @('Textures','Portraits','Localization','Sounds','Books','Voices','Letters','Licenses')) {
        Copy-Item -LiteralPath (Join-Path $package $folder) -Destination $installPath -Recurse -Force
    }
    foreach ($oldVoice in Get-ChildItem -LiteralPath (Join-Path $installPath 'Sounds') -Filter 'xm_voice_*.wav' -File) {
        if (-not (Test-Path -LiteralPath (Join-Path $package ('Sounds\' + $oldVoice.Name)))) {
            Remove-Item -LiteralPath $oldVoice.FullName -Force
        }
    }
    # These six views belonged to 0.5.0's removed drink popup, never to the story letter.
    $booksRoot = [IO.Path]::GetFullPath((Join-Path $installPath 'Books'))
    foreach ($drinkKey in @('whale_dream','sunset_blue','bluebird','cosmo_zone','sky_after_rain','rainy_blue')) {
        $oldView = [IO.Path]::GetFullPath((Join-Path $booksRoot ('feeling_' + $drinkKey + '.json')))
        if ([IO.Path]::GetDirectoryName($oldView) -ne $booksRoot) { throw 'Unexpected obsolete view path.' }
        if (Test-Path -LiteralPath $oldView) { Remove-Item -LiteralPath $oldView -Force }
    }
    foreach ($fileName in @('使用说明.txt','音乐来源.txt','配音说明.txt')) {
        $readme = Join-Path $package $fileName
        if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination $installPath -Force }
    }
    Write-Output ('Installed ' + $installPath)
}
