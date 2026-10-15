using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    public enum ToolType
    {
        Hand,
        Hoe,
        WateringCan,
        Sickle,
        FishingRod,
        Pickaxe,
        CatchNet,
        Shovel
    }

    public static class ToolTypeUtil
    {
        public static string Label(ToolType t)
        {
            switch (t)
            {
                case ToolType.Hand: return Loc.T("手", "Hand");
                case ToolType.Hoe: return Loc.T("クワ", "Hoe");
                case ToolType.WateringCan: return Loc.T("じょうろ", "Watering can");
                case ToolType.Sickle: return Loc.T("カマ", "Sickle");
                case ToolType.FishingRod: return Loc.T("釣り竿", "Fishing rod");
                case ToolType.Pickaxe: return Loc.T("ツルハシ", "Pickaxe");
                case ToolType.CatchNet: return Loc.T("網", "Net");
                case ToolType.Shovel: return Loc.T("スコップ", "Shovel");
                default: return t.ToString();
            }
        }

        public static string TierLabel(int level)
        {
            switch (level)
            {
                case 0: return Loc.T("未所持", "None");
                case 1: return Loc.T("ふつう", "Basic");
                case 2: return Loc.T("銅色", "Copper");
                case 3: return Loc.T("鋼", "Steel");
                case 4: return Loc.T("輝石", "Lumen");
                case 5: return Loc.T("星鉄", "Starsteel");
                default: return Loc.Format("Lv{0}", "Lv{0}", level);
            }
        }
    }

    /// <summary>道具の所持と強化段階。ツルハシの強化は鍛冶屋（鉱山モジュール）、竿の強化は釣りモジュールが行う。</summary>
    [Serializable]
    public class ToolBelt
    {
        public List<StringIntPair> levels = new List<StringIntPair>();
        public ToolType equipped = ToolType.Hand;

        public int GetLevel(ToolType t)
        {
            if (t == ToolType.Hand) return 1;
            foreach (var p in levels) if (p.key == t.ToString()) return p.value;
            return 0;
        }

        public void SetLevel(ToolType t, int level)
        {
            foreach (var p in levels)
            {
                if (p.key == t.ToString())
                {
                    p.value = level;
                    return;
                }
            }
            levels.Add(new StringIntPair(t.ToString(), level));
        }

        public bool Has(ToolType t) => GetLevel(t) > 0;

        public static ToolBelt CreateStarter()
        {
            var b = new ToolBelt();
            b.SetLevel(ToolType.Hoe, 1);
            b.SetLevel(ToolType.WateringCan, 1);
            b.SetLevel(ToolType.FishingRod, 1);
            b.SetLevel(ToolType.Pickaxe, 1);
            b.SetLevel(ToolType.CatchNet, 1);
            b.SetLevel(ToolType.Sickle, 1);
            return b;
        }
    }

    /// <summary>お金（プレイヤー個人の携帯資産）。通貨記号は仮に "G"。</summary>
    [Serializable]
    public class Wallet
    {
        public static string CurrencySymbol = "G";

        public int money;
        [NonSerialized] public string ownerId;

        public bool CanAfford(int amount) => amount <= money;

        public void Add(int amount, string reason)
        {
            if (amount == 0) return;
            money += amount;
            EventBus.Publish(new MoneyChangedEvent { PlayerId = ownerId, Delta = amount, Balance = money, Reason = reason });
        }

        public bool TrySpend(int amount, string reason)
        {
            if (amount < 0 || amount > money) return false;
            money -= amount;
            EventBus.Publish(new MoneyChangedEvent { PlayerId = ownerId, Delta = -amount, Balance = money, Reason = reason });
            return true;
        }

        public static string Format(int amount) => amount.ToString("N0") + CurrencySymbol;
    }

    /// <summary>
    /// 主人公の身振り（主人公は普通のセリフを話さない：メインストーリー方針）。
    /// 住民・クエストはこの身振りを「はい／いいえ」等の意思表示として受け取る。
    /// </summary>
    public enum PlayerGesture
    {
        Nod,        // うなずく（はい）
        ShakeHead,  // 首を振る（いいえ）
        Wave,       // 手を振る（挨拶・別れ）
        Point,      // 指さす
        Bow,        // お辞儀（お礼・謝罪）
        Clap,       // 拍手（喜び・祝福）
        Think,      // 考える（わからない）
        Happy,      // 喜ぶ
        Sad,        // 悲しむ
        Surprised,  // 驚く
        Beckon,     // 手招き（一緒に来て）
        ShowItem    // 持っている物を見せる
    }

    public static class PlayerGestureUtil
    {
        public static string Label(PlayerGesture g)
        {
            switch (g)
            {
                case PlayerGesture.Nod: return Loc.T("うなずく", "Nod");
                case PlayerGesture.ShakeHead: return Loc.T("首を振る", "Shake head");
                case PlayerGesture.Wave: return Loc.T("手を振る", "Wave");
                case PlayerGesture.Point: return Loc.T("指さす", "Point");
                case PlayerGesture.Bow: return Loc.T("お辞儀", "Bow");
                case PlayerGesture.Clap: return Loc.T("拍手", "Clap");
                case PlayerGesture.Think: return Loc.T("考える", "Think");
                case PlayerGesture.Happy: return Loc.T("喜ぶ", "Cheer");
                case PlayerGesture.Sad: return Loc.T("しょんぼり", "Sad");
                case PlayerGesture.Surprised: return Loc.T("驚く", "Surprised");
                case PlayerGesture.Beckon: return Loc.T("手招き", "Beckon");
                case PlayerGesture.ShowItem: return Loc.T("持ち物を見せる", "Show item");
                default: return g.ToString();
            }
        }
    }

    /// <summary>
    /// キャラクターメイクのデータ（基本決定 v0.2：キャラクターメイクあり）。
    /// 具体的な項目数は未決定のため、インデックス＋色で拡張しやすい形にしている。
    /// 見た目の生成は Story/CharacterMake モジュールの PlayerAvatarBuilder が担当する。
    /// </summary>
    [Serializable]
    public class CharacterAppearance
    {
        public string displayName = "";
        public int bodyType;          // 体型
        public float height = 1f;     // 身長倍率 0.85〜1.15
        public float headSize = 1f;   // 頭の大きさ倍率
        public Color skinColor = new Color(1f, 0.85f, 0.72f);
        public int faceType;          // 顔
        public int eyeType;
        public Color eyeColor = new Color(0.2f, 0.25f, 0.4f);
        public int hairStyle;
        public Color hairColor = new Color(0.25f, 0.18f, 0.12f);
        public int outfitTop;
        public Color topColor = new Color(0.3f, 0.55f, 0.9f);
        public int outfitBottom;
        public Color bottomColor = new Color(0.25f, 0.25f, 0.35f);
        public int accessory;         // 0=なし
        public Color accessoryColor = Color.white;
        public int shoes;
        public Color shoesColor = new Color(0.4f, 0.3f, 0.25f);

        public CharacterAppearance Clone() => (CharacterAppearance)MemberwiseClone();
    }
}
