"""村の新しいモデル（住民の家・村の小物・主人公のタブレット・ロケット）を Resources/Art へ集める。

元データは Residents フォルダ（C:\\Users\\81901\\Documents\\ChatGPT\\Residents）で、Blender で作ったもの：
  houses/<系統>/colors/export/house_<系統2桁>_<住民4桁ID>.fbx … 住民ごとの色の家（100軒）
  props/<小物>/<小物>.fbx                                    … 街灯・橋・看板（＋矢印板）・花壇・ベンチ・広場の目印
  tablet/tablet_before.fbx, tablet_after.fbx                   … 主人公のタブレット（修理前・修理後、幅 0.40 m）
  rocket/before|after/spaceship.glb                            … ロケット（修理前・修理後）。FBX が無いのでここで書き出す

FBX はどれも Blender の −Y 正面・Z 上を Forward −Z / Up Y / FBX_SCALE_ALL で書き出したもの（住民100人のモデルと同じ）。

実行（リポジトリのルートで）:
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" --background --factory-startup --python Tools/VillageArt/collect_village_art.py
"""

import shutil
from pathlib import Path

import bpy

SRC = Path(r"C:\Users\81901\Documents\ChatGPT\Residents")
REPO = Path(__file__).resolve().parents[2]
DST = REPO / "Assets" / "_Project" / "Resources" / "Art"

PROPS = {
    "lamp": ["lamp"],
    "bridge": ["bridge"],
    "sign": ["sign", "sign_arrow"],
    "flowerbed": ["flowerbed", "bench"],
    "plaza": ["plaza"],
}


def copy(src: Path, dst: Path) -> None:
    if not src.exists():
        raise FileNotFoundError(src)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def export_rocket(glb: Path, fbx: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(glb))
    fbx.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(fbx), apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             add_leaf_bones=False, object_types={"EMPTY", "MESH"}, mesh_smooth_type="FACE",
                             bake_anim=False)


def main() -> None:
    houses = sorted(SRC.glob("houses/*/colors/export/house_*.fbx"))
    if len(houses) != 100:
        raise RuntimeError(f"住民の家の FBX が100軒ありません: {len(houses)}")
    for f in houses:
        copy(f, DST / "Houses" / f.name)
    for folder, names in PROPS.items():
        for n in names:
            copy(SRC / "props" / folder / f"{n}.fbx", DST / "Village" / f"{n}.fbx")
    for state in ("before", "after"):
        copy(SRC / "tablet" / f"tablet_{state}.fbx", DST / "Tablet" / f"tablet_{state}.fbx")
        export_rocket(SRC / "rocket" / state / "spaceship.glb", DST / "Rocket" / f"rocket_{state}.fbx")
    print(f"COLLECTED houses={len(houses)} -> {DST}")


main()
