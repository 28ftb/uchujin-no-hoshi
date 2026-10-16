using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイクの選択肢（基本決定 §3【確定】キャラクターメイクあり。項目・選択肢の数は【未決定】→充実させた仮案）。
    /// CharacterAppearance（コア）のインデックスと色に対応する。項目を増やす時は、ここの一覧と部品ファイル名（*Files）、
    /// Tools/Avatar/build_avatar_kit.py の部品を足して FBX を書き出す。
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
            new LocText("ふたつおだんご", "Twin buns"),
            new LocText("くるくる", "Curls"),
            new LocText("触角ヘア", "Feeler locks"),
            new LocText("みつあみ", "Braids"),
            new LocText("七三わけ", "Side part"),
            new LocText("モヒカン", "Mohawk"),
            new LocText("サイドだけ", "Side tufts"),
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
            new LocText("ポンチョ", "Poncho"),
            new LocText("白衣", "Lab coat"),
            new LocText("エプロン", "Apron"),
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
            new LocText("キャップ", "Cap"),
            new LocText("ゴーグル", "Goggles"),
            new LocText("リボン", "Ribbon"),
            new LocText("ニット帽", "Beanie"),
            new LocText("メガネ", "Glasses"),
            new LocText("ヘッドホン", "Headphones"),
            new LocText("花かんむり", "Flower crown"),
            new LocText("宇宙ヘルメット", "Space helmet"),
            new LocText("ネコ耳", "Cat ears"),
            new LocText("王冠", "Crown"),
            new LocText("マフラー", "Scarf"),
            new LocText("星のヘアピン", "Star pin"),
            new LocText("つの", "Horns"),
            new LocText("ベレー帽", "Beret"),
            new LocText("麦わら帽子", "Straw hat"),
            new LocText("シルクハット", "Top hat"),
            new LocText("バケットハット", "Bucket hat"),
            new LocText("ターバン", "Turban"),
            new LocText("セーラー帽", "Sailor hat"),
            new LocText("コック帽", "Chef hat"),
            new LocText("ハンチング", "Newsboy cap"),
            new LocText("バンダナ", "Bandana"),
        };

        /// <summary>宇宙人のしるし（主人公は別の宇宙から来た人：企画書 §2.1）。NOKA の住民の種族の特徴が元。</summary>
        public static readonly LocText[] AlienTraits =
        {
            new LocText("なし", "None"),
            new LocText("ひれ耳", "Fin ears"),
            new LocText("ながい耳", "Long ears"),
            new LocText("光る触角", "Glowing feelers"),
            new LocText("ちいさなつの", "Little horns"),
            new LocText("星のしっぽ", "Star tail"),
            new LocText("光るもよう", "Glowing marks"),
            new LocText("まる耳", "Round ears"),
        };

        // ---- 部品ファイル（Resources/Avatar/*.fbx。上の一覧と同じ順。Tools/Avatar/build_avatar_kit.py の部品名）----

        public static readonly string[] BodyFiles = { "Normal", "Normal", "Slim", "Sturdy", "Round", "Fluffy" };
        public static readonly string[] FaceFiles = { "Smile", "Open", "Calm", "Cat", "Smirk", "Rosy", "Freckles", "Brows", "Tiny", "Stars" };
        public static readonly string[] EyeFiles = { "Round", "Droopy", "Sharp", "Sparkly", "Happy", "Dots", "Big", "One", "Three", "Sleepy" };
        public static readonly string[] HairFiles =
        {
            null, "Short", "Bob", "Long", "Twintails", "Ponytail", "Topbun", "Spiky", "Afro", "Buns", "Curls", "Feelers", "Braids", "Sidepart",
            "Mohawk", "Sides",
        };
        public static readonly string[] TopFiles =
            { "Tshirt", "Hoodie", "Jacket", "Spacesuit", "Dress", "Sweater", "Overalls", "Cape", "Vest", "Poncho", "Labcoat", "Apron" };
        public static readonly string[] BottomFiles = { "Trousers", "Shorts", "Skirt", "Puffy", "Spacepants", "Leggings" };
        public static readonly string[] ShoeFiles = { "Sneakers", "Boots", "Sandals", "Moonboots", "Barefoot" };
        public static readonly string[] AccessoryFiles =
        {
            null, "Trait_Antennae", "Acc_Cap", "Acc_Goggles", "Acc_Ribbon", "Acc_Beanie", "Acc_Glasses", "Acc_Headphones", "Acc_Flowers",
            "Acc_Helmet", "Acc_CatEars", "Acc_Crown", "Acc_Scarf", "Acc_StarPin", "Trait_Horns", "Acc_Beret", "Acc_Straw", "Acc_Tophat",
            "Acc_Bucket", "Acc_Turban", "Acc_Sailor", "Acc_Chef", "Acc_Newsboy", "Acc_Bandana",
        };
        public static readonly string[] TraitFiles = { null, "Fins", "LongEars", "Antennae", "Horns", "Tail", "Glowdots", "RoundEars" };

        /// <summary>頭にかぶる帽子（高く盛った髪を隠す）。</summary>
        public static bool IsHat(int accessory)
        {
            switch (accessory)
            {
                case 2: case 5: case 15: case 16: case 17: case 18: case 19: case 20: case 21: case 22: case 23: return true;
                default: return false;
            }
        }

        /// <summary>帽子を髪の上に持ち上げる高さ（メートル、キットの大きさで）。もこもこ・くるくるの髪だけ。</summary>
        public static float HatLift(int hairStyle) => hairStyle == 8 ? 0.15f : hairStyle == 10 ? 0.05f : 0f;

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
            a.alienTrait = Wrap(a.alienTrait, AlienTraits.Length);
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
                alienTrait = Random.value < 0.4f ? 0 : Random.Range(1, AlienTraits.Length),
            };
            a.skinColor = Random.value < 0.45f ? Pick(SkinHuman) : Pick(SkinAlien);
            a.eyeColor = Pick(EyeColors);
            a.hairColor = Pick(HairColors);
            a.topColor = Pick(ClothColors);
            a.bottomColor = Pick(ClothColors);
            a.shoesColor = Pick(ClothColors);
            a.accessoryColor = Pick(ClothColors);
            // 「ひとつ目」「みっつ目」は少し出にくくする（選べば必ず使える）
            if ((a.eyeType == 7 || a.eyeType == 8) && Random.value < 0.5f) a.eyeType = Random.Range(0, 7);
            // こものの「アンテナ」「つの」と、しるしの触角・つのは重ねない
            if ((a.accessory == 1 && a.alienTrait == 3) || (a.accessory == 14 && a.alienTrait == 4)) a.alienTrait = 0;
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
