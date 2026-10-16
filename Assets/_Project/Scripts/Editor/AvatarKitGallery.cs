#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Story;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// キャラクターメイクの部品キットの確認用：全項目を順に使った主人公を並べて撮影し、Logs/AvatarGallery/*.png に保存する。
    /// メニュー「宇宙人の星 → キャラメイクの部品を一覧撮影」、またはバッチモード（-nographics なし）で
    /// -executeMethod Uchujin.EditorTools.AvatarKitGallery.Capture
    /// </summary>
    public static class AvatarKitGallery
    {
        private const int Count = 24, PerRow = 12;

        [MenuItem("宇宙人の星/キャラメイクの部品を一覧撮影", priority = 70)]
        public static void Capture()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "AvatarGallery");
            Directory.CreateDirectory(dir);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // 裏でのシェーダーのコンパイル中の仮表示を写さない
            var root = new GameObject("AvatarGallery");
            try
            {
                var rows = new GameObject[Count / PerRow];
                for (int r = 0; r < rows.Length; r++)
                {
                    rows[r] = new GameObject("Row" + r);
                    rows[r].transform.SetParent(root.transform, false);
                }
                for (int i = 0; i < Count; i++)
                {
                    var holder = new GameObject("Avatar" + i);
                    holder.transform.SetParent(rows[i / PerRow].transform, false);
                    holder.transform.localPosition = new Vector3((i % PerRow - (PerRow - 1) / 2f) * 1.25f, 0f, 0f);
                    var v = Uchujin.Story.AvatarBuilder.Build(Sample(i), holder.transform);
                    var tool = v.transform.Find("ToolHolder");
                    if (tool != null) tool.gameObject.SetActive(false);
                }
                var lightGo = new GameObject("Sun");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.3f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.65f);

                // 最初の1枚は描画の準備（シェーダー・材質の初期化）で色が正しく出ないことがあるので捨てる
                Shoot(root, Path.Combine(dir, "_warmup.png"), new Vector3(0f, 1.9f, 12.5f), new Vector3(0f, 0.85f, 0f));
                File.Delete(Path.Combine(dir, "_warmup.png"));
                for (int r = 0; r < rows.Length; r++)
                {
                    for (int k = 0; k < rows.Length; k++) rows[k].SetActive(k == r);
                    string n = "row" + (r + 1);
                    Shoot(root, Path.Combine(dir, n + "_front.png"), new Vector3(0f, 1.9f, 12.5f), new Vector3(0f, 0.85f, 0f));
                    Shoot(root, Path.Combine(dir, n + "_three_quarter.png"), new Vector3(-7f, 2.2f, 10.5f), new Vector3(0f, 0.85f, 0f));
                    Shoot(root, Path.Combine(dir, n + "_back.png"), new Vector3(0f, 1.9f, -12.5f), new Vector3(0f, 0.85f, 0f));
                    // 顔のアップ（左から6人ずつ）
                    Shoot(root, Path.Combine(dir, n + "_faces_left.png"), new Vector3(-4.4f, 1.3f, 4.6f), new Vector3(-4.4f, 1.15f, 0f));
                    Shoot(root, Path.Combine(dir, n + "_faces_right.png"), new Vector3(3.1f, 1.3f, 4.6f), new Vector3(3.1f, 1.15f, 0f));
                }
                Debug.Log("[AvatarKitGallery] 保存しました: " + dir);
            }
            finally
            {
                Object.DestroyImmediate(root);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        /// <summary>i 番目の見本。どの項目も一覧の全部が少なくとも1回は出るように順に回す。</summary>
        private static CharacterAppearance Sample(int i)
        {
            var a = new CharacterAppearance
            {
                bodyType = i % AppearanceCatalog.BodyTypes.Length,
                height = 0.92f + (i % 5) * 0.04f,
                headSize = 1.0f + (i % 3) * 0.1f,
                faceType = i % AppearanceCatalog.FaceTypes.Length,
                eyeType = (i * 3) % AppearanceCatalog.EyeTypes.Length,
                hairStyle = i % AppearanceCatalog.HairStyles.Length,
                outfitTop = i % AppearanceCatalog.Tops.Length,
                outfitBottom = (i * 5) % AppearanceCatalog.Bottoms.Length,
                shoes = (i * 2) % AppearanceCatalog.Shoes.Length,
                accessory = i % AppearanceCatalog.Accessories.Length,
                alienTrait = (i * 3) % AppearanceCatalog.AlienTraits.Length,
            };
            a.skinColor = i % 2 == 0 ? AppearanceCatalog.SkinHuman[i / 2 % AppearanceCatalog.SkinHuman.Length]
                : AppearanceCatalog.SkinAlien[i / 2 % AppearanceCatalog.SkinAlien.Length];
            a.hairColor = AppearanceCatalog.HairColors[(i * 5) % AppearanceCatalog.HairColors.Length];
            a.eyeColor = AppearanceCatalog.EyeColors[i % AppearanceCatalog.EyeColors.Length];
            var cloth = AppearanceCatalog.ClothColors;
            a.topColor = cloth[(i * 3 + 2) % cloth.Length];
            a.bottomColor = cloth[(i * 7 + 9) % cloth.Length];
            a.shoesColor = cloth[(i * 5 + 3) % cloth.Length];
            a.accessoryColor = cloth[(i * 11 + 4) % cloth.Length];
            return AppearanceCatalog.Sanitize(a);
        }

        private static void Shoot(GameObject root, string path, Vector3 from, Vector3 at, float fov = 34f)
        {
            const int w = 2400, h = 1100;
            var camGo = new GameObject("GalleryCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = from;
            camGo.transform.LookAt(at);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.28f);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}
#endif
