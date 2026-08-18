# Produces one self-contained BashPrinterAgent.exe with no runtime to install first.
# Run from the repo root on a machine with the .NET 8 SDK.

$ErrorActionPreference = "Stop"

dotnet publish src/BashPrinterAgent/BashPrinterAgent.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -o publish

Copy-Item src/BashPrinterAgent/appsettings.example.json publish/ -Force

Write-Host ""
Write-Host "Built: publish\BashPrinterAgent.exe"
Write-Host "Copy appsettings.example.json to appsettings.json next to it and fill in apiBaseUrl and agentKey."
