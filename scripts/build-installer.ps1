param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "native\YitDesktopFold.Native.csproj"
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\publish\win-x64"))
$installerScript = Join-Path $repoRoot "installer\YitDesktopFold.iss"
$releaseDirectory = Join-Path $repoRoot "release"
$installerPath = Join-Path $releaseDirectory "YitDesktopFold-Setup-0.1.0-win-x64.exe"

if (-not $publishDirectory.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory resolved outside the repository."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $userInstall = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $userInstall) {
        $isccPath = $userInstall
    }
    else {
        throw "Inno Setup 6 compiler was not found."
    }
}
else {
    $isccPath = $iscc.Source
}

& $isccPath $installerScript
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not created: $installerPath"
}

$installer = Get-Item -LiteralPath $installerPath
$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Output "Installer: $($installer.FullName)"
Write-Output "Size: $($installer.Length) bytes"
Write-Output "SHA256: $($hash.Hash)"
