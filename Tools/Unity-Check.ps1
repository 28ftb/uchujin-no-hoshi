<#
.SYNOPSIS
  宇宙人の星：Unity を使う確認をまとめて実行する（Claude Code / Codex 共通）。

.DESCRIPTION
  -Mode TypeCheck : Unity 不要の型チェック（Tools/CompileCheck/check.sh を Git Bash で実行。dotnet SDK 8 以上が必要）
  -Mode Compile   : Unity をバッチモードで開いてコンパイルし、error CS を一覧にする
  -Mode Smoke     : バッチモード（ウィンドウなし・GPU は使う）で起動スモークテスト（タイトル〜キャラメイク〜プロローグ〜会話〜商店〜睡眠〜セーブ〜ロード）
  -Mode Visual    : 画面ありで同じスモークテストを行い、各段階を Logs/Smoke/*.png に撮影する（見た目の確認用。5〜7分）

  Unity の版は ProjectSettings/ProjectVersion.txt に合わせる。違う版の Unity では開かない（プロジェクトが別の版に変換されるため）。
  Unity Editor でこのプロジェクトを開いている間はバッチモードが使えない（同じプロジェクトは同時に1つしか開けない）。

.EXAMPLE
  pwsh Tools/Unity-Check.ps1 -Mode Compile
#>
[CmdletBinding()]
param(
    [ValidateSet('TypeCheck', 'Compile', 'Smoke', 'Visual')][string]$Mode = 'Compile',
    [string]$UnityPath,
    [int]$TimeoutMinutes = 25
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $root 'Logs'
New-Item -ItemType Directory -Force $logs | Out-Null

function Find-DotnetSdk {
    $candidates = @(
        $env:DOTNET_ROOT,
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'),
        'C:\Users\81901\Documents\Codex\UnityDevelopment\Toolchains\dotnet-10.0.400',
        'C:\Program Files\dotnet'
    ) | Where-Object { $_ -and (Test-Path (Join-Path $_ 'dotnet.exe')) }
    foreach ($c in $candidates) {
        $sdks = & (Join-Path $c 'dotnet.exe') --list-sdks 2>$null
        if ($sdks | Where-Object { [int](($_ -split '\.')[0]) -ge 8 }) { return $c }
    }
    return $null
}

if ($Mode -eq 'TypeCheck') {
    $sdk = Find-DotnetSdk
    if (-not $sdk) { throw 'dotnet SDK 8 以上が見つかりません（ランタイムだけでは型チェックできません）。' }
    $bash = 'C:\Program Files\Git\bin\bash.exe'
    if (-not (Test-Path $bash)) { throw "Git Bash が見つかりません: $bash" }
    $env:DOTNET_ROOT = $sdk
    $env:PATH = "$sdk;$env:PATH"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    & $bash -lc "cd '$($root -replace '\\','/')' && Tools/CompileCheck/check.sh"
    exit $LASTEXITCODE
}

# ---- Unity を使うモード ----
$versionLine = Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' }
$version = ($versionLine -split ':\s*', 2)[1].Trim()
if (-not $UnityPath) { $UnityPath = $env:UCHUJIN_UNITY }
if (-not $UnityPath) { $UnityPath = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path $UnityPath)) { throw "Unity $version が見つかりません: $UnityPath（Unity Hub で $version を入れるか、-UnityPath で指定）" }
if ($UnityPath -notmatch [regex]::Escape($version)) {
    throw "Unity の版がプロジェクト（$version）と違う可能性があります: $UnityPath。違う版で開くとプロジェクトが変換されるので中止しました。"
}

$lock = Join-Path $root 'Temp\UnityLockfile'
if (Test-Path $lock) {
    try { $fs = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $fs.Close() }
    catch { throw 'このプロジェクトを Unity Editor が開いています。閉じてから実行してください（同時に1つしか開けません）。' }
}

$log = Join-Path $logs ("unity_" + $Mode.ToLower() + ".log")
if (Test-Path $log) { Remove-Item $log -Force }
$smokeArgs = @('-executeMethod', 'Uchujin.EditorTools.BatchSmokeTest.Run')
$unityArgs = switch ($Mode) {
    'Compile' { @('-batchmode', '-quit', '-nographics') }
    'Smoke'   { @('-batchmode') + $smokeArgs }   # -nographics だと URP の RenderTexture（キャラメイクのプレビュー）が作れずエラーになる
    'Visual'  { $smokeArgs }
}
$unityArgs += @('-projectPath', "`"$root`"", '-logFile', "`"$log`"")
Write-Host "Unity $version で $Mode を実行します（ログ: $log）"
$p = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -PassThru
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $p.Kill()
    throw "$TimeoutMinutes 分たっても終わらないので止めました。ログ: $log"
}
$text = if (Test-Path $log) { Get-Content $log -Encoding utf8 } else { @() }

$errors = $text | Where-Object { $_ -match 'error CS\d+' } | Sort-Object -Unique
if ($errors) {
    Write-Host "== コンパイルエラー $($errors.Count) 件 ==" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host $_ }
    exit 1
}
if ($text | Where-Object { $_ -match 'Scripts have compiler errors' }) {
    Write-Host '== コンパイルエラーあり（ログを確認）==' -ForegroundColor Red
    exit 1
}
if ($Mode -eq 'Compile') {
    Write-Host "== OK: コンパイルエラー 0（終了コード $($p.ExitCode)）==" -ForegroundColor Green
    exit $p.ExitCode
}

$smoke = $text | Where-Object { $_ -match '^\[SMOKE' }
$smoke | ForEach-Object { Write-Host $_ }
$finish = $smoke | Where-Object { $_ -match '^\[SMOKE\] 終了' } | Select-Object -Last 1
if (-not $finish) { Write-Host '== スモークテストが最後まで進みませんでした（ログを確認）==' -ForegroundColor Red; exit 1 }
if ($Mode -eq 'Visual') { Write-Host "撮影: $(Join-Path $logs 'Smoke')" }
if ($finish -match '完了 エラー/例外 0 件') { Write-Host '== OK ==' -ForegroundColor Green; exit 0 }
Write-Host '== エラー・例外あり ==' -ForegroundColor Red
exit 1
