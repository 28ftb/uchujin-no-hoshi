using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 食事（§7.2【未決定／提案】）。スタミナ回復は不採用（基本決定 §4）。
    /// 提案として「短時間の能力補助」と「食の記録（コレクション）」を実装する。CookingTuning.FoodEffectsEnabled で無効化できる。
    /// 食べなければ主要コンテンツに参加できない、という必須条件にはしない。
    /// </summary>
    public static class FoodEffects
    {
        public static bool IsEdible(ItemDefinition d)
        {
            if (d == null) return false;
            if (d.HasTag(ItemTags.Fuel) || d.Category == ItemCategory.RocketPart) return false;
            if (d.Category != ItemCategory.Food && d.Category != ItemCategory.Processed) return false;
            return d.HasTag(ItemTags.Edible);
        }

        /// <summary>そのアイテムを食べた時の効果（作り方に紐づく）。</summary>
        public static RecipeDef EffectRecipeFor(string itemId)
        {
            foreach (var r in RecipeBook.Producing(itemId)) if (!string.IsNullOrEmpty(r.FoodEffectId)) return r;
            return null;
        }

        /// <summary>持ち物のスロットから1つ食べる（ローカル操作）。</summary>
        public static bool Eat(CookingSystem sys, PlayerState player, int slotIndex)
        {
            if (player == null || !player.IsLocal) return false;
            var s = player.Inventory.GetSlot(slotIndex);
            if (s == null || s.IsEmpty || !IsEdible(s.Definition)) return false;
            var taken = player.Inventory.TakeFromSlot(slotIndex, 1, "eat");
            if (taken == null) return false;
            var def = taken.Definition;
            var prof = sys.Profile(player.PlayerId);
            string effectText = "";
            if (CookingTuning.FoodEffectsEnabled)
            {
                var r = EffectRecipeFor(taken.itemId);
                if (r != null)
                {
                    var eff = CookingTables.Effect(r.FoodEffectId);
                    prof.effectId = r.FoodEffectId;
                    prof.effectMinutes = Mathf.RoundToInt(r.FoodEffectMinutes * (1f + 0.25f * taken.quality));
                    prof.effectSourceItemId = taken.itemId;
                    if (eff != null) effectText = "\n" + eff.Desc;
                }
            }
            if (CookingTuning.FoodLogEnabled) LogFood(sys, prof, taken);
            var game = Game.Instance;
            if (game != null && game.PlayerSys != null && game.PlayerSys.LocalController != null)
                game.PlayerSys.LocalController.PlayGesture(PlayerGesture.Happy);
            string taste = CookingUI.FlavorText(def);
            string msg = Loc.Format("{0}を食べた。", "Ate {0}.", def.Name);
            if (def.HasTag(CookingTags.Mystery)) msg += Loc.T("…ふしぎな味がする。", " ...a mysterious taste.");
            else if (!string.IsNullOrEmpty(taste)) msg += Loc.Format("（{0}）", " ({0})", taste);
            UIManager.Toast(msg + effectText, 4f);
            CookingFx.Puff(player.Position, def.Color, 3);
            EnsureRunner(sys);
            return true;
        }

        private static void LogFood(CookingSystem sys, CookingProfileData prof, ItemStack taken)
        {
            FoodLogEntry entry = null;
            foreach (var f in prof.foodLog) if (f.itemId == taken.itemId) entry = f;
            if (entry == null)
            {
                var game = Game.Instance;
                string planetName = null;
                if (game != null && game.Planet != null)
                {
                    planetName = game.Planet.playerGivenName;
                    if (string.IsNullOrEmpty(planetName))
                    {
                        var speech = game.Service<ISpeechService>();
                        planetName = speech != null ? speech.PlanetName : null;
                    }
                }
                entry = new FoodLogEntry { itemId = taken.itemId, firstDay = game != null && game.Time != null ? game.Time.Day : 0, planetName = planetName };
                prof.foodLog.Add(entry);
                UIManager.Toast(Loc.Format("食の記録に「{0}」が加わった", "\"{0}\" added to your food log", taken.Definition.Name));
            }
            entry.times++;
            entry.bestQuality = Mathf.Max(entry.bestQuality, taken.quality);
        }

        /// <summary>ゲーム内時間で効果の残り時間を減らす。</summary>
        public static void Advance(CookingSystem sys, int minutes)
        {
            var game = Game.Instance;
            if (game == null || game.LocalPlayerId == null) return;
            var prof = sys.Profile(game.LocalPlayerId);
            if (string.IsNullOrEmpty(prof.effectId) || prof.effectMinutes <= 0) return;
            prof.effectMinutes -= minutes;
            if (prof.effectMinutes <= 0)
            {
                prof.effectMinutes = 0;
                var eff = CookingTables.Effect(prof.effectId);
                prof.effectId = null;
                if (eff != null) UIManager.Toast(Loc.Format("「{0}」がおさまった", "\"{0}\" wore off", eff.Name));
            }
            EnsureRunner(sys);
        }

        public static void EnsureRunner(CookingSystem sys)
        {
            var game = Game.Instance;
            if (game == null || game.PlayerSys == null) return;
            var avatar = game.PlayerSys.LocalAvatar;
            if (avatar == null) return;
            var runner = avatar.GetComponent<FoodEffectRunner>();
            if (runner == null) runner = avatar.AddComponent<FoodEffectRunner>();
            runner.System = sys;
        }
    }

    /// <summary>ローカルプレイヤーのアバターに付き、効果の見た目（光・湯気）と歩く速さを反映する。</summary>
    public sealed class FoodEffectRunner : MonoBehaviour
    {
        public CookingSystem System;
        private string current;
        private Light glowLight;
        private float baseWalk = -1f, baseRun = -1f;
        private float puffTimer;

        private void Update()
        {
            var game = Game.Instance;
            if (game == null || System == null) return;
            string eff = System.GetActiveFoodEffect(game.LocalPlayerId);
            if (eff != current)
            {
                current = eff;
                Apply();
            }
            if (current == CookingTables.EffectWarm)
            {
                puffTimer -= UnityEngine.Time.deltaTime;
                if (puffTimer <= 0f)
                {
                    puffTimer = 2.5f;
                    CookingFx.Puff(transform.position + Vector3.up * 0.8f, new Color(1f, 1f, 1f, 0.6f), 1);
                }
            }
        }

        private void Apply()
        {
            var ctrl = GetComponent<PlayerController>();
            if (ctrl != null)
            {
                if (baseWalk < 0f) { baseWalk = ctrl.walkSpeed; baseRun = ctrl.runSpeed; }
                float mult = current == CookingTables.EffectSwift ? CookingTuning.SwiftMultiplier : 1f;
                ctrl.walkSpeed = baseWalk * mult;
                ctrl.runSpeed = baseRun * mult;
            }
            bool glow = current == CookingTables.EffectGlow || current == CookingTables.EffectWarm;
            if (glow && glowLight == null)
            {
                var go = new GameObject("FoodGlow");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                glowLight = go.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.range = 6f;
            }
            if (glowLight != null)
            {
                glowLight.enabled = glow;
                glowLight.color = current == CookingTables.EffectWarm ? new Color(1f, 0.6f, 0.35f) : new Color(1f, 1f, 0.65f);
                glowLight.intensity = current == CookingTables.EffectWarm ? 0.6f : 1.4f;
            }
        }
    }

    /// <summary>[F]／クリックで、手に持った料理を食べる（道具が「手」で、何かを狙っていない時だけ）。</summary>
    public sealed class FoodEatHandler : IToolHandler
    {
        private readonly CookingSystem sys;
        public FoodEatHandler(CookingSystem sys) { this.sys = sys; }

        /// <summary>他の用途（贈る・置く・棚に並べる・餌にする）を優先するため、いちばん低い優先度。</summary>
        public int Priority => -100;

        public bool TryUseTool(ToolUseContext ctx)
        {
            if (ctx == null || ctx.Player == null || !ctx.Player.IsLocal) return false;
            if (ctx.Tool != ToolType.Hand || ctx.SelectedItem == null || ctx.SelectedItem.IsEmpty) return false;
            if (!FoodEffects.IsEdible(ctx.SelectedItem.Definition)) return false;
            if (ctx.HitCollider != null)
            {
                if (ctx.HitCollider.GetComponentInParent<IToolTarget>() != null) return false;
                if (ctx.HitCollider.GetComponentInParent<IInteractable>() != null) return false;
            }
            return FoodEffects.Eat(sys, ctx.Player, ctx.SelectedSlot);
        }
    }

    /// <summary>効いている食べ物の効果を画面左に小さく表示する。</summary>
    public sealed class FoodEffectOverlay : IUIOverlay
    {
        private readonly CookingSystem sys;
        public FoodEffectOverlay(CookingSystem sys) { this.sys = sys; }
        public int OverlayOrder => 5;

        public void DrawOverlay()
        {
            var game = Game.Instance;
            if (game == null || game.State != GameState.Playing || !UIManager.HudVisible) return;
            if (!CookingTuning.FoodEffectsEnabled) return;
            var prof = sys.Profile(game.LocalPlayerId);
            if (string.IsNullOrEmpty(prof.effectId) || prof.effectMinutes <= 0) return;
            var eff = CookingTables.Effect(prof.effectId);
            if (eff == null) return;
            var r = new Rect(20, 140, 300, 40);
            GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
            UIStyles.FillRect(new Rect(r.x + 6, r.y + 6, 28, 28), eff.Color);
            var center = new GUIStyle(UIStyles.LabelSmall) { alignment = TextAnchor.MiddleCenter };
            center.normal.textColor = new Color(0.1f, 0.1f, 0.15f);
            GUI.Label(new Rect(r.x + 6, r.y + 6, 28, 28), eff.Glyph, center);
            GUI.Label(new Rect(r.x + 42, r.y + 8, 250, 26), Loc.Format("{0}（あと{1}分）", "{0} ({1} min)", eff.Name, prof.effectMinutes), UIStyles.LabelSmall);
        }
    }
}
