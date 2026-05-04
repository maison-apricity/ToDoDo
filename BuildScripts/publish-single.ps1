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
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish\single

Write-Host ""
Write-Host "Publish complete: .\publish\single\ToDoDo.exe"
