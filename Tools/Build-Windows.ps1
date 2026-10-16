# 宇宙人の星の Windows ビルド（バッチ）。成功すると Builds/Windows をゲームランチャーへ公開し、公開の完了まで待つ。
# 使い方:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Build-Windows.ps1
#   検証用（公開しない）: $env:GAMEHUB_SKIP_PUBLISH='1' を付けて実行
# Unity Editor でこのプロジェクトを開いている間は実行できない（プロジェクトのロックのため）。
param([string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity が見つかりません: $UnityPath" }
$logPath = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Path $logPath -Force | Out-Null
$unityLog = Join-Path $logPath 'unity-build.log'
$publishResultPath = 'C:\GameHubPublisher\work\publish-after-build-uchujin-no-hoshi.json'

function Read-PublishResult([string]$Path) {
    # 公開結果は日本語入りの UTF-8（BOM なし）。Windows PowerShell 5 は既定で Shift-JIS として読み、JSON が壊れるので UTF-8 を指定する
    try { Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
}

# 前回の公開がまだビルドを読んでいる間は、ビルドを上書きしない。
$deadline = [DateTimeOffset]::Now.AddMinutes(15)
while (Test-Path -LiteralPath $publishResultPath) {
    $previous = Read-PublishResult $publishResultPath
    if ($previous -and (-not $previous.startedAt -or $previous.finishedAt)) { break }
    if ([DateTimeOffset]::Now -gt $deadline) { throw '前回のランチャー公開が終わっていません。' }
    Start-Sleep -Seconds 1
}

$buildStarted = [DateTimeOffset]::Now
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $projectRoot + '"'),
    '-executeMethod', 'Uchujin.EditorTools.BuildTools.BuildWindows', '-logFile', ('"' + $unityLog + '"'))
$unity = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
$unity.WaitForExit()
if ($unity.ExitCode -ne 0) { throw "Unity のビルドが失敗しました（終了コード $($unity.ExitCode)）。ログ: $unityLog" }
Get-Content -LiteralPath (Join-Path $logPath 'build-results.txt') -Encoding UTF8

$buildLog = Get-Content -LiteralPath $unityLog -Raw -Encoding UTF8
$skip = [regex]::Match($buildLog, 'GAMEHUB PUBLISH: SKIP[^\r\n]*')
if ($skip.Success) { Write-Output $skip.Value; return }
if ($buildLog -notmatch 'GAMEHUB PUBLISH: STARTED') { Write-Warning 'GAMEHUB PUBLISH: FAIL（公開を開始できませんでした。Unity のログを確認）'; exit 1 }

$deadline = [DateTimeOffset]::Now.AddMinutes(15)
while ($true) {
    $current = Read-PublishResult $publishResultPath
    if ($current -and $current.startedAt -and [DateTimeOffset]$current.startedAt -ge $buildStarted -and $current.finishedAt) {
        if ($current.exitCode -ne 0) { Write-Warning "GAMEHUB PUBLISH: FAIL（終了コード $($current.exitCode)）$($current.error)"; exit 1 }
        Write-Output "GAMEHUB PUBLISH: PASS（$($current.distribution.result) / $($current.r2.result)）$($current.distribution.version)"
        break
    }
    if ([DateTimeOffset]::Now -gt $deadline) { Write-Warning 'GAMEHUB PUBLISH: FAIL（15分以内に公開が終わりませんでした）'; exit 1 }
    Start-Sleep -Seconds 1
}
