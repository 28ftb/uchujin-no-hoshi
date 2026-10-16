#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// 村の静止モデル（Resources/Art/ の住民の家・村の小物・タブレット・ロケット）の取り込み設定。
    /// ・住民100人のモデル（ResidentModelImporter）と同じく、Blender の FBX は既定の軸変換のまま取り込む
    ///   （Y が上・正面が +Z・スケール1。軸変換の焼き込みをオンにすると正面が反転する）。
    /// ・動かさないのでアニメーションと骨は取り込まない。
    /// ・材質は実行時に MaterialCache の材質へ差し替える（ArtModels）。その元になる色・不透明度・発光を、
    ///   FBX の材質情報（DiffuseColor / TransparencyFactor / EmissiveColor×EmissiveFactor）からそのまま材質に書く。
    ///   従来の取り込み（ImportStandard）では発光が読まれず、窓や街灯のつぼみが光らなかったため。
    ///   色の値は ImportStandard と同じく FBX の値そのまま（ArtModels 側で gamma で sRGB に戻す）。
    /// </summary>
    public sealed class ArtModelImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Art/";

        /// <summary>取り込み方を変えたら上げる（上げると Art の FBX がすべて取り込み直される）。</summary>
        public override uint GetVersion() => 3;

        /// <summary>自然物は実行時にメッシュの頂点を読んで結合メッシュへ入れる（NatureModels）ので Read/Write を有効にする。</summary>
        private const string ReadableFolder = Folder + "Nature/";

        /// <summary>URP の FBX 材質の前処理より後に、色・発光を上書きする。</summary>
        public override int GetPostprocessOrder() => 100;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.isReadable = assetPath.StartsWith(ReadableFolder);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!assetPath.StartsWith(Folder)) return;
            Color c = Color.white;
            if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse)) c = new Color(diffuse.x, diffuse.y, diffuse.z, 1f);
            if (description.TryGetProperty("TransparencyFactor", out float transparency) && transparency > 0.001f) c.a = Mathf.Clamp01(1f - transparency);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            if (material.HasProperty("_Color")) material.SetColor("_Color", c);

            Color glow = Color.black;
            if (description.TryGetProperty("EmissiveColor", out Vector4 emissive))
            {
                float factor = description.TryGetProperty("EmissiveFactor", out float f) ? f : 1f;
                glow = new Color(emissive.x, emissive.y, emissive.z) * factor;
            }
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", glow);
            if (glow.maxColorComponent > 0.05f) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
        }
    }
}
#endif
