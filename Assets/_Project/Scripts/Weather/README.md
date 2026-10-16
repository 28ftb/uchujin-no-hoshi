# Weather モジュール（天候・気候・宇宙固有現象・昼夜の光）

> **2026-10-16 の変更（マップ設計 v0.2 §8.5）**：天候の最低頻度。雨・晴れ（昼）・強風・霧・無風・荒天（強風＋雨）・晴れた夜が、それぞれ 5・3・6・7・6・14・3 日（`WeatherTuning.ConditionMaxGap`）より空きそうな日は、決まった時間帯にその天気を入れる（1時間以上続いた時だけ「起きた」と数える）。湖のエリアでは朝や曇りの日に霧が溜まり、風が一段弱まる（`GetAt`）。

`Uchujin.Weather.WeatherSystem : GameSystemBase, IWeatherService`（SaveKey `"weather"`、DailyUpdateOrder 10）。仕様：`uchujin_no_hoshi_time_weather_spec_v0_2.md`。

## ファイル
| ファイル | 内容 |
|---|---|
| `WeatherSystem.cs` | 本体（IWeatherService、現在の天気、局所差、イベント、セーブ、エリア切替） |
| `ClimateProfile.cs` | シードから作る気候傾向（晴れ・降水・風・霧・現象の重み、空の色、卓越風、月、光帯の向き） |
| `WeatherPlanner.cs` | 1日の天候の予定（区間）の生成・保存、宇宙固有現象の挿入 |
| `WeatherVisuals.cs` | 太陽・月・環境光・フォグ・空の色・星空・光帯・パーティクル、屋内/地下の切替 |
| `WeatherLogApp.cs` | 天気の記録アプリ（AppId `"weather"`） |
| `WeatherTuning.cs` | 調整用の【仮数値】 |

## 実装した内容
- **気候傾向**（§13）：固定タイプではなく、晴れ・降水・風・霧・現象の発生傾向の組み合わせ。この星らしい現象を1〜2種類多めに。雨の多い星は光雨、晴れの多い星は星風がやや多い。
- **一日の途中で変わる天候**（§5）：日ごとに独立した乱数で 1〜5 区間。空模様（前の状態を引き継ぎやすい）・降水・風・霧を組み合わせる。0:00 は前日の夜の続き。1日目の午後までは穏やか（不時着の日）。
- **数日先まで生成して保存**：常に「今日＋4日」まで、過去2日分を保持。`GetPlanned(day,hour)` は生成済みの予定を返す（住民の天気読みの予報用。予報の精度は未決定なので予定そのもの）。範囲外はホストなら延長生成、ゲストは決定的な目安。
- **四季なし**（§6）。**宇宙固有現象**（§12）：光雨（夕方〜夜）、星風（午後）、逆雨（朝。直前に小雨）、星の光帯（夜）、結晶霞（早朝）、花潮（午前）。各現象は最大 12 日に1回は必ず起こる（季節待ちで進行が止まらない）。60日の試算で現象のある日は約半数。
- **局所差**（§13「地形との関係」）：`GetAt(position)` が Layout のエリアを見て、海岸は風が強め、森は朝霧、山・鉱山入口は霧が出やすい、池・川辺は無風の朝に霧、高台は見晴らしがよく風が通る。HUD・記録アプリ・演出はこの局所の天気を使う。
- **WeatherChangedEvent**：予定上の天気（全体）が変わった時に発行。
- **演出**：昼夜の太陽（Game の "Sun" を探して制御。東から昇り西へ）、夜は月明かり、色・強さ・環境光（Flat＋ambientProbe）・フォグ（見通しと雨で変化）・カメラ背景色（空の色、朝焼け・夕焼け、現象ごとの色味）、星空と1〜2個の月、星の光帯、雨・霧雨・大雨・光雨（光る色つきの粒）・逆雨（上へ昇る粒）・星風（流れる光の筋）・結晶霞（きらめく粒）・花潮（光る花粉）。パーティクルはプレイヤーの周りに追従（ワールド空間で風に流れる）。
- **屋内・地下**：`AreaChangedEvent` で切替。`ShowWeather=false` の屋内は天候演出を消し、`AmbientOverride`（無ければ既定の室内光）。**地下（`IsUnderground=true`、例 `mine:N`）では `AmbientOverride` を反映し、太陽光（Directional Light）を 0.02 までほぼ消し、影を切り、暗いフォグにする**（洞窟に天井が無くても地下らしい暗さ）。屋外に戻ると元の昼夜の光へなめらかに戻る。
- **Daylight**：昼らしさ×雲の量（0〜1）。夜の環境光と月明かりは暗すぎない値。
- **天気の記録アプリ**（§14）：屋外で体験した天気を時間帯ごとに記録（日・時刻・時間帯・天気）。日ごとに自由メモ（聞いた言葉など）を書ける。予報の和訳は出さない。`SaveScope.PlayerPlanetKnowledge`。
- **マルチ**（§16）：予定（`PlannerState`）を Planet スコープで保存・同期。ゲストは `RestoreState` で受け取り、現在の天気を作り直して変化があればイベントを出す（冪等）。時刻はコアの TimeSystem が同期。

## 未実装・簡略化
- 天候による地面の濡れ・水たまり・木の揺れ・雷は未実装。
- 気温（warmth）は目安値のみ（演出には未使用）。
- 予報の当たり外れ（精度）は未決定のため、予定そのものを返す。

## Unity 上で確認が必要な点
- 空の色・夜の暗さ・フォグの距離・パーティクルの量と見た目（URP では `Sprites/Default` → `Universal Render Pipeline/Particles/Unlit` の順でシェーダーを探す。ビルドに含める設定が必要な場合あり）。
- URP で `Camera.clearFlags = SolidColor` と `RenderSettings.ambientProbe` の反映。
- 地下・屋内に入った時の明るさ（Mining / Housing の照明と合わせて調整）。

## 他モジュールとの連携
- 提供：`IWeatherService`（`Current` / `GetPlanned` / `GetAt` / `Daylight`）、`WeatherChangedEvent`。
- 購読：`AreaChangedEvent`（コア）。
- 参照：`Game.Layout` のエリア（World モジュール）、`Game.Areas`（AreaDescriptor）。
- タブレット：`ITabletService.RegisterApp`（全システムの OnPlayStarted 後の最初の Tick で登録）。

## 調整用の仮数値
`WeatherTuning.cs`（予定の日数・区間の時間帯・現象の最大間隔・日の出入り・光の強さ・屋内/地下の光・パーティクル量・記録の保持日数）、`ClimateProfile.Create` の各範囲、`WeatherPlanner.Evolve` の確率。
