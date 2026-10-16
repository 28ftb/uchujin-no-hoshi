using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 住民の家の外観（村仕様 §5.1：家の外観・大きさ・庭に住民の個性を反映。同じ系統でも同じ家とは限らない）。
    /// デザイン済みの住民は、系統ごとの形をその住民の配色で塗った家のモデルを使う（ユーザーの指定。BuildModel）。
    /// モデルが無い住民はプリミティブの7つの形から選ぶ。
    /// 空いている区画は空き地＋「空き家」の看板。引っ越しで作り直す（村仕様 §5.3、§11）。
    /// 引っ越し準備中（Packing）の住民の家の前には荷物の箱を積む。
    /// </summary>
    public static class HouseBuilder
    {
        private static readonly Color Wood = new Color(0.62f, 0.44f, 0.28f);
        private static readonly Color DarkWood = new Color(0.42f, 0.29f, 0.18f);
        private static readonly Color WindowOn = new Color(1f, 0.85f, 0.5f);
        private static readonly Color Glass = new Color(0.55f, 0.6f, 0.68f);

        public enum Style { Cottage, Dome, Mushroom, Tower, Stilts, Workshop, Box }

        public static Style StyleFor(string residentId, AlienLineage? lineage, int plotIndex)
        {
            ulong h = StableHash.Fnv1a64((residentId ?? "") + "#" + plotIndex);
            int roll = (int)(h % 100);
            if (lineage.HasValue)
            {
                switch (lineage.Value)
                {
                    case AlienLineage.Round:
                    case AlienLineage.Mochi: return roll < 60 ? Style.Dome : roll < 85 ? Style.Mushroom : Style.Cottage;
                    case AlienLineage.Plush: return roll < 55 ? Style.Cottage : roll < 85 ? Style.Dome : Style.Box;
                    case AlienLineage.ChibiAnimal: return roll < 50 ? Style.Cottage : roll < 80 ? Style.Mushroom : Style.Stilts;
                    case AlienLineage.Floating: return roll < 65 ? Style.Stilts : roll < 85 ? Style.Dome : Style.Tower;
                    case AlienLineage.MultiArm: return roll < 60 ? Style.Workshop : roll < 85 ? Style.Box : Style.Cottage;
                    case AlienLineage.StarChild: return roll < 60 ? Style.Tower : roll < 85 ? Style.Dome : Style.Stilts;
                    case AlienLineage.Lifestyle: return roll < 50 ? Style.Cottage : roll < 80 ? Style.Box : Style.Workshop;
                    case AlienLineage.Expressive: return roll < 45 ? Style.Box : roll < 75 ? Style.Mushroom : Style.Cottage;
                }
            }
            return (Style)(h % 7);
        }

        /// <summary>区画の外観を作る。owner が null なら空き地。</summary>
        public static GameObject Build(HousePlot plot, FacilityInfo f, IResidentInfo owner, string ownerId, bool packing, WorldPalette P, Transform parent)
        {
            var root = StructureBuilder.Root(parent, "HousePlot_" + plot.index, f.position, f.rotationY);
            if (string.IsNullOrEmpty(ownerId))
            {
                Vacant(root, f, P);
                return root.gameObject;
            }
            ulong h = StableHash.Fnv1a64(ownerId + "#" + plot.index);
            var r = new SeededRandom(h);
            Color main = owner != null ? owner.MainColor : Color.HSVToRGB(r.NextFloat(), 0.4f, 0.9f);
            Color.RGBToHSV(main, out float hh, out float ss, out float vv);
            var wall = Color.HSVToRGB(hh, Mathf.Clamp(ss * 0.35f, 0.08f, 0.35f), 0.95f);
            var roof = Color.HSVToRGB(Mathf.Repeat(hh + r.Range(-0.08f, 0.08f), 1f), Mathf.Clamp(ss, 0.35f, 0.7f), Mathf.Clamp(vv * 0.85f, 0.45f, 0.85f));
            var door = Color.HSVToRGB(Mathf.Repeat(hh + 0.5f, 1f), 0.45f, 0.7f);
            var style = StyleFor(ownerId, owner != null ? owner.Lineage : (AlienLineage?)null, plot.index);
            var s = f.size;
            float scale = r.Range(0.88f, 1.08f);
            var size = new Vector3(s.x * scale, s.y * r.Range(0.9f, 1.15f), s.z * scale);
            var windows = new List<Renderer>();
            if (BuildModel(root, owner != null ? owner.DesignId : null, s, out var modelSize)) size = modelSize;
            else switch (style)
            {
                case Style.Cottage:
                    windows.AddRange(StructureBuilder.BasicBuilding(root, size, wall, roof, true, door));
                    StructureBuilder.Box(root, new Vector3(-size.x * 0.25f, size.y + 1.3f, -size.z * 0.2f), new Vector3(0.6f, 1.6f, 0.6f), Color.Lerp(roof, Color.gray, 0.5f), false, "Chimney");
                    break;
                case Style.Box:
                    windows.AddRange(StructureBuilder.BasicBuilding(root, size, wall, roof, false, door, 2));
                    StructureBuilder.Box(root, new Vector3(size.x * 0.2f, size.y + 1.2f, -size.z * 0.15f), new Vector3(size.x * 0.5f, 1.6f, size.z * 0.5f), Color.Lerp(wall, roof, 0.3f), false, "Upper");
                    break;
                case Style.Workshop:
                    windows.AddRange(StructureBuilder.BasicBuilding(root, size, Color.Lerp(wall, Color.gray, 0.2f), roof, false, door));
                    StructureBuilder.Cyl(root, new Vector3(size.x * 0.32f, size.y + 1f, size.z * 0.1f), new Vector3(0.8f, 1f, 0.8f), Color.Lerp(roof, Color.white, 0.3f), false, "Tank");
                    StructureBuilder.Crate(root, new Vector3(-size.x * 0.5f - 0.8f, 0f, size.z * 0.2f), 0.7f, Wood);
                    break;
                case Style.Dome:
                {
                    StructureBuilder.Box(root, new Vector3(0f, size.y * 0.35f, 0f), new Vector3(size.x * 1.05f, size.y * 0.7f, size.z * 1.05f), wall, true, "DomeBody", null, PrimitiveType.Cylinder);
                    StructureBuilder.Ball(root, new Vector3(0f, size.y * 0.7f, 0f), new Vector3(size.x * 1.1f, size.y * 1.1f, size.z * 1.1f), roof, "Dome");
                    StructureBuilder.Ball(root, new Vector3(0f, size.y * 1.3f, 0f), Vector3.one * 0.6f, Color.Lerp(roof, Color.white, 0.4f), "Top");
                    float fz = size.z * 0.52f;
                    StructureBuilder.Box(root, new Vector3(0f, 1f, fz), new Vector3(1.2f, 2f, 0.2f), door, false, "Door");
                    var w1 = StructureBuilder.Ball(root, new Vector3(-1.6f, 1.8f, fz - 0.15f), new Vector3(0.9f, 0.9f, 0.2f), Glass, "RoundWindow");
                    var w2 = StructureBuilder.Ball(root, new Vector3(1.6f, 1.8f, fz - 0.15f), new Vector3(0.9f, 0.9f, 0.2f), Glass, "RoundWindow");
                    windows.Add(w1.GetComponent<Renderer>());
                    windows.Add(w2.GetComponent<Renderer>());
                    break;
                }
                case Style.Mushroom:
                {
                    StructureBuilder.Cyl(root, new Vector3(0f, size.y * 0.5f, 0f), new Vector3(size.x * 0.8f, size.y * 0.5f, size.z * 0.8f), wall, true, "Stalk");
                    StructureBuilder.Ball(root, new Vector3(0f, size.y + 0.5f, 0f), new Vector3(size.x * 1.35f, size.y * 0.75f, size.z * 1.35f), roof, "Cap");
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 60f * Mathf.Deg2Rad + 0.4f;
                        StructureBuilder.Ball(root, new Vector3(Mathf.Cos(a) * size.x * 0.45f, size.y + 0.9f, Mathf.Sin(a) * size.z * 0.45f), Vector3.one * 0.55f, Color.white, "Spot");
                    }
                    float fz = size.z * 0.4f;
                    StructureBuilder.Box(root, new Vector3(0f, 1f, fz), new Vector3(1.1f, 2f, 0.2f), door, false, "Door");
                    var w = StructureBuilder.Ball(root, new Vector3(1.4f, 2f, fz - 0.25f), new Vector3(0.8f, 0.8f, 0.2f), Glass, "RoundWindow");
                    windows.Add(w.GetComponent<Renderer>());
                    break;
                }
                case Style.Tower:
                {
                    StructureBuilder.Cyl(root, new Vector3(0f, size.y * 0.75f, 0f), new Vector3(size.x * 0.75f, size.y * 0.75f, size.z * 0.75f), wall, true, "Tower");
                    StructureBuilder.Cyl(root, new Vector3(0f, size.y * 1.55f, 0f), new Vector3(size.x * 0.85f, 0.15f, size.z * 0.85f), roof, false, "Rim");
                    StructureBuilder.Ball(root, new Vector3(0f, size.y * 1.75f, 0f), new Vector3(size.x * 0.7f, size.y * 0.5f, size.z * 0.7f), roof, "RoofDome");
                    StructureBuilder.Glow(root, PrimitiveType.Sphere, new Vector3(0f, size.y * 2.15f, 0f), Vector3.one * 0.55f, new Color(1f, 0.95f, 0.55f), "Star");
                    float fz = size.z * 0.37f;
                    StructureBuilder.Box(root, new Vector3(0f, 1f, fz), new Vector3(1.1f, 2f, 0.2f), door, false, "Door");
                    for (int i = 0; i < 2; i++)
                    {
                        var w = StructureBuilder.Box(root, new Vector3(0f, 3.2f + i * 1.6f, fz - 0.05f), new Vector3(0.7f, 0.9f, 0.15f), Glass, false, "Window");
                        windows.Add(w.GetComponent<Renderer>());
                    }
                    break;
                }
                case Style.Stilts:
                {
                    float lift = 1.4f;
                    for (int i = 0; i < 4; i++)
                        StructureBuilder.Cyl(root, new Vector3((i % 2 - 0.5f) * size.x * 0.8f, lift * 0.5f, (i / 2 - 0.5f) * size.z * 0.8f), new Vector3(0.3f, lift * 0.5f, 0.3f), DarkWood, true, "Stilt");
                    var house = new GameObject("Raised").transform;
                    house.SetParent(root, false);
                    house.localPosition = new Vector3(0f, lift, 0f);
                    windows.AddRange(StructureBuilder.BasicBuilding(house, new Vector3(size.x * 0.9f, size.y * 0.85f, size.z * 0.9f), wall, roof, true, door));
                    // 階段（スロープ）
                    StructureBuilder.Box(root, new Vector3(0f, lift * 0.5f, size.z * 0.45f + 1.2f), new Vector3(1.2f, 0.15f, 2.6f), Wood, false, "Stairs", new Vector3(-28f, 0f, 0f));
                    for (int i = 0; i < 3; i++)
                    {
                        var bal = StructureBuilder.Ball(root, new Vector3(-size.x * 0.35f + i * 0.5f, size.y + lift + 2f + i * 0.3f, -size.z * 0.3f), new Vector3(0.6f, 0.75f, 0.6f), Color.HSVToRGB(Mathf.Repeat(hh + i * 0.2f, 1f), 0.5f, 1f), "Balloon");
                        var gm = bal.AddComponent<GentleMotion>();
                        gm.bobAmplitude = 0.15f;
                        gm.bobSpeed = 1.2f + i * 0.3f;
                    }
                    break;
                }
            }
            // 系統ごとの小さな特徴（プリミティブの家だけ。モデルの家は形に系統の特徴がある）
            if (owner != null && owner.Lineage == AlienLineage.ChibiAnimal && root.Find("HouseModel") == null)
            {
                StructureBuilder.Ball(root, new Vector3(-size.x * 0.28f, size.y + 1.6f, size.z * 0.1f), new Vector3(0.7f, 1f, 0.35f), roof, "Ear");
                StructureBuilder.Ball(root, new Vector3(size.x * 0.28f, size.y + 1.6f, size.z * 0.1f), new Vector3(0.7f, 1f, 0.35f), roof, "Ear");
            }
            // 庭（花壇・郵便受け・ベンチなど。区画ごとに違う）
            int flowers = r.Range(2, 6);
            for (int i = 0; i < flowers; i++)
                StructureBuilder.Pot(root, new Vector3(size.x * 0.5f + 0.8f, 0f, size.z * 0.4f - i * 0.8f), Color.Lerp(roof, Color.white, 0.3f), P.Flowers[(int)((h >> (i * 4)) % (ulong)P.Flowers.Length)]);
            StructureBuilder.Cyl(root, new Vector3(-size.x * 0.5f - 0.6f, 0.55f, size.z * 0.5f + 1.2f), new Vector3(0.1f, 0.55f, 0.1f), DarkWood, false, "MailPost");
            StructureBuilder.Box(root, new Vector3(-size.x * 0.5f - 0.6f, 1.2f, size.z * 0.5f + 1.2f), new Vector3(0.42f, 0.32f, 0.55f), roof, false, "Mailbox");
            if (r.Chance(0.4f)) StructureBuilder.Barrel(root, new Vector3(-size.x * 0.5f - 0.7f, 0f, -size.z * 0.2f), Color.Lerp(Wood, roof, 0.3f));

            if (packing)
            {
                // 引っ越し準備（荷物の箱）
                for (int i = 0; i < 5; i++)
                    StructureBuilder.Crate(root, new Vector3(1.2f + (i % 3) * 0.75f - 0.75f, (i / 3) * 0.65f, size.z * 0.5f + 2.4f), 0.62f, new Color(0.82f, 0.68f, 0.45f), false);
            }
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
            return root.gameObject;
        }

        /// <summary>【仮数値】家のモデルの区画に対する最大の大きさ（区画の幅・奥行きに掛ける。通行不可の範囲 (3.8, 3.3)×2 に合わせる）。</summary>
        private const float ModelMaxWidth = 1.1f, ModelMaxDepth = 1.1f;

        /// <summary>
        /// 住民ごとの色の家のモデル（Resources/Art/Houses/house_&lt;系統2桁&gt;_&lt;デザイン4桁&gt;。系統ごとの形を住民の配色で塗ったもの）。
        /// 等倍で置き（扉の高さ 1.65〜2.4 m。主人公と1.5倍の住民が通れる）、区画からはみ出す家だけ一様に縮める。
        /// 正面（扉）を区画の正面の線にそろえ、入口のドアマット（区画の正面 +1.6 m）と離れないようにする。
        /// デザインの無い住民・モデルが無い時は false（従来のプリミティブの家）。確認用の撮影（Editor/VillageArtGallery）でも使う。
        /// </summary>
        public static bool BuildModel(Transform root, string design, Vector3 plotSize, out Vector3 size)
        {
            size = plotSize;
            if (string.IsNullOrEmpty(design) || design.Length != 4) return false;
            var holder = new GameObject("HouseModel").transform;
            holder.SetParent(root, false);
            var model = ArtModels.BuildFitted(holder, "Houses/house_" + design.Substring(0, 2) + "_" + design,
                new Vector3(plotSize.x * ModelMaxWidth, 0f, plotSize.z * ModelMaxDepth));
            if (model == null)
            {
                if (Application.isPlaying) Object.Destroy(holder.gameObject);
                else Object.DestroyImmediate(holder.gameObject);
                return false;
            }
            var b = ArtModels.LocalBounds(model, root);
            model.transform.localPosition += new Vector3(-b.center.x, 0f, plotSize.z * 0.5f - b.max.z);
            b = ArtModels.LocalBounds(model, root);
            var body = PrimitiveBuilder.Empty("Body", root, Vector3.zero);
            PrimitiveBuilder.AddBoxCollider(body, b.center, b.size);
            size = new Vector3(b.size.x, b.size.y, plotSize.z);
            return true;
        }

        private static void Vacant(Transform root, FacilityInfo f, WorldPalette P)
        {
            var s = f.size;
            // 区画の角の杭とロープ
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                StructureBuilder.Cyl(root, new Vector3(c.x * (s.x * 0.5f + 0.5f), 0.35f, c.y * (s.z * 0.5f + 0.5f)), new Vector3(0.12f, 0.35f, 0.12f), DarkWood, false, "Stake");
            StructureBuilder.Box(root, new Vector3(0f, 0.04f, 0f), new Vector3(s.x, 0.06f, s.z), Color.Lerp(P.GrassLight, P.Dirt, 0.25f), false, "Lot");
            // 「空き家」の看板
            var post = StructureBuilder.Cyl(root, new Vector3(s.x * 0.3f, 0.7f, s.z * 0.5f + 0.8f), new Vector3(0.12f, 0.7f, 0.12f), DarkWood, false, "Post");
            var board = StructureBuilder.Box(root, new Vector3(s.x * 0.3f, 1.4f, s.z * 0.5f + 0.8f), new Vector3(1.1f, 0.6f, 0.08f), new Color(0.95f, 0.95f, 0.88f), false, "VacantBoard");
            // 家の形の絵（言葉が分からなくても意味が伝わる）
            StructureBuilder.Box(root, new Vector3(s.x * 0.3f, 1.35f, s.z * 0.5f + 0.85f), new Vector3(0.3f, 0.22f, 0.02f), P.Roofs[0], false, "Icon");
            StructureBuilder.Box(root, new Vector3(s.x * 0.3f, 1.52f, s.z * 0.5f + 0.85f), new Vector3(0.26f, 0.1f, 0.02f), P.Roofs[0], false, "IconRoof", new Vector3(0f, 0f, 0f));
            WorldLabels.Add(board.transform, () => Loc.T("空き家", "Vacant"), new Vector3(0f, 0.6f, 0f), UIStyles.TextDim, 18f, false);
        }
    }
}
