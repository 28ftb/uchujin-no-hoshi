#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// デザイン済み住民100人のモデル（Resources/Residents/&lt;4桁ID&gt;.fbx）の取り込み設定。
    /// ・Blender の FBX は既定の軸変換のまま取り込む（Y が上・正面が +Z・スケール1。軸変換の焼き込みをオンにすると正面が反転する）。
    /// ・アクション（"&lt;ID&gt;_Rig|&lt;ID&gt;_Rig|&lt;ID&gt;_Idle" など）は Legacy のクリップ "Idle" / "Signature" / "Sleep" として取り込む。
    ///   コードから Animation で再生するので、AnimatorController は作らない。
    /// ・部品は1本の骨に100%結び付いた硬い部品なので、実行時に骨の子の MeshRenderer へ置き換える（ResidentModelBuilder）。
    ///   そのために bindposes と骨の重みを読めるようにする。
    /// ・材質は色だけを使い、実行時に MaterialCache の材質へ差し替える。
    /// </summary>
    public sealed class ResidentModelImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Residents/";

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
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            for (int i = 0; i < clips.Length; i++)
            {
                string take = clips[i].takeName ?? clips[i].name ?? "";
                int cut = take.LastIndexOf('_');
                string kind = cut >= 0 ? take.Substring(cut + 1) : take;
                clips[i].name = kind;
                bool loop = kind == "Idle" || kind == "Signature";
                clips[i].loopTime = loop;
                clips[i].wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            }
            importer.clipAnimations = clips;
        }
    }
}
#endif
