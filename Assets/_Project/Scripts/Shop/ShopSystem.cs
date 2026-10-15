using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Shop
{
    /// <summary>
    /// 主人公のセルフ販売の商店（商店仕様 v0.2 ＋ タブレット仕様 v0.3 の最新方針）。
    ///  ・商品を棚に並べ値段を付けると、住民が自分で選んで買う。店番・レジはしない（商店 §1【決定】）。
    ///  ・プレイヤー不在でも星の時間中に売れる（ホストの星で進行。バランス §7.1）。
    ///  ・旧「顔付き文字付箋」は廃止。住民の反応は意味データの発話として作り、マネピが声・表情・仕草を覚える（タブレット §4〜§5）。
    ///  ・売上レポート・来店履歴・価格研究（タブレット §10.1）・要望ボード・店舗拡張・委託販売（マルチ §12）。
    /// </summary>
    public sealed partial class ShopSystem : GameSystemBase, IShopService
    {
        public const string ManepiBoardId = "shop";
        public const string TabletAppId = "shop";
        public const string ResidentsAppId = "residents";
        public const string PurchaseWhere = "player_shop";
        /// <summary>ホストが締めた日次集計を委託した持ち主へ届ける一時メッセージのチャンネル。</summary>
        public const string DaySummaryChannel = "shop.day_summary";

        public override string SaveKey => "shop";
        public override int DailyUpdateOrder => 450;

        /// <summary>モジュール内の画面から参照する（他モジュールは IShopService を使うこと）。</summary>
        public static ShopSystem Instance { get; private set; }

        internal ShopSaveData Data = ShopSaveData.CreateDefault();
        internal ShopKnowledgeData Knowledge = new ShopKnowledgeData();

        /// <summary>店の位置・向き（ローカル +Z が入口側）。</summary>
        internal ShopFrame Frame;
        internal ShopVisuals Visuals;

        private readonly ShopTabletApp tabletApp;
        private readonly ShopCarpenterProvider carpenterProvider;
        private readonly List<PendingDropIn> dropIns = new List<PendingDropIn>();
        private int dropInMinuteAccum;
        private float dropInPollTimer;
        private bool subscribed;

        private sealed class PendingDropIn
        {
            public string residentId;
            public long deadline;
        }

        public ShopSystem()
        {
            Instance = this;
            tabletApp = new ShopTabletApp(this);
            carpenterProvider = new ShopCarpenterProvider(this);
        }

        // =================================================================
        // ライフサイクル
        // =================================================================

        public override void RegisterContent()
        {
            RegisterCommands();
            WorldAuthority.RegisterTransientHandler(DaySummaryChannel, OnDaySummaryMessage);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            Frame = ShopFrame.FromLayout(ctx.Layout);
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            Data = ShopSaveData.CreateDefault();
            Knowledge = new ShopKnowledgeData();
        }

        public override void BuildScene()
        {
            if (Frame == null) Frame = ShopFrame.FromLayout(Game.Layout);
            Visuals = new ShopVisuals(this);
            Visuals.Build(Game.SceneRoot);
        }

        public override void OnPlayStarted()
        {
            var tablet = Game.Service<ITabletService>();
            tablet?.RegisterApp(tabletApp);
            Game.Service<IResidentDirectory>()?.RegisterInteractionProvider(carpenterProvider);
            if (!subscribed)
            {
                EventBus.Subscribe<PlayerLeftEvent>(OnPlayerLeft);
                subscribed = true;
            }
            Visuals?.Refresh();
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<PlayerLeftEvent>(OnPlayerLeft);
                subscribed = false;
            }
            Visuals?.Dispose();
            Visuals = null;
            dropIns.Clear();
            dropInMinuteAccum = 0;
            Data = ShopSaveData.CreateDefault();
            Knowledge = new ShopKnowledgeData();
        }

        private void OnPlayerLeft(PlayerLeftEvent e)
        {
            // 委託品・未精算はホストの星に残り、再訪した時に受け取れる（マルチ §12.3）
            Visuals?.Refresh();
        }

        public override void Tick(float deltaTime)
        {
            Visuals?.Tick(deltaTime);
            if (!IsAuthority || dropIns.Count == 0) return;
            dropInPollTimer -= deltaTime;
            if (dropInPollTimer > 0f) return;
            dropInPollTimer = 0.5f;
            ProcessDropIns();
        }

        public override void OnGameMinutesAdvanced(int minutes)
        {
            if (!IsAuthority) return;
            dropInMinuteAccum += minutes;
            while (dropInMinuteAccum >= ShopConfig.DropInCheckMinutes)
            {
                dropInMinuteAccum -= ShopConfig.DropInCheckMinutes;
                TryDropIns();
            }
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (IsAuthority && IsOpen && AutoRestockAll()) MarkDirty();
            Visuals?.Refresh();
        }

        /// <summary>
        /// 1日の締め：日別集計と「今日の売上」の通知（商店 §9）。
        /// 集計の正本はホスト。ゲストの端末の集計は同期待ちで締めの直前の売上が欠けることがあるので、ゲストは自分では通知せず、
        /// ホストが確定した値を委託した持ち主ごとに一時メッセージで送り、受け取った側で通知する（<see cref="OnDaySummaryMessage"/>）。
        /// </summary>
        public override void OnDayEnded(int day)
        {
            if (WorldAuthority.IsGuest) return;
            var sum = Data.Summary(day, false);
            if (WorldAuthority.IsMultiplayer && sum != null) SendDaySummaries(day, sum);
            var local = LocalPlayer;
            if (local == null || !IsLocalHost) return;
            int revenue = 0;
            if (sum != null)
            {
                foreach (var p in sum.revenueByOwner)
                    if (IsHostOwner(p.key)) revenue += p.value;
            }
            int sold = sum == null ? 0 : sum.itemsSold;
            int visitors = sum == null ? 0 : sum.visitors;
            int messages = sum == null ? 0 : sum.manepiMessages;
            if (OfferCount == 0 && visitors == 0 && revenue == 0) return;
            ToastDaySales(revenue, sold, visitors, messages);
        }

        private static void ToastDaySales(int revenue, int sold, int visitors, int messages)
        {
            string text = Loc.Format("今日の売上：{0}（{1}個・来店{2}人）", "Today's sales: {0} ({1} sold, {2} visitors)",
                Wallet.Format(revenue), sold, visitors);
            if (messages > 0) text += Loc.Format("／マネピの伝言 {0}件", " / {0} Manepi messages", messages);
            UIManager.Toast(text, 6f);
        }

        /// <summary>ホスト：その日に委託品が売れた持ち主（ゲスト）へ、確定した日次集計を送る。</summary>
        private void SendDaySummaries(int day, DailySummaryData sum)
        {
            foreach (var p in sum.revenueByOwner)
            {
                if (p == null || p.value <= 0 || IsHostOwner(p.key)) continue;
                int sold = 0;
                foreach (var v in Data.visits)
                {
                    if (v.day != day) continue;
                    foreach (var l in v.lines)
                        if (l.outcome == VisitLineOutcome.Bought && l.ownerId == p.key) sold += l.qty;
                }
                var msg = new ShopDaySummaryMessage
                {
                    day = day,
                    revenue = p.value,
                    sold = sold,
                    visitors = sum.visitors,
                    manepiMessages = sum.manepiMessages
                };
                WorldAuthority.SendTransient(p.key, DaySummaryChannel, JsonUtility.ToJson(msg));
            }
        }

        /// <summary>委託した持ち主（ゲスト）：ホストが締めた自分の委託品の売上を通知する。</summary>
        private void OnDaySummaryMessage(string fromPlayerId, string json)
        {
            // ホスト（権威側）はゲストからの集計を受け付けない
            if (WorldAuthority.IsAuthority && (LocalPlayer == null || fromPlayerId != LocalPlayer.PlayerId)) return;
            if (string.IsNullOrEmpty(json)) return;
            ShopDaySummaryMessage m = null;
            try { m = JsonUtility.FromJson<ShopDaySummaryMessage>(json); }
            catch (Exception e) { Debug.LogException(e); }
            if (m == null || m.revenue <= 0) return;
            ToastDaySales(m.revenue, m.sold, m.visitors, m.manepiMessages);
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority)
            {
                Visuals?.Refresh();
                return;
            }
            bool changed = false;
            // 改装の完成（家具職人が翌朝までに仕上げる）
            for (int i = Data.pendingUpgrades.Count - 1; i >= 0; i--)
            {
                var p = Data.pendingUpgrades[i];
                if (p.readyDay > day) continue;
                ApplyUpgrade(p.upgradeId);
                Data.pendingUpgrades.RemoveAt(i);
                changed = true;
                var def = ShopConfig.GetUpgrade(p.upgradeId);
                if (def != null && IsLocalHost)
                    UIManager.Toast(Loc.Format("商店の改装「{0}」が完成しました", "Shop upgrade \"{0}\" is finished", def.Name), 5f);
            }
            // 古い要望・記録の整理
            for (int i = Data.requests.Count - 1; i >= 0; i--)
            {
                var r = Data.requests[i];
                bool expired = !r.fulfilled && day - r.day > ShopConfig.RequestExpireDays;
                bool oldDone = r.fulfilled && day - r.fulfilledDay > ShopConfig.RequestExpireDays;
                if (expired || oldDone)
                {
                    Data.requests.RemoveAt(i);
                    changed = true;
                }
            }
            int before = Data.visits.Count;
            Data.visits.RemoveAll(v => day - v.day > ShopConfig.VisitLogKeepDays);
            while (Data.visits.Count > ShopConfig.MaxVisitLog) Data.visits.RemoveAt(0);
            if (Data.visits.Count != before) changed = true;
            before = Data.days.Count;
            Data.days.RemoveAll(d => day - d.day > ShopConfig.DailySummaryKeepDays);
            if (Data.days.Count != before) changed = true;
            Game.Service<IManepiService>()?.ClearMessagesOlderThan(ManepiBoardId, day - ShopConfig.ManepiKeepDays);
            if (changed) MarkDirty();
            Visuals?.Refresh();
        }

        // =================================================================
        // セーブ（棚・売上は星のセーブ。個人の知識は伝言の確認位置だけ）
        // =================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(Data);
                case SaveScope.PlayerPlanetKnowledge:
                    if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                    return JsonUtility.ToJson(Knowledge);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                    var d = JsonUtility.FromJson<ShopSaveData>(json);
                    if (d == null) return;
                    d.Normalize();
                    Data = d;
                    // ゲストではプレイ中にも呼ばれる：表示まで冪等に作り直す
                    Visuals?.Refresh();
                    break;
                case SaveScope.PlayerPlanetKnowledge:
                    if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                    Knowledge = JsonUtility.FromJson<ShopKnowledgeData>(json) ?? new ShopKnowledgeData();
                    break;
            }
        }

        // =================================================================
        // IShopService
        // =================================================================

        public bool IsOpen
        {
            get
            {
                if (Clock == null) return false;
                int h = Clock.Hour;
                return h >= Data.openHour && h < Data.closeHour;
            }
        }

        public Vector3 EntrancePosition => Frame != null ? Frame.Entrance : Vector3.zero;

        public int OfferCount
        {
            get
            {
                int n = 0;
                foreach (var s in Data.slots) if (!s.IsEmpty) n++;
                return n;
            }
        }

        // ResidentVisit は ShopSystem.Purchase.cs

        // =================================================================
        // 共通ヘルパー
        // =================================================================

        /// <summary>星の持ち主（商店の店主）。</summary>
        internal string HostId
        {
            get
            {
                // ホスト（権威側）の端末では、その端末のプレイヤーが店主。ゲストでは星の持ち主のID。
                if (Game == null) return null;
                var p = Game.Planet;
                if (WorldAuthority.IsGuest && p != null && !string.IsNullOrEmpty(p.hostPlayerId)) return p.hostPlayerId;
                return Game.LocalPlayerId;
            }
        }

        /// <summary>店主（ホスト）の商品か（持ち主が空・ホスト・星の作成者）。それ以外は委託品。</summary>
        internal bool IsHostOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId) || ownerId == HostId) return true;
            var p = Game != null ? Game.Planet : null;
            return p != null && ownerId == p.hostPlayerId;
        }

        internal ulong Seed => Game != null && Game.Planet != null ? Game.Planet.seed : 1UL;
        internal int Today => Clock != null ? Clock.Day : 1;
        internal int Level(string upgradeId) => Data.Level(upgradeId);
        internal bool IsLocalHost => LocalPlayer != null && LocalPlayer.PlayerId == HostId;

        internal ShelfSlotData GetSlot(int index) => index >= 0 && index < Data.slots.Count ? Data.slots[index] : null;

        /// <summary>ローカルプレイヤーが店の近く（屋外）にいるか。</summary>
        internal bool IsLocalPlayerNear(float radius)
        {
            var p = LocalPlayer;
            if (p == null || Frame == null) return false;
            if (p.CurrentAreaId != AreaIds.Overworld && p.CurrentAreaId != AreaIds.Shop) return false;
            var d = p.Position - Frame.Center;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        internal bool LocalAtShop => IsLocalPlayerNear(ShopConfig.AtShopRadius);

        internal Transform ManepiTransform => Visuals != null ? Visuals.ManepiAnchor : null;

        /// <summary>棚枠のワールド位置（住民が品定めに歩く先・演出）。</summary>
        internal Vector3 SlotWorldPosition(int index)
        {
            if (Frame == null) return Vector3.zero;
            var local = Frame.SlotLocalPosition(index, Data.slots.Count);
            var fwd = Quaternion.Euler(0f, Frame.SlotLocalYaw(local), 0f) * Vector3.forward;
            local.y = 0f;
            return Frame.ToWorld(local + fwd * 0.9f);
        }

        internal string OwnerLabel(string ownerId)
        {
            if (IsHostOwner(ownerId))
            {
                if (IsLocalHost) return Loc.T("自分", "Mine");
                return Loc.T("店主", "Shopkeeper");
            }
            if (LocalPlayer != null && ownerId == LocalPlayer.PlayerId) return Loc.T("自分（委託）", "Mine (consigned)");
            var net = Game.Service<INetworkService>();
            string name = net != null ? net.GetPlayerName(ownerId) : null;
            return Loc.Format("{0}さんの委託", "{0}'s consignment", string.IsNullOrEmpty(name) ? "?" : name);
        }

        /// <summary>伝言の記録を探す（マネピの伝言板 → 発話記録の順）。</summary>
        internal UtteranceRecord FindRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return null;
            var manepi = Game.Service<IManepiService>();
            if (manepi != null)
            {
                var list = manepi.GetMessages(ManepiBoardId);
                if (list != null) foreach (var r in list) if (r != null && r.recordId == recordId) return r;
            }
            return Game.Service<ISpeechService>()?.GetRecord(recordId);
        }

        /// <summary>マネピに記録をもう一度再現してもらう（原本は変えない）。店にいる時だけ。</summary>
        internal void Replay(UtteranceRecord record)
        {
            if (record == null) return;
            var speech = Game.Service<ISpeechService>();
            if (speech == null)
            {
                UIManager.Toast(Loc.T("（声を再生する仕組みがありません）", "(no speech playback available)"));
                return;
            }
            Transform at = ManepiTransform;
            if (at == null && LocalPlayer != null) at = LocalPlayer.Avatar;
            speech.Replay(record, at);
            Visuals?.OnManepiHeard();
        }

        /// <summary>未確認の伝言の数（ローカルプレイヤー）。</summary>
        internal int UnreviewedMessageCount()
        {
            int n = 0;
            foreach (var v in Data.visits)
            {
                if (string.IsNullOrEmpty(v.recordId)) continue;
                long t = TimeSystem.ToTotalMinutes(v.day, v.minute / 60, v.minute % 60);
                if (t > Knowledge.lastReviewedTotalMinutes) n++;
            }
            return n;
        }

        internal void MarkMessagesReviewed()
        {
            if (Clock != null) Knowledge.lastReviewedTotalMinutes = Clock.TotalMinutes;
        }

        internal void AfterStateChanged()
        {
            MarkDirty();
            Visuals?.Refresh();
        }

        internal void ApplyUpgrade(string upgradeId)
        {
            var def = ShopConfig.GetUpgrade(upgradeId);
            if (def == null) return;
            int level = Data.Level(upgradeId);
            if (level >= def.MaxLevel) return;
            Data.SetLevel(upgradeId, level + 1);
            if (def.AddsDisplay.HasValue)
            {
                for (int i = 0; i < Mathf.Max(1, def.SlotsPerLevel); i++)
                    Data.slots.Add(new ShelfSlotData { display = def.AddsDisplay.Value });
            }
            if (upgradeId == ShopUpgradeIds.Storage) Data.storage.SetCapacity(Data.storage.capacity + 12);
        }

        /// <summary>補充の仕掛け：倉庫から同じ商品を同じ値段で補充する（何を売るかは主人公の決定のまま）。</summary>
        internal bool AutoRestockAll()
        {
            if (Data.Level(ShopUpgradeIds.AutoRestock) <= 0) return false;
            bool changed = false;
            foreach (var slot in Data.slots)
            {
                if (!slot.autoRestock || !IsHostOwner(slot.ownerId)) continue;
                string wantId = slot.IsEmpty ? slot.lastItemId : slot.goods.itemId;
                int wantQ = slot.IsEmpty ? slot.lastQuality : slot.goods.quality;
                string wantData = slot.IsEmpty ? slot.lastData : slot.goods.data;
                if (string.IsNullOrEmpty(wantId)) continue;
                int cap = ShopConfig.MaxPerSlot(slot.display) - slot.Count;
                if (cap <= 0) continue;
                for (int i = 0; i < Data.storage.slots.Count && cap > 0; i++)
                {
                    var s = Data.storage.slots[i];
                    if (s == null || s.IsEmpty || s.itemId != wantId || s.quality != wantQ) continue;
                    if ((s.data ?? "") != (wantData ?? "")) continue;
                    var taken = Data.storage.TakeFromSlot(i, cap, "shop_restock");
                    if (taken == null) continue;
                    AddToSlot(slot, taken);
                    cap -= taken.count;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>既存の枠へ同じ商品を足す（並べた日は数量で按分して鮮度を平均する）。</summary>
        internal void AddToSlot(ShelfSlotData slot, ItemStack add)
        {
            int today = Today;
            if (slot.IsEmpty)
            {
                slot.goods = add.Clone();
                slot.goods.ownerId = null;
                slot.listedDay = today;
                return;
            }
            int total = slot.goods.count + add.count;
            slot.listedDay = Mathf.RoundToInt((slot.goods.count * (float)slot.listedDay + add.count * (float)today) / Mathf.Max(1, total));
            slot.goods.count = total;
            if (add.size > slot.goods.size) slot.goods.size = add.size;
        }

        // =================================================================
        // 看板による立ち寄り（商店 §8「看板・広告：来店客の注目度を高める」）
        // 住民モジュールの来店呼び出しが2日以上無い時は保険として立ち寄りを起こす（売れない店にならないように）。
        // =================================================================

        private void TryDropIns()
        {
            if (!IsOpen || OfferCount == 0 || Frame == null) return;
            // プロローグ・エンディング等の演出中（Playing 以外）は立ち寄りを起こさない（演出中の住民を動かさない）
            if (Game == null || Game.State != GameState.Playing) return;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return;
            int sign = Data.Level(ShopUpgradeIds.Sign);
            int decor = Data.Level(ShopUpgradeIds.Decor);
            bool fallback = Data.lastExternalVisitDay < Today - 1;
            if (sign <= 0 && !fallback) return;
            float radius = fallback ? 9999f : ShopConfig.DropInBaseRadius + ShopConfig.DropInRadiusPerSign * sign;
            int started = 0;
            long now = Clock.TotalMinutes;
            foreach (var r in dir.ActiveResidents)
            {
                if (started >= ShopConfig.DropInMaxPerCheck) break;
                if (r == null || !r.IsActive) continue;
                if (dropIns.Exists(p => p.residentId == r.Id)) continue;
                var mem = Data.Memory(r.Id);
                if (now - mem.lastVisitTotalMinutes < ShopConfig.RevisitCooldownMinutes) continue;
                if (!DropInActivityAllowed(r.Activity, fallback)) continue;
                var d = r.Position - Frame.Entrance;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                float chance = sign * ShopConfig.DropInChancePerSign + decor * ShopConfig.DropInChancePerDecor;
                if (fallback) chance += ShopConfig.DropInFallbackChance;
                if (OpenRequestMatchesStock(r.Id)) chance += ShopConfig.DropInRequestMatchBonus;
                var rng = SeededRandom.For(Seed, "shop.dropin:" + r.Id + ":" + now);
                if (!rng.Chance(chance)) continue;
                dropIns.Add(new PendingDropIn { residentId = r.Id, deadline = now + ShopConfig.DropInDeadlineMinutes });
                dir.RequestGoTo(r.Id, Frame.Entrance, (Clock.MinuteOfDay + ShopConfig.DropInDeadlineMinutes) % TimeSystem.MinutesPerDay, "shop_visit");
                started++;
            }
        }

        private static bool DropInActivityAllowed(ResidentActivity a, bool fallback)
        {
            switch (a)
            {
                case ResidentActivity.Walking:
                case ResidentActivity.AtPlaza:
                case ResidentActivity.Resting:
                case ResidentActivity.Playing:
                    return true;
                case ResidentActivity.Sleeping:
                case ResidentActivity.Packing:
                case ResidentActivity.ShelteringFromRain:
                case ResidentActivity.Shopping:
                    return false;
                default:
                    return fallback;
            }
        }

        private void ProcessDropIns()
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || Clock == null)
            {
                dropIns.Clear();
                return;
            }
            long now = Clock.TotalMinutes;
            for (int i = dropIns.Count - 1; i >= 0; i--)
            {
                var p = dropIns[i];
                var r = dir.Get(p.residentId);
                if (r == null || !r.IsActive)
                {
                    dropIns.RemoveAt(i);
                    continue;
                }
                var d = r.Position - Frame.Entrance;
                d.y = 0f;
                bool arrived = d.sqrMagnitude <= 2.5f * 2.5f;
                if (!arrived && now < p.deadline) continue;
                dropIns.RemoveAt(i);
                // 期限までに入口へ着けなかった住民は来店しない（店から離れた場所で来店処理をしない）
                if (!arrived || !IsOpen) continue;
                RunVisit(r, DefaultIntent(r), true);
            }
        }

        private bool OpenRequestMatchesStock(string residentId)
        {
            var req = Data.OpenRequestOf(residentId);
            if (req == null) return false;
            foreach (var s in Data.slots)
            {
                if (s.IsEmpty) continue;
                if (RequestMatches(req, ItemRegistry.Get(s.goods.itemId))) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 店の配置（WorldLayout の PlayerShop 施設から決める）。ローカル座標は +Z が入口側、X が横幅。
    /// </summary>
    public sealed class ShopFrame
    {
        public Vector3 Origin;
        public float Yaw;
        public float Width = 9f;
        public float Depth = 7.5f;
        public string FacilityId;

        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);
        public Vector3 Center => Origin;
        /// <summary>入口の地面上の点（店の正面の外）。</summary>
        public Vector3 Entrance => ToWorld(new Vector3(0f, 0f, Depth * 0.5f + 1.2f));

        public Vector3 ToWorld(Vector3 local)
        {
            var p = Origin + Rotation * local;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            if (layout != null && Mathf.Abs(local.y) < 0.001f) p.y = layout.HeightAt(p);
            return p;
        }

        public static ShopFrame FromLayout(WorldLayout layout)
        {
            var f = new ShopFrame();
            if (layout == null)
            {
                f.Origin = new Vector3(8f, 0f, 8f);
                return f;
            }
            var fac = layout.GetFacility(FacilityType.PlayerShop);
            if (fac == null)
            {
                // 施設が無い場合の保険：プレイヤーの出現位置の近く
                f.Origin = layout.OnGround(layout.PlayerSpawn + new Vector3(10f, 0f, 6f));
                f.Yaw = 180f;
                return f;
            }
            f.FacilityId = fac.id;
            f.Origin = layout.OnGround(fac.position);
            f.Width = Mathf.Max(8f, Mathf.Min(fac.size.x, 12f));
            f.Depth = Mathf.Max(7f, Mathf.Min(fac.size.z, 10f));
            var toEntrance = fac.entrance - fac.position;
            toEntrance.y = 0f;
            f.Yaw = toEntrance.sqrMagnitude > 0.25f ? Quaternion.LookRotation(toEntrance.normalized, Vector3.up).eulerAngles.y : fac.rotationY;
            return f;
        }

        // ---- 棚の配置：入口に近い順に並べる（index が小さいほど入口付近＝目に付きやすい：商店 §8） ----

        private List<Vector3> anchors;
        private float anchorsW, anchorsD;

        public List<Vector3> Anchors
        {
            get
            {
                if (anchors == null || anchorsW != Width || anchorsD != Depth) BuildAnchors();
                return anchors;
            }
        }

        private void BuildAnchors()
        {
            anchorsW = Width;
            anchorsD = Depth;
            var list = new List<Vector3>();
            float hw = Width * 0.5f, hd = Depth * 0.5f;
            const float step = 1.25f;
            // 左右の壁沿い
            for (float z = hd - 1.4f; z >= -hd + 1.3f; z -= step)
            {
                list.Add(new Vector3(-hw + 0.65f, 0f, z));
                list.Add(new Vector3(hw - 0.65f, 0f, z));
            }
            // 奥の壁沿い（右奥はカウンター）
            for (float x = -hw + 1.9f; x <= hw - 3.3f; x += step) list.Add(new Vector3(x, 0f, -hd + 0.6f));
            // 中央の島
            for (float z = hd - 2.6f; z >= -hd + 2.4f; z -= step)
            {
                list.Add(new Vector3(-0.75f, 0f, z));
                list.Add(new Vector3(0.75f, 0f, z));
            }
            // 店頭の台（入口の両脇の外）
            list.Add(new Vector3(-hw + 1.2f, 0f, hd + 0.5f));
            list.Add(new Vector3(hw - 1.2f, 0f, hd + 0.5f));
            // 入口から近い順
            var entrance = new Vector3(0f, 0f, hd + 1.2f);
            list.Sort((a, b) => (a - entrance).sqrMagnitude.CompareTo((b - entrance).sqrMagnitude));
            anchors = list;
        }

        /// <summary>棚枠 index のローカル位置（枠が足りなければ上段に重ねる）。</summary>
        public Vector3 SlotLocalPosition(int index, int total)
        {
            var a = Anchors;
            if (a.Count == 0) return Vector3.zero;
            var p = a[index % a.Count];
            int tier = index / a.Count;
            return p + new Vector3(0f, tier * 1.0f, 0f);
        }

        /// <summary>棚枠の向き（壁側を背にして店の内側を向く）。</summary>
        public float SlotLocalYaw(Vector3 local)
        {
            float hw = Width * 0.5f, hd = Depth * 0.5f;
            if (local.z > hd) return 0f;
            if (Mathf.Abs(local.x) > hw - 1f) return local.x < 0 ? 90f : -90f;
            if (local.z < -hd + 1f) return 0f;
            return local.x < 0 ? -90f : 90f;
        }
    }
}
