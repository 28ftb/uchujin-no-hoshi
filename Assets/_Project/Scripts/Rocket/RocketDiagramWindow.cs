using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Rocket
{
    /// <summary>
    /// 修理屋が身振りと図で修理に必要なものを教える（メインストーリー第1章・ロケット仕様 §13.3・§14.3）。
    /// ・ロケットの絵に故障箇所を光らせる
    /// ・必要な素材は「アイテムの色と形」の絵、数は丸の数で示す（納品済みは塗りつぶし）
    /// ・どの活動で得られそうかを絵（魚・作物・鉱石・生物・お金・修理屋）で示す
    /// 文字の和訳は出さない。修理屋は意味データで話す（宇宙語）。
    /// </summary>
    internal sealed class RocketDiagramWindow : UIWindow
    {
        private readonly RocketSystem sys;
        private readonly string mechanicId;
        private int selectedIndex;
        private float lastSpeakTime = -10f;

        public RocketDiagramWindow(RocketSystem sys, string mechanicId)
        {
            this.sys = sys;
            this.mechanicId = mechanicId;
            selectedIndex = FirstBrokenIndex();
        }

        public override string Title => Loc.T("修理屋の図", "The Mechanic's Diagram");
        public override Vector2 Size => new Vector2(1240, 800);

        private int FirstBrokenIndex()
        {
            for (int i = 0; i < RocketTables.Devices.Count; i++)
                if (!sys.IsDeviceRepaired(RocketTables.Devices[i].Id)) return i;
            return 0;
        }

        public override void DrawContents(Rect content)
        {
            // 紙の背景
            var paper = new Rect(0, 0, content.width, content.height - 64);
            UIStyles.FillRect(paper, new Color(0.93f, 0.9f, 0.8f));
            UIStyles.FillRect(new Rect(paper.x + 6, paper.y + 6, paper.width - 12, paper.height - 12), new Color(0.97f, 0.95f, 0.87f));

            var shipArea = new Rect(20, 16, 440, paper.height - 32);
            DrawShip(shipArea);
            var detail = new Rect(shipArea.xMax + 30, 24, paper.width - shipArea.xMax - 60, paper.height - 48);
            DrawDetail(detail);

            // 下部：前後の故障箇所へ（矢印だけ）
            var bottom = new Rect(0, content.height - 56, content.width, 52);
            if (GUI.Button(new Rect(bottom.x + bottom.width * 0.5f - 170, bottom.y, 150, 50), "◀", UIStyles.Button)) Select((selectedIndex + RocketTables.Devices.Count - 1) % RocketTables.Devices.Count);
            if (GUI.Button(new Rect(bottom.x + bottom.width * 0.5f + 20, bottom.y, 150, 50), "▶", UIStyles.Button)) Select((selectedIndex + 1) % RocketTables.Devices.Count);
        }

        private void Select(int index)
        {
            selectedIndex = Mathf.Clamp(index, 0, RocketTables.Devices.Count - 1);
            var def = RocketTables.Devices[selectedIndex];
            var ds = sys.DS(def.Id);
            if (ds != null && !ds.inspected) WorldAuthority.Submit(new RocketInspectCommand { deviceId = def.Id });
            SpeakAbout(def);
        }

        /// <summary>修理屋が指さして、必要なものと数を宇宙語＋身振りで示す（近くにいる時だけ）。</summary>
        private void SpeakAbout(DeviceDef def)
        {
            if (string.IsNullOrEmpty(mechanicId) || UnityEngine.Time.unscaledTime - lastSpeakTime < 1.2f) return;
            var dir = Game.Instance?.Service<IResidentDirectory>();
            var me = Game.Instance?.LocalPlayer;
            var r = dir?.Get(mechanicId);
            if (dir == null || r == null || me == null) return;
            if ((r.Position - me.Position).sqrMagnitude > 10f * 10f) return;
            lastSpeakTime = UnityEngine.Time.unscaledTime;
            var ds = sys.DS(def.Id);
            string item = null;
            int qty = 0;
            if (!def.IsFinal && ds != null && !ds.repaired)
            {
                foreach (var req in def.Items)
                {
                    int left = req.Count - ds.Delivered(req.ItemId);
                    if (left > 0)
                    {
                        item = req.ItemId;
                        qty = left;
                        break;
                    }
                }
            }
            else if (def.IsFinal && !sys.IsLaunchReady)
            {
                int left = RocketTables.FuelRequiredForLaunch - sys.State.fuelInTank;
                if (left > 0)
                {
                    item = SharedItemIds.FuelStarHoney;
                    qty = left;
                }
            }
            bool needCreature = !def.IsFinal && ds != null && !ds.repaired && def.RequiresCreature && !sys.CreatureSatisfied(def, ds);
            bool needMoney = !def.IsFinal && ds != null && !ds.repaired && ds.paid < sys.EffectiveFee(def);
            dir.Speak(mechanicId, RocketSpeech.MechanicDeviceExplain(mechanicId, def, item, qty, needCreature, needMoney));
        }

        // ------------------------------------------------------------
        // ロケットの絵と故障箇所
        // ------------------------------------------------------------

        private void DrawShip(Rect a)
        {
            var ink = new Color(0.25f, 0.3f, 0.45f);
            var fill = new Color(0.85f, 0.88f, 0.95f);
            float cx = a.x + a.width * 0.5f;
            float bw = a.width * 0.3f;
            float top = a.y + a.height * 0.16f, bottom = a.y + a.height * 0.8f;

            // アンテナ
            Picto.Rect(new Rect(cx - 2, a.y + a.height * 0.04f, 4, a.height * 0.1f), ink);
            Picto.Circle(new Rect(cx - 16, a.y + a.height * 0.03f, 32, 18), ink);
            // 本体（丸い先端＋胴）
            Picto.Circle(new Rect(cx - bw * 0.5f - 3, top - bw * 0.5f - 3, bw + 6, bw + 6), ink);
            Picto.Circle(new Rect(cx - bw * 0.5f, top - bw * 0.5f, bw, bw), fill);
            Picto.Rect(new Rect(cx - bw * 0.5f - 3, top, bw + 6, bottom - top), ink);
            Picto.Rect(new Rect(cx - bw * 0.5f, top, bw, bottom - top), fill);
            // 位相リング
            Picto.Rect(new Rect(cx - bw * 0.85f, a.y + a.height * 0.31f, bw * 1.7f, 10), new Color(0.6f, 0.45f, 0.8f));
            // 窓
            Picto.Circle(new Rect(cx - 18, a.y + a.height * 0.38f, 36, 36), ink);
            Picto.Circle(new Rect(cx - 13, a.y + a.height * 0.38f + 5, 26, 26), new Color(0.6f, 0.75f, 0.9f));
            // ひれ
            Picto.Triangle(new Rect(cx - bw * 0.5f - 60, bottom - 120, 60, 120), ink, true);
            Picto.Triangle(new Rect(cx + bw * 0.5f, bottom - 120, 60, 120), ink, false);
            // 冷却配管
            Picto.Rect(new Rect(cx + bw * 0.5f + 4, top + 40, 8, bottom - top - 80), new Color(0.4f, 0.75f, 0.85f));
            // 燃料タンク
            Picto.Rect(new Rect(cx + bw * 0.5f + 18, a.y + a.height * 0.65f, 46, 80), new Color(0.75f, 0.6f, 0.35f));
            // エンジン
            Picto.Rect(new Rect(cx - bw * 0.35f, bottom, bw * 0.7f, a.height * 0.07f), ink);
            Picto.Triangle(new Rect(cx - bw * 0.25f, bottom + a.height * 0.07f, bw * 0.5f, a.height * 0.08f), new Color(0.95f, 0.6f, 0.3f), false, true);

            // 故障箇所（壊れている所は赤く点滅、納品途中は黄、直った所は緑）
            float blink = Mathf.Repeat(UnityEngine.Time.unscaledTime, 1f) < 0.55f ? 1f : 0.45f;
            for (int i = 0; i < RocketTables.Devices.Count; i++)
            {
                var def = RocketTables.Devices[i];
                var p = new Vector2(a.x + def.DiagramPos.x * a.width, a.y + def.DiagramPos.y * a.height);
                float rad = def.IsFinal ? 20f : 26f;
                var hr = new Rect(p.x - rad, p.y - rad, rad * 2, rad * 2);
                bool repaired = sys.IsDeviceRepaired(def.Id);
                float partial = def.IsFinal ? (sys.State.fuelInTank / (float)RocketTables.FuelRequiredForLaunch) : sys.PartialProgress(def);
                Color c = repaired ? new Color(0.3f, 0.8f, 0.35f, 0.85f)
                    : partial > 0f ? new Color(1f, 0.8f, 0.2f, 0.85f * blink)
                    : new Color(1f, 0.25f, 0.2f, 0.85f * blink);
                if (i == selectedIndex) Picto.Ring(new Rect(hr.x - 8, hr.y - 8, hr.width + 16, hr.height + 16), new Color(0.15f, 0.2f, 0.6f));
                Picto.Ring(hr, c);
                Picto.Circle(new Rect(p.x - rad * 0.45f, p.y - rad * 0.45f, rad * 0.9f, rad * 0.9f), c);
                if (repaired) Picto.CheckMark(new Rect(p.x - 10, p.y - 10, 20, 20));
                if (GUI.Button(hr, GUIContent.none, GUIStyle.none)) Select(i);
            }
        }

        // ------------------------------------------------------------
        // 選んだ箇所に必要なもの（絵だけ）
        // ------------------------------------------------------------

        private void DrawDetail(Rect a)
        {
            var def = RocketTables.Devices[selectedIndex];
            var ds = sys.DS(def.Id);
            bool repaired = sys.IsDeviceRepaired(def.Id);

            // 見出しの代わりに、ロケットの小さな絵とその箇所の色
            Picto.Circle(new Rect(a.x, a.y, 44, 44), def.DiagramColor);
            Picto.Ring(new Rect(a.x - 4, a.y - 4, 52, 52), new Color(0.25f, 0.3f, 0.45f));
            if (repaired)
            {
                Picto.CheckMark(new Rect(a.x + 60, a.y + 4, 36, 36));
            }

            float y = a.y + 70;
            const float rowH = 96f;
            if (def.IsFinal)
            {
                // 全部の箇所に✔ ＋ 燃料の滴を満たす
                float x = a.x;
                foreach (var d in RocketTables.Devices)
                {
                    if (d.IsFinal) continue;
                    Picto.Circle(new Rect(x, y, 40, 40), d.DiagramColor);
                    if (sys.IsDeviceRepaired(d.Id)) Picto.CheckMark(new Rect(x + 8, y + 8, 24, 24));
                    x += 52;
                }
                y += rowH;
                var fuelDef = ItemRegistry.GetOrPlaceholder(SharedItemIds.FuelStarHoney);
                Picto.Activity(new Rect(a.x, y, 56, 56), SourceKind.Farming);
                Picto.Arrow(new Rect(a.x + 64, y + 14, 40, 28));
                Picto.ItemShape(new Rect(a.x + 112, y, 56, 56), fuelDef);
                Picto.Pips(new Rect(a.x + 184, y + 6, a.width - 190, 44), sys.State.fuelInTank, RocketTables.FuelRequiredForLaunch, fuelDef.Color);
                return;
            }

            foreach (var r in def.Items)
            {
                if (y + rowH > a.yMax) break;
                DrawRequirementRow(new Rect(a.x, y, a.width, rowH - 8), r.ItemId, r.Count, ds != null ? ds.Delivered(r.ItemId) : 0);
                y += rowH;
            }
            // お金（硬貨の山。1枚＝1,000G 目安）
            if (def.LaborFee > 0 && y + rowH <= a.yMax)
            {
                int fee = sys.EffectiveFee(def);
                int coins = Mathf.Clamp(Mathf.CeilToInt(fee / 1000f), 1, 12);
                int paidCoins = fee > 0 ? Mathf.FloorToInt(coins * Mathf.Clamp01((ds != null ? ds.paid : 0) / (float)fee)) : coins;
                Picto.Activity(new Rect(a.x, y, 56, 56), SourceKind.Mechanic);
                Picto.Arrow(new Rect(a.x + 64, y + 14, 40, 28), true);
                Picto.Activity(new Rect(a.x + 112, y, 56, 56), SourceKind.Money);
                Picto.Pips(new Rect(a.x + 184, y + 6, a.width - 190, 44), paidCoins, coins, new Color(1f, 0.82f, 0.25f));
                y += rowH;
            }
            // 生物の能力（生物の絵＋能力の記号）
            if (def.RequiresCreature && y + rowH <= a.yMax)
            {
                Picto.Activity(new Rect(a.x, y, 56, 56), SourceKind.Creatures);
                float x = a.x + 70;
                foreach (var ab in def.RequiredAbilities)
                {
                    Picto.Ability(new Rect(x, y + 4, 48, 48), ab);
                    x += 60;
                }
                if (ds != null && sys.CreatureSatisfied(def, ds)) Picto.CheckMark(new Rect(x + 10, y + 10, 36, 36));
            }
        }

        /// <summary>[活動の絵] → [原料の形] → [加工] → [部品の形] ●●○</summary>
        private void DrawRequirementRow(Rect r, string itemId, int need, int delivered)
        {
            var hint = RocketTables.Hint(itemId);
            var def = ItemRegistry.GetOrPlaceholder(itemId);
            float x = r.x;
            if (hint != null && hint.Sources != null)
            {
                foreach (var s in hint.Sources)
                {
                    if (s == SourceKind.Processing) continue;
                    Picto.Activity(new Rect(x, r.y, 56, 56), s);
                    x += 62;
                }
            }
            if (hint != null && !string.IsNullOrEmpty(hint.RawItemId))
            {
                Picto.Arrow(new Rect(x, r.y + 14, 34, 28));
                x += 40;
                Picto.ItemShape(new Rect(x, r.y + 4, 48, 48), ItemRegistry.GetOrPlaceholder(hint.RawItemId));
                x += 56;
                Picto.Activity(new Rect(x, r.y + 8, 40, 40), SourceKind.Processing);
                x += 46;
            }
            Picto.Arrow(new Rect(x, r.y + 14, 34, 28));
            x += 40;
            Picto.ItemShape(new Rect(x, r.y, 56, 56), def);
            x += 70;
            Picto.Pips(new Rect(x, r.y + 6, r.xMax - x, 44), delivered, need, def.Color);
        }
    }

    /// <summary>図で使う絵の部品（手続き生成のテクスチャ）。</summary>
    internal static class Picto
    {
        private static Texture2D circle, ring, triangleRight, triangleLeft, triangleDown, diamond;

        private static Texture2D Make(System.Func<float, float, float> alpha)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = 1f - (y + 0.5f) / n; // v=0 が上
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(alpha(u, v))));
                }
            t.Apply();
            return t;
        }

        private static float Edge(float d) => Mathf.Clamp01(d * 40f);

        private static Texture2D Circle_ => circle != null ? circle : (circle = Make((u, v) => Edge(0.5f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)))));
        private static Texture2D Ring_ => ring != null ? ring : (ring = Make((u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
            return Mathf.Min(Edge(0.5f - d), Edge(d - 0.38f));
        }));
        private static Texture2D TriRight => triangleRight != null ? triangleRight : (triangleRight = Make((u, v) => Edge(Mathf.Min(0.5f - Mathf.Abs(v - 0.5f) - u * 0.5f, u))));
        private static Texture2D TriLeft => triangleLeft != null ? triangleLeft : (triangleLeft = Make((u, v) => Edge(Mathf.Min(0.5f - Mathf.Abs(v - 0.5f) - (1f - u) * 0.5f, 1f - u))));
        private static Texture2D TriDown => triangleDown != null ? triangleDown : (triangleDown = Make((u, v) => Edge(Mathf.Min(0.5f - Mathf.Abs(u - 0.5f) - v * 0.5f, v))));
        private static Texture2D Diamond_ => diamond != null ? diamond : (diamond = Make((u, v) => Edge(0.5f - Mathf.Abs(u - 0.5f) - Mathf.Abs(v - 0.5f))));

        private static void Draw(Rect r, Texture2D tex, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        public static void Rect(Rect r, Color c) => UIStyles.FillRect(r, c);
        public static void Circle(Rect r, Color c) => Draw(r, Circle_, c);
        public static void Ring(Rect r, Color c) => Draw(r, Ring_, c);
        public static void Diamond(Rect r, Color c) => Draw(r, Diamond_, c);

        /// <summary>ひれ（直角三角形の代わりに向きつき三角）。down=true なら下向き（炎）。</summary>
        public static void Triangle(Rect r, Color c, bool pointLeft, bool down = false)
        {
            if (down) Draw(r, TriDown, c);
            else Draw(r, pointLeft ? TriLeft : TriRight, c);
        }

        public static void Arrow(Rect r, bool reverse = false)
        {
            var ink = new Color(0.35f, 0.35f, 0.4f);
            float shaft = r.height * 0.3f;
            if (!reverse)
            {
                Rect(new UnityEngine.Rect(r.x, r.y + (r.height - shaft) * 0.5f, r.width * 0.6f, shaft), ink);
                Draw(new UnityEngine.Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f, r.height), TriRight, ink);
            }
            else
            {
                Rect(new UnityEngine.Rect(r.x + r.width * 0.4f, r.y + (r.height - shaft) * 0.5f, r.width * 0.6f, shaft), ink);
                Draw(new UnityEngine.Rect(r.x, r.y, r.width * 0.5f, r.height), TriLeft, ink);
            }
        }

        public static void CheckMark(Rect r)
        {
            Circle(r, new Color(0.25f, 0.7f, 0.3f));
            var s = new GUIStyle(UIStyles.LabelBold) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(r.height * 0.7f) };
            s.normal.textColor = Color.white;
            GUI.Label(r, "✔", s);
        }

        /// <summary>アイテムの色と形のアイコン（名前は書かない）。</summary>
        public static void ItemShape(Rect r, ItemDefinition def)
        {
            var c = def != null ? def.Color : Color.gray;
            var outline = new Color(0.2f, 0.2f, 0.25f);
            var inner = new UnityEngine.Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
            switch (def != null ? def.Shape : PrimitiveType.Cube)
            {
                case PrimitiveType.Sphere:
                    Circle(r, outline);
                    Circle(inner, c);
                    break;
                case PrimitiveType.Capsule:
                {
                    float w = r.width * 0.55f;
                    var pr = new UnityEngine.Rect(r.x + (r.width - w) * 0.5f, r.y, w, r.height);
                    Circle(new UnityEngine.Rect(pr.x, pr.y, w, w), outline);
                    Circle(new UnityEngine.Rect(pr.x, pr.yMax - w, w, w), outline);
                    Rect(new UnityEngine.Rect(pr.x, pr.y + w * 0.5f, w, pr.height - w), outline);
                    var pi = new UnityEngine.Rect(pr.x + 3, pr.y + 3, w - 6, pr.height - 6);
                    float iw = pi.width;
                    Circle(new UnityEngine.Rect(pi.x, pi.y, iw, iw), c);
                    Circle(new UnityEngine.Rect(pi.x, pi.yMax - iw, iw, iw), c);
                    Rect(new UnityEngine.Rect(pi.x, pi.y + iw * 0.5f, iw, pi.height - iw), c);
                    break;
                }
                case PrimitiveType.Cylinder:
                {
                    float eh = r.height * 0.3f;
                    Rect(new UnityEngine.Rect(r.x + 4, r.y + eh * 0.5f, r.width - 8, r.height - eh), outline);
                    Circle(new UnityEngine.Rect(r.x + 4, r.yMax - eh, r.width - 8, eh), outline);
                    Rect(new UnityEngine.Rect(r.x + 7, r.y + eh * 0.5f, r.width - 14, r.height - eh - 3), c);
                    Circle(new UnityEngine.Rect(r.x + 7, r.yMax - eh - 2, r.width - 14, eh - 2), c);
                    Circle(new UnityEngine.Rect(r.x + 4, r.y, r.width - 8, eh), outline);
                    Circle(new UnityEngine.Rect(r.x + 7, r.y + 2, r.width - 14, eh - 4), Color.Lerp(c, Color.white, 0.35f));
                    break;
                }
                default:
                    if (def != null && (def.Category == ItemCategory.Ore || def.Category == ItemCategory.Gem || def.HasTag(ItemTags.Crystal)))
                    {
                        Diamond(r, outline);
                        Diamond(inner, c);
                    }
                    else
                    {
                        Rect(r, outline);
                        Rect(inner, c);
                    }
                    break;
            }
        }

        /// <summary>数量を丸で示す（塗り＝揃った分、白抜き＝足りない分）。</summary>
        public static void Pips(Rect r, int filled, int total, Color c)
        {
            total = Mathf.Clamp(total, 0, 24);
            filled = Mathf.Clamp(filled, 0, total);
            float size = Mathf.Min(r.height, 30f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(r.width / (size + 6f)));
            for (int i = 0; i < total; i++)
            {
                int row = i / perRow, col = i % perRow;
                var pr = new UnityEngine.Rect(r.x + col * (size + 6f), r.y + row * (size + 4f), size, size);
                Ring(pr, new Color(0.25f, 0.25f, 0.3f));
                if (i < filled) Circle(new UnityEngine.Rect(pr.x + 4, pr.y + 4, pr.width - 8, pr.height - 8), c);
            }
        }

        /// <summary>活動の絵（魚・作物・鉱石・生物・お金・加工・修理屋）。</summary>
        public static void Activity(Rect r, SourceKind k)
        {
            Circle(r, new Color(1f, 1f, 1f, 0.85f));
            Ring(r, new Color(0.3f, 0.32f, 0.4f));
            float cx = r.center.x, cy = r.center.y, s = r.width;
            switch (k)
            {
                case SourceKind.Fishing:
                {
                    var body = new Color(0.35f, 0.65f, 0.95f);
                    Circle(new UnityEngine.Rect(cx - s * 0.28f, cy - s * 0.15f, s * 0.48f, s * 0.3f), body);
                    Triangle(new UnityEngine.Rect(cx + s * 0.16f, cy - s * 0.14f, s * 0.18f, s * 0.28f), body, true);
                    Circle(new UnityEngine.Rect(cx - s * 0.2f, cy - s * 0.07f, s * 0.07f, s * 0.07f), Color.black);
                    break;
                }
                case SourceKind.Farming:
                {
                    Rect(new UnityEngine.Rect(cx - s * 0.03f, cy - s * 0.1f, s * 0.06f, s * 0.32f), new Color(0.3f, 0.6f, 0.25f));
                    Circle(new UnityEngine.Rect(cx - s * 0.28f, cy - s * 0.2f, s * 0.26f, s * 0.16f), new Color(0.4f, 0.8f, 0.35f));
                    Circle(new UnityEngine.Rect(cx + s * 0.02f, cy - s * 0.26f, s * 0.26f, s * 0.16f), new Color(0.4f, 0.8f, 0.35f));
                    Circle(new UnityEngine.Rect(cx - s * 0.12f, cy + s * 0.02f, s * 0.24f, s * 0.24f), new Color(1f, 0.6f, 0.3f));
                    break;
                }
                case SourceKind.Mining:
                {
                    Diamond(new UnityEngine.Rect(cx - s * 0.24f, cy - s * 0.26f, s * 0.48f, s * 0.52f), new Color(0.45f, 0.5f, 0.6f));
                    Diamond(new UnityEngine.Rect(cx - s * 0.12f, cy - s * 0.18f, s * 0.2f, s * 0.22f), new Color(0.8f, 0.9f, 1f));
                    break;
                }
                case SourceKind.Creatures:
                {
                    var c = new Color(1f, 0.6f, 0.75f);
                    Circle(new UnityEngine.Rect(cx - s * 0.25f, cy - s * 0.18f, s * 0.5f, s * 0.44f), c);
                    Rect(new UnityEngine.Rect(cx - s * 0.16f, cy - s * 0.32f, s * 0.03f, s * 0.16f), c);
                    Rect(new UnityEngine.Rect(cx + s * 0.13f, cy - s * 0.32f, s * 0.03f, s * 0.16f), c);
                    Circle(new UnityEngine.Rect(cx - s * 0.2f, cy - s * 0.38f, s * 0.09f, s * 0.09f), c);
                    Circle(new UnityEngine.Rect(cx + s * 0.1f, cy - s * 0.38f, s * 0.09f, s * 0.09f), c);
                    Circle(new UnityEngine.Rect(cx - s * 0.12f, cy - s * 0.06f, s * 0.08f, s * 0.08f), Color.black);
                    Circle(new UnityEngine.Rect(cx + s * 0.04f, cy - s * 0.06f, s * 0.08f, s * 0.08f), Color.black);
                    break;
                }
                case SourceKind.Money:
                {
                    var g = new Color(1f, 0.8f, 0.2f);
                    var d = new Color(0.75f, 0.55f, 0.1f);
                    for (int i = 0; i < 3; i++)
                    {
                        var cr = new UnityEngine.Rect(cx - s * 0.22f, cy + s * 0.1f - i * s * 0.12f, s * 0.44f, s * 0.18f);
                        Circle(cr, d);
                        Circle(new UnityEngine.Rect(cr.x + 2, cr.y + 1, cr.width - 4, cr.height - 4), g);
                    }
                    break;
                }
                case SourceKind.Processing:
                {
                    // 金槌
                    Rect(new UnityEngine.Rect(cx - s * 0.04f, cy - s * 0.1f, s * 0.08f, s * 0.36f), new Color(0.6f, 0.45f, 0.3f));
                    Rect(new UnityEngine.Rect(cx - s * 0.22f, cy - s * 0.26f, s * 0.44f, s * 0.18f), new Color(0.45f, 0.48f, 0.55f));
                    break;
                }
                case SourceKind.Mechanic:
                {
                    // 四本腕の宇宙人
                    var c = new Color(0.55f, 0.75f, 0.6f);
                    Circle(new UnityEngine.Rect(cx - s * 0.17f, cy - s * 0.3f, s * 0.34f, s * 0.3f), c);
                    Circle(new UnityEngine.Rect(cx - s * 0.14f, cy - s * 0.02f, s * 0.28f, s * 0.3f), c);
                    for (int i = 0; i < 2; i++)
                    {
                        float yy = cy + s * (0.02f + i * 0.1f);
                        Rect(new UnityEngine.Rect(cx - s * 0.34f, yy, s * 0.2f, s * 0.05f), c);
                        Rect(new UnityEngine.Rect(cx + s * 0.14f, yy, s * 0.2f, s * 0.05f), c);
                    }
                    Circle(new UnityEngine.Rect(cx - s * 0.08f, cy - s * 0.2f, s * 0.06f, s * 0.06f), Color.black);
                    Circle(new UnityEngine.Rect(cx + s * 0.02f, cy - s * 0.2f, s * 0.06f, s * 0.06f), Color.black);
                    break;
                }
            }
        }

        /// <summary>能力の記号（反響＝広がる輪、安定＝水平にそろった線 など）。</summary>
        public static void Ability(Rect r, CreatureAbility a)
        {
            Circle(r, new Color(1f, 1f, 1f, 0.85f));
            float cx = r.center.x, cy = r.center.y, s = r.width;
            var c = new Color(0.55f, 0.35f, 0.85f);
            switch (a)
            {
                case CreatureAbility.Echo:
                    for (int i = 1; i <= 3; i++)
                    {
                        float d = s * 0.2f * i;
                        Ring(new UnityEngine.Rect(cx - d * 0.5f, cy - d * 0.5f, d, d), c);
                    }
                    break;
                case CreatureAbility.Stabilize:
                    for (int i = 0; i < 3; i++)
                        Rect(new UnityEngine.Rect(cx - s * 0.3f, cy - s * 0.2f + i * s * 0.17f, s * 0.6f, s * 0.07f), c);
                    Circle(new UnityEngine.Rect(cx - s * 0.08f, cy - s * 0.36f, s * 0.16f, s * 0.16f), c);
                    break;
                default:
                    Circle(new UnityEngine.Rect(cx - s * 0.2f, cy - s * 0.2f, s * 0.4f, s * 0.4f), c);
                    break;
            }
            Ring(r, new Color(0.3f, 0.32f, 0.4f));
        }
    }
}
