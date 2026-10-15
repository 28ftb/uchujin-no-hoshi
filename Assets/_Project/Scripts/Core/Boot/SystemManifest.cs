using System;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// ゲームシステムの生成順。型名で指定し、反射で生成する（コアがモジュールにコンパイル時依存しないため）。
    /// 順序の意味：
    ///   PreparePlanet・OnNewPlanet・BuildScene はこの順に呼ばれる。
    ///   World（地形・配置）→ Weather → Language（宇宙語定義）→ Tablet → 生物・農業・釣り・鉱山・加工・家
    ///   → Residents（住民生成。家と職業の割り当て）→ Shop → Quests → Rocket → Story → Network
    /// 日次更新の順番は各システムの DailyUpdateOrder で別に決める。
    /// </summary>
    public static class SystemManifest
    {
        public static readonly string[] SystemTypeNames =
        {
            "Uchujin.Core.TimeSystem",
            "Uchujin.World.WorldSystem",
            "Uchujin.Weather.WeatherSystem",
            "Uchujin.Language.LanguageSystem",
            "Uchujin.Tablet.TabletSystem",
            "Uchujin.Core.PlayerSystem",
            "Uchujin.Creatures.CreatureSystem",
            "Uchujin.Farming.FarmingSystem",
            "Uchujin.Fishing.FishingSystem",
            "Uchujin.Mining.MiningSystem",
            "Uchujin.Cooking.CookingSystem",
            "Uchujin.Housing.HousingSystem",
            "Uchujin.Residents.ResidentSystem",
            "Uchujin.Shop.ShopSystem",
            "Uchujin.Quests.QuestSystem",
            "Uchujin.Rocket.RocketSystem",
            "Uchujin.Story.StorySystem",
            "Uchujin.Network.NetworkSystem",
        };

        public static Type FindType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }

    /// <summary>
    /// 空のシーンで再生しても自動的にゲームが始まるようにする。
    /// （シーン・プレハブを用意しなくても動くコード主体の構成）
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Game.Instance != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<Game>() != null) return;
            if (PlayerPrefs.GetInt("uchujin.disableAutoBoot", 0) == 1) return;
            var go = new GameObject("Game");
            go.AddComponent<Game>();
        }
    }
}
