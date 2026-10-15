# Language モジュール（宇宙語）— `Uchujin.Language`

本作の核である宇宙語の「生成（音韻・語彙・文法・数詞・名前・文字）」「意味データからの変換」「発話の表示・記録・再生・声」を担当する。
システムクラス：`Uchujin.Language.LanguageSystem : GameSystemBase, ISpeechService`（SaveKey `"language"`、DailyUpdateOrder 50）。

## ファイル構成

| ファイル | 内容 |
|---|---|
| `LanguageSystem.cs` | システム本体。ISpeechService 実装、吹き出し・記録・会話ログ・マルチの発話中継・セーブ |
| `LanguageData.cs` | セーブ DTO（Planet / PlayerPlanetKnowledge）、表示・声の設定（`LanguageSettings`、PlayerPrefs） |
| `LanguageSelfTest.cs` | 言語生成の自己検証（決定性・シード差・衝突・意味の区別・一貫性・数詞） |
| `Generation/Phonology.cs` | 音韻（子音・母音目録、音節構造、単語長、音の偏り、品詞ごとの形の傾向、名前の語尾）と語形生成 |
| `Generation/Grammar.cs` | 文法パラメータ（語順・形容詞位置・格・否定・疑問・時制・希望/依頼・所有・複数・数詞など）と生成 |
| `Generation/Lexicon.cs` | 語彙（概念ID→語）。基本語彙の固定順生成、動的語の遅延生成、衝突検出・回避、文法標識ID（`GramIds`） |
| `Generation/NumberSystem.cs` | 数詞の体系（5/6/8/10/12進）と合成規則 |
| `Generation/AlienScript.cs` | 宇宙人の書き文字（看板・手紙用。読み方とは別の表記）【仮の実装案】 |
| `Generation/LanguageDefinition.cs` | 星の言語定義（シード＋生成バージョンから決定的に生成。バージョン分岐） |
| `Rendering/SentenceRenderer.cs` | MeaningFrame → 宇宙語の語の列（SurfaceToken）への変換、記録からの文区切りの復元 |
| `Presentation/SpeechBubbleOverlay.cs` | 頭上の吹き出し（IUIOverlay）、単語クリック→辞書、推測訳の併記、表情・身振り記号（`SpeechSymbols`） |
| `Presentation/SpeechVoice.cs` | 簡単な「声」（音節ごとの手続き生成音。話者ごとの音程・疑問の上昇調） |
| `Presentation/LanguageSettingsApp.cs` | タブレットのアプリ「声と文字」（設定のみ） |
| `Presentation/LanguageDebugWindow.cs` | 開発者向けデバッグ窓（`#if UNITY_EDITOR`、F12） |

## 実装した内容（仕様との対応）

### 言語生成（宇宙語仕様 §3, §4, §14.3／企画仕様 §5.2〜5.4）
- `PreparePlanet` で `PlanetInfo.seed` と `PlanetInfo.languageGenVersion` から決定的に生成（`LanguageDefinition.Generate` がバージョンで分岐。現行は v1）。乱数は `SeededRandom`/`StableHash` のみ。
- **音韻（§3.3 発音規則, §3.6）**：子音 9〜15・母音 3〜5 の目録、音節構造（CV 必須＋V / CVC / CCV を星ごとに）、語末子音の種類、平均音節数（単語長の傾向）、長母音、音の傾向（Balanced / 濁音が多い Voiced / 柔らかい Soft / 硬い Crisp）と Zipf 風の音素の偏り。
  綴りは小文字 ASCII の **1文字＝1音素**（c/q/x は不使用）で、辞書入力・接辞の分割・宇宙文字への写しが一貫する（タブレット仕様 §3.2）。
- **品詞ごとの形の傾向**：動詞・形容詞の語末母音の傾向、名前の語尾（星ごとに3種）、間投詞の重複（「pinpin」など）。推理の手掛かりになる規則性として。
- **語彙（§4）**：
  - 基本語彙＝コア `Concepts` の定数（反射で取得するのでモジュールの追加登録に影響されない）＋文法標識＋数詞を**固定順**で一括生成。
  - それ以外（`ITEM:` `NAME:` `PLACE:` `SPECIES:`、モジュール独自概念、未知の任意のID）は初めて使われた時に生成。**必ず語ができる**。
  - **偶然の衝突を検出して回避**（同じ綴りにならない）。短い基本語どうしの「1文字違い」も避ける（聞き分けにくさ対策）。
  - 動的語どうしの衝突回避結果（bumps）はホストが Planet スコープに保存・同期し、全員で同じ綴りにする。`OnPlayStarted` でアイテム・生物種・住民名を整列順で先に生成して順序依存を減らす。
- **文法（§3.2〜3.3）**：基本語順 SOV/SVO/VSO/VOS/OVS/OSV（自然言語の頻度順に重み）、形容詞の位置、格（語順のみ／付属語／接辞、前置・後置、主語・目的語標識の有無）、否定（動詞前後の否定語／接頭辞／接尾辞）、疑問（文頭・文末の標識／動詞前置）＋疑問詞の前置、時制（付属語／接辞／時の言葉のみ）、希望（Desire）と依頼（Request）と誘い（Invitation）を別の標識で区別（動詞の前後・文頭・文末・接辞）、所有（順序＋属格標識の有無）、複数（なし／重複／接辞／付属語、数詞との併用の有無）、数詞（5/6/8/10/12進、位の順、係数と位の順、1の省略、つなぎ語）、副詞・時の言葉の位置、コピュラの有無、理由節の位置、呼びかけの位置。
  OV/VO の類型に合わせて「ありがちな組み合わせ」へ寄せ、筋の通った言語にする。**採用しない**：終助詞のニュアンス、冠詞、文法上の性（§3.4）。
- **星の名前**（`PlanetName`）、**住民名**（`ResidentName(id)` = `RenderWord("NAME:"+id)`、2〜3音節＋名前の語尾、先頭大文字）、**地名**（`PLACE:`）。

### 変換（Render）（§5, §6.2, §14.1, §14.4）
- `MeaningFrame` の act / tense / negative / predicate / agent / patient / recipient / location / timeWord / adverbs / reason と `MeaningArg` の adjectives / quantity / possessor / plural を、その星の規則で語の列にする。複数文・理由節（「〜だから」）に対応。
- 場所・受け手は目的語の「動詞と反対側」に置く（OV なら目的語の前、VO なら後）。形容詞・名詞述語はコピュラの有無で処理。
- 定型：Greeting / Farewell / Thanks / Apology（＋時の言葉・強調・呼びかけ、述語があれば内容の文を続ける）、Offer（「どうぞ」＋物）、Invitation（誘いの標識）、Exclamation。1語の発話（はい・何？など）。
- `SurfaceToken`：surface（表示）、internalId（概念ID または `GRAM:*` / `NUM:*`。**表示禁止**）、grammatical、attachedToPrevious（接辞）。文末記号は text のみに入り tokens には入らない。
- 日本語の文を単語置換する処理は一切無い。同じ意味→常に同じ文。名詞を差し替えるとその語だけが変わる（比較で推理できる §6.2、自己テストで確認）。

### 話す・記録する（Speak / CreateRecord / Replay / RecentRecords / GetRecord）
- `Speak`：話者の頭上（Renderer の高さから自動算出）に吹き出し。宇宙語テキスト＋表情・身振りの記号（`♪ … ! ? ♥ ↓↑ ←→ ●●●` 等。指で示す数は ● の数、差し出す・指さす物はアイテムの色の四角で示し名前は出さない）。Overheard は小さめ・短め。表示時間は語数・文字数に応じる。
- **聞こえる距離（14m、Broadcast 60m、看板 9m）にいる時だけ吹き出しが見え、その時に記録**（見えた発話＝会話ログの発話）。後から近づいた場合もその時点で記録。記録時に `UtteranceSpokenEvent` を Publish。
- 記録（`UtteranceRecord`）：recordId、話者、種別、text、tokens、表情、身振り、チャンネル、contextTag、relatedIds（身振りの対象も追加）、日・時刻、エリア（聞いた時のプレイヤーのエリア）、位置、天気（`IWeatherService.GetAt(pos).Summary()`）、`meaning`（意味データの複製。内部用）。
- `Speak` は聞こえなくても記録オブジェクトを返す（商店がマネピに預ける等のため）。未聴の記録・`CreateRecord` の記録は一時キャッシュ（最大400件）にあり `GetRecord` で引ける。
- 単語クリック → `DictionaryLookupRequestEvent { Surface, RecordId }`。接辞も別の語として選べる（語幹と接辞は区切らず連続表示し、マウスを乗せると別々に強調）。
- `ITabletService.ShowGlossOverlay` が true なら、各語の下に `GetPlayerGloss(surface)` を小さく斜体で併記（無い語は空欄。正解は補完しない）。
- `Replay(record, at)`：原本の text / tokens をそのまま再表示（再変換しない）。at が null なら画面下の字幕。聞いたら会話ログへ（イベントは再発行しない）。声は元の話者の声をマネピが少し真似た音程で鳴らす。
- 会話ログ：最近300件、`PlayerPlanetKnowledge` スコープで保存（古い順に並び、末尾が最新）。
- **声**（§11.2 提案）：音節ごとに AudioClip を手続き生成（母音で音色、子音で立ち上がり、話者IDと住民の系統で音程、疑問は語尾上昇）。3D 音源。設定でオフ可。看板・手紙は声を出さない。
- **宇宙人の文字**（タブレット仕様 §3）：`RenderScript(RenderedUtterance)` / `RenderScript(UtteranceRecord)`。1音素＝1字の音素文字を星ごとの字形の組（ギリシャ風・キリル風・図形記号・混合）からシードで割り当てる。日本語フォント（JIS X 0208）と欧文フォント（WGL4）の両方にある文字のみ。
  チャンネル `Sign` / `Letter`（または speakerKind `"sign"` `"letter"`）の吹き出しは宇宙文字で表示し、設定で読み方（発音表記）を併記。読み方を出さない設定では文字から読みを開示しない（クリックも無効）。

### マルチ（マルチ仕様 §13、宇宙語仕様 §12, §14.3）
- 同じシード＋同じ `languageGenVersion` なら同じ宇宙語。Planet スコープに生成バージョンと**言語の指紋**を保存し、ロード時・ゲスト受信時に一致しなければ警告ログ。
- **発話の中継**：ホストで `Speak` された発話（`relayToGuests==true` の時だけ。4引数版は true）は、意味データ（`SpeechRelayEntry`：話者・種別・チャンネル・位置・`Utterance`）を一時メッセージ `WorldAuthority.BroadcastTransient("speech.relay", json)` で送る（セーブしない・状態スナップショットには載せない）。ゲストは `RegisterContent` で登録した受信処理で自分の規則で変換し、吹き出し・（聞こえる距離なら）記録・イベントにする。会話（Direct / Overheard）は `Utterance.addresseeId` がローカルのプレイヤーなら Direct、それ以外は Overheard。呼びかけ・看板・手紙・マネピの再現は元のチャンネルのまま。ホストに届いた "speech.relay"（ゲストから）は無視する。
- 各端末がそれぞれ生成する発話（独り言・立ち話など）は `Speak(..., relayToGuests: false)` で中継しない（二重表示を避ける）。
- 旧版の Planet 状態（セーブ）にある中継バッファ（`relaySerial` / `relay`）は読み込み時に無視される（JsonUtility は型に無いフィールドを読み飛ばす）。
- 辞書の自動配布はしない（本モジュールは辞書を持たない）。会話ログは各プレイヤーの PlayerPlanetKnowledge。

### 開発者向け検証
- `LanguageDebugWindow`（`#if UNITY_EDITOR` 限定、**F12**）：文法・音韻・文字の説明、語彙一覧（概念ID→綴り・音節・試行回数）、任意の意味の変換結果と語の内訳（internalId 付き）、自分の頭上で話させる表示テスト、別シードとの比較、自己テスト、会話ログの内部表示。
- `LanguageSelfTest.RunAll(out report)`：Unity API に依存しない静的検証。Unity 外の検証ハーネス（.NET 8 + 参照アセンブリ）で実行し全項目成功を確認済み（60個の星で語順6種・格3・否定4・疑問3・時制3・希望依頼5・複数4・基数5、全語彙の綴り重複0、主語/目的語・否定・時制・疑問・受け手・希望/依頼の区別、1〜300 の数詞がすべて異なる）。

## 未実装・簡略化した内容（理由）
- **宇宙文字の表示経路**：`ISpeechService` に `RenderScript` が無いため、他モジュールが看板を宇宙文字で表示するには `Speak(u, 看板Transform, SpeechChannel.Sign, "sign")` か `Replay` を使う必要がある（下記「コアへの追加要望」）。
- **吹き出しクリックと道具の使用の重なり**：コアの `PlayerInteractor` が IMGUI のクリックを考慮しないため、吹き出しの単語を左クリックすると道具も使われる可能性がある。判定関数 `LanguageSystem.IsPointerOverBubble(screenPos)` は用意済み（コアへの追加要望）。
- **設定の置き場所**：コアの設定画面に追加口が無いため、タブレットのアプリ「声と文字」で切り替える（タブレットが無い場合は既定値：声オン、読み方オン）。
- 読み込み中のゲストに届いた中継発話は、読み込み完了後に届いた順でまとめて表示される（古い発話を捨てる時刻判定はしていない）。
- 動的語どうしの偶然の衝突は「先に使われた方が勝つ」ため、ホストの記録（bumps）が届く前にゲストが独自にその語を使うと一時的に綴りがずれることがある（確率はごく低い。届いた時点でホストに合わせる）。
- 多義語の意図的な統合（同音異義語）は採用していない（§4.2 未決定。偶然の衝突は全て回避）。
- 敬語・話し方の個性（§5.2 未決定）は未実装。住民ごとの違いは発話の内容・量・声の高さで表す想定。
- 長い名詞句への接頭辞型の格標識は「名詞句の先頭の語」に付く（形容詞が前に来る星では形容詞に付く）。規則としては一貫している。

## Unity 上で確認が必要な点
- 吹き出しの見た目（大きさ・位置・重なり回避・しっぽ）、単語ボタンのホバー表示、推測訳の併記の読みやすさ。
- 記号・宇宙文字がフォント（Yu Gothic UI / Hiragino / Noto / Arial）で文字化けしないか（特に図形記号セット ◇☆∀∇⌒♯ など、英語環境の Windows/Mac）。
- 声の聞こえ方（音量、うるささ、話者ごとの高さの差、Overheard の頻度とのバランス、AudioClip の生成負荷）。
- 吹き出しの表示時間、聞こえる距離 14m が村の広さ・カメラ距離に対して適切か。
- ゲストでの中継吹き出しの遅延・頻度（一時メッセージ "speech.relay"）、Direct / Overheard の振り分け（`addresseeId` を設定している呼び出し側の確認）。
- 会話ログ300件（meaning 込み）のセーブサイズ。
- F12 デバッグ窓の表示（UNITY_EDITOR のみ）。

## 他モジュールとの連携点
- **実装する契約**：`ISpeechService`（Render / RenderWord / ResidentName / PlanetName / Speak / CreateRecord / Replay / RecentRecords / GetRecord / RenderNumber）。
- **一時メッセージ**：チャンネル `"speech.relay"`（ホスト→ゲスト。受信処理は `RegisterContent` で登録）。
- **呼ぶ契約**：`ITabletService`（`ShowGlossOverlay`、`GetPlayerGloss`、`RegisterApp`）、`IResidentDirectory`（話者の色・系統・中継時の Transform、住民名の先行生成）、`ICreatureService`（種IDの先行生成）、`IWeatherService`（記録の天気）、`ItemRegistry`（アイテム語の先行生成・差し出す物の色）。
- **発行するイベント**：`UtteranceSpokenEvent`（ローカルプレイヤーが聞いた時。Replay では発行しない）、`DictionaryLookupRequestEvent`（吹き出しの単語クリック）。
- **購読するイベント**：なし。
- **タブレットアプリ**：`language.settings`（声と文字）。
- **アイテムID・WorldCommand**：なし。
- **使い方の約束**：
  - 住民・商店・クエスト・ロケット・ストーリーは意味データ（`Utterance`）を組み立て、`IResidentDirectory.Speak`（住民）または `ISpeechService.Speak` で話す。身振りの再生は住民モジュール、吹き出し・記録・声は本モジュール。
  - 看板・掲示：`Speak(u, 看板のTransform, SpeechChannel.Sign, "sign")`（調べた時など）。手紙：`CreateRecord(u, SpeechChannel.Letter, "letter", pos)` → 読む時に `Replay(record, null)`。
  - マネピ：`Speak` の戻り値（聞こえなくても返る）を `IManepiService.LeaveMessage` に渡し、再生は `Replay(record, マネピのTransform)`。
  - `UtteranceRecord.meaning` は JsonUtility の仕様で、保存・同期後は null だった引数が「concept が空の MeaningArg」になる。参照する側は `concept` の空チェックをすること。
  - `internalId` / `meaning` はプレイヤーに表示しないこと（デバッグ窓を除く）。

## 調整用の仮数値の場所
- `LanguageSystem`：聞こえる距離、ログ件数、未聴キャッシュ、中継チャンネル名（`SpeechRelayChannel`）、吹き出しの表示時間。
- `SpeechBubbleOverlay`：吹き出しの幅・文字サイズ・色・余白。`SpeechSymbols`：表情・身振りの記号の対応。
- `SpeechVoice`：サンプルレート、音節の長さ、語・文の間、音量距離、プール数。
- `Phonology`：子音・母音の候補、語末子音候補、音節構造・単語長・傾向の確率（`GenerateV1` 内）。
- `GrammarParams`：語順の重み（`WordOrderWeights`）、基数と重み（`NumberBases` / `NumberBaseWeights`）、各文法パラメータの確率（`GenerateV1` 内）。
- `Lexicon`：紛らわしさ回避の長さ・回数、最大試行回数。`GramIds.All`（生成順。追加は末尾に）。
- `AlienScript`：字形の組（`GlyphSets`）、文末記号の写し。
- `LanguageSettings`：既定値（声オン・音量0.35・読み方併記オン・大きさ1.0・記号オン）。
- **注意**：生成ロジック（上記の確率・候補・順序）を変えると既存の星の言葉が変わる。リリース後に変える場合は `PlanetInfo.CurrentLanguageGenVersion` を上げ、`LanguageDefinition.Generate` に新バージョンの分岐を追加し、旧バージョンの生成関数を残すこと。

## コアへの追加要望（統括者向け）
1. **ISpeechService に宇宙文字の口**（看板・手紙を各モジュールが宇宙文字で描けるように）：
   ```csharp
   // ISpeechService に追加
   string RenderScript(RenderedUtterance rendered);   // 実装済み：LanguageSystem.RenderScript
   bool ShowScriptReading { get; }                     // 実装案：=> LanguageSettings.ShowSignReading
   ```
2. **オーバーレイ上のクリックで道具を使わない**判定：
   ```csharp
   // Core/UI/UIWindow.cs
   public interface IPointerBlockingOverlay { bool IsPointerOver(Vector2 screenPosition); }
   // Core/UI/UIManager.cs
   public static bool PointerOverOverlay(Vector2 screenPos)
   { foreach (var o in overlays) if (o is IPointerBlockingOverlay b && b.IsPointerOver(screenPos)) return true; return false; }
   // Core/Player/PlayerInteractor.cs の IsPointerOverHud() に追加
   if (UIManager.PointerOverOverlay(GameInput.MousePosition)) return true;
   ```
   （`SpeechBubbleOverlay.IsPointerOver(Vector2)` が同じシグネチャで実装済み。インターフェース追加後に `: IUIOverlay, IPointerBlockingOverlay` とするだけで有効になる。）
3. **設定画面への追加口**（声のオン／オフ等を Esc メニューの設定にも出したい）：`public static readonly List<Action> UIManager.ExtraSettingsDrawers` を `PauseMenuWindow.DrawSettings` の末尾で呼ぶ。
4. ~~ホスト→ゲストの一時イベント送信~~（対応済み：`WorldAuthority.RegisterTransientHandler` / `BroadcastTransient`。発話の中継はこれへ移行）。
5. **UtteranceRecord に `public List<int> sentenceStarts`**（現在は text と tokens の照合で文区切りを復元している）。
6. ~~Utterance に宛先~~（対応済み：`Utterance.addresseeId`。中継先の Direct / Overheard の判定に使用）。
