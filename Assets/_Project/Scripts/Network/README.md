# Network（マルチプレイ：EOS P2P）— `Uchujin.Network`

マルチプレイ仕様 v0.2、基本決定 v0.2 §6（**通信は EOS の P2P で確定**）にもとづく実装です。
常時稼働の専用サーバーや全世界共通の市場は前提にしていません。EOS は「通信路」として使うだけで、
NPC やワールドセーブの同期はすべてこのモジュール（ホスト権威）が行います。

## ファイル構成

| ファイル | 内容 |
|---|---|
| `NetConfig.cs` | 調整用の定数（人数・同期間隔・タイムアウト・EOS のソケット名など）【仮数値】 |
| `NetworkSystem.cs` | システム本体（`GameSystemBase, INetworkService, INetworkBridge`）。ライフサイクル、受信の振り分け、リモートのアバター、位置、身振り。`NetworkRunner`（常駐 MonoBehaviour） |
| `NetworkSystem.Host.cs` | ホスト：星を開く／閉じる、参加受付、スナップショット、差分同期、コマンド実行、ゲストの持ち物ミラー、`INetworkBridge`、権限・追い出し |
| `NetworkSystem.Guest.cs` | ゲスト：友達の星へ行く（UDP／EOS）、読み込み、状態の適用、Tx の適用と受領台帳、退出と持ち物の保存 |
| `NetworkSystem.Sleep.cs` | 睡眠の投票（全員同意で翌朝／不同意なら個人の休息） |
| `NetworkSystem.Dictionary.cs` | 辞書の任意の手渡し（全部／選んだ語だけ、別冊／統合／受け取らない） |
| `NetworkSystem.Trade.cs` | 直接交換（取引ID・預け・確定・不成立の巻き戻し） |
| `Transport/INetTransport.cs` | トランスポート抽象（`INetTransport`、`ILobbyTransport`、`TransportFactory`） |
| `Transport/PacketFragmenter.cs` | 分割・再構成（EOS のパケット上限 1170 バイト対策） |
| `Transport/LocalUdpTransport.cs` | 開発検証用の LAN／同一PC UDP（簡易な再送・順序付け、受信スレッド→メインスレッドのキュー） |
| `Transport/EosP2PTransport.cs` | EOS P2P＋Lobby（`#if UCHUJIN_EOS` の時だけコンパイル） |
| `Protocol/NetMessages.cs` | メッセージ種別 `NetMsg`（enum）と各メッセージの DTO（JsonUtility） |
| `Protocol/NetCodec.cs` | 符号化：`[種別1B][フラグ1B][JSON(UTF-8)]`、768B 超は GZip。位置だけ簡易バイナリ |
| `Session/SessionData.cs` | セッションの参加者、ゲストの持ち物ミラー、コマンド結果キャッシュ、セーブ用データ |
| `Session/RemotePlayerAvatar.cs` | ほかのプレイヤーの表示（補間、名前ラベル、身振り、エリア違いは非表示） |
| `Session/NetDevArgs.cs` | 同一PCで2起動テストする時の起動引数 `-uchujinTestSlot 2` |
| `UI/MultiplayerMenuWindow.cs` | マルチメニュー（星を開く／友達の星へ行く／参加者と権限／辞書を渡す／交換する／退出） |
| `UI/SleepVoteWindow.cs`, `UI/TradeWindow.cs`, `UI/NetworkOverlay.cs` | 睡眠の投票、交換画面、HUD の参加者一覧、タイトル画面の「友達の星へ行く」ボタン |

## 実装した内容（仕様との対応）

### 構成（マルチ §20.1〜§20.4）
- **ホスト権威・スター型**：ゲストはホストとだけ繋がる。ゲスト同士の位置・身振り・辞書・交換はホストが中継（送り主のIDはホストが上書きしてなりすましを防ぐ）。
- **参加の流れ（§4.1, §20.3）**：
  1. ゲスト → `Hello`（プロトコル版・プレイヤーID・名前・`PlayerState.ProfileData`＝見た目・持ち物・所持金・道具の要約、生成規則の対応版）
  2. ホスト → `JoinAccepted`（権限など）→ `SnapshotBegin`（`PlanetInfo`：WorldId・シード・生成バージョン）→ `SnapshotEntries`（`Game.CaptureAllPlanetEntries()`＋`"core.flags"`、約48KB ごとにまとめ、さらにトランスポートが 1170B 以下に分割）→ `SnapshotEnd`
  3. ゲスト → `Game.BeginGuestSession(planet, entries, flags, onReady)`。読み込み中に届いた差分・Tx・参加者一覧は溜めておき、読み込み後に順に適用 → `GuestReady`
  - 途中参加も「今の状態」から（§4.2）。シードだけでは復元しない。
  - 生成規則の版（`worldGenVersion` / `languageGenVersion`）が対応外なら参加を断る。
- **状態差分（§20.2）**：各システムの `MarkDirty()` →（`WorldAuthority.MarkDirty` → `INetworkBridge.MarkDirty`）→ 0.4 秒ごとにまとめて `StateDelta` で送信。キー単位（システム単位）の状態で、同じキーは 0.4 秒に1回まで、内容が変わっていなければ送らない（ハッシュ比較）。`"core.time"` は5秒ごと、`"core.flags"` は2秒ごとに変化を確認して送る。ゲストでは `Game.ApplyRemoteState` → 各システムの `RestoreState(Planet)`。
- **コマンド**：ゲストの `WorldAuthority.Submit` → `SendCommandToHost` → ホストで `WorldAuthority.ExecuteRemote`（発行者IDは接続元で上書き）→ `CommandResult` → ゲストで `WorldAuthority.CompletePending`。**同じ commandId の再送には実行せずキャッシュした結果を返す**（二重実行防止）。
- **付与・課金・取り去り（`INetworkBridge`）**：ホスト側でゲストの持ち物・お金のミラー（ゲストの要約＋まだ要約に反映されていない送信済み Tx）を持ち、`RemoteHasItem` / `TryChargeRemote` / `TakeItemFromRemote` を判定。`GrantItemToRemote` 等は **取引ID付きの Tx** としてゲストへ送る。
  - **未受領の Tx はホストの星のセーブ（`"network"` Planet スコープ）に残し、再訪・再接続時に再送**。
  - **ゲストは受領済み取引IDの台帳（`"network"` PlayerProfile スコープ、持ち物と一緒に自分のスロットへ保存）で、確定済みの再送を二重受領しない（§20.5）**。
- **位置**：unreliable 約10Hz（`Transform`、簡易バイナリ）。受信側は 0.15 秒遅らせて補間、短時間だけ外挿、12m 以上は瞬間移動、違うエリアにいる人は非表示。ホスト側ではゲストの `PlayerState.Position` / `CurrentAreaId` も更新されるので、他モジュールがゲストの位置を参照できる。
- **身振り**：自分の身振り（`PlayerGestureEvent`）を送信し、相手側でアバターが動き頭上に「（うなずく）」等を表示。ホストではゲストの身振りを `PlayerGestureEvent`（PlayerId＝ゲスト、近くの住民を TargetResidentId）として発行し、住民などが反応できる。主人公は話さない方針のため**文字チャットは作っていない**。

### ゲスト（§4.3, §5, §20.5）
- 自分の持ち物（Profile）を持ち込む。**タイトルから参加する場合はどのセーブの主人公で行くか選ぶ**（`SaveSystem.ReadPlayer(slot).profile` → `Game.RestoreLocalProfileEntries`）。プレイ中（ソロ）からなら自分の星を保存してから出かけ、そのスロットを保存先にする。
- 退出・切断・ホスト終了時：訪問先の知識（辞書など）は `Game.SaveGame`（ゲストは訪問先ごとに保存）→ 交換の預け物を巻き戻し → **持ち物を自分のスロットへ `WritePlayer`**（そのスロットの星の知識は残す）→ 切断。
- 訪問中も 120 秒ごと・日付更新ごとに持ち物を自分のスロットへ自動保存（異常終了時の損失を減らす）。
- ホストが終了したらタイトルへ（§20.5 基本案）。自分から帰る時は「自分の星へ帰る」（そのスロットを読み込む）か「タイトルへ戻る」。
- ゲストの `PlayerState.Permission` はホストが割り当てた権限になる（帰ると Owner に戻る）。

### ホスト（§3.3, §6.2）
- プレイ中の自分の星を開く（Esc メニュー → マルチプレイ → LAN で開く／EOS で開く）。ソロのセーブをそのまま開放し、閉じればソロに戻る。
- 参加者の一覧と **権限3段階（観光・お手伝い・共同生活）** の設定（`PlayerState.Permission`）。前回の権限は星に記録し、再訪時に復元。帰ってもらう（追い出し）。
- リモートプレイヤーの `PlayerState` を `Game.Players.AddRemote` で作り、アバターを `PlayerAvatarFactory.BuildVisual`、名前を `WorldLabels` で表示。`PlayerJoinedEvent` / `PlayerLeftEvent` を発行（ゲスト側でも、ほかの参加者について発行）。
- 人数は `NetConfig.MaxPlayers = 4`（ホスト含む・仮）。

### 時間・睡眠（時間天候 §16、マルチ §18）
- 時刻・日付・天候はホストの状態を共有（`"core.time"` と天候システムの差分）。ゲストの `TimeSystem` はローカルでも進み、ホストの値で補正される。
- **睡眠**：`SleepHelper.RequestSleep` → `INetworkService.RequestSleep` → 全員に投票ウィンドウ（30秒）→ **全員同意でホストが `Game.SleepRoutine`（`SleepUntilHour`）**、ゲストは暗転。同意が揃わない・時間切れ・誰かが「まだ起きている」なら、**眠ろうとした人だけ個人の休息（暗転のみ・時間は進めない）**。ひとりだけならそのまま眠る。
- 個人のタブレット操作で全員の時間は止まらない（コアの `TimeSystem` が対応済み）。

### 辞書の任意共有（§13、宇宙語 §12、タブレット §12）
- **自動配布は一切しない**。マルチメニュー「辞書を渡す」→ 相手と範囲（全部／選んだ語だけ）→ `ITabletService.ExportDictionary(...)` → 送信 → 受信側に「別冊として保存／自分の辞書に統合／受け取らない」→ `ImportDictionary`。渡した側には受け取ったかどうかだけ伝わる。
- 正誤は判定しない（推測・まちがい・うその辞書もそのまま渡る）。コピーして渡すので自分の辞書は減らない。
- 「選んだ語だけ」の候補は、最近聞いた発話（`ISpeechService.RecentRecords`）の表面形のうち、自分が推測訳を書いた語（`GetPlayerGloss`）＋手入力。**内部の意味は表示しない**。

### 直接交換（§12.2：直接交換・直接販売・プレゼント）
- 申し込み → 相手が受ける → 双方が品物・お金を出す（提示が変わると同意は外れる）→ 双方「これでOK」→ ゲストは自分の出した物を持ち物から外して**預ける（台帳に記録）**→ 揃ったらホストが確定し、**取引ID付きの `TradeCommit` Tx**（未受領は保存・再送・二重受領しない）で相手の品物を渡す。
- 不成立（やめた・時間切れ・切断・持ち物不足）→ 預けた物は戻る（`TradeAbort` Tx、またはゲスト自身が切断時に巻き戻し）。巻き戻した後に確定が届いた場合は、出した物を改めて外してから受け取る（遅れた確定）。
- ホスト自身は預けず、確定の瞬間に自分の持ち物から外す（ホストの終了で品物が宙に浮かないように）。
- 片方だけが出せばプレゼント。お金だけ出せば直接販売。

### 通信事故と不正の区別（§20.6）
- 通常の切断・再接続で消えたり増えたりしにくい構造を優先：Tx の取引ID＋受領台帳、コマンドの再送キャッシュ、交換の預け・確定・巻き戻し、未受領 Tx の永続化と再送、退出時・定期の持ち物保存。
- **完全な不正対策はしていない**（下記「限界」）。

## 未実装・簡略化した内容（理由）

- **共同生活・星主不在のアクセス（§4.4）**：未決定のため実装しない。ホストが星を起動している間だけ遊べる。ホスト移行もしない（EOS ロビーも `DisableHostMigration = true`）。
- **再接続の自動化**：切断されたらゲストはタイトルへ戻る（持ち物は保存済み）。手動で繋ぎ直せば未受領の Tx は再送される。自動再接続・タイムアウトの詳細（§20.5【要技術詳細設計】）は未実装。
- **差分は「システム単位の状態」**：`MarkDirty(key)` のキーごとに `CaptureState(Planet)` 全体を送る（変化が無ければ送らない・圧縮あり）。オブジェクト単位の細かい差分は各システムの状態設計しだい。
- **住民などの NPC の位置補間**：住民の位置は Residents モジュールの状態同期に任せている（このモジュールはプレイヤーの位置だけ補間）。
- **ゲストの持ち物の正本はゲスト側**：ホストのミラーは要約＋未反映 Tx。ゲスト端末で同時に使った物との競合（ホストが「持っている」と判断した直後にゲストが使った等）では Tx が一部失敗することがある（ログに出す）。
- **ゲストの身振りとクエスト**：ゲスト端末の `Game.PerformLocalGesture` が `IQuestService.TryHandleGesture` を呼ぶため、ホスト側では二重にならないよう `PlayerGestureEvent` の発行だけにしている（`NetConfig.ForwardGuestGestureToQuests` で切替）。
- **EOS の友達招待**：DeviceId（Connect）ログインには Epic アカウントが無いため、フレンド一覧・オーバーレイからの招待（`SendInvite` / `AddNotifyJoinLobbyAccepted`）は未実装。代わりに **招待コード（6文字）とロビー一覧**で参加する。
- **ボイスチャット・文字チャット**：作っていない（主人公は話さず身振りで意思表示する方針）。
- **地図・音声カード・写真の共有（§13.2 拡張提案、§17）**：辞書のみ。`ITabletService` に共有用 API が無いため。
- **委託販売**：Shop モジュール担当（棚の ownerId）。宇宙生物の同行・預ける・譲る：Creatures モジュール側の所有データしだい（交換は `ItemStack` のみ対応）。
- **ゲストの SlotMeta（スロット一覧の所持金表示）**：`WritePlayer` では meta が更新されないため、自分の星を次にセーブするまで古い値のまま（コアへの要望あり）。

## 限界（P2P＋ローカルセーブ）
- ホストのセーブ改変、ゲストのセーブの巻き戻し・コピー、改造クライアントによる不正な要約送信などは防げない（§20.6）。ゲストの持ち物の正本はゲストの PC にある。
- `LocalUdpTransport` は**暗号化・認証なしの開発用**。インターネットに公開しないこと（ポート開放も不要な LAN 内の検証用）。

## 導入：EOS（Epic Online Services）

1. **Epic Developer Portal**（https://dev.epicgames.com/portal）で
   - 組織と製品（Product）を作り、**ProductId / SandboxId / DeploymentId** を控える。
   - 「Product Settings → Clients」で**クライアントを作成**し、**ClientId / ClientSecret** を控える。クライアントポリシーは Connect・P2P・Lobbies を使えるもの（Peer2Peer 用のポリシー等。名称はポータルの版で異なる：要確認）。
   - 「Epic Account Services」は不要（Connect の DeviceId ログインを使う）。
2. **PlayEveryWare の EOS Plugin for Unity** を導入（Package Manager → Add package from git URL：`https://github.com/PlayEveryWare/eos_plugin_for_unity_upm.git`、または公式リリースの tarball。版により導入方法が変わるので公式 README を確認）。
3. メニューの **EOS Plugin の設定画面**（例：`Tools > EOS Plugin > EOS Configuration`。版により名称が違う）で ProductId・SandboxId・DeploymentId・ClientId・ClientSecret・EncryptionKey（64桁16進）・ProductName/Version を入力（`StreamingAssets/EOS/` 以下に設定 JSON が作られる）。
4. **Project Settings → Player → Other Settings → Scripting Define Symbols** に **`UCHUJIN_EOS`** を追加（使う全プラットフォームに）。これで `EosP2PTransport` がコンパイルされ、マルチメニューに EOS の項目が出る。
5. シーンに `EOSManager` が無ければ実行時に自動で作る（`EosP2PTransport.EnsureInterfaces`）。プラグインのサンプルの `EOSManager` プレハブを置いてもよい。
6. 想定 SDK：**EOS C# SDK 1.15〜1.16 系（Options を `ref` の struct で渡す版）**。古い版（クラスの Options、`byte[]` の Data）では書き換えが必要。

使っている EOS API（Unity 上で名前・シグネチャを要確認）：
- Connect：`CreateDeviceId` → `Login`（`ExternalCredentialType.DeviceidAccessToken`、`UserLoginInfo.DisplayName`）→ 初回は `InvalidUser` → `CreateUser(ContinuanceToken)`。`AddNotifyAuthExpiration` で再ログイン。
- Lobby：`CreateLobby`（`BucketId = "uchujin:v1"`、`PermissionLevel = Publicadvertised`、`DisableHostMigration = true`）→ `UpdateLobbyModification` / `AddAttribute`（GAME・VER・CODE・HOSTNAME・PLANET）/ `UpdateLobby`、`CreateLobbySearch` / `SetParameter` / `Find` / `CopySearchResultByIndex` / `LobbyDetails.CopyInfo` / `CopyAttributeByKey` / `GetLobbyOwner`、`JoinLobby`、`LeaveLobby`、`DestroyLobby`。
- P2P：`SocketId { SocketName = "UCHUJINP2P" }`、`AddNotifyPeerConnectionRequest` → `AcceptConnection`、`AddNotifyPeerConnectionClosed`、`AddNotifyPeerConnectionEstablished`、`SendPacket`（チャンネル0=reliable 分割、1=unreliable、2=制御）、`GetNextReceivedPacketSize` / `ReceivePacket`、`CloseConnection(s)`。
- 1パケット 1170 バイト上限のため、reliable のメッセージは `FragmentAssembler` で分割・再構成。送信キューが満杯（`LimitExceeded`）なら次のフレームに再送。

## 開発用：LAN／同一PCでのテスト（EOS なし）

1. 1台目：自分の星で遊ぶ → Esc → マルチプレイ → 「LAN（UDP）で開く」（既定ポート 47777）。表示された IP を相手に伝える。
2. 2台目：自分の星から Esc → マルチプレイ → 「友達の星へ行く」→ LAN → `192.168.x.x:47777`。タイトルからなら右下の「友達の星へ行く」→ 主人公のセーブを選ぶ → アドレス入力。
3. **同じPCで2つ起動**する場合：PlayerPrefs が共有されプレイヤーIDが同じになるので、2つ目を `-uchujinTestSlot 2` 付きで起動する（例：`Game.exe -uchujinTestSlot 2`）。2つ目は別IDなので、まず自分の星を新しく始めてから「友達の星へ行く」で `127.0.0.1` に接続する。エディタ＋ビルドの組み合わせなら PlayerPrefs の保存先が別なので不要な場合が多い（OS による。要確認）。
4. ファイアウォールが UDP 47777 を塞いでいないか確認。

## 他モジュールとの連携点

| 種類 | 内容 |
|---|---|
| 実装する契約 | `INetworkService`（`IsOnline`, `Role`, `ConnectedPlayerIds`＝自分を含むセッションの全員、`RequestSleep`, `OpenMultiplayerMenu`, `GetPlayerName`）、`INetworkBridge`（`WorldAuthority.Bridge` に設定） |
| 使う契約 | `ITabletService.ExportDictionary / ImportDictionary / GetPlayerGloss`、`ISpeechService.RecentRecords / PlanetName`、`IResidentDirectory.ActiveResidents`（身振りの相手）、`IQuestService.TryHandleGesture`（既定は呼ばない） |
| コア | `WorldAuthority`（Role, Bridge, ExecuteRemote, CompletePending, MarkDirty）、`Game.CapturePlanetState / ApplyRemoteState / CaptureAllPlanetEntries / BeginGuestSession / CaptureLocalProfileEntries / RestoreLocalProfileEntries / SaveGame / LoadSlot / ReturnToTitle / SleepRoutine`、`SaveSystem.ReadPlayer / WritePlayer / ListSlots / SlotExists`、`Game.Players.AddRemote / Remove`、`PlayerAvatarFactory.BuildVisual`、`WorldLabels` |
| 発行するイベント | `PlayerJoinedEvent`、`PlayerLeftEvent`、`PlayerGestureEvent`（ホストでゲストの身振り） |
| 購読するイベント | `PlayerGestureEvent`（自分の身振りを送る）、`ItemAddedEvent / ItemRemovedEvent / MoneyChangedEvent / ToolUpgradedEvent`（ゲストの持ち物要約の送信） |
| セーブ | `SaveKey = "network"`：Planet＝来たことのあるゲストと権限、未受領 Tx（スナップショットではゲストへ送らない）／PlayerProfile＝受領済み取引IDの台帳、交換の預け物 |
| 他モジュールへのお願い | ①星の状態を変えたら必ず `MarkDirty()`（送られるのはそのキーの `CaptureState(Planet)` 全体）。②`RestoreState(Planet)` はゲストでプレイ中にも何度も呼ばれるので冪等に。③ゲストへの報酬・支払いは `ctx.GiveItem / TryCharge / TakeItem / PayMoney` だけを使う（直接 `Issuer.Inventory` を触らない）。④権限の判定は各コマンドのハンドラで `ctx.HasPermission(...)`。⑤Quests はゲスト端末では身振りへの返事を `WorldCommand` で送る実装にする。⑥Story のタイトル画面に「友達の星へ行く」を置く場合は `INetworkService.OpenMultiplayerMenu()` を呼ぶ（タイトル状態では主人公選択から始まる）。 |
| アイテムID・コマンド | 登録なし（`WorldCommand` は使わず、独自プロトコルで処理） |

## 調整用の仮数値
- `NetConfig.cs`：最大人数 4、UDP ポート 47777、同期間隔 0.4 秒、時刻同期 5 秒、位置 10Hz・補間遅延 0.15 秒、睡眠投票 30 秒、交換のタイムアウト、台帳の大きさ、初回の権限（お手伝い）、圧縮しきい値、EOS のソケット名・バケットID・招待コード長、タイトルのボタン表示。
- `LocalUdpTransport` 先頭：パケット 1200B、再送 0.25 秒×40回、送信窓 128、ピアのタイムアウト 10 秒。
- `EosP2PTransport` 先頭：1フレームの送受信上限、挨拶のタイムアウト。

## Unity 上で確認が必要な点
- **EOS 全般**：SDK／プラグインの版に合わせた API 名・シグネチャ（`ReceivePacket` の `ref/out`、`LobbyPermissionLevel` の列挙子名、`Utf8String` の変換、`ProductUserId.ToString()`、`EOSManager` の自動生成で初期化されるか）。ロビー検索の結果、招待コード検索、接続要求の受理、切断通知。
- 大きな星（スナップショット数百KB）の参加時間、GZip（`System.IO.Compression`）が対象プラットフォームで使えるか。
- 差分の帯域：頻繁に `MarkDirty` するシステム（住民など）の状態サイズ×0.4秒ごと。重ければ `PerKeyMinInterval` を延ばすか、各システムの状態を分割する。
- ゲストの読み込み（`BeginGuestSession`）中・直後に届く差分の適用順、ゲスト到着時にホストの近くに立つ処理。
- アバターの補間の滑らかさ、ジャンプ・段差、エリア移動時の表示切替、名前ラベルの位置。
- 睡眠の投票ウィンドウ（非モーダル）と暗転のタイミング、ホストの `SleepRoutine` 後のゲストの時刻合わせ。
- 交換画面の操作感（品物を1つずつ加える、+/-、お金の増減）、確定・不成立の表示。
- UDP：Windows ファイアウォール、`SIO_UDP_CONNRESET` の無効化、受信スレッドの終了。
- 同一PC 2起動（`-uchujinTestSlot`）でのプレイヤーID切り替え。
