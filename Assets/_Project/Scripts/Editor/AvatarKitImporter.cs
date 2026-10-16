#if UNITY_EDITOR
using UnityEditor;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// 主人公アバターの部品キット（Tools/Avatar/build_avatar_kit.py が書き出す Resources/Avatar/*.fbx）の取り込み設定。
    /// 骨組みの空オブジェクト（Rig_*）をゲームが回すので、軸の変換をメッシュに焼き込み、どのオブジェクトも回転なしで取り込む。
    /// アニメーション・カメラ・ライトは無し。材質は名前（AvTint_／AvArt_／AvGlow_／AvGlass_）だけを使い、実行時に差し替える。
    /// </summary>
    public sealed class AvatarKitImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Resources/Avatar/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
#endif
