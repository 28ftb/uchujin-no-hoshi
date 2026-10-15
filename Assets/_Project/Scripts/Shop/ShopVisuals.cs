using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Shop
{
    /// <summary>
    /// 商店の見た目（プリミティブ）。開放的な店構えで、住民が中で商品を見る姿が外からも見える（商店 §8）。
    /// 状態（ShopSaveData）から冪等に作り直せる：ゲストではホストのスナップショットのたびに Refresh される。
    /// </summary>
    internal sealed class ShopVisuals
    {
        private const float FloorY = 0.12f;

        private static readonly Color Wood = new Color(0.74f, 0.56f, 0.38f);
        private static readonly Color WoodDark = new Color(0.52f, 0.38f, 0.26f);
        private static readonly Color Wall = new Color(0.99f, 0.93f, 0.84f);
        private static readonly Color Roof = new Color(0.96f, 0.52f, 0.47f);
        private static readonly Color Stone = new Color(0.78f, 0.76f, 0.72f);
        private static readonly Color AwningA = new Color(1f, 0.98f, 0.95f);
        private static readonly Color AwningB = new Color(0.45f, 0.82f, 0.74f);

        private readonly ShopSystem shop;
        private GameObject root;
        private Transform shelvesRoot, requestRoot, upgradesRoot, boardFace;
        private GameObject manepiObject;
        private Transform manepiAnchor;
        private bool manepiIsFallback;
        private readonly List<SlotView> slotViews = new List<SlotView>();
        private string layoutSignature;
        private string requestSignature;
        private string upgradeSignature;
        private float time;

        private sealed class SlotView
        {
            public int index;
            public GameObject unit;
            public Transform goodsRoot;
            public string goodsSignature;
            public readonly List<Transform> bobbing = new List<Transform>();
        }

        public ShopVisuals(ShopSystem shop)
        {
            this.shop = shop;
        }

        public Transform ManepiAnchor => manepiObject != null ? manepiObject.transform : manepiAnchor;

        private ShopFrame Frame => shop.Frame;
        private float W => Frame.Width;
        private float D => Frame.Depth;

        // =================================================================
        // 構築
        // =================================================================

        public void Build(Transform parent)
        {
            if (Frame == null) return;
            root = PrimitiveBuilder.Empty("PlayerShop", parent, Frame.Origin);
            root.transform.rotation = Frame.Rotation;
            BuildStructure();
            BuildCounterAndManepi();
            BuildStorageAndBoards();
            shelvesRoot = PrimitiveBuilder.Empty("Shelves", root.transform, Vector3.zero).transform;
            upgradesRoot = PrimitiveBuilder.Empty("Upgrades", root.transform, Vector3.zero).transform;
            Refresh();
        }

        private void BuildStructure()
        {
            var t = root.transform;
            float hw = W * 0.5f, hd = D * 0.5f;
            // 土台・床（斜面でも浮かないよう厚めにする）
            PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(0f, -0.3f, 0f), new Vector3(W + 0.4f, 0.84f, D + 0.4f), Stone, "Foundation");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, FloorY - 0.01f, 0f), new Vector3(W, 0.02f, D), Wood, "Floor");
            // 奥の壁と低い側壁（外から中が見える）
            PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(0f, 1.5f, -hd), new Vector3(W, 2.8f, 0.2f), Wall, "BackWall");
            PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(-hw, 0.55f, -0.7f), new Vector3(0.2f, 0.9f, D - 1.4f), Wall, "SideWallL");
            PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(hw, 0.55f, -0.7f), new Vector3(0.2f, 0.9f, D - 1.4f), Wall, "SideWallR");
            // 柱
            foreach (var p in new[] { new Vector3(-hw, 0f, -hd), new Vector3(hw, 0f, -hd), new Vector3(-hw, 0f, hd), new Vector3(hw, 0f, hd) })
                PrimitiveBuilder.Create(PrimitiveType.Cylinder, t, p + new Vector3(0f, 1.5f, 0f), new Vector3(0.28f, 1.5f, 0.28f), WoodDark, "Pillar");
            // 屋根（二段）
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 3.05f, 0f), new Vector3(W + 0.8f, 0.25f, D + 0.8f), Roof, "Roof");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 3.3f, -0.2f), new Vector3(W * 0.7f, 0.25f, D * 0.6f), Roof * 0.9f + new Color(0, 0, 0, 0.1f), "RoofTop");
            // しましまのひさし
            int stripes = 8;
            float sw = (W + 0.6f) / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float x = -(W + 0.6f) * 0.5f + sw * (i + 0.5f);
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(x, 2.72f, hd + 0.55f), new Vector3(sw, 0.07f, 1.1f),
                    i % 2 == 0 ? AwningA : AwningB, "Awning", new Vector3(20f, 0f, 0f));
            }
            // 店の目印（屋根の上の星）
            var star = PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0f, 3.75f, hd * 0.4f), new Vector3(0.6f, 0.6f, 0.2f), new Color(1f, 0.85f, 0.3f), "ShopStar");
            WorldLabels.Add(star.transform, () => Loc.T("お店", "Shop"), new Vector3(0f, 0.7f, 0f), UIStyles.AccentWarm, 40f);
        }

        private void BuildCounterAndManepi()
        {
            var t = root.transform;
            float hw = W * 0.5f, hd = D * 0.5f;
            var counterPos = new Vector3(hw - 1.5f, FloorY + 0.48f, -hd + 1.0f);
            var counter = PrimitiveBuilder.Create(PrimitiveType.Cube, t, counterPos, new Vector3(2.2f, 0.95f, 0.7f), Wood, "Counter");
            PrimitiveBuilder.Part(counter.transform, PrimitiveType.Cube, new Vector3(0f, 0.52f, 0f), new Vector3(1.05f, 0.06f, 1.1f), WoodDark, "CounterTop");
            // 帳簿（店の管理）
            var ledger = PrimitiveBuilder.Part(t, PrimitiveType.Cube, counterPos + new Vector3(-0.6f, 0.53f, 0.05f), new Vector3(0.4f, 0.08f, 0.3f), new Color(0.35f, 0.5f, 0.85f), "Ledger");
            SimpleInteractable.Attach(counter, "店の管理（帳簿）", "Manage shop (ledger)", p => ShopWindows.OpenManage(p, ShopManageWindow.Tab.Shelves));
            WorldLabels.Add(ledger.transform, () => shop.IsOpen ? "" : Loc.T("準備中", "Closed"), new Vector3(0f, 0.4f, 0f), UIStyles.TextDim, 8f);

            // マネピの止まり木と本体（タブレットモジュールが再生・音声カード保存の操作を付ける）
            var perch = PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, counterPos + new Vector3(0.55f, 0.6f, 0f), new Vector3(0.08f, 0.12f, 0.08f), WoodDark, "ManepiPerch");
            manepiAnchor = PrimitiveBuilder.Empty("ManepiAnchor", t, counterPos + new Vector3(0.55f, 0.78f, 0f)).transform;
            manepiAnchor.localRotation = Quaternion.Euler(0f, 0f, 0f);
            var manepi = Game.Instance.Service<IManepiService>();
            if (manepi != null)
            {
                manepiObject = manepi.SpawnManepi(ShopSystem.ManepiBoardId, manepiAnchor, Vector3.zero, "商店のマネピ", "Shop Manepi");
                manepiIsFallback = manepiObject == null;
            }
            else manepiIsFallback = true;
            if (manepiIsFallback) manepiObject = BuildFallbackManepi(manepiAnchor);
            WorldLabels.Add(manepiAnchor, () =>
            {
                int n = shop.UnreviewedMessageCount();
                return n > 0 ? Loc.Format("♪ 伝言 {0}", "♪ {0} new", n) : null;
            }, new Vector3(0f, 0.75f, 0f), UIStyles.AlienText, 16f);
        }

        /// <summary>マネピの仕組み（タブレットモジュール）が無い時の代わり：商店の伝言を聞ける簡易マネピ。</summary>
        private GameObject BuildFallbackManepi(Transform parent)
        {
            var body = PrimitiveBuilder.Create(PrimitiveType.Sphere, parent, new Vector3(0f, 0.18f, 0f), new Vector3(0.36f, 0.32f, 0.36f), new Color(1f, 0.72f, 0.82f), "FallbackManepi");
            PrimitiveBuilder.Part(body.transform, PrimitiveType.Sphere, new Vector3(-0.22f, 0.15f, 0.42f), Vector3.one * 0.16f, Color.black, "EyeL");
            PrimitiveBuilder.Part(body.transform, PrimitiveType.Sphere, new Vector3(0.22f, 0.15f, 0.42f), Vector3.one * 0.16f, Color.black, "EyeR");
            PrimitiveBuilder.Part(body.transform, PrimitiveType.Capsule, new Vector3(-0.35f, 0.55f, 0f), new Vector3(0.12f, 0.3f, 0.12f), new Color(1f, 0.6f, 0.75f), "EarL", new Vector3(0f, 0f, 25f));
            PrimitiveBuilder.Part(body.transform, PrimitiveType.Capsule, new Vector3(0.35f, 0.55f, 0f), new Vector3(0.12f, 0.3f, 0.12f), new Color(1f, 0.6f, 0.75f), "EarR", new Vector3(0f, 0f, -25f));
            SimpleInteractable.Attach(body, "マネピの伝言を聞く", "Listen to the Manepi", p => ShopWindows.OpenMessages(p));
            return body;
        }

        private void BuildStorageAndBoards()
        {
            var t = root.transform;
            float hw = W * 0.5f, hd = D * 0.5f;
            // 倉庫箱（店の右側の外）
            var box = PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(hw + 0.85f, 0.42f, -hd + 1.2f), new Vector3(1.1f, 0.84f, 0.85f), WoodDark, "StorageBox");
            PrimitiveBuilder.Part(box.transform, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(1.05f, 0.1f, 1.05f), Wood, "Lid");
            SimpleInteractable.Attach(box, "倉庫箱", "Storage box", p => ShopWindows.OpenManage(p, ShopManageWindow.Tab.Storage));

            // 営業時間の看板（入口の右）
            var post = PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(hw - 0.3f, 0.6f, hd + 1.5f), new Vector3(0.1f, 0.6f, 0.1f), WoodDark, "HoursPost");
            var board = PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(hw - 0.3f, 1.35f, hd + 1.5f), new Vector3(1.0f, 0.65f, 0.08f), Wall, "HoursBoard");
            SimpleInteractable.Attach(board, "営業時間", "Opening hours", p => ShopWindows.OpenManage(p, ShopManageWindow.Tab.Hours));
            WorldLabels.Add(board.transform, () =>
                (shop.IsOpen ? Loc.T("営業中", "OPEN") : Loc.T("準備中", "CLOSED")) + string.Format("  {0}:00–{1}:00", shop.Data.openHour, shop.Data.closeHour),
                new Vector3(0f, 0.55f, 0f), UIStyles.AccentWarm, 18f);

            // 要望ボード（入口の左）：住民がマネピに伝えた「欲しい物」を形と色の絵で貼る（文字の訳は無い）
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(-hw + 0.2f - 0.75f, 0.8f, hd + 1.5f), new Vector3(0.08f, 0.8f, 0.08f), WoodDark, "BoardPostL");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(-hw + 0.2f + 0.75f, 0.8f, hd + 1.5f), new Vector3(0.08f, 0.8f, 0.08f), WoodDark, "BoardPostR");
            var req = PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(-hw + 0.2f, 1.35f, hd + 1.5f), new Vector3(1.7f, 1.0f, 0.1f), new Color(0.62f, 0.45f, 0.3f), "RequestBoard");
            SimpleInteractable.Attach(req, "要望ボードを見る", "Look at the request board", p => ShopWindows.OpenRequestBoard(p));
            boardFace = PrimitiveBuilder.Empty("BoardFace", t, new Vector3(-hw + 0.2f, 1.35f, hd + 1.56f)).transform;
            requestRoot = PrimitiveBuilder.Empty("Pictures", boardFace, Vector3.zero).transform;
        }

        // =================================================================
        // 更新（冪等）
        // =================================================================

        public void Refresh()
        {
            if (root == null || Frame == null) return;
            var data = shop.Data;
            var sb = new StringBuilder();
            sb.Append(data.slots.Count).Append(':');
            foreach (var s in data.slots) sb.Append((int)s.display);
            string sig = sb.ToString();
            if (sig != layoutSignature)
            {
                layoutSignature = sig;
                RebuildShelves();
            }
            foreach (var v in slotViews) RefreshGoods(v);
            RefreshRequests();
            RefreshUpgrades();
        }

        private void RebuildShelves()
        {
            foreach (Transform c in shelvesRoot) Object.Destroy(c.gameObject);
            slotViews.Clear();
            int total = shop.Data.slots.Count;
            for (int i = 0; i < total; i++)
            {
                var slot = shop.Data.slots[i];
                var local = Frame.SlotLocalPosition(i, total);
                bool storefront = local.z > D * 0.5f;
                if (!storefront) local.y += FloorY;
                float yaw = Frame.SlotLocalYaw(local);
                var unit = PrimitiveBuilder.Empty("Slot_" + i, shelvesRoot, local);
                unit.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
                float topY = BuildDisplayUnit(unit.transform, slot.display);
                var col = unit.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, topY * 0.5f, 0f);
                col.size = new Vector3(1.0f, topY, 0.6f);
                var it = unit.AddComponent<ShopSlotInteractable>();
                it.index = i;
                var goods = PrimitiveBuilder.Empty("Goods", unit.transform, new Vector3(0f, topY, 0f)).transform;
                int idx = i;
                WorldLabels.Add(unit.transform, () => PriceLabel(idx), new Vector3(0f, topY + 0.75f, 0f), UIStyles.TextMain, 9f);
                slotViews.Add(new SlotView { index = i, unit = unit, goodsRoot = goods });
            }
        }

        /// <summary>陳列台の形を作り、商品を置く高さを返す。</summary>
        private static float BuildDisplayUnit(Transform t, ShopDisplayType type)
        {
            switch (type)
            {
                case ShopDisplayType.Chilled:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(0.95f, 0.8f, 0.6f), new Color(0.75f, 0.9f, 1f), "Case");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 1.0f, 0f), new Vector3(0.95f, 0.4f, 0.6f), new Color(0.8f, 0.95f, 1f, 0.35f), "Glass");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, new Vector3(0f, 1.22f, 0f), new Vector3(0.9f, 0.03f, 0.1f), new Color(0.7f, 0.95f, 1f), "ColdLight");
                    return 0.82f;
                case ShopDisplayType.Aquarium:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f), new Vector3(0.95f, 0.6f, 0.6f), new Color(0.35f, 0.4f, 0.55f), "Stand");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 0.6f, 0.55f), new Color(0.4f, 0.75f, 1f, 0.4f), "Tank");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.63f, 0f), new Vector3(0.85f, 0.05f, 0.5f), new Color(0.95f, 0.9f, 0.7f), "Sand");
                    return 0.7f;
                case ShopDisplayType.FurnitureStage:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.1f, 0f), new Vector3(1.2f, 0.2f, 1.0f), new Color(0.85f, 0.7f, 0.9f), "Stage");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.22f, 0f), new Vector3(1.1f, 0.04f, 0.9f), new Color(0.95f, 0.85f, 0.97f), "StageTop");
                    return 0.24f;
                case ShopDisplayType.Specials:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.35f, 0f), new Vector3(0.95f, 0.7f, 0.55f), new Color(1f, 0.82f, 0.3f), "Table");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0.42f, 1.0f, -0.2f), new Vector3(0.04f, 0.35f, 0.04f), WoodDark, "FlagPole");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0.6f, 1.2f, -0.2f), new Vector3(0.35f, 0.22f, 0.02f), new Color(1f, 0.35f, 0.35f), "Flag");
                    return 0.72f;
                default:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.38f, -0.05f), new Vector3(0.95f, 0.76f, 0.5f), Wood, "Shelf");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.78f, -0.05f), new Vector3(1.0f, 0.04f, 0.56f), WoodDark, "Board");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.4f, -0.3f), new Vector3(0.95f, 0.8f, 0.04f), WoodDark, "Back");
                    return 0.8f;
            }
        }

        private string PriceLabel(int index)
        {
            var slot = shop.GetSlot(index);
            if (slot == null) return null;
            if (slot.IsEmpty) return Loc.T("（空き）", "(empty)");
            var def = ItemRegistry.GetOrPlaceholder(slot.goods.itemId);
            string s = Wallet.Format(slot.price);
            string q = ItemStack.QualityStars(slot.goods.quality);
            if (!string.IsNullOrEmpty(q)) s += " " + q;
            if (!shop.IsHostOwner(slot.ownerId)) s += " ◆";
            if (slot.price < def.BasePrice && slot.display == ShopDisplayType.Specials) s = "SALE " + s;
            return s;
        }

        private void RefreshGoods(SlotView v)
        {
            var slot = shop.GetSlot(v.index);
            if (slot == null || v.goodsRoot == null) return;
            int shown = slot.IsEmpty ? 0 : Mathf.Min(slot.goods.count, slot.display == ShopDisplayType.FurnitureStage ? 1 : 3);
            string sig = slot.IsEmpty ? "-" : slot.goods.itemId + "|" + slot.goods.quality + "|" + shown;
            if (sig == v.goodsSignature) return;
            v.goodsSignature = sig;
            v.bobbing.Clear();
            foreach (Transform c in v.goodsRoot) Object.Destroy(c.gameObject);
            if (shown == 0) return;
            var def = ItemRegistry.GetOrPlaceholder(slot.goods.itemId);
            if (slot.display == ShopDisplayType.FurnitureStage)
            {
                float big = def.Category == ItemCategory.Furniture ? 0.6f : 0.4f;
                PrimitiveBuilder.Part(v.goodsRoot, def.Shape, new Vector3(0f, big * 0.5f, 0f), Vector3.one * big, def.Color, "Goods");
                return;
            }
            bool fishSwim = slot.display == ShopDisplayType.Aquarium;
            float spacing = 0.28f;
            for (int i = 0; i < shown; i++)
            {
                float x = (i - (shown - 1) * 0.5f) * spacing;
                Vector3 scale = Vector3.one * 0.22f;
                Vector3? euler = null;
                if (def.Category == ItemCategory.Fish)
                {
                    scale = new Vector3(0.12f, 0.16f, 0.12f);
                    euler = new Vector3(0f, 0f, 90f);
                }
                var go = PrimitiveBuilder.Part(v.goodsRoot, def.Shape, new Vector3(x, fishSwim ? 0.22f : 0.12f, 0.02f), scale, def.Color, "Goods", euler);
                if (def.HasTag(ItemTags.Glowing))
                {
                    var r = go.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(def.Color);
                }
                if (fishSwim) v.bobbing.Add(go.transform);
            }
            if (slot.goods.count > 3 && slot.display != ShopDisplayType.Aquarium)
                PrimitiveBuilder.Part(v.goodsRoot, PrimitiveType.Cube, new Vector3(0f, 0.03f, -0.12f), new Vector3(0.75f, 0.05f, 0.18f), def.Color * 0.8f, "Pile");
        }

        private void RefreshRequests()
        {
            if (requestRoot == null) return;
            var open = new List<ShopRequestData>();
            foreach (var r in shop.Data.requests) if (!r.fulfilled) open.Add(r);
            var sb = new StringBuilder();
            foreach (var r in open) sb.Append(r.id).Append(',');
            string sig = sb.ToString();
            if (sig == requestSignature) return;
            requestSignature = sig;
            foreach (Transform c in requestRoot) Object.Destroy(c.gameObject);
            int max = Mathf.Min(open.Count, 8);
            for (int i = 0; i < max; i++)
            {
                var r = open[open.Count - 1 - i];
                int col = i % 4, row = i / 4;
                var card = PrimitiveBuilder.Part(requestRoot, PrimitiveType.Cube, new Vector3(-0.6f + col * 0.4f, 0.22f - row * 0.45f, 0f), new Vector3(0.34f, 0.38f, 0.02f), new Color(0.98f, 0.97f, 0.9f), "Card");
                // 欲しい物の絵（形と色）
                PrimitiveBuilder.Part(card.transform, r.pictureShape, new Vector3(0f, 0.08f, 1.5f), new Vector3(0.45f, 0.4f, 2f), r.pictureColor, "Picture");
                // 要望した住民の顔（色）
                PrimitiveBuilder.Part(card.transform, PrimitiveType.Sphere, new Vector3(-0.3f, -0.33f, 1.5f), new Vector3(0.25f, 0.22f, 2f), r.faceColor, "Face");
                // 数を示す点
                for (int q = 0; q < Mathf.Min(r.quantity, 4); q++)
                    PrimitiveBuilder.Part(card.transform, PrimitiveType.Sphere, new Vector3(0.05f + q * 0.13f, -0.33f, 1.5f), new Vector3(0.09f, 0.08f, 1.5f), new Color(0.2f, 0.2f, 0.25f), "Dot");
            }
        }

        private void RefreshUpgrades()
        {
            if (upgradesRoot == null) return;
            var d = shop.Data;
            int decor = d.Level(ShopUpgradeIds.Decor), lights = d.Level(ShopUpgradeIds.Lights), sign = d.Level(ShopUpgradeIds.Sign);
            string sig = decor + "|" + lights + "|" + sign;
            if (sig == upgradeSignature) return;
            upgradeSignature = sig;
            foreach (Transform c in upgradesRoot) Object.Destroy(c.gameObject);
            float hw = W * 0.5f, hd = D * 0.5f;
            var t = upgradesRoot;
            // 内装
            if (decor >= 1)
            {
                foreach (var x in new[] { -hw + 0.4f, hw - 0.4f })
                {
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(x, 0.25f, hd + 0.2f), new Vector3(0.4f, 0.25f, 0.4f), new Color(0.85f, 0.5f, 0.35f), "Pot");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(x, 0.75f, hd + 0.2f), new Vector3(0.6f, 0.6f, 0.6f), new Color(0.4f, 0.8f, 0.45f), "Plant");
                }
            }
            if (decor >= 2)
            {
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, FloorY + 0.01f, 0.3f), new Vector3(W * 0.45f, 0.02f, D * 0.55f), new Color(0.95f, 0.75f, 0.55f), "Rug");
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 2.2f, -hd + 0.15f), new Vector3(1.2f, 0.8f, 0.05f), new Color(0.6f, 0.75f, 0.95f), "Picture");
            }
            if (decor >= 3)
            {
                for (int i = 0; i < 6; i++)
                {
                    var c = Color.HSVToRGB(i / 6f, 0.5f, 1f);
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-W * 0.45f + i * W * 0.18f, 2.55f, hd + 0.05f), new Vector3(0.25f, 0.25f, 0.02f), c, "Bunting", new Vector3(0f, 0f, 45f));
                }
                foreach (var x in new[] { -hw, hw })
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(x, 1.05f, 0.5f), new Vector3(0.3f, 0.15f, D * 0.4f), new Color(1f, 0.7f, 0.8f), "FlowerBox");
            }
            // 灯り（営業時間の延長）
            if (lights >= 1)
            {
                foreach (var p in new[] { new Vector3(-hw, 2.3f, hd + 0.2f), new Vector3(hw, 2.3f, hd + 0.2f) })
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, p, Vector3.one * 0.3f, new Color(1f, 0.85f, 0.55f), "Lantern");
            }
            if (lights >= 2)
            {
                for (int i = 0; i < 3; i++)
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(-W * 0.3f + i * W * 0.3f, 2.6f, 0f), Vector3.one * 0.25f, new Color(1f, 0.92f, 0.7f), "Lamp");
            }
            // 看板（通りがかりの目を引く）
            if (sign >= 1)
            {
                var basePos = new Vector3(hw + 1.6f, 0f, hd + 2.6f);
                PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, basePos + new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), WoodDark, "SignPole");
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, basePos + new Vector3(0f, 2.3f, 0f), new Vector3(1.4f, 0.8f, 0.1f), new Color(1f, 0.95f, 0.8f), "Sign");
                var mark = sign >= 3
                    ? PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, basePos + new Vector3(0f, 2.3f, 0.08f), new Vector3(0.45f, 0.45f, 0.05f), new Color(1f, 0.6f, 0.3f), "SignMark")
                    : PrimitiveBuilder.Part(t, PrimitiveType.Sphere, basePos + new Vector3(0f, 2.3f, 0.08f), new Vector3(0.45f, 0.45f, 0.05f), new Color(1f, 0.6f, 0.3f), "SignMark");
                if (sign >= 2)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var bc = Color.HSVToRGB(0.1f + i * 0.3f, 0.6f, 1f);
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, basePos + new Vector3(-0.6f + i * 0.6f, 3.2f + (i % 2) * 0.2f, 0f), new Vector3(0.3f, 0.38f, 0.3f), bc, "Balloon");
                    }
                }
            }
        }

        // =================================================================
        // 演出
        // =================================================================

        public void Tick(float dt)
        {
            if (root == null) return;
            time += dt;
            foreach (var v in slotViews)
            {
                for (int i = 0; i < v.bobbing.Count; i++)
                {
                    var tr = v.bobbing[i];
                    if (tr == null) continue;
                    var p = tr.localPosition;
                    p.y = 0.22f + Mathf.Sin(time * 2f + i * 1.7f + v.index) * 0.05f;
                    tr.localPosition = p;
                }
            }
            if (manepiIsFallback && manepiObject != null)
                manepiObject.transform.localPosition = new Vector3(0f, 0.18f + Mathf.Abs(Mathf.Sin(time * 2.2f)) * 0.04f, 0f);
        }

        public void PlaySaleFx(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotViews.Count) return;
            var v = slotViews[slotIndex];
            if (v.unit == null) return;
            var coin = PrimitiveBuilder.Create(PrimitiveType.Cylinder, root.transform.parent, v.unit.transform.position + Vector3.up * 1.1f,
                new Vector3(0.22f, 0.02f, 0.22f), new Color(1f, 0.85f, 0.25f), "SaleCoin", false);
            coin.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            coin.AddComponent<ShopFloatFx>().spin = true;
        }

        public void OnManepiHeard()
        {
            var a = ManepiAnchor;
            if (a == null || !shop.IsLocalPlayerNear(ShopConfig.HearingRadius)) return;
            var note = PrimitiveBuilder.Create(PrimitiveType.Sphere, root.transform.parent, a.position + Vector3.up * 0.5f,
                Vector3.one * 0.12f, new Color(0.8f, 1f, 0.85f), "ManepiNote", false);
            note.AddComponent<ShopFloatFx>();
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            slotViews.Clear();
        }
    }

    /// <summary>棚の1枠（[E]で並べる・値段・下げる）。</summary>
    public class ShopSlotInteractable : MonoBehaviour, IInteractable
    {
        public int index;

        public string GetInteractionPrompt(PlayerState player)
        {
            var shop = ShopSystem.Instance;
            var slot = shop != null ? shop.GetSlot(index) : null;
            if (slot == null) return null;
            if (slot.IsEmpty) return Loc.Format("{0}：商品を並べる", "{0}: put goods out", ShopRules.DisplayLabel(slot.display));
            return Loc.Format("{0} ×{1}（{2}）", "{0} x{1} ({2})", slot.goods.DisplayName, slot.goods.count, Wallet.Format(slot.price));
        }

        public bool CanInteract(PlayerState player) => ShopSystem.Instance != null;

        public void Interact(PlayerState player) => ShopWindows.OpenSlotMenu(player, index);
    }

    /// <summary>売れた時のコイン・マネピの音符など、上に浮かんで消える演出（見た目だけなので UnityEngine.Random 可）。</summary>
    public class ShopFloatFx : MonoBehaviour
    {
        public bool spin;
        private float t;
        private Vector3 drift;

        private void Start()
        {
            drift = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            t += dt;
            transform.position += (Vector3.up * 0.9f + drift) * dt;
            if (spin) transform.Rotate(0f, 0f, 360f * dt, Space.Self);
            transform.localScale *= 1f - 0.4f * dt;
            if (t > 1.2f) Destroy(gameObject);
        }
    }
}
