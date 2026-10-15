using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイクの選択肢（基本決定 §3【確定】キャラクターメイクあり。項目・選択肢の数は【未決定】→充実させた仮案）。
    /// CharacterAppearance（コア）のインデックスと色に対応する。項目を増やす時はここと AvatarBuilder に追加する。
    /// 肌の色は人間以外の色も選べる（主人公は別宇宙から来た人：企画書 §2.1）。
    /// </summary>
    public static class AppearanceCatalog
    {
        public static readonly LocText[] BodyTypes =
        {
            new LocText("ふつう", "Standard"),
            new LocText("ちんまり", "Tiny"),
            new LocText("ほっそり", "Slim"),
            new LocText("がっしり", "Sturdy"),
            new LocText("まんまる", "Round"),
            new LocText("ふわふわ", "Fluffy"),
        };

        public static readonly LocText[] FaceTypes =
        {
            new LocText("にっこり", "Smile"),
            new LocText("ぽかん", "Agape"),
            new LocText("すまし顔", "Calm"),
            new LocText("ねこ口", "Cat mouth"),
            new LocText("にやり", "Smirk"),
            new LocText("ほっぺ赤", "Rosy cheeks"),
            new LocText("そばかす", "Freckles"),
            new LocText("キリッと眉", "Bold brows"),
            new LocText("おちょぼ口", "Tiny mouth"),
            new LocText("星もよう", "Star marks"),
        };

        public static readonly LocText[] EyeTypes =
        {
            new LocText("まる目", "Round"),
            new LocText("たれ目", "Droopy"),
            new LocText("つり目", "Sharp"),
            new LocText("きらきら", "Sparkly"),
            new LocText("にっこり目", "Happy arcs"),
            new LocText("点目", "Dots"),
            new LocText("大きな瞳", "Big pupils"),
            new LocText("ひとつ目", "One eye"),
            new LocText("みっつ目", "Three eyes"),
            new LocText("ねむそう", "Sleepy"),
        };

        public static readonly LocText[] HairStyles =
        {
            new LocText("なし", "None"),
            new LocText("ショート", "Short"),
            new LocText("ボブ", "Bob"),
            new LocText("ロング", "Long"),
            new LocText("ツインテール", "Twin tails"),
            new LocText("ポニーテール", "Ponytail"),
            new LocText("おだんご", "Bun"),
            new LocText("つんつん", "Spiky"),
            new LocText("もこもこ", "Fluffy"),
            new LocText("ぱっつん", "Blunt bangs"),
            new LocText("アホ毛", "Cowlick"),
            new LocText("触角ヘア", "Feeler locks"),
        };

        public static readonly LocText[] Tops =
        {
            new LocText("Tシャツ", "T-shirt"),
            new LocText("パーカー", "Hoodie"),
            new LocText("ジャケット", "Jacket"),
            new LocText("宇宙服", "Space suit"),
            new LocText("ワンピース", "Dress"),
            new LocText("セーター", "Sweater"),
            new LocText("オーバーオール", "Overalls"),
            new LocText("マント", "Cape"),
            new LocText("ベスト", "Vest"),
        };

        public static readonly LocText[] Bottoms =
        {
            new LocText("ズボン", "Trousers"),
            new LocText("半ズボン", "Shorts"),
            new LocText("スカート", "Skirt"),
            new LocText("ふんわりスカート", "Puffy skirt"),
            new LocText("宇宙服パンツ", "Space pants"),
            new LocText("レギンス", "Leggings"),
        };

        public static readonly LocText[] Shoes =
        {
            new LocText("スニーカー", "Sneakers"),
            new LocText("ブーツ", "Boots"),
            new LocText("サンダル", "Sandals"),
            new LocText("宇宙ブーツ", "Moon boots"),
            new LocText("はだし", "Barefoot"),
        };

        public static readonly LocText[] Accessories =
        {
            new LocText("なし", "None"),
            new LocText("アンテナ", "Antennae"),
            new LocText("帽子", "Cap"),
            new LocText("ゴーグル", "Goggles"),
            new LocText("リボン", "Ribbon"),
            new LocText("ニット帽", "Beanie"),
            new LocText("メガネ", "Glasses"),
            new LocText("ヘッドホン", "Headphones"),
            new LocText("花かざり", "Flower"),
            new LocText("ヘルメット", "Helmet"),
            new LocText("ネコ耳", "Cat ears"),
            new LocText("王冠", "Crown"),
            new LocText("マフラー", "Scarf"),
            new LocText("星のヘアピン", "Star pin"),
            new LocText("つの", "Horns"),
        };

        // ---- 色見本 ----

        /// <summary>肌（地球の人に近い色）。</summary>
        public static readonly Color[] SkinHuman =
        {
            new Color(1f, 0.89f, 0.80f), new Color(1f, 0.85f, 0.72f), new Color(0.96f, 0.78f, 0.62f), new Color(0.88f, 0.67f, 0.50f),
            new Color(0.76f, 0.55f, 0.40f), new Color(0.62f, 0.43f, 0.30f), new Color(0.47f, 0.32f, 0.22f), new Color(0.35f, 0.24f, 0.17f),
        };

        /// <summary>肌（別の宇宙から来た人ならではの色）。</summary>
        public static readonly Color[] SkinAlien =
        {
            new Color(0.62f, 0.93f, 0.80f), new Color(0.78f, 0.70f, 0.98f), new Color(0.62f, 0.82f, 1f), new Color(1f, 0.72f, 0.85f),
            new Color(1f, 0.74f, 0.52f), new Color(0.76f, 0.95f, 0.50f), new Color(0.80f, 0.83f, 0.88f), new Color(1f, 0.88f, 0.45f),
            new Color(0.38f, 0.48f, 0.85f), new Color(1f, 0.55f, 0.50f),
        };

        public static readonly Color[] HairColors =
        {
            new Color(0.12f, 0.10f, 0.10f), new Color(0.30f, 0.20f, 0.13f), new Color(0.52f, 0.30f, 0.16f), new Color(0.95f, 0.82f, 0.45f),
            new Color(0.82f, 0.84f, 0.88f), new Color(0.98f, 0.98f, 1f), new Color(0.85f, 0.25f, 0.20f), new Color(1f, 0.55f, 0.75f),
            new Color(0.35f, 0.55f, 1f), new Color(0.45f, 0.92f, 0.78f), new Color(0.60f, 0.40f, 0.90f), new Color(1f, 0.58f, 0.20f),
        };

        public static readonly Color[] EyeColors =
        {
            new Color(0.20f, 0.14f, 0.10f), new Color(0.20f, 0.35f, 0.75f), new Color(0.20f, 0.55f, 0.30f), new Color(0.85f, 0.55f, 0.15f),
            new Color(0.55f, 0.30f, 0.80f), new Color(0.85f, 0.20f, 0.25f), new Color(0.95f, 0.80f, 0.20f), new Color(0.15f, 0.65f, 0.70f),
            new Color(1f, 0.50f, 0.70f),
        };

        public static readonly Color[] ClothColors =
        {
            new Color(0.95f, 0.95f, 0.95f), new Color(0.20f, 0.20f, 0.25f), new Color(0.90f, 0.30f, 0.30f), new Color(1f, 0.60f, 0.25f),
            new Color(1f, 0.85f, 0.30f), new Color(0.55f, 0.85f, 0.35f), new Color(0.25f, 0.65f, 0.45f), new Color(0.35f, 0.80f, 0.90f),
            new Color(0.30f, 0.55f, 0.90f), new Color(0.25f, 0.30f, 0.60f), new Color(0.60f, 0.40f, 0.85f), new Color(1f, 0.60f, 0.80f),
            new Color(0.60f, 0.45f, 0.30f), new Color(0.85f, 0.75f, 0.60f), new Color(0.55f, 0.58f, 0.62f), new Color(0.15f, 0.45f, 0.40f),
        };

        public static int Count(LocText[] list) => list.Length;

        public static string Label(LocText[] list, int index)
        {
            if (list == null || list.Length == 0) return "";
            index = Mathf.Clamp(index, 0, list.Length - 1);
            return list[index].ToString();
        }

        public static int Wrap(int index, int count) => count <= 0 ? 0 : ((index % count) + count) % count;

        /// <summary>範囲外の値を直す（古いセーブ・他プレイヤーから届いたデータへの保険）。</summary>
        public static CharacterAppearance Sanitize(CharacterAppearance a)
        {
            if (a == null) a = new CharacterAppearance();
            a.bodyType = Wrap(a.bodyType, BodyTypes.Length);
            a.faceType = Wrap(a.faceType, FaceTypes.Length);
            a.eyeType = Wrap(a.eyeType, EyeTypes.Length);
            a.hairStyle = Wrap(a.hairStyle, HairStyles.Length);
            a.outfitTop = Wrap(a.outfitTop, Tops.Length);
            a.outfitBottom = Wrap(a.outfitBottom, Bottoms.Length);
            a.shoes = Wrap(a.shoes, Shoes.Length);
            a.accessory = Wrap(a.accessory, Accessories.Length);
            if (a.height <= 0f) a.height = 1f;
            if (a.headSize <= 0f) a.headSize = 1f;
            a.height = Mathf.Clamp(a.height, 0.85f, 1.15f);
            a.headSize = Mathf.Clamp(a.headSize, 0.8f, 1.4f);
            if (a.displayName == null) a.displayName = "";
            return a;
        }

        /// <summary>おまかせ（ランダム）。見た目だけの揺らぎなので UnityEngine.Random を使ってよい。</summary>
        public static CharacterAppearance Randomize(string keepName)
        {
            var a = new CharacterAppearance
            {
                displayName = keepName ?? "",
                bodyType = Random.Range(0, BodyTypes.Length),
                height = Random.Range(0.88f, 1.12f),
                headSize = Random.Range(0.9f, 1.3f),
                faceType = Random.Range(0, FaceTypes.Length),
                eyeType = Random.Range(0, EyeTypes.Length),
                hairStyle = Random.Range(0, HairStyles.Length),
                outfitTop = Random.Range(0, Tops.Length),
                outfitBottom = Random.Range(0, Bottoms.Length),
                shoes = Random.Range(0, Shoes.Length),
                accessory = Random.value < 0.25f ? 0 : Random.Range(1, Accessories.Length),
            };
            a.skinColor = Random.value < 0.55f ? Pick(SkinHuman) : Pick(SkinAlien);
            a.eyeColor = Pick(EyeColors);
            a.hairColor = Pick(HairColors);
            a.topColor = Pick(ClothColors);
            a.bottomColor = Pick(ClothColors);
            a.shoesColor = Pick(ClothColors);
            a.accessoryColor = Pick(ClothColors);
            // 「ひとつ目」「みっつ目」は少し出にくくする（選べば必ず使える）
            if ((a.eyeType == 7 || a.eyeType == 8) && Random.value < 0.5f) a.eyeType = Random.Range(0, 7);
            return a;
        }

        private static Color Pick(Color[] list) => list[Random.Range(0, list.Length)];

        /// <summary>初期の見た目（初めてキャラクターメイクを開いた時）。</summary>
        public static CharacterAppearance Default()
        {
            return new CharacterAppearance
            {
                bodyType = 0,
                height = 1f,
                headSize = 1.15f,
                skinColor = SkinHuman[1],
                faceType = 0,
                eyeType = 0,
                eyeColor = EyeColors[0],
                hairStyle = 1,
                hairColor = HairColors[1],
                outfitTop = 3,
                topColor = new Color(0.95f, 0.95f, 0.95f),
                outfitBottom = 4,
                bottomColor = new Color(0.55f, 0.58f, 0.62f),
                shoes = 3,
                shoesColor = new Color(1f, 0.6f, 0.25f),
                accessory = 1,
                accessoryColor = new Color(1f, 0.85f, 0.3f),
            };
        }
    }
}
