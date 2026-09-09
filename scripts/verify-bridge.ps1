$ErrorActionPreference = 'Stop'
$widgetWorkspace = Split-Path $PSScriptRoot -Parent
$widgetTestRoot = Join-Path $widgetWorkspace ('artifacts\bridge-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $widgetTestRoot | Out-Null
$widgetBackup = @{ originalStatusLine = @{type='command';command='cat'}; installedCommand='test' } | ConvertTo-Json
Set-Content -LiteralPath (Join-Path $widgetTestRoot 'claude-integration.json') -Value $widgetBackup
$widgetInput = @{session_id='bridge-integration-test';prompt='転送確認だけのテスト入力';rate_limits=@{five_hour=@{used_percentage=37;resets_at=1900000000}}} | ConvertTo-Json -Depth 5 -Compress
$widgetInfo = [System.Diagnostics.ProcessStartInfo]::new()
$widgetInfo.FileName = Join-Path $widgetWorkspace 'artifacts\win-x64\bridge\AiUsageWidget.ClaudeBridge.exe'
$widgetInfo.ArgumentList.Add('--root')
$widgetInfo.ArgumentList.Add($widgetTestRoot)
$widgetInfo.UseShellExecute=$false
$widgetInfo.CreateNoWindow=$true
$widgetInfo.RedirectStandardInput=$true
$widgetInfo.RedirectStandardOutput=$true
$widgetInfo.RedirectStandardError=$true
$widgetInfo.StandardInputEncoding=[System.Text.UTF8Encoding]::new($false)
$widgetInfo.StandardOutputEncoding=[System.Text.UTF8Encoding]::new($false)
$widgetProcess=[System.Diagnostics.Process]::Start($widgetInfo)
$widgetOutputTask=$widgetProcess.StandardOutput.ReadToEndAsync()
$widgetErrorTask=$widgetProcess.StandardError.ReadToEndAsync()
$widgetProcess.StandardInput.Write($widgetInput)
$widgetProcess.StandardInput.Close()
if (-not $widgetProcess.WaitForExit(15000)) { $widgetProcess.Kill($true); throw 'Bridge timed out.' }
if ($widgetOutputTask.Result -cne $widgetInput) { throw 'Original statusline stdin/stdout was not preserved.' }
$widgetFiles=@(Get-ChildItem -LiteralPath (Join-Path $widgetTestRoot 'claude') -Filter '*.json')
if ($widgetFiles.Count -ne 1) { throw 'Expected one session snapshot.' }
$widgetSnapshot=Get-Content -LiteralPath $widgetFiles[0].FullName -Raw | ConvertFrom-Json
if ($widgetSnapshot.windows[0].remainingPercent -ne 63) { throw 'Unexpected collected usage.' }
if ((Get-Content -LiteralPath $widgetFiles[0].FullName -Raw) -match 'prompt') { throw 'Conversation data leaked to snapshot.' }
$widgetProcess.Dispose()
Write-Output 'PASS: bridge stdin/stdout, Unicode, quota extraction, and conversation exclusion.'
