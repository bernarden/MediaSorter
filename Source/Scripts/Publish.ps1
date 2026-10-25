[CmdletBinding()]
param(
    [Parameter(Position = 0, HelpMessage = "Target runtime identifier (e.g. win-x64, osx-arm64, linux-x64).")]
    [string]$Runtime
)

$ErrorActionPreference = "Stop"

$outputDir = $PSScriptRoot
$projectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\Vima.MediaSorter\Vima.MediaSorter.csproj")).Path
if (-not (Test-Path -Path $projectPath -PathType Leaf)) {
    throw "Project file not found at: '$projectPath'"
}

# Extract version from project file
[xml]$proj = Get-Content -Path $projectPath -Raw
$version = ($proj.Project.PropertyGroup.AssemblyVersion | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
if (-not $version) {
    $version = ($proj.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
}
if (-not $version) {
    $version = "0.0.0"
}

# Extract short commit hash from git if available
$commitHash = "unknown"
try {
    $gitHash = & git rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitHash)) {
        $commitHash = $gitHash.Trim()
    }
}
catch {
    # Git is either unavailable or the directory is not a git repository
}

# Auto-detect OS and architecture if runtime was not specified
if (-not $Runtime) {
    $os = if ($PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows) {
        "win"
    } elseif ($IsMacOS) {
        "osx"
    } else {
        "linux"
    }

    $arch = if ([Type]::GetType("System.Runtime.InteropServices.RuntimeInformation") -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    } elseif ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        "arm64"
    } else {
        "x64"
    }

    $Runtime = "$os-$arch"
}

$versionInfo = "v${version}_${commitHash}"

Write-Host "Publishing self-contained executable..." -ForegroundColor Magenta
Write-Host "Version: $versionInfo | Target: $Runtime | Trimming: Enabled | Compression: Enabled" -ForegroundColor Cyan

$publishArgs = @(
    "publish", $projectPath,
    "-r", $Runtime,
    "-c", "Release",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebuggerSupport=false",
    "-p:EventSourceSupport=false",
    "-p:EnableUnsafeBinaryFormatterSerialization=false",
    "-o", $outputDir
)
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Clean up PDB debug symbols from output directory
Get-ChildItem -Path $outputDir -Filter "*.pdb" | Remove-Item -Force

# Rename executable to include version and short commit hash
$isWindowsTarget = $Runtime.StartsWith("win")
$defaultExeName = if ($isWindowsTarget) { "Vima.MediaSorter.exe" } else { "Vima.MediaSorter" }
$versionedExeName = if ($isWindowsTarget) { "Vima.MediaSorter_${versionInfo}.exe" } else { "Vima.MediaSorter_${versionInfo}" }

$defaultExePath = Join-Path -Path $outputDir -ChildPath $defaultExeName
$versionedExePath = Join-Path -Path $outputDir -ChildPath $versionedExeName

if (-not (Test-Path -Path $defaultExePath)) {
    throw "Published executable was not found at: '$defaultExePath'"
}

if (Test-Path -Path $versionedExePath) {
    Remove-Item -Path $versionedExePath -Force
}

Rename-Item -Path $defaultExePath -NewName $versionedExeName -Force

if (Test-Path -Path $versionedExePath) {
    $sizeMb = [math]::Round((Get-Item -Path $versionedExePath).Length / 1MB, 2)
    Write-Host "`n-------------------- DONE --------------------" -ForegroundColor Green
    Write-Host "Executable generated: $versionedExePath ($sizeMb MB)" -ForegroundColor Green
}
