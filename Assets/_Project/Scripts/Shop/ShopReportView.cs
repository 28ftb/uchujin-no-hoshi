using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Shop
{
    /// <summary>簡易アイコン描画（顔・形・色）。名前や訳は描かない。</summary>
    internal static class ShopIcons
    {
        private static Texture2D circle;

        private static Texture2D Circle
        {
            get
            {
                if (circle != null) return circle;
                const int n = 64;
                circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                float r = n * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
                    }
                }
                circle.SetPixels(px);
                circle.Apply();
                return circle;
            }
        }

        public static void Disc(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Circle);
            GUI.color = prev;
        }

        /// <summary>物の形と色の絵（要望ボード）。</summary>
        public static void Shape(Rect r, PrimitiveType shape, Color c)
        {
            UIStyles.FillRect(r, new Color(0.98f, 0.97f, 0.9f, 1f));
            var inner = new Rect(r.x + r.width * 0.15f, r.y + r.height * 0.15f, r.width * 0.7f, r.height * 0.7f);
            switch (shape)
            {
                case PrimitiveType.Sphere:
                    Disc(inner, c);
                    break;
                case PrimitiveType.Capsule:
                    // 魚のような横長
                    Disc(new Rect(inner.x, inner.y + inner.height * 0.25f, inner.width * 0.8f, inner.height * 0.5f), c);
                    Disc(new Rect(inner.x + inner.width * 0.7f, inner.y + inner.height * 0.3f, inner.width * 0.3f, inner.height * 0.4f), c * 0.85f);
                    break;
                case PrimitiveType.Cylinder:
                    UIStyles.FillRect(new Rect(inner.x + inner.width * 0.2f, inner.y + inner.height * 0.1f, inner.width * 0.6f, inner.height * 0.8f), c);
                    Disc(new Rect(inner.x + inner.width * 0.2f, inner.y, inner.width * 0.6f, inner.height * 0.2f), c * 1.1f);
                    break;
                case PrimitiveType.Quad:
                case PrimitiveType.Plane:
                    UIStyles.FillRect(new Rect(inner.x, inner.y + inner.height * 0.4f, inner.width, inner.height * 0.2f), c);
                    break;
                default:
                    UIStyles.FillRect(inner, c);
                    break;
            }
        }

        /// <summary>住民の顔アイコン（色＋系統の特徴）。住民辞典の名前は使わない。</summary>
        public static void Face(Rect r, Color body, AlienLineage lineage)
        {
            var b = r;
            switch (lineage)
            {
                case AlienLineage.Mochi:
                    b = new Rect(r.x, r.y + r.height * 0.25f, r.width, r.height * 0.7f);
                    break;
                case AlienLineage.Floating:
                    Disc(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.85f, r.width * 0.6f, r.height * 0.15f), new Color(0, 0, 0, 0.35f));
                    b = new Rect(r.x + r.width * 0.05f, r.y, r.width * 0.9f, r.height * 0.8f);
                    break;
                case AlienLineage.ChibiAnimal:
                    Disc(new Rect(r.x + r.width * 0.05f, r.y, r.width * 0.32f, r.height * 0.32f), body * 0.9f);
                    Disc(new Rect(r.x + r.width * 0.63f, r.y, r.width * 0.32f, r.height * 0.32f), body * 0.9f);
                    b = new Rect(r.x, r.y + r.height * 0.12f, r.width, r.height * 0.88f);
                    break;
                case AlienLineage.Plush:
                    for (int i = 0; i < 3; i++)
                        Disc(new Rect(r.x + r.width * (0.1f + i * 0.28f), r.y, r.width * 0.3f, r.height * 0.3f), Color.Lerp(body, Color.white, 0.3f));
                    break;
                case AlienLineage.MultiArm:
                    UIStyles.FillRect(new Rect(r.x - r.width * 0.12f, r.y + r.height * 0.45f, r.width * 0.2f, r.height * 0.08f), body * 0.8f);
                    UIStyles.FillRect(new Rect(r.x - r.width * 0.12f, r.y + r.height * 0.65f, r.width * 0.2f, r.height * 0.08f), body * 0.8f);
                    UIStyles.FillRect(new Rect(r.xMax - r.width * 0.08f, r.y + r.height * 0.45f, r.width * 0.2f, r.height * 0.08f), body * 0.8f);
                    UIStyles.FillRect(new Rect(r.xMax - r.width * 0.08f, r.y + r.height * 0.65f, r.width * 0.2f, r.height * 0.08f), body * 0.8f);
                    break;
                case AlienLineage.StarChild:
                    Disc(new Rect(r.x + r.width * 0.38f, r.y - r.height * 0.1f, r.width * 0.24f, r.height * 0.24f), new Color(1f, 0.9f, 0.35f));
                    break;
                case AlienLineage.Lifestyle:
                    UIStyles.FillRect(new Rect(r.x + r.width * 0.25f, r.y - r.height * 0.05f, r.width * 0.5f, r.height * 0.18f), body * 0.6f);
                    break;
                case AlienLineage.Mixed:
                    UIStyles.FillRect(new Rect(r.x + r.width * 0.48f, r.y - r.height * 0.12f, r.width * 0.04f, r.height * 0.2f), body * 0.6f);
                    Disc(new Rect(r.x + r.width * 0.42f, r.y - r.height * 0.18f, r.width * 0.16f, r.height * 0.16f), body * 1.2f);
                    break;
            }
            Disc(b, body);
            float eye = lineage == AlienLineage.Expressive ? 0.2f : 0.13f;
            var ec = new Color(0.1f, 0.1f, 0.15f);
            Disc(new Rect(b.x + b.width * 0.27f, b.y + b.height * 0.36f, b.width * eye, b.height * eye), ec);
            Disc(new Rect(b.x + b.width * (0.73f - eye), b.y + b.height * 0.36f, b.width * eye, b.height * eye), ec);
            UIStyles.FillRect(new Rect(b.x + b.width * 0.42f, b.y + b.height * 0.66f, b.width * 0.16f, b.height * 0.05f), ec);
        }

        public static void Dots(Rect r, int n)
        {
            n = Mathf.Clamp(n, 0, 6);
            float s = Mathf.Min(r.height, r.width / Mathf.Max(1, n) - 2);
            for (int i = 0; i < n; i++) Disc(new Rect(r.x + i * (s + 3), r.y, s, s), new Color(0.15f, 0.15f, 0.2f));
        }
    }

    /// <summary>
    /// 売上レポート・来店履歴・価格の比較・棚・要望・未精算（商店 §9、タブレット §10.1）。
    /// 店の帳簿（その場）とタブレットのアプリ（どこでも）で共通に使う。
    /// システムの数値・事実は主人公の言語で表示し、住民の発言の訳は出さない。
    /// マネピの再現・音声カードへの保存は、店のマネピの前にいる時だけできる（タブレット §4.4）。
    /// </summary>
    internal sealed class ShopReportView
    {
        public enum Tab { Today, Days, Visits, Compare, Shelves, Requests, Settlement }

        private readonly ShopSystem shop;
        public Tab tab = Tab.Today;
        private Vector2 scroll, scrollItems;
        private int visitDay = -1;
        private int detailDay = -1;
        private string compareItem;
        private readonly HashSet<string> savedCards = new HashSet<string>();
        private readonly HashSet<string> savedMaterials = new HashSet<string>();

        public ShopReportView(ShopSystem shop)
        {
            this.shop = shop;
        }

        public void Draw(bool showTabs = true)
        {
            if (showTabs) DrawTabs();
            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case Tab.Today: DrawToday(); break;
                case Tab.Days: DrawDays(); break;
                case Tab.Visits: DrawVisits(); break;
                case Tab.Compare: DrawCompare(); break;
                case Tab.Shelves: DrawShelves(); break;
                case Tab.Requests: DrawRequests(); break;
                case Tab.Settlement: DrawSettlement(); break;
            }
            GUILayout.EndScrollView();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            TabButton(Tab.Today, Loc.T("今日", "Today"));
            TabButton(Tab.Days, Loc.T("日別", "By day"));
            int unread = shop.UnreviewedMessageCount();
            TabButton(Tab.Visits, unread > 0 ? Loc.Format("来店履歴 ♪{0}", "Visits ♪{0}", unread) : Loc.T("来店履歴", "Visits"));
            TabButton(Tab.Compare, Loc.T("値段の比較", "Price study"));
            TabButton(Tab.Shelves, Loc.T("棚", "Shelves"));
            TabButton(Tab.Requests, Loc.T("要望", "Requests"));
            TabButton(Tab.Settlement, Loc.T("未精算", "Unsettled"));
            GUILayout.EndHorizontal();
            if (!shop.LocalAtShop)
                GUILayout.Label(Loc.T("※ マネピの再現と音声カードへの保存は、店のマネピの前でできます。", "* Manepi playback and saving voice cards work at the shop's Manepi."), UIStyles.LabelDim);
        }

        private void TabButton(Tab t, string label)
        {
            if (GUILayout.Button(label, tab == t ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(36)))
            {
                tab = t;
                scroll = Vector2.zero;
            }
        }

        // ---------------- 今日 ----------------

        private void DrawToday()
        {
            int today = shop.Today;
            var sum = shop.Data.Summary(today, false);
            GUILayout.Label(Loc.Format("{0}日目の売上", "Sales on day {0}", today), UIStyles.LabelBold);
            GUILayout.Label(shop.IsOpen ? Loc.Format("営業中（{0}:00〜{1}:00）", "Open ({0}:00-{1}:00)", shop.Data.openHour, shop.Data.closeHour)
                                        : Loc.Format("準備中（{0}:00〜{1}:00）", "Closed ({0}:00-{1}:00)", shop.Data.openHour, shop.Data.closeHour), UIStyles.LabelDim);
            DrawSummaryBlock(sum);
            int unread = shop.UnreviewedMessageCount();
            if (unread > 0) GUILayout.Label(Loc.Format("マネピが新しく覚えた声：{0}件", "New voices the Manepi remembered: {0}", unread), UIStyles.Label);
        }

        private void DrawSummaryBlock(DailySummaryData sum)
        {
            if (sum == null)
            {
                GUILayout.Label(Loc.T("まだ記録がありません。", "No records yet."), UIStyles.LabelDim);
                return;
            }
            GUILayout.BeginHorizontal(UIStyles.Box);
            Stat(Loc.T("売上", "Revenue"), Wallet.Format(sum.revenue));
            Stat(Loc.T("販売個数", "Sold"), sum.itemsSold.ToString());
            Stat(Loc.T("来店", "Visitors"), sum.visitors.ToString());
            Stat(Loc.T("買った人", "Buyers"), sum.buyers.ToString());
            Stat(Loc.T("戻された", "Put back"), sum.putBacks.ToString());
            Stat(Loc.T("マネピの声", "Voices"), sum.manepiMessages.ToString());
            GUILayout.EndHorizontal();
            if (sum.revenueByOwner.Count > 1)
            {
                foreach (var p in sum.revenueByOwner)
                    GUILayout.Label("・" + shop.OwnerLabel(p.key) + "：" + Wallet.Format(p.value), UIStyles.LabelSmall);
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("商品", "Item"), UIStyles.LabelBold, GUILayout.Width(320));
            GUILayout.Label(Loc.T("販売数", "Sold"), UIStyles.LabelBold, GUILayout.Width(90));
            GUILayout.Label(Loc.T("売上", "Revenue"), UIStyles.LabelBold, GUILayout.Width(120));
            GUILayout.Label(Loc.T("見た", "Looked"), UIStyles.LabelBold, GUILayout.Width(80));
            GUILayout.Label(Loc.T("戻された", "Put back"), UIStyles.LabelBold, GUILayout.Width(100));
            GUILayout.EndHorizontal();
            foreach (var it in sum.items)
            {
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36), GUILayout.Height(36));
                UIStyles.ItemIcon(r, new ItemStack(it.itemId, 1));
                GUILayout.Label(ItemRegistry.NameOf(it.itemId), UIStyles.Label, GUILayout.Width(280));
                GUILayout.Label(it.sold.ToString(), UIStyles.Label, GUILayout.Width(90));
                GUILayout.Label(Wallet.Format(it.revenue), UIStyles.Label, GUILayout.Width(120));
                GUILayout.Label(it.looked.ToString(), UIStyles.Label, GUILayout.Width(80));
                GUILayout.Label(it.putBack.ToString(), UIStyles.Label, GUILayout.Width(100));
                if (GUILayout.Button(Loc.T("比較", "Study"), UIStyles.ButtonSmall, GUILayout.Width(80)))
                {
                    compareItem = it.itemId;
                    tab = Tab.Compare;
                }
                GUILayout.EndHorizontal();
            }
            if (sum.soldOut.Count > 0)
            {
                var names = new List<string>();
                foreach (var id in sum.soldOut) names.Add(ItemRegistry.NameOf(id));
                GUILayout.Label(Loc.T("売り切れ：", "Sold out: ") + string.Join("、", names), UIStyles.LabelSmall);
            }
        }

        private static void Stat(string label, string value)
        {
            GUILayout.BeginVertical(GUILayout.Width(120));
            GUILayout.Label(label, UIStyles.LabelDim);
            GUILayout.Label(value, UIStyles.LabelBold);
            GUILayout.EndVertical();
        }

        // ---------------- 日別 ----------------

        private void DrawDays()
        {
            var days = new List<DailySummaryData>(shop.Data.days);
            days.Sort((a, b) => b.day.CompareTo(a.day));
            if (days.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ記録がありません。", "No records yet."), UIStyles.LabelDim);
                return;
            }
            int max = 1;
            foreach (var d in days) max = Mathf.Max(max, d.revenue);
            foreach (var d in days)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.Format("{0}日目", "Day {0}", d.day), detailDay == d.day ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Width(110)))
                    detailDay = detailDay == d.day ? -1 : d.day;
                var bar = GUILayoutUtility.GetRect(300, 24, GUILayout.Width(300), GUILayout.Height(24));
                UIStyles.ProgressBar(bar, d.revenue / (float)max, UIStyles.AccentWarm);
                GUILayout.Label(Loc.Format("{0}　{1}個　来店{2}人", "{0}  {1} sold  {2} visitors", Wallet.Format(d.revenue), d.itemsSold, d.visitors), UIStyles.Label);
                GUILayout.EndHorizontal();
                if (detailDay == d.day)
                {
                    GUILayout.BeginVertical(UIStyles.Box);
                    DrawSummaryBlock(d);
                    if (GUILayout.Button(Loc.T("この日の来店履歴", "Visits on this day"), UIStyles.ButtonSmall, GUILayout.Width(220)))
                    {
                        visitDay = d.day;
                        tab = Tab.Visits;
                    }
                    GUILayout.EndVertical();
                }
            }
        }

        // ---------------- 来店履歴 ----------------

        private void DrawVisits()
        {
            int today = shop.Today;
            if (visitDay < 0) visitDay = today;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", UIStyles.ButtonSmall, GUILayout.Width(48))) visitDay = Mathf.Max(1, visitDay - 1);
            GUILayout.Label(Loc.Format("{0}日目", "Day {0}", visitDay), UIStyles.LabelBold, GUILayout.Width(120));
            if (GUILayout.Button("▶", UIStyles.ButtonSmall, GUILayout.Width(48))) visitDay = Mathf.Min(today, visitDay + 1);
            GUILayout.EndHorizontal();
            var list = new List<VisitRecordData>();
            foreach (var v in shop.Data.visits) if (v.day == visitDay) list.Add(v);
            list.Reverse();
            if (list.Count == 0) GUILayout.Label(Loc.T("この日の来店はありません。", "No visitors on this day."), UIStyles.LabelDim);
            foreach (var v in list) DrawVisitRow(v);
            if (shop.LocalAtShop && visitDay == today) shop.MarkMessagesReviewed();
        }

        /// <summary>来店1件（時刻・顔・行動の事実・操作）。</summary>
        public void DrawVisitRow(VisitRecordData v)
        {
            GUILayout.BeginHorizontal(UIStyles.Box);
            GUILayout.Label(string.Format("{0:00}:{1:00}", v.minute / 60, v.minute % 60), UIStyles.Label, GUILayout.Width(70));
            var fr = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
            ShopIcons.Face(fr, v.faceColor, v.lineage);
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            foreach (var f in VisitFacts(v, true)) GUILayout.Label(f, UIStyles.LabelSmall);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(190));
            DrawRecordButtons(v.recordId, v, null);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawRecordButtons(string recordId, VisitRecordData v, ShopRequestData req)
        {
            var tablet = Game.Instance.Service<ITabletService>();
            bool atShop = shop.LocalAtShop;
            var record = shop.FindRecord(recordId);
            if (record != null)
            {
                GUI.enabled = atShop;
                if (GUILayout.Button(Loc.T("▶ マネピの再現", "▶ Manepi replay"), UIStyles.ButtonSmall)) shop.Replay(record);
                bool saved = savedCards.Contains(recordId);
                GUI.enabled = atShop && tablet != null && !saved;
                if (GUILayout.Button(saved ? Loc.T("音声カード保存済み", "Card saved") : Loc.T("音声カードに保存", "Save voice card"), UIStyles.ButtonSmall))
                {
                    var label = CardLabel(v, req);
                    tablet.SaveVoiceCard(record, label.ja, label.en);
                    savedCards.Add(recordId);
                    UIManager.Toast(Loc.T("音声カードを保存しました", "Voice card saved"));
                }
                GUI.enabled = true;
            }
            else if (!string.IsNullOrEmpty(recordId))
            {
                GUILayout.Label(Loc.T("（マネピはもう覚えていない）", "(the Manepi forgot)"), UIStyles.LabelDim);
            }
            if (v != null && tablet != null)
            {
                string mid = "shop.visit." + v.id;
                bool done = savedMaterials.Contains(mid);
                GUI.enabled = !done;
                if (GUILayout.Button(done ? Loc.T("資料に追加済み", "In materials") : Loc.T("事実を資料へ", "Add to materials"), UIStyles.ButtonSmall))
                {
                    tablet.AddMaterial(BuildMaterial(v));
                    savedMaterials.Add(mid);
                    UIManager.Toast(Loc.T("資料ボックスに追加しました", "Added to the materials box"));
                }
                GUI.enabled = true;
            }
            string residentId = v != null ? v.residentId : req?.residentId;
            if (tablet != null && !string.IsNullOrEmpty(residentId) && GUILayout.Button(Loc.T("住民辞典", "Resident notes"), UIStyles.ButtonSmall))
            {
                // 特定の住民のページを開く契約がまだ無いため、住民辞典アプリを開く（コアへの追加要望あり）
                tablet.Open(ShopSystem.ResidentsAppId);
            }
        }

        // ---------------- 値段の比較（価格研究：タブレット §10.1） ----------------

        private void DrawCompare()
        {
            var items = new List<string>();
            for (int i = shop.Data.visits.Count - 1; i >= 0; i--)
                foreach (var l in shop.Data.visits[i].lines)
                    if (!items.Contains(l.itemId)) items.Add(l.itemId);
            if (items.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ比べられる記録がありません。値段を変えて、住民の反応を集めてみよう。", "Nothing to compare yet. Try changing prices and collect reactions."), UIStyles.LabelDim);
                return;
            }
            if (compareItem == null || !items.Contains(compareItem)) compareItem = items[0];
            scrollItems = GUILayout.BeginScrollView(scrollItems, GUILayout.Height(74));
            GUILayout.BeginHorizontal();
            foreach (var id in items)
            {
                GUILayout.BeginVertical(GUILayout.Width(120));
                var r = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                UIStyles.ItemIcon(r, new ItemStack(id, 1), id == compareItem);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) compareItem = id;
                GUILayout.Label(ItemRegistry.NameOf(id), UIStyles.LabelDim, GUILayout.Width(120));
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            // 販売の推移（直近7日）
            GUILayout.Label(Loc.Format("{0}：直近7日の販売数", "{0}: sold in the last 7 days", ItemRegistry.NameOf(compareItem)), UIStyles.LabelBold);
            int today = shop.Today;
            GUILayout.BeginHorizontal();
            int maxSold = 1;
            var counts = new int[7];
            for (int i = 0; i < 7; i++)
            {
                var s = shop.Data.Summary(today - 6 + i, false);
                foreach (var it in s?.items ?? new List<ItemDayStat>()) if (it.itemId == compareItem) counts[i] = it.sold;
                maxSold = Mathf.Max(maxSold, counts[i]);
            }
            for (int i = 0; i < 7; i++)
            {
                GUILayout.BeginVertical(GUILayout.Width(70));
                var r = GUILayoutUtility.GetRect(60, 60, GUILayout.Width(60), GUILayout.Height(60));
                UIStyles.FillRect(r, new Color(0, 0, 0, 0.3f));
                float h = r.height * counts[i] / maxSold;
                UIStyles.FillRect(new Rect(r.x + 8, r.yMax - h, r.width - 16, h), UIStyles.Good);
                GUILayout.Label(string.Format("{0}:{1}", today - 6 + i, counts[i]), UIStyles.LabelDim);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            // 値段ごとの反応
            var rows = new List<KeyValuePair<VisitRecordData, VisitLineData>>();
            foreach (var v in shop.Data.visits)
                foreach (var l in v.lines)
                    if (l.itemId == compareItem) rows.Add(new KeyValuePair<VisitRecordData, VisitLineData>(v, l));
            rows.Sort((a, b) =>
            {
                int c = a.Value.unitPrice.CompareTo(b.Value.unitPrice);
                if (c != 0) return c;
                return (a.Key.day * 1440 + a.Key.minute).CompareTo(b.Key.day * 1440 + b.Key.minute);
            });
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("値段ごとの様子（同じ値段でも住民によって違います）", "Reactions by price (they differ by resident)"), UIStyles.LabelBold);
            int lastPrice = -1;
            foreach (var kv in rows)
            {
                var v = kv.Key;
                var l = kv.Value;
                if (l.unitPrice != lastPrice)
                {
                    lastPrice = l.unitPrice;
                    GUILayout.Label("— " + Wallet.Format(l.unitPrice) + " —", UIStyles.LabelBold);
                }
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}", "Day {0} {1:00}:{2:00}", v.day, v.minute / 60, v.minute % 60), UIStyles.LabelSmall, GUILayout.Width(150));
                var fr = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                ShopIcons.Face(fr, v.faceColor, v.lineage);
                GUILayout.Space(6);
                GUILayout.Label(LineFact(l, false) + (FocusOf(v) == l && !string.IsNullOrEmpty(v.recordId) ? "　♪" : ""), UIStyles.LabelSmall);
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(190));
                if (FocusOf(v) == l) DrawRecordButtons(v.recordId, v, null);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>その来店で反応の中心になった商品の行（マネピの声はこの商品についての声）。</summary>
        private static VisitLineData FocusOf(VisitRecordData v)
        {
            foreach (var l in v.lines) if (l.outcome == VisitLineOutcome.Bought) return l;
            foreach (var l in v.lines) if (l.outcome == VisitLineOutcome.PutBackAfterPrice) return l;
            foreach (var l in v.lines) if (l.outcome == VisitLineOutcome.PutBackAfterWallet) return l;
            foreach (var l in v.lines) if (l.outcome == VisitLineOutcome.PutBackAfterSniff) return l;
            return null;
        }

        // ---------------- 棚 ----------------

        private void DrawShelves()
        {
            int today = shop.Today;
            for (int i = 0; i < shop.Data.slots.Count; i++)
            {
                var s = shop.Data.slots[i];
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(string.Format("{0}. {1}", i + 1, ShopRules.DisplayLabel(s.display)), UIStyles.LabelSmall, GUILayout.Width(150));
                var r = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                UIStyles.ItemIcon(r, s.IsEmpty ? null : s.goods);
                if (s.IsEmpty) GUILayout.Label(Loc.T("（空き）", "(empty)"), UIStyles.LabelDim);
                else
                {
                    var def = ItemRegistry.GetOrPlaceholder(s.goods.itemId);
                    GUILayout.Label(string.Format("{0} ×{1}", s.goods.DisplayName, s.goods.count), UIStyles.Label, GUILayout.Width(260));
                    GUILayout.Label(Wallet.Format(s.price) + Loc.Format("（目安 {0}）", " (ref {0})", Wallet.Format(ShopRules.SuggestedPrice(def, s.goods.quality))), UIStyles.LabelSmall, GUILayout.Width(220));
                    GUILayout.Label(shop.OwnerLabel(s.ownerId), UIStyles.LabelDim, GUILayout.Width(150));
                    string info = Loc.Format("並べて{0}日", "{0} days", Mathf.Max(0, today - s.listedDay));
                    if (ShopRules.Freshness(s, def, today) < 1f) info += Loc.T("・鮮度↓", " · less fresh");
                    if (s.autoRestock) info += Loc.T("・自動補充", " · auto restock");
                    GUILayout.Label(info, UIStyles.LabelDim);
                }
                GUILayout.EndHorizontal();
            }
        }

        // ---------------- 要望 ----------------

        private void DrawRequests()
        {
            GUILayout.Label(Loc.T("住民がマネピに何かを伝え、要望ボードに絵を貼っていきました。", "Residents told the Manepi something and pinned pictures on the board."), UIStyles.LabelDim);
            DrawRequestCards(this, shop, true);
        }

        /// <summary>要望ボードのカード（絵・顔・数・再現）。文字の訳は出さない。</summary>
        public static void DrawRequestCards(ShopReportView view, ShopSystem shop, bool includeFulfilled)
        {
            var list = new List<ShopRequestData>(shop.Data.requests);
            list.Reverse();
            int shown = 0;
            foreach (var r in list)
            {
                if (r.fulfilled && !includeFulfilled) continue;
                shown++;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var pic = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72), GUILayout.Height(72));
                ShopIcons.Shape(pic, r.pictureShape, r.pictureColor);
                GUILayout.Space(8);
                GUILayout.BeginVertical(GUILayout.Width(150));
                var fr = GUILayoutUtility.GetRect(44, 44, GUILayout.Width(44), GUILayout.Height(44));
                ShopIcons.Face(fr, r.faceColor, r.lineage);
                var dots = GUILayoutUtility.GetRect(120, 14, GUILayout.Width(120), GUILayout.Height(14));
                ShopIcons.Dots(dots, r.quantity);
                GUILayout.EndVertical();
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}", "Day {0} {1:00}:{2:00}", r.day, r.minute / 60, r.minute % 60), UIStyles.LabelSmall);
                if (r.fulfilled) GUILayout.Label(Loc.Format("{0}日目、この住民が店で買い物をした", "Day {0}: this resident bought something here", r.fulfilledDay), UIStyles.LabelSmall);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(190));
                if (view != null) view.DrawRecordButtons(r.recordId, null, r);
                else
                {
                    var rec = shop.FindRecord(r.recordId);
                    GUI.enabled = rec != null && shop.LocalAtShop;
                    if (GUILayout.Button(Loc.T("▶ マネピの再現", "▶ Manepi replay"), UIStyles.ButtonSmall)) shop.Replay(rec);
                    GUI.enabled = true;
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label(Loc.T("ボードには何も貼られていません。", "Nothing is pinned on the board."), UIStyles.LabelDim);
        }

        // ---------------- 未精算（委託販売） ----------------

        private void DrawSettlement()
        {
            var local = Game.Instance.LocalPlayer;
            if (local == null) return;
            var mine = shop.Data.Settlement(local.PlayerId, false);
            GUILayout.Label(Loc.T("委託販売の未精算（持ち主が店で受け取ります）", "Unsettled consignment sales (owners collect them at the shop)"), UIStyles.LabelBold);
            if (mine != null && !mine.IsEmpty)
            {
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.Label(Loc.Format("あなたの売上：{0}（{1}個）", "Your sales: {0} ({1} sold)", Wallet.Format(mine.money), mine.soldCount), UIStyles.Label);
                foreach (var it in mine.returnedItems) GUILayout.Label(Loc.Format("返品：{0} ×{1}", "Returned: {0} x{1}", it.DisplayName, it.count), UIStyles.LabelSmall);
                GUI.enabled = shop.LocalAtShop;
                if (GUILayout.Button(Loc.T("受け取る", "Collect"), UIStyles.Button, GUILayout.Width(200)))
                    WorldAuthority.Submit(new ShopCollectSettlementCommand());
                GUI.enabled = true;
                if (!shop.LocalAtShop) GUILayout.Label(Loc.T("（店で受け取れます）", "(collect at the shop)"), UIStyles.LabelDim);
                GUILayout.EndVertical();
            }
            else GUILayout.Label(Loc.T("あなたの未精算はありません。", "Nothing unsettled for you."), UIStyles.LabelDim);

            if (shop.IsLocalHost)
            {
                GUILayout.Space(8);
                GUILayout.Label(Loc.T("預かっている委託販売の売上", "Consignment money held for others"), UIStyles.LabelBold);
                bool any = false;
                foreach (var s in shop.Data.settlements)
                {
                    if (s.ownerId == local.PlayerId || s.IsEmpty) continue;
                    any = true;
                    GUILayout.Label(string.Format("・{0}：{1}（{2}）", shop.OwnerLabel(s.ownerId), Wallet.Format(s.money), Loc.Format("返品{0}件", "{0} returns", s.returnedItems.Count)), UIStyles.LabelSmall);
                }
                if (!any) GUILayout.Label(Loc.T("ありません。", "None."), UIStyles.LabelDim);
            }
        }

        // =================================================================
        // 事実の文章（主人公の装置の記録。行動の事実だけで、住民の発言の訳や理由の断定は書かない）
        // =================================================================

        internal static string LineFact(VisitLineData l, bool withName)
        {
            string name = withName ? ItemRegistry.NameOf(l.itemId) + ItemStack.QualityStars(l.quality) : "";
            string price = Wallet.Format(l.unitPrice);
            switch (l.outcome)
            {
                case VisitLineOutcome.Bought:
                    return withName ? Loc.Format("{0} を{1}個買った（{2}）", "Bought {1} x {0} ({2})", name, l.qty, Wallet.Format(l.unitPrice * l.qty))
                                    : Loc.Format("{0}個買った", "Bought {0}", l.qty);
                case VisitLineOutcome.PutBackAfterPrice:
                    return withName ? Loc.Format("{0}（{1}）の値札を見て、棚に戻した", "Looked at the tag of {0} ({1}) and put it back", name, price)
                                    : Loc.T("値札を見て、棚に戻した", "Looked at the price tag, put it back");
                case VisitLineOutcome.PutBackAfterWallet:
                    return withName ? Loc.Format("{0}（{1}）を手に取り、財布をのぞいて戻した", "Picked up {0} ({1}), peeked in their purse, put it back", name, price)
                                    : Loc.T("財布をのぞいて戻した", "Peeked in their purse, put it back");
                case VisitLineOutcome.PutBackAfterSniff:
                    return withName ? Loc.Format("{0}（{1}）のにおいをかいで戻した", "Sniffed {0} ({1}) and put it back", name, price)
                                    : Loc.T("においをかいで戻した", "Sniffed it, put it back");
                case VisitLineOutcome.PutBackOther:
                    return withName ? Loc.Format("{0}（{1}）を手に取ったが、戻した", "Picked up {0} ({1}) but put it back", name, price)
                                    : Loc.T("手に取ったが、戻した", "Picked it up, put it back");
                default:
                    return withName ? Loc.Format("{0}（{1}）を見ていった", "Looked at {0} ({1})", name, price)
                                    : Loc.T("見ていった", "Looked at it");
            }
        }

        internal static List<string> VisitFacts(VisitRecordData v, bool withNames)
        {
            var facts = new List<string>();
            foreach (var l in v.lines) facts.Add(LineFact(l, withNames));
            bool boughtAny = false;
            foreach (var l in v.lines) if (l.outcome == VisitLineOutcome.Bought) boughtAny = true;
            if (!boughtAny && v.lines.Count == 0) facts.Add(Loc.T("棚を見回して、何も持たずに帰った", "Looked around the shelves and left empty-handed"));
            else if (!boughtAny) facts.Add(Loc.T("何も買わずに帰った", "Left without buying"));
            if (!string.IsNullOrEmpty(v.requestId)) facts.Add(Loc.T("要望ボードに絵が増えた", "A new picture appeared on the request board"));
            if (!string.IsNullOrEmpty(v.recordId)) facts.Add(Loc.T("♪ マネピに何か伝えていった", "♪ Told the Manepi something"));
            if (v.dropIn) facts.Add(Loc.T("（通りがかりに立ち寄った）", "(dropped in while passing)"));
            return facts;
        }

        private (string ja, string en) CardLabel(VisitRecordData v, ShopRequestData req)
        {
            if (v != null)
            {
                var focus = FocusOf(v);
                string ja = string.Format("商店のマネピ：{0}日目 {1:00}:{2:00}", v.day, v.minute / 60, v.minute % 60);
                string en = string.Format("Shop Manepi: day {0} {1:00}:{2:00}", v.day, v.minute / 60, v.minute % 60);
                if (focus != null)
                {
                    var def = ItemRegistry.GetOrPlaceholder(focus.itemId);
                    string outJa = focus.outcome == VisitLineOutcome.Bought ? string.Format("{0}個購入", focus.qty) : "購入せず";
                    string outEn = focus.outcome == VisitLineOutcome.Bought ? string.Format("bought {0}", focus.qty) : "not bought";
                    ja += string.Format("／{0}{1} {2}／{3}", def.NameJa, ItemStack.QualityStars(focus.quality), Wallet.Format(focus.unitPrice), outJa);
                    en += string.Format(" / {0}{1} {2} / {3}", def.NameEn, ItemStack.QualityStars(focus.quality), Wallet.Format(focus.unitPrice), outEn);
                }
                else
                {
                    ja += "／購入せず";
                    en += " / not bought";
                }
                return (ja, en);
            }
            if (req != null)
                return (string.Format("商店のマネピ（要望ボード）：{0}日目 {1:00}:{2:00}", req.day, req.minute / 60, req.minute % 60),
                        string.Format("Shop Manepi (request board): day {0} {1:00}:{2:00}", req.day, req.minute / 60, req.minute % 60));
            return ("商店のマネピ", "Shop Manepi");
        }

        /// <summary>来店の事実を資料（SalesRecord）にする。訳や理由の断定は書かない。</summary>
        private TabletMaterial BuildMaterial(VisitRecordData v)
        {
            var prev = Loc.Current;
            Loc.Current = UILanguage.Japanese;
            var ja = string.Join("。", VisitFacts(v, true));
            Loc.Current = UILanguage.English;
            var en = string.Join(". ", VisitFacts(v, true));
            Loc.Current = prev;
            var m = new TabletMaterial
            {
                id = "shop.visit." + v.id,
                kind = TabletMaterialKind.SalesRecord,
                titleJa = string.Format("商店の記録 {0}日目 {1:00}:{2:00}", v.day, v.minute / 60, v.minute % 60),
                titleEn = string.Format("Shop record day {0} {1:00}:{2:00}", v.day, v.minute / 60, v.minute % 60),
                factJa = ja,
                factEn = en,
                day = v.day,
                minuteOfDay = v.minute,
                linkedRecordId = v.recordId,
                linkedResidentId = v.residentId
            };
            m.tags.Add("shop");
            foreach (var l in v.lines)
            {
                if (!m.relatedIds.Contains(l.itemId)) m.relatedIds.Add(l.itemId);
                string pt = "price:" + l.itemId + ":" + l.unitPrice;
                if (!m.tags.Contains(pt)) m.tags.Add(pt);
            }
            m.tags.Add(v.TotalSpent > 0 ? "bought" : "not_bought");
            return m;
        }
    }
}
