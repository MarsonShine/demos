[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AppPublishDir,

    [Parameter(Mandatory)]
    [string]$EngineDir,

    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Require-Directory([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description is missing: $Path"
    }
}

$installerDirectory = Split-Path -Parent $PSCommandPath
$scriptPath = Join-Path $installerDirectory 'installer.iss'
$publishDirectory = (Resolve-Path -LiteralPath $AppPublishDir).Path
$engineDirectory = (Resolve-Path -LiteralPath $EngineDir).Path

$publishedExecutable = Join-Path $publishDirectory 'VideoAnalysisDesktop.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    $clickOnceManifest = Get-ChildItem -LiteralPath $publishDirectory -Filter '*.application' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $clickOnceManifest) {
        throw "This is a ClickOnce publish directory ($($clickOnceManifest.Name)), not a self-contained Folder publish. " +
            'In Visual Studio choose the Folder-win-x64 profile, then point this script at that folder.'
    }
    throw "Published desktop executable is missing: $publishedExecutable"
}

Require-File (Join-Path $publishDirectory 'Assets\pipeline_config.json') 'Published pipeline configuration'
Require-File (Join-Path $engineDirectory 'engine.manifest.json') 'Staged engine manifest'
Require-File (Join-Path $engineDirectory 'python\python.exe') 'Staged embedded Python'
Require-File (Join-Path $engineDirectory 'ffmpeg\ffmpeg.exe') 'Staged FFmpeg'
Require-File (Join-Path $engineDirectory 'ffmpeg\ffprobe.exe') 'Staged FFprobe'
Require-Directory (Join-Path $engineDirectory 'models') 'Staged model directory'

$compiler = Get-Command iscc -ErrorAction SilentlyContinue
if ($null -eq $compiler) {
    throw 'Inno Setup Compiler (iscc) was not found. Install Inno Setup 6 on the release computer, then run this script again.'
}

Write-Host 'Building the end-user installer...'
& $compiler.Source "/DAppPublishDir=$publishDirectory" "/DEngineDir=$engineDirectory" "/DMyAppVersion=$Version" $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$outputDirectory = Join-Path $installerDirectory 'output'
$installer = Get-ChildItem -LiteralPath $outputDirectory -Filter "VideoAnalysisDesktop-Setup-x64-$Version.exe" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $installer) {
    throw "Inno Setup reported success but no installer was found in $outputDirectory."
}

Write-Host "Installer created: $($installer.FullName)"
