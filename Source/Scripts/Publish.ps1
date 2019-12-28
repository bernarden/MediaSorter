# Install/Upgrade dotnet-warp tool.
dotnet tool update --global dotnet-warp

# Publish the app.
Push-Location -Path $PSScriptRoot/../Vima.MediaSorter
dotnet-warp -p:OutputType=Exe -p:PublishTrimmed=true -v
mv *.exe $PSScriptRoot -Force
Pop-Location