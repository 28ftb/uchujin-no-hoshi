using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 主人公のタブレット（タブレット仕様 v0.3）。
    /// ・異世界（故郷）側の携帯端末。翻訳機ではなく、聞いた言葉・見たもの・写真・観察を記録して後で調べる道具（§2.1）。
    /// ・内蔵アプリ：宇宙語辞書／音声カード／会話ログ／資料ボックス／住民辞典／設定。他モジュールは RegisterApp で自分のアプリを足す。
    /// ・マネピ（No.033）の伝言板（IManepiService §4, §9）もここで管理する。
    ///
    /// 保存：辞書・カード・資料・住民辞典は PlayerPlanetKnowledge（星ごと・個人）、マネピの伝言は Planet（星の状態）。
    /// 正解の自動翻訳はしない。内部の意味（internalId / meaning）は一切表示しない。
    /// </summary>
    public sealed partial class TabletSystem : GameSystemBase, ITabletService, IManepiService
    {
        public override string SaveKey => "tablet";
        /// <summary>住民（伝言を残す）より後、クエストより前あたりで古い伝言を整理する。</summary>
        public override int DailyUpdateOrder => 720;

        /// <summary>タブレット内部の UI から参照する（Game が1つだけ生成する）。</summary>
        public static TabletSystem Instance { get; private set; }

        internal KnowledgeData Know = new KnowledgeData();
        internal ManepiPlanetData ManepiData = new ManepiPlanetData();

        private readonly List<ITabletApp> builtinApps = new List<ITabletApp>();
        private readonly List<ITabletApp> externalApps = new List<ITabletApp>();
        private readonly List<ITabletApp> appBuffer = new List<ITabletApp>();
        private TabletWindow window;
        private TabletOverlay overlay;

        internal DictionaryApp DictApp { get; private set; }
        internal VoiceCardsApp CardsApp { get; private set; }
        internal ConversationLogApp LogApp { get; private set; }
        internal MaterialsApp MaterialsApp { get; private set; }
        internal ResidentsApp ResidentsApp { get; private set; }
        internal SettingsApp SettingsApp { get; private set; }
        internal TabletCamera PhotoCamera { get; private set; }

        /// <summary>直前に聞いた発話（[K] でその場で保存 §8.4）。</summary>
        internal UtteranceRecord LastHeard { get; private set; }
        internal float LastHeardTime { get; private set; }

        private bool subscribed;

        public TabletSystem()
        {
            Instance = this;
        }

        // ------------------------------------------------------------
        // ライフサイクル
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            builtinApps.Clear();
            DictApp = new DictionaryApp(this);
            CardsApp = new VoiceCardsApp(this);
            LogApp = new ConversationLogApp(this);
            MaterialsApp = new MaterialsApp(this);
            ResidentsApp = new ResidentsApp(this);
            SettingsApp = new SettingsApp(this);
            builtinApps.Add(DictApp);
            builtinApps.Add(CardsApp);
            builtinApps.Add(LogApp);
            builtinApps.Add(MaterialsApp);
            builtinApps.Add(ResidentsApp);
            builtinApps.Add(SettingsApp);
            PhotoCamera = new TabletCamera(this);
            overlay = new TabletOverlay(this);
            WorldAuthority.Register<ManepiDeleteMessageCommand>(HandleDeleteMessage);
            WorldAuthority.RegisterTransientHandler(RecordChannel, OnRecordMessage);
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            Know = new KnowledgeData { worldId = ctx.Planet.worldId };
            ManepiData = new ManepiPlanetData();
        }

        public override void BuildScene()
        {
            BuildPlazaManepi();
        }

        public override void OnPlayStarted()
        {
            if (Game?.Planet != null && string.IsNullOrEmpty(Know.worldId)) Know.worldId = Game.Planet.worldId;
            Subscribe();
            UIManager.RegisterOverlay(overlay);
            if (heldView == null)
            {
                heldView = new GameObject("TabletHeldView").AddComponent<TabletHeldView>();
                heldView.system = this;
            }
        }

        /// <summary>開いている間、主人公が手に持つ3Dのタブレット（見た目だけ）。</summary>
        private TabletHeldView heldView;

        public override void Tick(float deltaTime)
        {
            PhotoCamera?.Tick();
            if (LastHeard != null && UnityEngine.Time.unscaledTime - LastHeardTime > TabletTuning.QuickSaveWindowSeconds) LastHeard = null;
            // 住民との会話中（会話ウィンドウはモーダル）でもワンボタンで保存できるようにする（タブレット仕様 §8.4）。
            // 文字入力中・タブレット表示中・カメラ中だけは受け付けない。
            if (LastHeard != null && TabletSettings.QuickSaveKeyEnabled && QuickSaveKeyAllowed
                && GameInput.KeyDown(TabletTuning.QuickSaveKey))
            {
                var rec = LastHeard;
                LastHeard = null;
                SaveVoiceCard(rec, "その場で保存", "Saved on the spot");
            }
        }

        /// <summary>[K] の即時保存を受け付けてよいか（会話などのモーダル中も可）。</summary>
        internal bool QuickSaveKeyAllowed => !UIManager.TextFieldFocused && !IsOpen && (PhotoCamera == null || !PhotoCamera.Active);

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            bool changed = false;
            foreach (var b in ManepiData.boards)
            {
                int keep = RetentionDays(b.boardId);
                int before = b.messages.Count;
                b.messages.RemoveAll(m => m == null || m.day < day - keep);
                if (b.messages.Count != before) changed = true;
            }
            if (changed) MarkDirty();
        }

        public override void Teardown()
        {
            Unsubscribe();
            if (heldView != null) UnityEngine.Object.Destroy(heldView.gameObject);
            heldView = null;
            PhotoCamera?.Cancel(false);
            if (window != null && window.IsOpen) UIManager.Close(window);
            window = null;
            UIManager.UnregisterOverlay(overlay);
            externalApps.Clear();
            manepis.Clear();
            boardLabels.Clear();
            Know = new KnowledgeData();
            ManepiData = new ManepiPlanetData();
            LastHeard = null;
            lastForwardKey = null;
            forwardedMet.Clear();
            foreach (var a in builtinApps) (a as TabletAppBase)?.ResetState();
            TabletGui.ClearPhotoCache();
        }

        // ------------------------------------------------------------
        // セーブ（スコープを分ける）
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(ManepiData);
                case SaveScope.PlayerPlanetKnowledge:
                    if (Game?.Planet != null && string.IsNullOrEmpty(Know.worldId)) Know.worldId = Game.Planet.worldId;
                    return JsonUtility.ToJson(Know);
                default:
                    return null;
            }
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                if (scope == SaveScope.Planet)
                {
                    // ゲストではプレイ中にも届く。表示（マネピの新着印）は毎フレームこのデータを読むので置き換えるだけで冪等。
                    var d = JsonUtility.FromJson<ManepiPlanetData>(json);
                    ManepiData = d ?? new ManepiPlanetData();
                    if (ManepiData.boards == null) ManepiData.boards = new List<ManepiBoardData>();
                }
                else if (scope == SaveScope.PlayerPlanetKnowledge)
                {
                    var k = JsonUtility.FromJson<KnowledgeData>(json);
                    Know = k ?? new KnowledgeData();
                    Know.EnsureLists();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // ------------------------------------------------------------
        // イベント購読
        // ------------------------------------------------------------

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            EventBus.Subscribe<DictionaryLookupRequestEvent>(OnLookupRequest);
            EventBus.Subscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
            EventBus.Subscribe<ResidentMovedInEvent>(OnResidentMovedIn);
            EventBus.Subscribe<ResidentTalkedEvent>(OnResidentTalked);
            EventBus.Subscribe<GiftGivenEvent>(OnGiftGiven);
            EventBus.Subscribe<ShopItemSoldEvent>(OnShopItemSold);
            EventBus.Subscribe<QuestStateChangedEvent>(OnQuestStateChanged);
            EventBus.Subscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Subscribe<UtteranceSpokenEvent>(OnUtteranceSpoken);
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            EventBus.Unsubscribe<DictionaryLookupRequestEvent>(OnLookupRequest);
            EventBus.Unsubscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
            EventBus.Unsubscribe<ResidentMovedInEvent>(OnResidentMovedIn);
            EventBus.Unsubscribe<ResidentTalkedEvent>(OnResidentTalked);
            EventBus.Unsubscribe<GiftGivenEvent>(OnGiftGiven);
            EventBus.Unsubscribe<ShopItemSoldEvent>(OnShopItemSold);
            EventBus.Unsubscribe<QuestStateChangedEvent>(OnQuestStateChanged);
            EventBus.Unsubscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Unsubscribe<UtteranceSpokenEvent>(OnUtteranceSpoken);
        }

        private string LocalId => Game != null ? Game.LocalPlayerId : WorldAuthority.LocalPlayerId;

        private void OnLookupRequest(DictionaryLookupRequestEvent e)
        {
            if (string.IsNullOrEmpty(e.Surface)) return;
            if (PhotoCamera != null && PhotoCamera.Active) return;
            Open(DictionaryApp.Id);
            DictApp.ShowLookup(e.Surface, e.RecordId);
        }

        private void OnResidentMovedOut(ResidentMovedOutEvent e)
        {
            var page = FindResidentPage(e.ResidentId);
            if (page == null) return;
            page.movedOut = true;
            page.movedOutDay = e.Day;
            AddObservation(page, "moved_out", "村から引っ越していった", "Moved away from the village", null);
        }

        /// <summary>
        /// 住民辞典の「引っ越した」印を住民ディレクトリの状態に合わせる。
        /// ResidentMovedOutEvent はホストでしか発行されないため、ゲストはこれで同期する（引っ越しを知った日として今日を記録）。
        /// </summary>
        internal void SyncResidentMoves()
        {
            var dir = Uchujin.Core.Game.Instance?.Service<IResidentDirectory>();
            if (dir == null || Know?.residents == null) return;
            HashSet<string> former = null;
            foreach (var page in Know.residents)
            {
                if (page == null || page.movedOut || string.IsNullOrEmpty(page.residentId)) continue;
                var info = dir.Get(page.residentId);
                if (info == null || info.IsActive) continue;
                if (former == null)
                {
                    former = new HashSet<string>();
                    foreach (var f in dir.FormerResidents) if (f != null) former.Add(f.Id);
                }
                if (!former.Contains(page.residentId)) continue;
                page.movedOut = true;
                page.movedOutDay = Today;
                AddObservation(page, "moved_out", "村から引っ越していった", "Moved away from the village", null);
            }
        }

        private void OnResidentMovedIn(ResidentMovedInEvent e)
        {
            var page = FindResidentPage(e.ResidentId);
            if (page == null || !page.movedOut) return;
            page.movedOut = false;
            AddObservation(page, "moved_in", "また村で暮らし始めた", "Started living in the village again", null);
        }

        // 以下の共有イベントの多くはホスト（コマンドを実行した側）でだけ発行される。
        // 本人がリモートのゲストなら、ホストがその本人のタブレットへ転送する（TabletSystem.Remote.cs）。

        private void OnResidentTalked(ResidentTalkedEvent e)
        {
            if (string.IsNullOrEmpty(e.ResidentId) || string.IsNullOrEmpty(e.PlayerId)) return;
            if (e.PlayerId == LocalId) RecordResidentMet(e.ResidentId);
            else ForwardMet(e.PlayerId, e.ResidentId);
        }

        private void OnGiftGiven(GiftGivenEvent e)
        {
            if (string.IsNullOrEmpty(e.PlayerId) || string.IsNullOrEmpty(e.ResidentId)) return;
            string item = ItemRegistry.GetOrPlaceholder(e.ItemId).NameJa;
            string itemEn = ItemRegistry.GetOrPlaceholder(e.ItemId).NameEn;
            ObserveFor(e.PlayerId, e.ResidentId, "gift", $"{item}を贈った", $"Gave a {itemEn}", null, -1, -1);
        }

        private void OnShopItemSold(ShopItemSoldEvent e)
        {
            if (string.IsNullOrEmpty(e.ResidentId)) return;
            // 持ち主（空・自分＝店主）へ。委託品なら委託した本人へ転送する
            string owner = string.IsNullOrEmpty(e.ShopOwnerId) ? LocalId : e.ShopOwnerId;
            var def = ItemRegistry.GetOrPlaceholder(e.ItemId);
            // 商店の客観的な売上記録（タブレット仕様 §5.2「購入記録」）。好みの解釈は書かない。
            ObserveFor(owner, e.ResidentId, "purchase",
                $"商店で{def.NameJa}{QualityText(e.Quality)}を{Wallet.Format(e.Price)}で買った",
                $"Bought {def.NameEn}{QualityText(e.Quality)} at the shop for {Wallet.Format(e.Price)}", null, e.Day, e.Minute);
        }

        private void OnQuestStateChanged(QuestStateChangedEvent e)
        {
            if (e.Status != QuestStatus.Completed || string.IsNullOrEmpty(e.PlayerId) || string.IsNullOrEmpty(e.ResidentId)) return;
            ObserveFor(e.PlayerId, e.ResidentId, "quest", "お願いごとをやり遂げた（依頼帳の記録）", "Completed a request (request log)", null, -1, -1);
        }

        private void OnUtteranceSpoken(UtteranceSpokenEvent e)
        {
            var r = e.Record;
            if (r == null || r.speakerKind == "player") return;
            if (r.channel == SpeechChannel.Direct || r.channel == SpeechChannel.Overheard || r.channel == SpeechChannel.Broadcast)
            {
                LastHeard = r;
                LastHeardTime = UnityEngine.Time.unscaledTime;
            }
        }

        private static string QualityText(int quality) => quality > 0 ? "(" + ItemStack.QualityStars(quality) + ")" : "";

        // ------------------------------------------------------------
        // タブレット本体（ITabletService）
        // ------------------------------------------------------------

        public void RegisterApp(ITabletApp app)
        {
            if (app == null || string.IsNullOrEmpty(app.AppId)) return;
            externalApps.RemoveAll(a => a == null || a.AppId == app.AppId);
            externalApps.Add(app);
        }

        /// <summary>ホームに並べるアプリ（外部アプリが同じIDなら内蔵より優先）。</summary>
        internal List<ITabletApp> GetApps()
        {
            appBuffer.Clear();
            foreach (var a in builtinApps)
            {
                bool overridden = false;
                foreach (var x in externalApps) if (x.AppId == a.AppId) overridden = true;
                if (!overridden) appBuffer.Add(a);
            }
            appBuffer.AddRange(externalApps);
            appBuffer.Sort((a, b) => a.Order.CompareTo(b.Order));
            return appBuffer;
        }

        internal ITabletApp FindApp(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            foreach (var a in externalApps) if (a.AppId == appId) return a;
            foreach (var a in builtinApps) if (a.AppId == appId) return a;
            return null;
        }

        public void Open(string appId = null)
        {
            if (PhotoCamera != null && PhotoCamera.Active) PhotoCamera.Cancel(false);
            if (window == null) window = new TabletWindow(this);
            bool wasOpen = window.IsOpen;
            if (!wasOpen) SyncResidentMoves();
            if (!wasOpen) UIManager.Open(window);
            window.Navigate(appId);
            if (!wasOpen) EventBus.Publish(new TabletOpenedEvent { AppId = window.CurrentAppId ?? "home" });
        }

        public void Close()
        {
            if (window != null && window.IsOpen) UIManager.Close(window);
        }

        public bool IsOpen => window != null && window.IsOpen;

        public void UnregisterApp(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return;
            externalApps.RemoveAll(a => a == null || a.AppId == appId);
        }

        public void OpenResidentPage(string residentId)
        {
            Open(ResidentsApp.Id);
            if (!string.IsNullOrEmpty(residentId)) ResidentsApp?.ShowPage(residentId);
        }

        public IReadOnlyList<string> GetDictionarySurfaces()
        {
            var list = new List<string>();
            if (Know?.dictionary == null) return list;
            foreach (var e in Know.dictionary) if (e != null && !string.IsNullOrEmpty(e.surface)) list.Add(e.surface);
            return list;
        }

        public IReadOnlyList<KeyValuePair<string, string>> GetPlayerGlossEntries()
        {
            var list = new List<KeyValuePair<string, string>>();
            if (Know?.dictionary == null) return list;
            foreach (var e in Know.dictionary)
            {
                if (e == null || string.IsNullOrEmpty(e.surface)) continue;
                var g = GlossOf(e);
                if (!string.IsNullOrEmpty(g)) list.Add(new KeyValuePair<string, string>(e.surface, g));
            }
            return list;
        }

        /// <summary>TabletWindow.OnClose から呼ばれる。</summary>
        internal void NotifyClosed()
        {
            EventBus.Publish(new TabletClosedEvent());
        }

        internal TabletWindow Window => window;

        // ------------------------------------------------------------
        // 推測訳・辞書の受け渡し
        // ------------------------------------------------------------

        public bool ShowGlossOverlay => TabletSettings.GlossOverlay;

        public string GetPlayerGloss(string surface)
        {
            var e = FindEntry(surface);
            return e != null ? GlossOf(e) : null;
        }

        /// <summary>表示に使う訳：本人が選んだ仮説。選んでいなければ「否定された」以外の最初の仮説（どちらも本人の記述）。</summary>
        internal static string GlossOf(DictEntry e)
        {
            if (e == null || e.hypotheses == null) return null;
            if (!string.IsNullOrEmpty(e.glossHypothesisId))
            {
                foreach (var h in e.hypotheses)
                    if (h.id == e.glossHypothesisId && h.mark != ConfidenceMark.Rejected && !string.IsNullOrEmpty(h.text)) return h.text;
            }
            foreach (var h in e.hypotheses)
                if (h.mark != ConfidenceMark.Rejected && !string.IsNullOrEmpty(h.text)) return h.text;
            return null;
        }

        public string ExportDictionary(IList<string> onlySurfaces = null)
        {
            var data = new DictionaryExportData
            {
                worldId = Game?.Planet?.worldId ?? Know.worldId,
                exporterName = Game?.LocalPlayer?.DisplayName,
                exportedDay = Clock != null ? Clock.Day : 0
            };
            HashSet<string> filter = null;
            if (onlySurfaces != null)
            {
                filter = new HashSet<string>();
                foreach (var s in onlySurfaces) filter.Add(TabletText.Normalize(s));
            }
            foreach (var e in Know.dictionary)
            {
                if (filter != null && !filter.Contains(TabletText.Normalize(e.surface))) continue;
                data.entries.Add(MakeExportEntry(e));
            }
            if (filter == null)
            {
                foreach (var p in Know.grammarPages)
                {
                    // 比較・証拠のカードIDは相手の端末に無いので、本文と題だけを渡す
                    data.pages.Add(new GrammarPage { id = p.id, title = p.title, body = p.body, createdDay = p.createdDay, updatedDay = p.updatedDay });
                }
            }
            return JsonUtility.ToJson(data);
        }

        /// <summary>書き出し用のコピー。証拠カードの原文（聞いた宇宙語そのもの）は例文として添える。内部の意味データは含めない。</summary>
        private DictEntry MakeExportEntry(DictEntry e)
        {
            var c = JsonUtility.FromJson<DictEntry>(JsonUtility.ToJson(e));
            c.evidence = new List<EvidenceLink>();
            c.history = new List<string>();
            c.pinned = false;
            int n = 0;
            foreach (var ev in e.evidence)
            {
                if (n >= 5) break;
                if (ev.kind != "card") continue;
                var card = FindCard(ev.refId);
                if (card?.original == null || string.IsNullOrEmpty(card.original.text)) continue;
                string ex = Loc.Format("「{0}」（{1}）", "\"{0}\" ({1})", card.original.text, TabletText.DayTime(card.original.day, card.original.minuteOfDay));
                if (!string.IsNullOrEmpty(ev.note)) ex += " " + ev.note;
                if (!c.examples.Contains(ex)) c.examples.Add(ex);
                n++;
            }
            return c;
        }

        public void ImportDictionary(string json, string fromPlayerName, bool asSeparateVolume)
        {
            if (string.IsNullOrEmpty(json)) return;
            DictionaryExportData data;
            try { data = JsonUtility.FromJson<DictionaryExportData>(json); }
            catch (Exception e)
            {
                Debug.LogWarning("[Tablet] 辞書の取り込みに失敗: " + e.Message);
                UIManager.Toast(Loc.T("辞書を読み取れませんでした", "Could not read the dictionary"));
                return;
            }
            if (data == null || data.entries == null)
            {
                UIManager.Toast(Loc.T("辞書を読み取れませんでした", "Could not read the dictionary"));
                return;
            }
            string from = !string.IsNullOrEmpty(fromPlayerName) ? fromPlayerName : (!string.IsNullOrEmpty(data.exporterName) ? data.exporterName : Loc.T("だれか", "someone"));
            int today = Clock != null ? Clock.Day : 0;
            if (asSeparateVolume)
            {
                // 別冊：読み取り専用で保管し、自分の記述と混ぜない（正誤も判定しない）
                var vol = new DictVolume
                {
                    id = Ids.New(),
                    fromPlayerName = from,
                    receivedDay = today,
                    sourceWorldId = data.worldId,
                    entries = data.entries,
                    pages = data.pages ?? new List<GrammarPage>()
                };
                foreach (var e in vol.entries) e.EnsureLists();
                Know.volumes.Add(vol);
                UIManager.Toast(Loc.Format("{0}さんの辞書を別冊として受け取りました（{1}語）", "Received {0}'s dictionary as a separate volume ({1} words)", from, vol.entries.Count));
                return;
            }
            int added = 0, merged = 0;
            foreach (var src in data.entries)
            {
                if (src == null || string.IsNullOrEmpty(src.surface)) continue;
                src.EnsureLists();
                var mine = FindEntry(src.surface);
                if (mine == null)
                {
                    var copy = CopyEntryFrom(src, from);
                    Know.dictionary.Add(copy);
                    added++;
                }
                else
                {
                    MergeInto(mine, src, from);
                    merged++;
                }
            }
            if (data.pages != null)
            {
                foreach (var p in data.pages)
                {
                    if (p == null) continue;
                    Know.grammarPages.Add(new GrammarPage
                    {
                        id = Ids.New(),
                        title = Loc.Format("{0}（{1}さんより）", "{0} (from {1})", p.title, from),
                        body = p.body,
                        createdDay = today,
                        updatedDay = today
                    });
                }
            }
            UIManager.Toast(Loc.Format("{0}さんの辞書を取り込みました（新しい語{1}・追記{2}）", "Merged {0}'s dictionary ({1} new, {2} merged)", from, added, merged));
        }

        /// <summary>他人の項目を自分の辞書へコピー（出典を付け、仮説には送り主の名前を残す）。</summary>
        internal DictEntry CopyEntryFrom(DictEntry src, string from)
        {
            int today = Clock != null ? Clock.Day : 0;
            var c = JsonUtility.FromJson<DictEntry>(JsonUtility.ToJson(src));
            c.EnsureLists();
            c.id = Ids.New();
            c.importedFrom = from;
            c.evidence.Clear();
            c.pinned = false;
            c.createdDay = today;
            c.updatedDay = today;
            foreach (var h in c.hypotheses)
            {
                if (string.IsNullOrEmpty(h.id)) h.id = Ids.New();
                if (string.IsNullOrEmpty(h.source)) h.source = from;
            }
            c.history.Clear();
            AddHistory(c, Loc.Format("{0}さんの辞書から写した", "Copied from {0}'s dictionary", from));
            return c;
        }

        /// <summary>統合：自分の記述は上書きせず、相手の仮説・メモ・タグを追記する。</summary>
        internal void MergeInto(DictEntry mine, DictEntry src, string from)
        {
            foreach (var h in src.hypotheses)
            {
                if (h == null || string.IsNullOrEmpty(h.text)) continue;
                bool exists = false;
                foreach (var m in mine.hypotheses) if (m.text == h.text) exists = true;
                if (exists) continue;
                mine.hypotheses.Add(new Hypothesis { id = Ids.New(), text = h.text, mark = h.mark, source = string.IsNullOrEmpty(h.source) ? from : h.source, day = Clock != null ? Clock.Day : 0 });
            }
            if (!string.IsNullOrEmpty(src.memo))
                mine.memo = (string.IsNullOrEmpty(mine.memo) ? "" : mine.memo + "\n") + Loc.Format("―{0}さんのメモ―\n{1}", "-- {0}'s note --\n{1}", from, src.memo);
            foreach (var t in src.tags) if (!mine.tags.Contains(t)) mine.tags.Add(t);
            foreach (var r in src.related) if (!mine.related.Contains(r)) mine.related.Add(r);
            foreach (var x in src.examples) if (!mine.examples.Contains(x)) mine.examples.Add(x);
            if (string.IsNullOrEmpty(mine.posGuess)) mine.posGuess = src.posGuess;
            AddHistory(mine, Loc.Format("{0}さんの辞書から追記した", "Merged notes from {0}", from));
            mine.updatedDay = Clock != null ? Clock.Day : mine.updatedDay;
        }
    }

    internal static class KnowledgeExtensions
    {
        /// <summary>古いセーブや受け取ったデータで null のリストを補う。</summary>
        public static void EnsureLists(this KnowledgeData k)
        {
            if (k.dictionary == null) k.dictionary = new List<DictEntry>();
            if (k.grammarPages == null) k.grammarPages = new List<GrammarPage>();
            if (k.volumes == null) k.volumes = new List<DictVolume>();
            if (k.cards == null) k.cards = new List<VoiceCard>();
            if (k.cardNotes == null) k.cardNotes = new List<CardNote>();
            if (k.materials == null) k.materials = new List<TabletMaterial>();
            if (k.materialNotes == null) k.materialNotes = new List<MaterialNote>();
            if (k.residents == null) k.residents = new List<ResidentPage>();
            if (k.relations == null) k.relations = new List<RelationLink>();
            if (k.diagramNodes == null) k.diagramNodes = new List<DiagramNode>();
            if (k.photos == null) k.photos = new List<PhotoInfo>();
            if (k.heardMessageIds == null) k.heardMessageIds = new List<string>();
            if (k.receivedRecords == null) k.receivedRecords = new List<UtteranceRecord>();
            foreach (var e in k.dictionary) e.EnsureLists();
            foreach (var v in k.volumes)
            {
                if (v.entries == null) v.entries = new List<DictEntry>();
                if (v.pages == null) v.pages = new List<GrammarPage>();
                foreach (var e in v.entries) e.EnsureLists();
            }
            foreach (var p in k.grammarPages)
            {
                if (p.cardIds == null) p.cardIds = new List<string>();
                if (p.marks == null) p.marks = new List<StringIntPair>();
            }
            foreach (var n in k.cardNotes) if (n.tags == null) n.tags = new List<string>();
            foreach (var n in k.materialNotes)
            {
                if (n.residentIds == null) n.residentIds = new List<string>();
                if (n.surfaces == null) n.surfaces = new List<string>();
            }
            foreach (var r in k.residents)
            {
                if (r.tags == null) r.tags = new List<string>();
                if (r.observations == null) r.observations = new List<ResidentObservation>();
                if (r.attachedCardIds == null) r.attachedCardIds = new List<string>();
                if (r.attachedMaterialIds == null) r.attachedMaterialIds = new List<string>();
                if (r.photoIds == null) r.photoIds = new List<string>();
            }
            foreach (var m in k.materials)
            {
                if (m.relatedIds == null) m.relatedIds = new List<string>();
                if (m.tags == null) m.tags = new List<string>();
            }
            foreach (var p in k.photos) if (p.residentIds == null) p.residentIds = new List<string>();
        }

        public static void EnsureLists(this DictEntry e)
        {
            if (e.hypotheses == null) e.hypotheses = new List<Hypothesis>();
            if (e.tags == null) e.tags = new List<string>();
            if (e.related == null) e.related = new List<string>();
            if (e.evidence == null) e.evidence = new List<EvidenceLink>();
            if (e.examples == null) e.examples = new List<string>();
            if (e.history == null) e.history = new List<string>();
            if (e.memo == null) e.memo = "";
        }
    }
}
