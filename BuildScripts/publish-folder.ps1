$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot\..

taskkill /IM ToDoDo.exe /F 2>$null
Remove-Item .\publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\bin -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish .\ToDoDo.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish\folder

Write-Host ""
Write-Host "Publish complete: .\publish\folder\ToDoDo.exe"
