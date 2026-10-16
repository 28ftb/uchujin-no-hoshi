using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイクの全項目を反映した主人公の見た目。NOKA の人物モデル（ちび頭身・パステル）を元にした
    /// 部品キット（Resources/Avatar/*.fbx、Tools/Avatar/build_avatar_kit.py で生成）を組み立てて、選んだ色で塗る。
    /// PlayerAvatarFactory.BuildVisual をこれで上書きする（マルチの他プレイヤー・演出の代役にも同じ関数が使われる）。
    /// 約束：ルートの名前は "Visual"、その直下に "ToolHolder" を置く（コアの PlayerController / PlayerInteractor が使う）。正面は +Z。
    ///
    /// 部品の組み方：体型ファイル（Body_*）の骨組み（Rig_Body / Rig_Head / Rig_Hip_P,N / Rig_Shoulder_P,N）に、
    /// 他の部品ファイルの同じ名前の骨組みの子をそのまま付け替える。胴まわりの服は Rig_Body/Fit_x_y_z（体型ごとの拡大）の下に入れる。
    /// 材質名：AvTint_役目＝キャラメイクの色、AvArt_RRGGBB＝固定色、AvGlow_＝自発光、AvGlass_＝半透明。
    /// </summary>
    public static class AvatarBuilder
    {
        private const string Folder = "Avatar/";
        /// <summary>NOKA の人物（頭のてっぺん約1.4m）を、この星の家・道具・カメラの大きさに合わせる倍率【仮数値】。</summary>
        public const float KitScale = 1.15f;
        /// <summary>「ちんまり」の縮小と頭の大きさ【仮数値】。</summary>
        private const float TinyScale = 0.86f, TinyHead = 1.1f;

        private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        private static bool warned;

        private static readonly Color Dark = new Color(0.14f, 0.1f, 0.16f);
        private static readonly Color White = new Color(1f, 0.99f, 0.97f);

        /// <summary>PlayerAvatarFactory.BuildVisual の差し替え。</summary>
        public static GameObject Build(CharacterAppearance source, Transform parent)
        {
            var a = AppearanceCatalog.Sanitize(source != null ? source.Clone() : AppearanceCatalog.Default());
            var bodyPrefab = Load("Body_" + AppearanceCatalog.BodyFiles[a.bodyType]);
            if (bodyPrefab == null)
            {
                if (!warned) Debug.LogWarning("[AvatarBuilder] 部品キット Resources/Avatar が見つからないので仮の見た目を使います（Tools/Avatar/build_avatar_kit.py で生成）");
                warned = true;
                return PlayerAvatarFactory.BuildDefault(a, parent);
            }

            var visual = PrimitiveBuilder.Empty("Visual", parent, Vector3.zero);
            bool tiny = a.bodyType == 1;
            float scale = KitScale * a.height * (tiny ? TinyScale : 1f);
            var model = PrimitiveBuilder.Empty("Model", visual.transform, Vector3.zero).transform;
            // キットは NOKA の住民と同じく −Z 向きで作るが、取り込み時の軸の変換（AvatarKitImporter の bakeAxisConversion）で +Z 向きになる
            model.localScale = Vector3.one * scale;

            var kit = Object.Instantiate(bodyPrefab, model, false);
            kit.name = "Kit";
            var rig = new Rig(kit.transform);
            var palette = MakePalette(a);
            Paint(kit.transform, palette, null);

            float headScale = a.headSize * (tiny ? TinyHead : 1f);
            if (rig.Head != null) rig.Head.localScale = Vector3.Scale(rig.Head.localScale, Vector3.one * headScale);

            var eyes = AddPart(rig, "Eyes_" + AppearanceCatalog.EyeFiles[a.eyeType], palette, null);
            AddPart(rig, "Face_" + AppearanceCatalog.FaceFiles[a.faceType], palette, null);
            var hair = a.hairStyle > 0 ? AddPart(rig, "Hair_" + AppearanceCatalog.HairFiles[a.hairStyle], palette, null) : new List<Transform>();
            AddPart(rig, "Top_" + AppearanceCatalog.TopFiles[a.outfitTop], palette, null);
            AddPart(rig, "Bottom_" + AppearanceCatalog.BottomFiles[a.outfitBottom], palette, null);
            AddPart(rig, "Shoes_" + AppearanceCatalog.ShoeFiles[a.shoes], palette, null);
            if (a.alienTrait > 0) AddPart(rig, "Trait_" + AppearanceCatalog.TraitFiles[a.alienTrait], palette, null);
            if (a.accessory > 0)
            {
                // 「アンテナ」「つの」は宇宙人のしるしと同じ形を、こものの色で付ける
                var asAccessory = new Dictionary<string, string> { { "trait", "acc" }, { "traitDark", "accDark" } };
                var acc = AddPart(rig, AppearanceCatalog.AccessoryFiles[a.accessory], palette, asAccessory);
                if (AppearanceCatalog.IsHat(a.accessory)) FitHat(a, hair, acc, model, headScale);
            }

            // ---- 動き ----
            var anim = visual.AddComponent<AvatarAnimator>();
            anim.motionRoot = model;
            anim.body = rig.Body;
            anim.head = rig.Head;
            anim.legL = rig.HipP;
            anim.legR = rig.HipN;
            anim.armL = rig.ShoulderP;
            anim.armR = rig.ShoulderN;
            var eyeParts = new List<Transform>();
            foreach (var t in eyes)
                if (t.name.StartsWith("Eye")) eyeParts.Add(t);
            anim.eyes = eyeParts.ToArray();

            // ---- 道具を持つ位置（コアとの約束：Visual 直下の ToolHolder）。右手（Visual の +X 側）の少し前 ----
            var holder = PrimitiveBuilder.Empty("ToolHolder", visual.transform, new Vector3(0.33f * scale, 0.36f * scale, 0.12f * scale));
            var handle = PrimitiveBuilder.Part(holder.transform, PrimitiveType.Cube, new Vector3(0, 0.22f, 0.08f), new Vector3(0.05f, 0.44f, 0.05f),
                new Color(0.55f, 0.38f, 0.22f), "Handle");
            var headPiece = PrimitiveBuilder.Part(holder.transform, PrimitiveType.Cube, new Vector3(0, 0.45f, 0.12f), new Vector3(0.16f, 0.08f, 0.1f),
                new Color(0.7f, 0.72f, 0.78f), "ToolHead");
            anim.toolVisual = holder.transform;
            anim.toolParts = new[] { handle, headPiece };
            return visual;
        }

        // ------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------

        private static GameObject Load(string name)
        {
            if (!cache.TryGetValue(name, out var prefab))
            {
                prefab = Resources.Load<GameObject>(Folder + name);
                cache[name] = prefab;
            }
            return prefab;
        }

        /// <summary>体型の骨組み。名前で探す（FBX の取り込みでは Kit の子・孫になる）。</summary>
        private sealed class Rig
        {
            public readonly Transform Body, Head, HipP, HipN, ShoulderP, ShoulderN, Fit;

            public Rig(Transform root)
            {
                Body = Find(root, "Rig_Body");
                Head = Find(root, "Rig_Head");
                HipP = Find(root, "Rig_Hip_P");
                HipN = Find(root, "Rig_Hip_N");
                ShoulderP = Find(root, "Rig_Shoulder_P");
                ShoulderN = Find(root, "Rig_Shoulder_N");
                Fit = Body != null ? FindPrefix(Body, "Fit_") : null;
                if (Fit != null)
                {
                    // 名前の百分率（Fit_x_y_z）が体型の拡大率。取り込み時の倍率に掛ける
                    var p = Fit.name.Split('_');
                    if (p.Length >= 4 && int.TryParse(p[1], out int x) && int.TryParse(p[2], out int y) && int.TryParse(p[3], out int z))
                        Fit.localScale = Vector3.Scale(Fit.localScale, new Vector3(x, y, z) / 100f);
                }
            }

            public Transform Target(string pivot)
            {
                switch (pivot)
                {
                    case "Rig_Body": return Fit != null ? Fit : Body;
                    case "Rig_Head": return Head;
                    case "Rig_Hip_P": return HipP;
                    case "Rig_Hip_N": return HipN;
                    case "Rig_Shoulder_P": return ShoulderP;
                    case "Rig_Shoulder_N": return ShoulderN;
                    default: return null;
                }
            }
        }

        /// <summary>部品ファイルを読み、骨組みごとに体型の骨組みへ付け替える。付けたもの（各部品のルート）を返す。</summary>
        private static List<Transform> AddPart(Rig rig, string file, Dictionary<string, Color> palette, Dictionary<string, string> remap)
        {
            var added = new List<Transform>();
            var prefab = Load(file);
            if (prefab == null)
            {
                Debug.LogWarning("[AvatarBuilder] 部品がありません: " + file);
                return added;
            }
            var inst = Object.Instantiate(prefab);
            Paint(inst.transform, palette, remap);
            var pivots = new List<Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Rig_")) pivots.Add(t);
            foreach (var pivot in pivots)
            {
                var target = rig.Target(pivot.name);
                if (target == null) continue;
                var children = new List<Transform>();
                foreach (Transform c in pivot)
                    if (!c.name.StartsWith("Rig_")) children.Add(c);
                foreach (var c in children)
                {
                    c.SetParent(target, false); // 骨組みの位置は全ファイルで同じなので、ローカルの位置のまま付け替える
                    added.Add(c);
                }
            }
            if (Application.isPlaying) Object.Destroy(inst);
            else Object.DestroyImmediate(inst); // エディタの確認用の撮影（AvatarKitGallery）
            return added;
        }

        /// <summary>帽子の時：高く盛った髪を隠し、もこもこ・くるくるの髪の上に帽子を持ち上げる。</summary>
        private static void FitHat(CharacterAppearance a, List<Transform> hair, List<Transform> acc, Transform model, float headScale)
        {
            foreach (var t in hair)
                if (t.name.StartsWith("Hair_Spike") || t.name.StartsWith("Hair_Crest") || t.name.StartsWith("Hair_Topbun"))
                    t.gameObject.SetActive(false);
            float lift = AppearanceCatalog.HatLift(a.hairStyle);
            if (lift <= 0f) return;
            var up = model.TransformVector(Vector3.up * lift * headScale);
            foreach (var t in acc) t.position += up;
        }

        // ------------------------------------------------------------
        // 色
        // ------------------------------------------------------------

        private static Dictionary<string, Color> MakePalette(CharacterAppearance a)
        {
            Color top = a.topColor;
            Color shirt = top, sleeve = top;
            switch (a.outfitTop)
            {
                case 0: // Tシャツ
                case 4: // ワンピース
                    sleeve = a.skinColor;
                    break;
                case 6: // オーバーオール
                    shirt = sleeve = Color.Lerp(top, White, 0.72f);
                    break;
                case 7: // マント
                case 9: // ポンチョ
                    shirt = sleeve = Color.Lerp(top, White, 0.55f);
                    break;
                case 8: // ベスト
                    shirt = sleeve = Color.Lerp(top, White, 0.78f);
                    break;
                case 10: // 白衣（中の服が上着の色、袖は白衣）
                    sleeve = White;
                    break;
                case 11: // エプロン
                    shirt = sleeve = Color.Lerp(top, White, 0.62f);
                    break;
            }
            Color legs;
            switch (a.outfitBottom)
            {
                case 1: case 2: case 3: legs = a.skinColor; break;          // 半ズボン・スカート
                case 5: legs = Color.Lerp(a.bottomColor, Dark, 0.12f); break; // レギンス
                default: legs = a.bottomColor; break;
            }
            return new Dictionary<string, Color>
            {
                { "skin", a.skinColor },
                { "hair", a.hairColor },
                { "eye", a.eyeColor },
                { "pupil", Color.Lerp(a.eyeColor, Dark, 0.7f) },
                { "shirt", shirt },
                { "sleeve", sleeve },
                { "fluff", Color.Lerp(shirt, White, 0.45f) },
                { "top", top },
                { "topDark", Shade(top, 0.78f) },
                { "legs", legs },
                { "bottom", a.bottomColor },
                { "shoes", a.shoesColor },
                { "shoesDark", Shade(a.shoesColor, 0.75f) },
                { "acc", a.accessoryColor },
                { "accDark", Shade(a.accessoryColor, 0.75f) },
                { "trait", a.skinColor },
                { "traitDark", Color.Lerp(a.skinColor, Dark, 0.28f) },
            };
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static void Paint(Transform root, Dictionary<string, Color> palette, Dictionary<string, string> remap)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = Resolve(src[i] != null ? src[i].name : "", palette, remap);
                r.sharedMaterials = dst;
            }
        }

        private static Material Resolve(string name, Dictionary<string, Color> palette, Dictionary<string, string> remap)
        {
            int cut = name.IndexOf(' ');
            if (cut > 0) name = name.Substring(0, cut);
            if (name.StartsWith("AvTint_"))
            {
                string slot = name.Substring(7);
                if (remap != null && remap.TryGetValue(slot, out var other)) slot = other;
                if (!palette.TryGetValue(slot, out var c)) c = Color.magenta;
                return MaterialCache.Get(c, 0.25f);
            }
            Color hex = Hex(name);
            if (name.StartsWith("AvGlow_")) return MaterialCache.GetEmissive(hex);
            if (name.StartsWith("AvGlass_")) return MaterialCache.Get(new Color(hex.r, hex.g, hex.b, 0.28f), 0.9f);
            return MaterialCache.Get(hex, 0.25f);
        }

        private static Color Hex(string name)
        {
            string hex = name.Length >= 6 ? name.Substring(name.Length - 6) : "d9c9a7";
            return ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : new Color(0.85f, 0.79f, 0.65f);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        private static Transform FindPrefix(Transform root, string prefix)
        {
            foreach (Transform c in root)
                if (c.name.StartsWith(prefix)) return c;
            return null;
        }
    }

    /// <summary>
    /// アバターの簡単な動き（歩くと手足が揺れる・まばたき・道具を持つ時だけ柄を出す）。
    /// 親オブジェクトの移動量から歩きを判定するので、マルチの他プレイヤーのアバターでも動く。
    /// コアの身振り（PlayerController.PlayGesture）は Visual ルートを動かすので、ここでは子（骨組み）だけを動かす。
    /// </summary>
    public class AvatarAnimator : MonoBehaviour
    {
        public Transform body, head, legL, legR, armL, armR, toolVisual;
        /// <summary>上下の揺れの向きと大きさ（メートル）の基準。キットの Model。</summary>
        public Transform motionRoot;
        public Transform[] eyes;
        public GameObject[] toolParts;
        /// <summary>演出用：外部から歩きの強さを指定（-1 なら移動量から自動）。</summary>
        public float forcedWalk = -1f;

        private Vector3 lastPos;
        private float phase;
        private float walk;
        private float blinkTimer;
        private float blinkT = -1f;
        private Vector3[] eyeScales;
        private Quaternion armLBase, armRBase, legLBase, legRBase, headBase;
        private Vector3 bodyBase;
        private PlayerController controller;

        private void Start()
        {
            lastPos = transform.position;
            blinkTimer = Random.Range(1.5f, 4f);
            if (eyes != null)
            {
                eyeScales = new Vector3[eyes.Length];
                for (int i = 0; i < eyes.Length; i++) if (eyes[i] != null) eyeScales[i] = eyes[i].localScale;
            }
            if (armL != null) armLBase = armL.localRotation;
            if (armR != null) armRBase = armR.localRotation;
            if (legL != null) legLBase = legL.localRotation;
            if (legR != null) legRBase = legR.localRotation;
            if (head != null) headBase = head.localRotation;
            if (body != null) bodyBase = body.localPosition;
            controller = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var pos = transform.position;
            var d = pos - lastPos;
            d.y = 0f;
            lastPos = pos;
            float speed = d.magnitude / dt;
            if (speed > 30f) speed = 0f; // 瞬間移動（場面の切り替え）は歩きにしない
            float target = forcedWalk >= 0f ? forcedWalk : Mathf.Clamp01(speed / 5f);
            walk = Mathf.MoveTowards(walk, target, dt * 4f);
            phase += dt * (6f + 6f * walk);

            float swing = Mathf.Sin(phase) * 34f * walk;
            if (legL != null) legL.localRotation = legLBase * Quaternion.Euler(swing, 0, 0);
            if (legR != null) legR.localRotation = legRBase * Quaternion.Euler(-swing, 0, 0);
            if (armL != null) armL.localRotation = armLBase * Quaternion.Euler(-swing * 0.8f, 0, 0);
            if (armR != null) armR.localRotation = armRBase * Quaternion.Euler(swing * 0.8f, 0, 0);
            if (body != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.04f * walk + Mathf.Sin(Time.time * 2f) * 0.006f * (1f - walk);
                var up = motionRoot != null && body.parent != null
                    ? body.parent.InverseTransformVector(motionRoot.TransformVector(Vector3.up * bob))
                    : Vector3.up * bob;
                body.localPosition = bodyBase + up;
            }
            if (head != null) head.localRotation = headBase * Quaternion.Euler(0, 0, Mathf.Sin(phase * 0.5f) * 4f * walk);

            // まばたき
            if (eyes != null && eyeScales != null)
            {
                blinkTimer -= dt;
                if (blinkTimer <= 0f && blinkT < 0f) blinkT = 0f;
                if (blinkT >= 0f)
                {
                    blinkT += dt;
                    float k = blinkT < 0.07f ? 1f - blinkT / 0.07f * 0.9f : blinkT < 0.14f ? 0.1f + (blinkT - 0.07f) / 0.07f * 0.9f : 1f;
                    for (int i = 0; i < eyes.Length; i++)
                        if (eyes[i] != null) eyes[i].localScale = new Vector3(eyeScales[i].x, eyeScales[i].y * k, eyeScales[i].z);
                    if (blinkT >= 0.14f)
                    {
                        blinkT = -1f;
                        blinkTimer = Random.Range(2f, 5f);
                    }
                }
            }

            // 道具の柄：道具を構えている時だけ表示
            if (toolParts != null)
            {
                bool show = false;
                if (controller != null && controller.Player != null)
                {
                    var p = controller.Player;
                    show = p.SelectedItem == null && p.Tools.equipped != ToolType.Hand;
                }
                for (int i = 0; i < toolParts.Length; i++)
                    if (toolParts[i] != null && toolParts[i].activeSelf != show) toolParts[i].SetActive(show);
            }
        }
    }
}
