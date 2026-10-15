using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>料理・加工の画面で使う小さな部品（能力アイコン・工程チップ・アイテムアイコン）。</summary>
    public static class CookingUI
    {
        private static readonly Dictionary<int, GUIStyle> chipStyles = new Dictionary<int, GUIStyle>();
        private static GUIStyle glyphStyle;
        private static GUIStyle smallWrap;
        private static GUIStyle header;

        public static GUIStyle SmallWrap
        {
            get
            {
                if (smallWrap == null)
                {
                    smallWrap = new GUIStyle(UIStyles.LabelSmall) { wordWrap = true, richText = true };
                }
                return smallWrap;
            }
        }

        public static GUIStyle Header
        {
            get
            {
                if (header == null)
                {
                    header = new GUIStyle(UIStyles.LabelBold) { fontSize = 24 };
                    header.normal.textColor = UIStyles.AccentWarm;
                }
                return header;
            }
        }

        private static GUIStyle ChipStyle(Color fg, int size)
        {
            Color32 c = fg;
            int key = (c.r << 24) ^ (c.g << 16) ^ (c.b << 8) ^ size;
            if (chipStyles.TryGetValue(key, out var s)) return s;
            s = new GUIStyle(UIStyles.LabelSmall) { alignment = TextAnchor.MiddleCenter, fontSize = size, wordWrap = false, richText = false };
            s.normal.textColor = fg;
            s.padding = new RectOffset(6, 6, 2, 2);
            chipStyles[key] = s;
            return s;
        }

        /// <summary>色付きの札（GUILayout 内）。</summary>
        public static Rect Chip(string text, Color bg, Color fg, float width = 0f, float height = 30f, int fontSize = 16)
        {
            var style = ChipStyle(fg, fontSize);
            var content = new GUIContent(text);
            float w = width > 0 ? width : style.CalcSize(content).x + 8f;
            var r = GUILayoutUtility.GetRect(w, height, GUILayout.Width(w), GUILayout.Height(height));
            UIStyles.FillRect(r, bg);
            GUI.Label(r, content, style);
            return r;
        }

        /// <summary>能力のアイコン（生物の能力の絵の代わり：1文字＋色の丸札）。</summary>
        public static Rect AbilityIcon(CreatureAbility a, float size = 30f, bool dim = false)
        {
            var info = CookingTables.Ability(a);
            var bg = info.Color * (dim ? 0.45f : 0.9f);
            bg.a = 1f;
            return Chip(info.Glyph, bg, dim ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.1f, 0.1f, 0.15f), size, size, 18);
        }

        /// <summary>工程の札。</summary>
        public static Rect StepIcon(CraftStep s, float size = 30f, bool dim = false)
        {
            var info = CookingTables.Step(s);
            var bg = info.Color * (dim ? 0.4f : 0.9f);
            bg.a = 1f;
            return Chip(info.Glyph, bg, dim ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.1f, 0.1f, 0.15f), size, size, 18);
        }

        /// <summary>工程の計画（誰が・どうやって）を縦に描く。</summary>
        public static void StepPlanBlock(StepPlan sp, string playerId, float width = 150f)
        {
            var info = CookingTables.Step(sp.Step);
            GUILayout.BeginVertical(UIStyles.Box, GUILayout.Width(width));
            GUILayout.BeginHorizontal();
            StepIcon(sp.Step, 30f, sp.Mode == StepMode.Missing);
            GUILayout.Label(info.Name, UIStyles.LabelSmall);
            GUILayout.EndHorizontal();
            string sub;
            Color col = UIStyles.TextDim;
            switch (sp.Mode)
            {
                case StepMode.BuiltIn:
                    sub = Loc.T("設備・手作業", "Station / by hand");
                    if (sp.Helper != null) sub += Loc.Format("\n＋{0}が手伝う", "\n+ {0} helps", sp.Helper.DisplayName);
                    break;
                case StepMode.Creature:
                    sub = sp.Source.DisplayName;
                    int m = CraftEngine.MasteryLevel(CookingSystem.Instance != null ? CookingSystem.Instance.Profile(playerId) : null, sp.Source.CreatureUid, sp.Source.Ability);
                    if (m > 0) sub += " " + new string('★', m);
                    if (sp.Source.InDevice) sub += Loc.T("（器具で）", " (in device)");
                    if (sp.Source.Borrowed) sub += Loc.T("（借りる）", " (borrowed)");
                    col = UIStyles.Good;
                    break;
                case StepMode.Weather:
                    sub = Loc.T("天気の力を借りる", "Borrow the weather");
                    col = UIStyles.Accent;
                    break;
                case StepMode.Timed:
                    sub = Loc.Format("約{0}時間", "~{0}h", Mathf.CeilToInt(sp.Hours));
                    if (sp.Source != null) sub += Loc.Format("\n{0}が早めてくれる", "\n{0} speeds it up", sp.Source.DisplayName);
                    col = UIStyles.AccentWarm;
                    break;
                case StepMode.Slow:
                    sub = Loc.Format("設備まかせ 約{0}時間", "Station alone ~{0}h", Mathf.CeilToInt(sp.Hours));
                    col = UIStyles.AccentWarm;
                    break;
                default:
                    sub = MissingText(sp.Step, playerId);
                    col = UIStyles.Bad;
                    break;
            }
            var style = new GUIStyle(SmallWrap);
            style.normal.textColor = col;
            if (sp.Mode == StepMode.Missing && info.Abilities.Length > 0)
            {
                // 足りない能力をアイコンで示す（生物の能力の絵の代わり）
                GUILayout.BeginHorizontal();
                foreach (var a in info.Abilities) AbilityIcon(a, 26f, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(sub, style);
            GUILayout.EndVertical();
        }

        /// <summary>足りない工程の案内：どの能力が要るか（アイコン）と、見つけたことのある生物の呼び名。</summary>
        public static string MissingText(CraftStep step, string playerId)
        {
            var info = CookingTables.Step(step);
            if (info.Timed)
            {
                foreach (var d in CookingContent.Devices)
                    if (d.TimedStep.HasValue && d.TimedStep.Value == step && d.Station == CraftStationType.Kitchen)
                        return Loc.Format("要：{0}（家に置く）", "Needs: {0} (place at home)", d.Name);
                return Loc.T("ここではできない", "Not possible here");
            }
            if (info.Abilities.Length == 0) return Loc.T("ここではできない", "Not possible here");
            var a = info.Abilities[0];
            string s = Loc.Format("要：「{0}」の力を持つ生き物", "Needs a creature that can \"{0}\"", CookingTables.Ability(a).Name);
            var names = CraftEngine.KnownSpeciesWith(a, playerId);
            if (names.Count > 0) s += Loc.Format("\n（{0} など）", "\n(e.g. {0})", string.Join(Loc.T("、", ", "), names.ToArray()));
            return s;
        }

        public static void ItemIcon(string itemId, int count = 1, int quality = 0, float size = 48f)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            UIStyles.ItemIcon(r, new ItemStack(itemId, Mathf.Max(1, count), quality));
        }

        public static string IngredientLabel(IngredientSpec spec)
        {
            if (!string.IsNullOrEmpty(spec.ItemId)) return ItemRegistry.NameOf(spec.ItemId);
            return spec.Label;
        }

        public static string QualityText(int q)
        {
            string s = ItemStack.QualityStars(q);
            return string.IsNullOrEmpty(s) ? Loc.T("ふつう", "Normal") : s;
        }

        /// <summary>アイテムの味・食感などの手掛かり（§7.1）。タグから表示する。</summary>
        public static string FlavorText(ItemDefinition d)
        {
            if (d == null) return "";
            var parts = new List<string>();
            if (d.HasTag(ItemTags.Sweet)) parts.Add(Loc.T("あまい", "sweet"));
            if (d.HasTag(ItemTags.Sour)) parts.Add(Loc.T("すっぱい", "sour"));
            if (d.HasTag(ItemTags.Savory)) parts.Add(Loc.T("うまみ", "savory"));
            if (d.HasTag(ItemTags.Spicy)) parts.Add(Loc.T("からい", "spicy"));
            if (d.HasTag(ItemTags.Fragrant)) parts.Add(Loc.T("香り", "fragrant"));
            if (d.HasTag(CookingTags.Fluffy)) parts.Add(Loc.T("ふわふわ", "fluffy"));
            if (d.HasTag(CookingTags.Jiggly)) parts.Add(Loc.T("ぷるぷる", "jiggly"));
            if (d.HasTag(CookingTags.Crispy)) parts.Add(Loc.T("カリカリ", "crispy"));
            if (d.HasTag(CookingTags.Chewy)) parts.Add(Loc.T("もちもち", "chewy"));
            if (d.HasTag(CookingTags.Smooth)) parts.Add(Loc.T("なめらか", "smooth"));
            if (d.HasTag(CookingTags.Hot)) parts.Add(Loc.T("あたたかい", "hot"));
            if (d.HasTag(CookingTags.Cold)) parts.Add(Loc.T("つめたい", "cold"));
            if (d.HasTag(ItemTags.Glowing)) parts.Add(Loc.T("光る", "glowing"));
            if (d.HasTag(CookingTags.Fermented)) parts.Add(Loc.T("発酵", "fermented"));
            return string.Join(Loc.T("・", " / "), parts.ToArray());
        }
    }
}
