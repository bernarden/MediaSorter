$ErrorActionPreference = "Stop"

Write-Host "Setting things up..." -ForegroundColor Magenta
Write-Host "Installing dotnet-warp... " -NoNewline
dotnet tool update --global dotnet-warp | Out-Null
Write-Host "Done"

if (($IsWindows -or $ENV:OS) -and !(Test-Path "$PSScriptRoot/ResourceHacker.exe" -PathType Leaf)) {
    Write-Host "Downloading Resource Hacker... " -NoNewline
    $ResourceHackerArchiveUrl = "http://www.angusj.com/resourcehacker/resource_hacker.zip"
    $ResourceHackerArchivePath = "$PSScriptRoot/resource_hacker.zip"
    (New-Object System.Net.WebClient).DownloadFile($ResourceHackerArchiveUrl, $ResourceHackerArchivePath)
    Write-Host "Done"

    Write-Host "Extracting Resource Hacker... " -NoNewline
    Expand-Archive -Path $ResourceHackerArchivePath -DestinationPath "$PSScriptRoot/ResourceHacker"
    Write-Host "Done"

    Write-Host "Cleaning up... " -NoNewline
    Move-Item "$PSScriptRoot/ResourceHacker/ResourceHacker.exe" -Destination $PSScriptRoot
    Remove-Item "$PSScriptRoot/resource_hacker.zip"
    Remove-Item "$PSScriptRoot/ResourceHacker" -Recurse -Force
    Write-Host "Done" 
}
Write-Host "-------------------- DONE --------------------`n" -ForegroundColor Magenta

Write-Host "Generating executable..." -ForegroundColor Green
Push-Location -Path $PSScriptRoot/../Vima.MediaSorter
dotnet-warp -p:OutputType=Exe -p:PublishTrimmed=true -v
Move-Item "Vima.MediaSorter.exe" -Destination $PSScriptRoot -Force
Pop-Location
Write-Host "-------------------- DONE --------------------`n" -ForegroundColor Green

if ($IsWindows -or $ENV:OS) {
    Push-Location -Path $PSScriptRoot
    Write-Host "Adding icon to the executable..." -NoNewline -ForegroundColor Green
    Start-Process -FilePath ResourceHacker.exe -ArgumentList "-open Vima.MediaSorter.exe -save Vima.MediaSorter-WithIcon.exe -action addskip -res ../Vima.MediaSorter/Resources/icon.ico -mask ICONGROUP,MAINICON" -Passthru -Wait
    Remove-Item "ResourceHacker.ini"
    Remove-Item "Vima.MediaSorter.exe"
    Rename-Item "Vima.MediaSorter-WithIcon.exe" "Vima.MediaSorter.exe" 
    Write-Host "-------------------- DONE --------------------`n" -ForegroundColor Green
    Pop-Location
}