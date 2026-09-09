param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$widgetOutput = Join-Path $PSScriptRoot 'artifacts\win-x64'
dotnet publish (Join-Path $PSScriptRoot 'src\AiUsageWidget.App') -c $Configuration -r win-x64 --self-contained true -o $widgetOutput
if ($LASTEXITCODE -ne 0) { throw 'Widget publish failed.' }
dotnet publish (Join-Path $PSScriptRoot 'src\AiUsageWidget.ClaudeBridge') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $widgetOutput 'bridge')
if ($LASTEXITCODE -ne 0) { throw 'Claude bridge publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $widgetOutput -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VALIDATION.md') -Destination $widgetOutput -Force
Write-Output "起動: $widgetOutput\AiUsageWidget.exe"
