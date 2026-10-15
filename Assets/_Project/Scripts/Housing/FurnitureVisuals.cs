using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Housing
{
    /// <summary>
    /// 家具の簡易モデル（プリミティブの組み合わせ）。アート制作時はここを差し替える。
    /// 原点は足跡の中心・床の高さ。ローカル +z が家具の「正面」。壁掛けは z=0 が壁面。
    /// </summary>
    public static class FurnitureVisuals
    {
        private static readonly Color White = new Color(0.97f, 0.97f, 0.95f);
        private static readonly Color Metal = new Color(0.55f, 0.56f, 0.6f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.35f);
        private static readonly Color Glass = new Color(0.75f, 0.92f, 1f, 0.3f);
        private static readonly Color WaterC = new Color(0.35f, 0.65f, 0.95f, 0.45f);

        public class BuildResult
        {
            public GameObject Root;
            public FurnitureFx Fx;
            public List<Transform> DisplayAnchors = new List<Transform>();
            public Transform SeatPoint;
        }

        public static BuildResult Build(ItemDefinition def, FurnitureSpec spec, Transform parent, Vector3 worldPos, float yaw, string name, bool withCollider = true)
        {
            var res = new BuildResult();
            var root = new GameObject(name ?? ("Furniture_" + def.Id));
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = worldPos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            res.Root = root;
            var t = root.transform;
            var fx = root.AddComponent<FurnitureFx>();
            res.Fx = fx;

            float W = Mathf.Max(1, spec.w) * HousingConst.Cell;
            float D = (spec.surface == FurnitureSurface.Wall ? 1 : Mathf.Max(1, spec.d)) * HousingConst.Cell;
            float H = Mathf.Max(0.02f, spec.height);
            var c = FurnitureCatalog.Tint(def.Color, spec);
            var acc = spec.accent == Color.white ? Lighter(c) : spec.accent;
            bool round = FurnitureCatalog.Culture != null && FurnitureCatalog.Culture.roundness > 0.6f;

            // 浮く家具は本体を持ち上げる「浮遊体」の下に組む
            Transform body = t;
            if (spec.floating)
            {
                body = PrimitiveBuilder.Empty("FloatBody", t, new Vector3(0f, 0.25f, 0f)).transform;
                fx.floatBody = body;
                PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(W * 0.7f, 0.01f, D * 0.7f), new Color(0.7f, 0.85f, 1f), "FloatGlow");
            }

            switch (spec.style)
            {
                case FurnitureStyle.Chair: Chair(body, W, D, H, c, acc, res); break;
                case FurnitureStyle.Stool: Stool(body, W, D, H, c, spec, res); break;
                case FurnitureStyle.Rocker: Chair(body, W * 0.8f, D * 0.8f, H, c, acc, res); Rockers(body, W, D, c); fx.rockBody = body; break;
                case FurnitureStyle.Sofa: Sofa(body, W, D, H, c, acc, spec, round, res); break;
                case FurnitureStyle.Cushion: Cushion(body, W, D, H, c, res, spec); break;
                case FurnitureStyle.Bed: Bed(body, W, D, H, c, acc, spec, res, fx); break;
                case FurnitureStyle.Table: Table(body, W, D, H, c, def, res, round); break;
                case FurnitureStyle.RoundTable: RoundTable(body, W, D, H, c, acc, spec, res, fx); break;
                case FurnitureStyle.Desk: Desk(body, W, D, H, c, res); break;
                case FurnitureStyle.Shelf: Shelf(body, W, D, H, c, spec, res, fx); break;
                case FurnitureStyle.Bookshelf: Bookshelf(body, W, D, H, c); break;
                case FurnitureStyle.Cabinet: Cabinet(body, W, D, H, c, false); break;
                case FurnitureStyle.Wardrobe: Cabinet(body, W, D, H, c, true); break;
                case FurnitureStyle.Chest: Chest(body, W, D, H, c); break;
                case FurnitureStyle.Box: OpenBox(body, W, D, H, c); break;
                case FurnitureStyle.Lamp: Lamp(body, W, D, H, c, spec, res, fx); break;
                case FurnitureStyle.FloorLamp: FloorLamp(body, W, D, H, c, fx); break;
                case FurnitureStyle.Chandelier: Chandelier(body, W, D, H, c, fx); break;
                case FurnitureStyle.Mobile: Mobile(body, W, D, H, c, fx); break;
                case FurnitureStyle.Aquarium: Aquarium(body, W, D, H, c, res, fx); break;
                case FurnitureStyle.WaterOrb: WaterOrb(body, W, D, H, res, fx); break;
                case FurnitureStyle.Plant: Plant(body, W, D, H, c, false); break;
                case FurnitureStyle.BigPlant: Plant(body, W, D, H, c, true); break;
                case FurnitureStyle.Tree: Tree(body, W, D, H, c); break;
                case FurnitureStyle.FlowerBed: FlowerBed(body, W, D, H, c); break;
                case FurnitureStyle.Rug: Rug(body, W, D, H, c, acc, spec, fx); break;
                case FurnitureStyle.Painting: Painting(body, W, H, c); break;
                case FurnitureStyle.Clock: Clock(body, W, H, c, fx); break;
                case FurnitureStyle.Window: Window(body, W, H, c, fx, false); break;
                case FurnitureStyle.RoundWindow: Window(body, W, H, c, fx, true); break;
                case FurnitureStyle.Curtain: Curtain(body, W, H, c, fx); break;
                case FurnitureStyle.Piano: Piano(body, W, D, H, c); break;
                case FurnitureStyle.Harp: Harp(body, W, D, H, c, fx); break;
                case FurnitureStyle.Xylophone: Xylophone(body, W, D, H, c); break;
                case FurnitureStyle.Drum: Drum(body, W, D, H, c); break;
                case FurnitureStyle.Gramophone: Gramophone(body, W, D, H, c); break;
                case FurnitureStyle.Ball: Ball(body, W, H, c); break;
                case FurnitureStyle.Blocks: Blocks(body, W, D); break;
                case FurnitureStyle.Bench: Bench(body, W, D, H, c, res); break;
                case FurnitureStyle.Fence: Fence(body, W, D, H, c); break;
                case FurnitureStyle.Path: PathStones(body, W, D, c); break;
                case FurnitureStyle.Pond: Pond(body, W, D, c, fx); break;
                case FurnitureStyle.Streetlight: Streetlight(body, H, c, fx); break;
                case FurnitureStyle.Sign: Sign(body, W, H, c); break;
                case FurnitureStyle.Rocks: Rocks(body, W, D, H, c); break;
                case FurnitureStyle.Perch: Perch(body, W, D, H, c, res); break;
                case FurnitureStyle.Mirror: Mirror(body, W, D, H, c); break;
                case FurnitureStyle.Swing: Swing(body, W, D, H, c, res, fx); break;
                case FurnitureStyle.Pedestal: Pedestal(body, W, D, H, c, res, fx); break;
                case FurnitureStyle.Vase: Vase(body, W, H, c, res); break;
                case FurnitureStyle.Photo: Photo(body, W, H, c); break;
                case FurnitureStyle.Figurine: Figurine(body, W, H, c); break;
                case FurnitureStyle.Playground: Playground(body, W, D, H, c); break;
                case FurnitureStyle.Nest: Nest(body, W, D, H, c, res); break;
                case FurnitureStyle.WindChime: WindChime(body, H, c, fx); break;
                case FurnitureStyle.SoundTiles: SoundTiles(body, W, D); break;
                case FurnitureStyle.Birdhouse: Birdhouse(body, H, c); break;
                case FurnitureStyle.ToolRack: ToolRack(body, W, H, c, res, spec); break;
                case FurnitureStyle.Workbench: Workbench(body, W, D, H, c); break;
                default: Generic(body, def, W, D, H, c); break;
            }

            if (spec.wobble) fx.wobbleBody = body;
            if (spec.shapeShift) fx.shapeBody = body;

            if (withCollider) AddCollider(root, spec, W, D, H);
            return res;
        }

        private static void AddCollider(GameObject root, FurnitureSpec spec, float W, float D, float H)
        {
            switch (spec.surface)
            {
                case FurnitureSurface.Rug:
                    PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, 0.05f, 0), new Vector3(W, 0.1f, D), true);
                    break;
                case FurnitureSurface.Wall:
                    PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, 0, 0.15f), new Vector3(W, Mathf.Max(0.4f, H), 0.3f), true);
                    break;
                case FurnitureSurface.Ceiling:
                    PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, H * 0.5f - 0.4f, 0), new Vector3(W, H + 0.8f, D), true);
                    break;
                case FurnitureSurface.Tabletop:
                    PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, H * 0.5f, 0), new Vector3(W, Mathf.Max(0.2f, H), D), true);
                    break;
                default:
                    float h = Mathf.Max(0.25f, H + (spec.floating ? 0.25f : 0f));
                    PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, h * 0.5f, 0), new Vector3(W * 0.95f, h, D * 0.95f), false);
                    break;
            }
        }

        // ------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------

        private static GameObject P(Transform t, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Vector3? euler = null)
            => PrimitiveBuilder.Part(t, type, pos, scale, c, null, euler);

        private static GameObject G(Transform t, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
            => PrimitiveBuilder.EmissivePart(t, type, pos, scale, c);

        public static Color Lighter(Color c) => Color.Lerp(c, Color.white, 0.4f);
        public static Color Darker(Color c) => Color.Lerp(c, Color.black, 0.3f);

        private static void Legs(Transform t, float W, float D, float H, Color c, float inset = 0.06f, float thick = 0.06f)
        {
            float x = W * 0.5f - inset, z = D * 0.5f - inset;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    P(t, PrimitiveType.Cylinder, new Vector3(sx * x, H * 0.5f, sz * z), new Vector3(thick, H * 0.5f, thick), c);
        }

        private static Transform Anchor(Transform t, Vector3 pos, BuildResult res)
        {
            var a = PrimitiveBuilder.Empty("Anchor", t, pos).transform;
            res.DisplayAnchors.Add(a);
            return a;
        }

        private static void Seat(Transform t, Vector3 pos, BuildResult res)
        {
            if (res.SeatPoint == null) res.SeatPoint = PrimitiveBuilder.Empty("Seat", t, pos).transform;
        }

        // ------------------------------------------------------------
        // 形ごとの組み立て
        // ------------------------------------------------------------

        private static void Chair(Transform t, float W, float D, float H, Color c, Color acc, BuildResult res)
        {
            float seatY = 0.45f;
            P(t, PrimitiveType.Cube, new Vector3(0, seatY, 0), new Vector3(W * 0.85f, 0.07f, D * 0.85f), c);
            Legs(t, W * 0.85f, D * 0.85f, seatY, Darker(c), 0.04f, 0.05f);
            P(t, PrimitiveType.Cube, new Vector3(0, seatY + (H - seatY) * 0.5f, -D * 0.4f), new Vector3(W * 0.85f, H - seatY, 0.06f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, seatY + 0.05f, 0.02f), new Vector3(W * 0.7f, 0.04f, D * 0.65f), acc);
            Seat(t, new Vector3(0, seatY + 0.05f, 0), res);
        }

        private static void Stool(Transform t, float W, float D, float H, Color c, FurnitureSpec spec, BuildResult res)
        {
            if (spec.soft)
                P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.55f, 0), new Vector3(W * 0.9f, H * 1.1f, D * 0.9f), c);
            else
            {
                P(t, PrimitiveType.Cylinder, new Vector3(0, H - 0.04f, 0), new Vector3(W * 0.85f, 0.04f, D * 0.85f), c);
                P(t, PrimitiveType.Cylinder, new Vector3(0, (H - 0.08f) * 0.5f, 0), new Vector3(W * 0.6f, (H - 0.08f) * 0.5f, D * 0.6f), Darker(c));
            }
            Seat(t, new Vector3(0, H, 0), res);
        }

        private static void Rockers(Transform t, float W, float D, Color c)
        {
            foreach (var sx in new[] { -1f, 1f })
                P(t, PrimitiveType.Capsule, new Vector3(sx * W * 0.35f, 0.04f, 0), new Vector3(0.05f, D * 0.6f, 0.05f), Darker(c), new Vector3(90, 0, 0));
        }

        private static void Sofa(Transform t, float W, float D, float H, Color c, Color acc, FurnitureSpec spec, bool round, BuildResult res)
        {
            var armType = round ? PrimitiveType.Capsule : PrimitiveType.Cube;
            P(t, PrimitiveType.Cube, new Vector3(0, 0.22f, 0.05f), new Vector3(W, 0.3f, D * 0.85f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, 0.55f, -D * 0.38f), new Vector3(W, H - 0.2f, D * 0.25f), c);
            foreach (var sx in new[] { -1f, 1f })
                P(t, armType, new Vector3(sx * (W * 0.5f - 0.1f), 0.45f, 0.05f), round ? new Vector3(0.2f, D * 0.42f, 0.25f) : new Vector3(0.2f, 0.4f, D * 0.85f), Darker(c), round ? new Vector3(90, 0, 0) : (Vector3?)null);
            int seats = Mathf.Max(1, spec.seats);
            float seatW = (W - 0.4f) / seats;
            for (int i = 0; i < seats; i++)
            {
                float x = -W * 0.5f + 0.2f + seatW * (i + 0.5f);
                P(t, spec.soft ? PrimitiveType.Sphere : PrimitiveType.Cube, new Vector3(x, 0.42f, 0.08f), new Vector3(seatW * 0.92f, 0.14f, D * 0.6f), Lighter(c));
            }
            if (acc != Lighter(c)) P(t, PrimitiveType.Cube, new Vector3(0, H, -D * 0.38f), new Vector3(W, 0.05f, D * 0.27f), acc);
            Seat(t, new Vector3(0, 0.48f, 0.08f), res);
        }

        private static void Cushion(Transform t, float W, float D, float H, Color c, BuildResult res, FurnitureSpec spec)
        {
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.95f, Mathf.Max(0.12f, H), D * 0.95f), c);
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.85f, 0), new Vector3(W * 0.2f, 0.05f, D * 0.2f), Lighter(c));
            Seat(t, new Vector3(0, spec.seatHeight, 0), res);
        }

        private static void Bed(Transform t, float W, float D, float H, Color c, Color acc, FurnitureSpec spec, BuildResult res, FurnitureFx fx)
        {
            var frame = spec.series == FurnitureSeries.Manmaru ? c : Darker(c);
            if (spec.series == FurnitureSeries.Manmaru)
            {
                P(t, PrimitiveType.Cylinder, new Vector3(0, 0.18f, 0), new Vector3(W, 0.18f, D), frame);
                P(t, PrimitiveType.Sphere, new Vector3(0, 0.38f, 0), new Vector3(W * 0.95f, 0.2f, D * 0.95f), White);
            }
            else
            {
                P(t, PrimitiveType.Cube, new Vector3(0, 0.15f, 0), new Vector3(W, 0.22f, D), frame);
                P(t, PrimitiveType.Cube, new Vector3(0, 0.33f, 0), new Vector3(W * 0.94f, 0.14f, D * 0.96f), White);
                P(t, PrimitiveType.Cube, new Vector3(0, 0.45f, -D * 0.5f + 0.04f), new Vector3(W, 0.7f, 0.08f), frame);
            }
            P(t, PrimitiveType.Cube, new Vector3(0, 0.44f, -D * 0.33f), new Vector3(W * 0.6f, 0.1f, D * 0.18f), White);
            P(t, PrimitiveType.Cube, new Vector3(0, 0.42f, D * 0.12f), new Vector3(W * 0.96f, 0.08f, D * 0.7f), spec.series == FurnitureSeries.Basic ? new Color(0.6f, 0.75f, 0.95f) : c);
            if (spec.glowing)
            {
                for (int i = 0; i < 7; i++)
                {
                    float x = Mathf.Sin(i * 2.3f) * W * 0.4f, z = D * 0.12f + Mathf.Cos(i * 1.7f) * D * 0.3f;
                    fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(x, 0.47f, z), Vector3.one * 0.05f, acc).GetComponent<Renderer>());
                }
            }
            Seat(t, new Vector3(0, spec.seatHeight, 0), res);
        }

        private static void Table(Transform t, float W, float D, float H, Color c, ItemDefinition def, BuildResult res, bool round)
        {
            bool crystal = def.HasTag(ItemTags.Crystal);
            var top = crystal ? new Color(c.r, c.g, c.b, 0.55f) : c;
            P(t, round ? PrimitiveType.Cylinder : PrimitiveType.Cube, new Vector3(0, H - 0.03f, 0), round ? new Vector3(W, 0.03f, D) : new Vector3(W, 0.06f, D), top);
            Legs(t, W, D, H - 0.06f, crystal ? Lighter(c) : Darker(c));
            TableAnchors(t, W, D, H, res);
        }

        private static void TableAnchors(Transform t, float W, float D, float H, BuildResult res)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(W / 0.7f)), nz = Mathf.Max(1, Mathf.RoundToInt(D / 0.7f));
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                    Anchor(t, new Vector3(-W * 0.5f + W * (x + 0.5f) / nx, H + 0.06f, -D * 0.5f + D * (z + 0.5f) / nz), res);
        }

        private static void RoundTable(Transform t, float W, float D, float H, Color c, Color acc, FurnitureSpec spec, BuildResult res, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H - 0.03f, 0), new Vector3(W, 0.03f, D), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, (H - 0.06f) * 0.5f, 0), new Vector3(0.12f, (H - 0.06f) * 0.5f, 0.12f), Darker(c));
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(W * 0.45f, 0.02f, D * 0.45f), Darker(c));
            if (spec.glowing)
                for (int i = 0; i < 9; i++)
                {
                    float a = i * 0.7f, r = (i % 3 + 1) * W * 0.12f;
                    fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, H + 0.005f, Mathf.Sin(a) * r), new Vector3(0.05f, 0.01f, 0.05f), acc).GetComponent<Renderer>());
                }
            TableAnchors(t, W * 0.7f, D * 0.7f, H, res);
        }

        private static void Desk(Transform t, float W, float D, float H, Color c, BuildResult res)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H - 0.03f, 0), new Vector3(W, 0.06f, D), c);
            foreach (var sx in new[] { -1f, 1f })
                P(t, PrimitiveType.Cube, new Vector3(sx * (W * 0.5f - 0.04f), (H - 0.06f) * 0.5f, 0), new Vector3(0.06f, H - 0.06f, D * 0.95f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(W * 0.25f, H - 0.15f, D * 0.1f), new Vector3(W * 0.4f, 0.12f, D * 0.8f), Darker(c));
            P(t, PrimitiveType.Sphere, new Vector3(W * 0.25f, H - 0.15f, D * 0.52f), Vector3.one * 0.04f, Gold);
            TableAnchors(t, W, D, H, res);
        }

        private static void Shelf(Transform t, float W, float D, float H, Color c, FurnitureSpec spec, BuildResult res, FurnitureFx fx)
        {
            int boards = Mathf.Clamp(Mathf.RoundToInt(H / 0.45f), 2, 5);
            if (!spec.floating)
            {
                foreach (var sx in new[] { -1f, 1f })
                    P(t, PrimitiveType.Cube, new Vector3(sx * (W * 0.5f - 0.03f), H * 0.5f, 0), new Vector3(0.05f, H, D), Darker(c));
                P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, -D * 0.5f + 0.02f), new Vector3(W, H, 0.03f), Darker(c));
            }
            bool colorful = spec.habitatSpecies.Length > 0 && spec.habitatSpecies[0] == "036";
            for (int i = 0; i < boards; i++)
            {
                float y = 0.05f + i * (H - 0.1f) / (boards - 1);
                var bc = colorful ? Color.HSVToRGB(i / (float)boards, 0.5f, 1f) : c;
                P(t, PrimitiveType.Cube, new Vector3(0, y, 0), new Vector3(W * 0.95f, 0.04f, D * 0.95f), bc);
                if (i > 0 && i < boards) Anchor(t, new Vector3(0, y + 0.12f, 0.02f), res);
            }
            if (spec.floating) fx.spinAnchors = true;
        }

        private static void Bookshelf(Transform t, float W, float D, float H, Color c)
        {
            Cabinet(t, W, D, H, c, false, true);
            int rows = 4;
            for (int r = 0; r < rows; r++)
            {
                float y = 0.15f + r * (H - 0.2f) / rows;
                float x = -W * 0.45f;
                int k = 0;
                while (x < W * 0.42f)
                {
                    float bw = 0.05f + (k * 37 % 5) * 0.012f;
                    float bh = 0.22f + (k * 13 % 4) * 0.03f;
                    P(t, PrimitiveType.Cube, new Vector3(x + bw * 0.5f, y + bh * 0.5f, 0.02f), new Vector3(bw, bh, D * 0.7f), Color.HSVToRGB((k * 0.17f + r * 0.31f) % 1f, 0.45f, 0.75f));
                    x += bw + 0.008f;
                    k++;
                }
            }
        }

        private static void Cabinet(Transform t, float W, float D, float H, Color c, bool drawers, bool open = false)
        {
            if (open)
            {
                foreach (var sx in new[] { -1f, 1f })
                    P(t, PrimitiveType.Cube, new Vector3(sx * (W * 0.5f - 0.03f), H * 0.5f, 0), new Vector3(0.05f, H, D), Darker(c));
                P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, -D * 0.5f + 0.02f), new Vector3(W, H, 0.03f), Darker(c));
                for (int i = 0; i <= 4; i++) P(t, PrimitiveType.Cube, new Vector3(0, 0.03f + i * (H - 0.06f) / 4f, 0), new Vector3(W, 0.04f, D), c);
                return;
            }
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, 0), new Vector3(W, H, D), c);
            if (drawers)
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(H / 0.35f));
                for (int i = 0; i < n; i++)
                {
                    float y = (i + 0.5f) * H / n;
                    P(t, PrimitiveType.Cube, new Vector3(0, y, D * 0.5f + 0.005f), new Vector3(W * 0.9f, H / n * 0.8f, 0.01f), Lighter(c));
                    P(t, PrimitiveType.Sphere, new Vector3(0, y, D * 0.5f + 0.03f), Vector3.one * 0.05f, Gold);
                }
            }
            else
            {
                foreach (var sx in new[] { -1f, 1f })
                {
                    P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.24f, H * 0.5f, D * 0.5f + 0.005f), new Vector3(W * 0.45f, H * 0.9f, 0.01f), Lighter(c));
                    P(t, PrimitiveType.Sphere, new Vector3(sx * 0.06f, H * 0.5f, D * 0.5f + 0.03f), Vector3.one * 0.05f, Gold);
                }
            }
        }

        private static void Chest(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.4f, 0), new Vector3(W * 0.95f, H * 0.8f, D * 0.85f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.86f, 0), new Vector3(W, H * 0.16f, D * 0.9f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.45f, 0), new Vector3(W * 0.97f, 0.05f, D * 0.87f), Metal);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.62f, D * 0.43f), new Vector3(0.1f, 0.12f, 0.03f), Gold);
        }

        private static void OpenBox(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(W * 0.9f, 0.04f, D * 0.9f), Darker(c));
            foreach (var sx in new[] { -1f, 1f })
            {
                P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.44f, H * 0.5f, 0), new Vector3(0.03f, H, D * 0.9f), c);
                P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, sx * D * 0.44f), new Vector3(W * 0.9f, H, 0.03f), c);
                P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.55f, H, 0), new Vector3(W * 0.25f, 0.02f, D * 0.85f), Lighter(c), new Vector3(0, 0, sx * -35f));
            }
        }

        private static void AddLight(Transform t, Vector3 pos, Color color, float range, FurnitureFx fx)
        {
            var go = PrimitiveBuilder.Empty("Light", t, pos);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = 1.3f;
            l.color = Color.Lerp(color, new Color(1f, 0.85f, 0.6f), 0.5f);
            l.shadows = LightShadows.None;
            fx.lights.Add(l);
        }

        private static void Lamp(Transform t, float W, float D, float H, Color c, FurnitureSpec spec, BuildResult res, FurnitureFx fx)
        {
            if (spec.displayKind == DisplayKind.Mineral)
            {
                // 星結晶ランプ：台座＋結晶の柱
                P(t, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0), new Vector3(W * 0.8f, 0.06f, D * 0.8f), Darker(c));
                var crystal = G(t, PrimitiveType.Cube, new Vector3(0, H * 0.55f, 0), new Vector3(W * 0.35f, H * 0.8f, D * 0.35f), c);
                crystal.transform.localEulerAngles = new Vector3(0, 45, 0);
                fx.glowParts.Add(crystal.GetComponent<Renderer>());
                fx.colorFromDisplay = crystal.GetComponent<Renderer>();
                Anchor(t, new Vector3(0, 0.16f, D * 0.3f), res);
                AddLight(t, new Vector3(0, H * 0.6f, 0), c, 5f, fx);
                return;
            }
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(W * 0.6f, 0.03f, D * 0.6f), Darker(c));
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.4f, 0), new Vector3(0.03f, H * 0.4f, 0.03f), Metal);
            fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(0, H * 0.78f, 0), Vector3.one * Mathf.Min(W, D) * 0.45f, c).GetComponent<Renderer>());
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.88f, 0), new Vector3(W * 0.8f, H * 0.12f, D * 0.8f), Lighter(c));
            AddLight(t, new Vector3(0, H * 0.8f, 0), c, spec.surface == FurnitureSurface.Tabletop ? 3.5f : 5f, fx);
            if (spec.habitatSpecies.Length > 0) fx.orbitHost = true;
        }

        private static void FloorLamp(Transform t, float W, float D, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(W * 0.7f, 0.03f, D * 0.7f), Metal);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.4f, 0), new Vector3(0.04f, H * 0.4f, 0.04f), Metal);
            float s = Mathf.Max(0.3f, Mathf.Min(W, D) * 0.9f);
            fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(0, H - s * 0.3f, 0), Vector3.one * s, c).GetComponent<Renderer>());
            AddLight(t, new Vector3(0, H - s * 0.3f, 0), c, 6f, fx);
            fx.orbitHost = true;
        }

        private static void Chandelier(Transform t, float W, float D, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.75f, 0), new Vector3(0.03f, H * 0.25f, 0.03f), Gold);
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.4f, 0), new Vector3(W * 0.3f, 0.12f, D * 0.3f), Gold);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var p = new Vector3(Mathf.Cos(a) * W * 0.38f, H * 0.35f, Mathf.Sin(a) * D * 0.38f);
                P(t, PrimitiveType.Cylinder, (p + new Vector3(0, H * 0.4f, 0)) * 0.5f, new Vector3(0.02f, 0.1f, 0.02f), Gold);
                fx.glowParts.Add(G(t, PrimitiveType.Sphere, p, Vector3.one * 0.12f, new Color(1f, 0.95f, 0.75f)).GetComponent<Renderer>());
            }
            AddLight(t, new Vector3(0, H * 0.3f, 0), c, 9f, fx);
        }

        private static void Mobile(Transform t, float W, float D, float H, Color c, FurnitureFx fx)
        {
            var spin = PrimitiveBuilder.Empty("Spin", t, new Vector3(0, H * 0.6f, 0)).transform;
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.8f, 0), new Vector3(0.02f, H * 0.2f, 0.02f), Metal);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                var p = new Vector3(Mathf.Cos(a) * W * 0.35f, -0.1f - (i % 2) * 0.15f, Mathf.Sin(a) * D * 0.35f);
                var star = G(spin, PrimitiveType.Cube, p, Vector3.one * 0.12f, c);
                star.transform.localEulerAngles = new Vector3(45, 0, 45);
                fx.glowParts.Add(star.GetComponent<Renderer>());
            }
            fx.spinner = spin;
        }

        private static void Aquarium(Transform t, float W, float D, float H, Color c, BuildResult res, FurnitureFx fx)
        {
            float standH = H * 0.35f;
            P(t, PrimitiveType.Cube, new Vector3(0, standH * 0.5f, 0), new Vector3(W, standH, D), new Color(0.35f, 0.3f, 0.3f));
            float tankH = H - standH;
            P(t, PrimitiveType.Cube, new Vector3(0, standH + tankH * 0.5f, 0), new Vector3(W * 0.98f, tankH, D * 0.98f), Glass);
            P(t, PrimitiveType.Cube, new Vector3(0, standH + tankH * 0.45f, 0), new Vector3(W * 0.94f, tankH * 0.85f, D * 0.94f), WaterC);
            P(t, PrimitiveType.Cube, new Vector3(0, standH + 0.03f, 0), new Vector3(W * 0.94f, 0.05f, D * 0.94f), new Color(0.85f, 0.8f, 0.6f));
            for (int i = 0; i < 3; i++)
                P(t, PrimitiveType.Capsule, new Vector3((i - 1) * W * 0.3f, standH + 0.15f, -D * 0.25f), new Vector3(0.04f, 0.12f + i * 0.03f, 0.04f), new Color(0.3f, 0.7f, 0.4f));
            fx.swimVolume = new Bounds(new Vector3(0, standH + tankH * 0.45f, 0), new Vector3(W * 0.8f, tankH * 0.6f, D * 0.7f));
            fx.swimSpace = t;
            fx.bubbles = true;
            for (int i = 0; i < 8; i++) Anchor(t, fx.swimVolume.center, res);
        }

        private static void WaterOrb(Transform t, float W, float D, float H, BuildResult res, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(W * 0.6f, 0.05f, D * 0.6f), new Color(0.6f, 0.65f, 0.75f));
            float r = Mathf.Min(W, D) * 0.95f;
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.6f, 0), Vector3.one * r, WaterC);
            fx.swimVolume = new Bounds(new Vector3(0, H * 0.6f, 0), Vector3.one * r * 0.55f);
            fx.swimSpace = t;
            fx.bubbles = true;
            for (int i = 0; i < 6; i++) Anchor(t, fx.swimVolume.center, res);
        }

        private static void Plant(Transform t, float W, float D, float H, Color c, bool big)
        {
            var pot = new Color(0.8f, 0.45f, 0.3f);
            float potH = big ? 0.35f : 0.25f;
            P(t, PrimitiveType.Cylinder, new Vector3(0, potH * 0.5f, 0), new Vector3(W * 0.5f, potH * 0.5f, D * 0.5f), pot);
            int n = big ? 6 : 3;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.4f, r = (big ? 0.18f : 0.08f);
                float y = potH + (H - potH) * (0.35f + 0.6f * i / n);
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.one * (big ? 0.42f : 0.22f), Color.Lerp(c, new Color(0.2f, 0.5f, 0.25f), (i % 2) * 0.4f));
            }
        }

        private static void Tree(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.25f, 0), new Vector3(0.22f, H * 0.25f, 0.22f), new Color(0.45f, 0.3f, 0.2f));
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.65f, 0), new Vector3(W * 0.9f, H * 0.45f, D * 0.9f), c);
            P(t, PrimitiveType.Sphere, new Vector3(W * 0.2f, H * 0.82f, -D * 0.1f), new Vector3(W * 0.55f, H * 0.3f, D * 0.55f), Lighter(c));
        }

        private static void FlowerBed(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0.1f, 0), new Vector3(W, 0.2f, D), new Color(0.5f, 0.35f, 0.25f));
            P(t, PrimitiveType.Cube, new Vector3(0, 0.21f, 0), new Vector3(W * 0.9f, 0.02f, D * 0.9f), new Color(0.35f, 0.25f, 0.18f));
            int nx = Mathf.Max(2, Mathf.RoundToInt(W / 0.25f)), nz = Mathf.Max(2, Mathf.RoundToInt(D / 0.25f));
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                {
                    var p = new Vector3(-W * 0.4f + W * 0.8f * x / Mathf.Max(1, nx - 1), 0.32f, -D * 0.4f + D * 0.8f * z / Mathf.Max(1, nz - 1));
                    P(t, PrimitiveType.Cylinder, p - new Vector3(0, 0.06f, 0), new Vector3(0.02f, 0.06f, 0.02f), new Color(0.3f, 0.6f, 0.3f));
                    P(t, PrimitiveType.Sphere, p + new Vector3(0, 0.03f, 0), Vector3.one * 0.12f, Color.HSVToRGB(((x * 3 + z * 7) % 10) / 10f, 0.55f, 1f));
                }
        }

        private static void Rug(Transform t, float W, float D, float H, Color c, Color acc, FurnitureSpec spec, FurnitureFx fx)
        {
            P(t, spec.series == FurnitureSeries.Manmaru ? PrimitiveType.Cylinder : PrimitiveType.Cube, new Vector3(0, 0.012f, 0), new Vector3(W, 0.012f, D), c);
            P(t, PrimitiveType.Cube, new Vector3(0, 0.018f, 0), new Vector3(W * 0.82f, 0.008f, D * 0.82f), Lighter(c));
            if (spec.glowing)
                for (int i = 0; i < 12; i++)
                {
                    float x = Mathf.Sin(i * 3.1f) * W * 0.38f, z = Mathf.Cos(i * 2.3f) * D * 0.38f;
                    fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(x, 0.025f, z), new Vector3(0.06f, 0.01f, 0.06f), acc).GetComponent<Renderer>());
                }
            else
                for (int i = 1; i < 4; i++)
                    P(t, PrimitiveType.Cube, new Vector3(0, 0.024f, -D * 0.4f + D * 0.8f * i / 4f), new Vector3(W * 0.8f, 0.004f, 0.05f), acc);
        }

        private static void Painting(Transform t, float W, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.03f), new Vector3(W * 0.95f, H, 0.05f), new Color(0.55f, 0.4f, 0.25f));
            P(t, PrimitiveType.Cube, new Vector3(0, 0.05f, 0.06f), new Vector3(W * 0.8f, H * 0.65f, 0.01f), c);
            P(t, PrimitiveType.Sphere, new Vector3(-W * 0.15f, -H * 0.15f, 0.065f), new Vector3(W * 0.5f, H * 0.22f, 0.01f), new Color(0.4f, 0.7f, 0.4f));
            P(t, PrimitiveType.Sphere, new Vector3(W * 0.25f, H * 0.22f, 0.065f), Vector3.one * 0.08f, new Color(1f, 0.9f, 0.5f));
        }

        private static void Clock(Transform t, float W, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.06f), new Vector3(W * 0.6f, H, 0.12f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.25f, 0.13f), new Vector3(W * 0.45f, 0.01f, W * 0.45f), White, new Vector3(90, 0, 0));
            var hand = PrimitiveBuilder.Empty("Hand", t, new Vector3(0, H * 0.25f, 0.14f)).transform;
            P(hand, PrimitiveType.Cube, new Vector3(0, 0.07f, 0), new Vector3(0.015f, 0.14f, 0.01f), Color.black);
            fx.clockHand = hand;
            var pend = PrimitiveBuilder.Empty("Pendulum", t, new Vector3(0, H * 0.05f, 0.13f)).transform;
            P(pend, PrimitiveType.Cylinder, new Vector3(0, -0.15f, 0), new Vector3(0.015f, 0.15f, 0.015f), Gold);
            P(pend, PrimitiveType.Cylinder, new Vector3(0, -0.32f, 0), new Vector3(0.1f, 0.01f, 0.1f), Gold, new Vector3(90, 0, 0));
            fx.pendulum = pend;
        }

        private static void Window(Transform t, float W, float H, Color c, FurnitureFx fx, bool round)
        {
            if (round)
            {
                P(t, PrimitiveType.Cylinder, new Vector3(0, 0, 0.03f), new Vector3(W * 0.95f, 0.03f, W * 0.95f), new Color(0.9f, 0.9f, 0.95f), new Vector3(90, 0, 0));
                var pane = G(t, PrimitiveType.Cylinder, new Vector3(0, 0, 0.065f), new Vector3(W * 0.8f, 0.005f, W * 0.8f), c);
                pane.transform.localEulerAngles = new Vector3(90, 0, 0);
                fx.weatherPane = pane.GetComponent<Renderer>();
                return;
            }
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.03f), new Vector3(W, H, 0.06f), new Color(0.95f, 0.95f, 0.95f));
            var p2 = G(t, PrimitiveType.Cube, new Vector3(0, 0, 0.065f), new Vector3(W * 0.88f, H * 0.85f, 0.01f), c);
            fx.weatherPane = p2.GetComponent<Renderer>();
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.075f), new Vector3(0.04f, H * 0.85f, 0.01f), White);
            P(t, PrimitiveType.Cube, new Vector3(0, 0, 0.075f), new Vector3(W * 0.88f, 0.04f, 0.01f), White);
            P(t, PrimitiveType.Cube, new Vector3(0, -H * 0.5f - 0.03f, 0.1f), new Vector3(W, 0.05f, 0.18f), White);
        }

        private static void Curtain(Transform t, float W, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0.08f), new Vector3(0.03f, W * 0.5f, 0.03f), Gold, new Vector3(0, 0, 90));
            foreach (var sx in new[] { -1f, 1f })
            {
                var panel = PrimitiveBuilder.Empty("Panel", t, new Vector3(sx * W * 0.28f, 0, 0.1f)).transform;
                P(panel, PrimitiveType.Cube, Vector3.zero, new Vector3(W * 0.42f, H, 0.03f), c);
                for (int i = 0; i < 5; i++)
                    fx.nightGlowParts.Add(G(panel, PrimitiveType.Sphere, new Vector3(Mathf.Sin(i * 2f) * W * 0.12f, -H * 0.4f + i * H * 0.2f, 0.02f), Vector3.one * 0.07f, new Color(0.7f, 1f, 0.85f)).GetComponent<Renderer>());
                fx.swayParts.Add(panel);
            }
        }

        private static void Piano(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, -D * 0.15f), new Vector3(W, H, D * 0.6f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.55f, D * 0.2f), new Vector3(W * 0.95f, 0.06f, D * 0.25f), White);
            for (int i = 0; i < 10; i++)
                P(t, PrimitiveType.Cube, new Vector3(-W * 0.42f + i * W * 0.093f, H * 0.6f, D * 0.17f), new Vector3(0.04f, 0.03f, D * 0.14f), Color.black);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.75f, D * 0.22f), new Vector3(W * 0.9f, 0.05f, D * 0.2f), White);
            for (int i = 0; i < 2; i++) Legs(t, W, D * 0.6f, H * 0.5f, Darker(c));
        }

        private static void Harp(Transform t, float W, float D, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(W * 0.8f, 0.1f, D * 0.8f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(-W * 0.3f, H * 0.5f, 0), new Vector3(0.06f, H * 0.5f, 0.06f), c);
            P(t, PrimitiveType.Cube, new Vector3(0.02f, H * 0.95f, 0), new Vector3(W * 0.75f, 0.06f, 0.06f), c, new Vector3(0, 0, -12));
            P(t, PrimitiveType.Cylinder, new Vector3(W * 0.3f, H * 0.55f, 0), new Vector3(0.05f, H * 0.42f, 0.05f), c, new Vector3(0, 0, 18));
            for (int i = 0; i < 6; i++)
            {
                float x = -W * 0.22f + i * W * 0.085f;
                fx.glowParts.Add(G(t, PrimitiveType.Cylinder, new Vector3(x, H * 0.5f + i * 0.02f, 0), new Vector3(0.008f, H * 0.38f, 0.008f), new Color(1f, 0.95f, 0.7f)).GetComponent<Renderer>());
            }
        }

        private static void Xylophone(Transform t, float W, float D, float H, Color c)
        {
            Legs(t, W * 0.9f, D * 0.6f, H * 0.8f, Darker(c), 0.04f, 0.04f);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.8f, 0), new Vector3(W * 0.9f, 0.04f, D * 0.6f), c);
            for (int i = 0; i < 8; i++)
                P(t, PrimitiveType.Cube, new Vector3(-W * 0.38f + i * W * 0.11f, H * 0.85f, 0), new Vector3(W * 0.08f, 0.03f, D * (0.6f - i * 0.04f)), Color.HSVToRGB(i / 8f, 0.55f, 1f));
        }

        private static void Drum(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.8f, H * 0.5f, D * 0.8f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H + 0.005f, 0), new Vector3(W * 0.82f, 0.01f, D * 0.82f), new Color(0.95f, 0.9f, 0.8f));
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.84f, 0.03f, D * 0.84f), Gold);
        }

        private static void Gramophone(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0.15f, 0), new Vector3(W * 0.7f, 0.3f, D * 0.7f), new Color(0.45f, 0.3f, 0.2f));
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.31f, 0), new Vector3(W * 0.55f, 0.01f, D * 0.55f), Color.black);
            for (int i = 0; i < 5; i++)
            {
                float s = 0.06f + i * 0.08f;
                P(t, PrimitiveType.Cylinder, new Vector3(0, 0.4f + i * 0.12f, -D * 0.1f + i * 0.06f), new Vector3(s, 0.06f, s), c, new Vector3(-25, 0, 0));
            }
        }

        private static void Ball(Transform t, float W, float H, Color c)
        {
            float s = Mathf.Max(0.2f, H);
            P(t, PrimitiveType.Sphere, new Vector3(0, s * 0.5f, 0), Vector3.one * s, c);
            P(t, PrimitiveType.Sphere, new Vector3(0, s * 0.5f, 0), new Vector3(s * 1.02f, s * 0.2f, s * 1.02f), White);
        }

        private static void Blocks(Transform t, float W, float D)
        {
            P(t, PrimitiveType.Cube, new Vector3(-0.06f, 0.06f, 0), Vector3.one * 0.12f, new Color(1f, 0.5f, 0.5f));
            P(t, PrimitiveType.Cube, new Vector3(0.07f, 0.06f, 0.03f), Vector3.one * 0.12f, new Color(0.5f, 0.7f, 1f));
            P(t, PrimitiveType.Cube, new Vector3(0, 0.18f, 0.01f), Vector3.one * 0.12f, new Color(1f, 0.9f, 0.4f), new Vector3(0, 20, 0));
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.28f, 0), new Vector3(0.1f, 0.04f, 0.1f), new Color(0.5f, 0.9f, 0.6f));
        }

        private static void Bench(Transform t, float W, float D, float H, Color c, BuildResult res)
        {
            for (int i = 0; i < 3; i++)
                P(t, PrimitiveType.Cube, new Vector3(0, 0.45f, -D * 0.25f + i * D * 0.22f), new Vector3(W, 0.05f, D * 0.18f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H - 0.05f, -D * 0.42f), new Vector3(W, 0.25f, 0.05f), c);
            foreach (var sx in new[] { -1f, 1f })
                P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.42f, 0.22f, 0), new Vector3(0.06f, 0.45f, D * 0.8f), new Color(0.3f, 0.3f, 0.32f));
            Seat(t, new Vector3(0, 0.48f, 0), res);
        }

        private static void Fence(Transform t, float W, float D, float H, Color c)
        {
            foreach (var sx in new[] { -1f, 0f, 1f })
                P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.45f, H * 0.5f, 0), new Vector3(0.08f, H, 0.08f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.75f, 0), new Vector3(W, 0.06f, 0.04f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.35f, 0), new Vector3(W, 0.06f, 0.04f), c);
        }

        private static void PathStones(Transform t, float W, float D, Color c)
        {
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3((i % 2 == 0 ? -1 : 1) * W * 0.22f, 0.02f, (i < 2 ? -1 : 1) * D * 0.22f);
                P(t, PrimitiveType.Cylinder, p, new Vector3(W * 0.42f, 0.02f, D * 0.38f), Color.Lerp(c, Color.white, (i % 3) * 0.1f));
            }
        }

        private static void Pond(Transform t, float W, float D, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(W, 0.03f, D), new Color(0.35f, 0.3f, 0.25f));
            var w = G(t, PrimitiveType.Cylinder, new Vector3(0, 0.07f, 0), new Vector3(W * 0.85f, 0.01f, D * 0.85f), new Color(0.35f, 0.65f, 0.95f));
            fx.ripple = w.transform;
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5f;
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * W * 0.47f, 0.08f, Mathf.Sin(a) * D * 0.47f), new Vector3(0.25f, 0.15f, 0.22f), new Color(0.6f, 0.6f, 0.62f));
            }
        }

        private static void Streetlight(Transform t, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(0.08f, H * 0.5f, 0.08f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H, 0), new Vector3(0.35f, 0.05f, 0.35f), c);
            fx.glowParts.Add(G(t, PrimitiveType.Sphere, new Vector3(0, H - 0.12f, 0), Vector3.one * 0.28f, new Color(1f, 0.92f, 0.65f)).GetComponent<Renderer>());
            AddLight(t, new Vector3(0, H - 0.2f, 0), new Color(1f, 0.9f, 0.6f), 9f, fx);
            fx.orbitHost = true;
            fx.onlyAtNight = true;
        }

        private static void Sign(Transform t, float W, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.35f, 0), new Vector3(0.06f, H * 0.35f, 0.06f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.78f, 0), new Vector3(W * 0.9f, H * 0.4f, 0.06f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.78f, 0.035f), new Vector3(W * 0.7f, H * 0.06f, 0.01f), Darker(c));
        }

        private static void Rocks(Transform t, float W, float D, float H, Color c)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(W * D * 6f), 3, 8);
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.2f, r = (i % 3) * 0.15f * W;
                float s = (0.5f - (i % 3) * 0.1f) * Mathf.Min(W, D);
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, s * 0.4f + (i == n - 1 ? H * 0.5f : 0), Mathf.Sin(a) * r), new Vector3(s, s * 0.75f, s * 0.9f), Color.Lerp(c, Color.white, (i % 4) * 0.08f));
            }
        }

        private static void Perch(Transform t, float W, float D, float H, Color c, BuildResult res)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.04f, 0), new Vector3(W * 0.6f, 0.04f, D * 0.6f), Darker(c));
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(0.05f, H * 0.5f, 0.05f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H, 0), new Vector3(0.04f, W * 0.4f, 0.04f), c, new Vector3(0, 0, 90));
            P(t, PrimitiveType.Sphere, new Vector3(W * 0.25f, H + 0.08f, 0), new Vector3(0.12f, 0.08f, 0.12f), new Color(0.4f, 0.75f, 0.4f));
            Anchor(t, new Vector3(-W * 0.15f, H + 0.12f, 0), res);
        }

        private static void Mirror(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.9f, H, 0.08f), Gold);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.52f, 0.045f), new Vector3(W * 0.78f, H * 0.88f, 0.01f), new Color(0.85f, 0.95f, 1f));
            foreach (var sx in new[] { -1f, 1f })
                P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.3f, 0.05f, 0), new Vector3(0.08f, 0.1f, D * 0.8f), Gold);
        }

        private static void Swing(Transform t, float W, float D, float H, Color c, BuildResult res, FurnitureFx fx)
        {
            float top = 2.0f;
            foreach (var sx in new[] { -1f, 1f })
                P(t, PrimitiveType.Cylinder, new Vector3(sx * W * 0.45f, top * 0.5f, 0), new Vector3(0.05f, top * 0.5f, 0.05f), Darker(c));
            P(t, PrimitiveType.Cylinder, new Vector3(0, top, 0), new Vector3(0.05f, W * 0.48f, 0.05f), Darker(c), new Vector3(0, 0, 90));
            var pivot = PrimitiveBuilder.Empty("SwingPivot", t, new Vector3(0, top, 0)).transform;
            foreach (var sx in new[] { -1f, 1f })
                P(pivot, PrimitiveType.Cylinder, new Vector3(sx * W * 0.25f, -(top - H) * 0.5f, 0), new Vector3(0.015f, (top - H) * 0.5f, 0.015f), White);
            P(pivot, PrimitiveType.Cube, new Vector3(0, -(top - H), 0), new Vector3(W * 0.6f, 0.05f, D * 0.4f), c);
            fx.swing = pivot;
            Seat(pivot, new Vector3(0, -(top - H) + 0.05f, 0), res);
        }

        private static void Pedestal(Transform t, float W, float D, float H, Color c, BuildResult res, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(W * 0.7f, 0.1f, D * 0.7f), Darker(c));
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.35f, H * 0.5f, D * 0.35f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H, 0), new Vector3(W * 0.6f, 0.05f, D * 0.6f), Lighter(c));
            Anchor(t, new Vector3(0, H + 0.18f, 0), res);
            Anchor(t, new Vector3(0.12f, H + 0.18f, 0.08f), res);
            fx.spinAnchors = true;
        }

        private static void Vase(Transform t, float W, float H, Color c, BuildResult res)
        {
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.25f, 0), new Vector3(W * 0.45f, H * 0.5f, W * 0.45f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.55f, 0), new Vector3(W * 0.2f, H * 0.1f, W * 0.2f), c);
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3((i - 1) * 0.06f, H * 0.85f + (i % 2) * 0.05f, 0);
                P(t, PrimitiveType.Cylinder, p - new Vector3(0, 0.12f, 0), new Vector3(0.01f, 0.12f, 0.01f), new Color(0.3f, 0.6f, 0.3f));
                P(t, PrimitiveType.Sphere, p, Vector3.one * 0.08f, Color.HSVToRGB(i * 0.3f, 0.5f, 1f));
            }
            Anchor(t, new Vector3(0, H * 0.9f, 0), res);
        }

        private static void Photo(Transform t, float W, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.6f, H, 0.03f), c, new Vector3(-12, 0, 0));
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.52f, 0.02f), new Vector3(W * 0.45f, H * 0.75f, 0.005f), new Color(0.7f, 0.85f, 1f), new Vector3(-12, 0, 0));
        }

        private static void Figurine(Transform t, float W, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(W * 0.4f, 0.02f, W * 0.4f), new Color(0.5f, 0.4f, 0.3f));
            P(t, PrimitiveType.Sphere, new Vector3(0, H * 0.5f, 0), Vector3.one * H * 0.8f, c);
            P(t, PrimitiveType.Sphere, new Vector3(-0.03f, H * 0.6f, H * 0.36f), Vector3.one * 0.03f, Color.black);
            P(t, PrimitiveType.Sphere, new Vector3(0.03f, H * 0.6f, H * 0.36f), Vector3.one * 0.03f, Color.black);
            P(t, PrimitiveType.Cylinder, new Vector3(0, H, 0), new Vector3(0.01f, 0.06f, 0.01f), c);
        }

        private static void Playground(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(W, 0.03f, D), Lighter(c));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * W * 0.45f, 0.12f, Mathf.Sin(a) * D * 0.45f), new Vector3(0.25f, 0.2f, 0.25f), c);
            }
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.4f, 0), new Vector3(W * 0.35f, 0.06f, D * 0.6f), Color.HSVToRGB(0.55f, 0.5f, 1f), new Vector3(20, 0, 0));
            P(t, PrimitiveType.Sphere, new Vector3(W * 0.2f, 0.15f, -D * 0.2f), Vector3.one * 0.18f, new Color(1f, 0.5f, 0.5f));
        }

        private static void Nest(Transform t, float W, float D, float H, Color c, BuildResult res)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(W * 0.85f, 0.05f, D * 0.85f), Lighter(c));
            int n = 10;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * W * 0.4f, H * 0.5f, Mathf.Sin(a) * D * 0.4f), new Vector3(W * 0.28f, H, D * 0.28f), c);
            }
            Seat(t, new Vector3(0, H * 0.6f, 0), res);
        }

        private static void WindChime(Transform t, float H, Color c, FurnitureFx fx)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.5f, 0), new Vector3(0.04f, H * 0.5f, 0.04f), new Color(0.4f, 0.32f, 0.25f));
            P(t, PrimitiveType.Cylinder, new Vector3(0.15f, H, 0), new Vector3(0.03f, 0.15f, 0.03f), new Color(0.4f, 0.32f, 0.25f), new Vector3(0, 0, 90));
            var hang = PrimitiveBuilder.Empty("Chime", t, new Vector3(0.28f, H - 0.02f, 0)).transform;
            for (int i = 0; i < 5; i++)
                fx.glowParts.Add(G(hang, PrimitiveType.Cylinder, new Vector3((i - 2) * 0.04f, -0.12f - (i % 2) * 0.04f, 0), new Vector3(0.015f, 0.1f, 0.015f), c).GetComponent<Renderer>());
            fx.swayParts.Add(hang);
        }

        private static void SoundTiles(Transform t, float W, float D)
        {
            int n = 4;
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    P(t, PrimitiveType.Cube, new Vector3(-W * 0.5f + W * (x + 0.5f) / n, 0.02f, -D * 0.5f + D * (z + 0.5f) / n), new Vector3(W / n * 0.92f, 0.04f, D / n * 0.92f), Color.HSVToRGB(((x + z * n) % 8) / 8f, 0.35f, 1f));
        }

        private static void Birdhouse(Transform t, float H, Color c)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.4f, 0), new Vector3(0.05f, H * 0.4f, 0.05f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.85f, 0), new Vector3(0.3f, 0.3f, 0.3f), c);
            P(t, PrimitiveType.Cube, new Vector3(0, H, 0), new Vector3(0.4f, 0.06f, 0.4f), new Color(0.75f, 0.35f, 0.3f), new Vector3(0, 0, 0));
            P(t, PrimitiveType.Cylinder, new Vector3(0, H * 0.87f, 0.15f), new Vector3(0.09f, 0.01f, 0.09f), Color.black, new Vector3(90, 0, 0));
        }

        private static void ToolRack(Transform t, float W, float H, Color c, BuildResult res, FurnitureSpec spec)
        {
            bool wall = spec.surface == FurnitureSurface.Wall;
            float baseY = wall ? 0f : H * 0.5f;
            if (!wall)
                foreach (var sx in new[] { -1f, 1f })
                    P(t, PrimitiveType.Cube, new Vector3(sx * W * 0.45f, H * 0.5f, 0), new Vector3(0.06f, H, 0.2f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(0, baseY, wall ? 0.02f : 0f), new Vector3(W * 0.9f, wall ? H : H * 0.6f, 0.04f), c);
            int slots = Mathf.Max(1, spec.displaySlots);
            for (int i = 0; i < slots; i++)
            {
                float x = -W * 0.35f + W * 0.7f * (slots == 1 ? 0.5f : i / (float)(slots - 1));
                P(t, PrimitiveType.Cylinder, new Vector3(x, baseY + 0.15f, wall ? 0.08f : 0.06f), new Vector3(0.02f, 0.05f, 0.02f), Metal, new Vector3(90, 0, 0));
                Anchor(t, new Vector3(x, baseY, wall ? 0.12f : 0.1f), res);
            }
        }

        private static void Workbench(Transform t, float W, float D, float H, Color c)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, H - 0.05f, 0), new Vector3(W, 0.1f, D), c);
            Legs(t, W, D, H - 0.1f, Darker(c), 0.08f, 0.09f);
            P(t, PrimitiveType.Cube, new Vector3(0, H * 0.3f, 0), new Vector3(W * 0.9f, 0.04f, D * 0.85f), Darker(c));
            P(t, PrimitiveType.Cube, new Vector3(W * 0.35f, H + 0.06f, D * 0.3f), new Vector3(0.15f, 0.12f, 0.1f), Metal);
        }

        private static void Generic(Transform t, ItemDefinition def, float W, float D, float H, Color c)
        {
            var shape = def.Shape;
            if (shape == PrimitiveType.Plane || shape == PrimitiveType.Quad) shape = PrimitiveType.Cube;
            P(t, shape, new Vector3(0, H * 0.5f, 0), new Vector3(W * 0.9f, H, D * 0.9f), c);
            if (def.HasTag(ItemTags.Kitchen))
            {
                // 調理器具（Cooking モジュール登録）：天板と取っ手で「台」らしく
                P(t, PrimitiveType.Cube, new Vector3(0, H + 0.02f, 0), new Vector3(W * 0.95f, 0.04f, D * 0.95f), Lighter(c));
                P(t, PrimitiveType.Cube, new Vector3(0, H * 0.6f, D * 0.46f), new Vector3(W * 0.5f, 0.04f, 0.04f), Metal);
            }
        }

        // ------------------------------------------------------------
        // 飾った物の見た目
        // ------------------------------------------------------------

        public static void BuildDisplayed(BuildResult res, FurnitureSpec spec, List<ItemStack> displayed)
        {
            if (res == null || res.Fx == null) return;
            foreach (var old in res.Fx.displayVisuals) if (old != null) Object.Destroy(old.gameObject);
            res.Fx.displayVisuals.Clear();
            res.Fx.fish.Clear();
            if (displayed == null) return;
            for (int i = 0; i < displayed.Count; i++)
            {
                var s = displayed[i];
                if (s == null || s.IsEmpty) continue;
                var def = s.Definition;
                Transform anchor = res.DisplayAnchors.Count > 0 ? res.DisplayAnchors[i % res.DisplayAnchors.Count] : res.Root.transform;
                bool fish = spec.displayKind == DisplayKind.Fish || spec.use == FurnitureUse.Aquarium;
                GameObject go;
                if (fish)
                {
                    float size = Mathf.Clamp(s.size > 0 ? s.size / 100f : 0.18f, 0.1f, 0.35f);
                    go = PrimitiveBuilder.Part(anchor, PrimitiveType.Capsule, Vector3.zero, new Vector3(size * 0.45f, size * 0.5f, size * 0.25f), def.Color, "Fish", new Vector3(0, 0, 90));
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, -0.9f, 0), new Vector3(0.8f, 0.4f, 0.3f), Darker(def.Color), "Tail");
                    if (res.Fx.swimSpace != null) res.Fx.fish.Add(go.transform);
                }
                else if (spec.displayKind == DisplayKind.Food)
                {
                    go = PrimitiveBuilder.Part(anchor, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.24f, 0.01f, 0.24f), White, "Plate");
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Sphere, new Vector3(0, 4f, 0), new Vector3(0.6f, 6f, 0.6f), def.Color, "Food");
                }
                else
                {
                    var shape = def.Shape == PrimitiveType.Plane || def.Shape == PrimitiveType.Quad ? PrimitiveType.Cube : def.Shape;
                    go = def.HasTag(ItemTags.Glowing) || def.HasTag(ItemTags.Crystal) || def.Category == ItemCategory.Gem
                        ? PrimitiveBuilder.EmissivePart(anchor, shape, Vector3.zero, Vector3.one * 0.16f, def.Color, "Displayed")
                        : PrimitiveBuilder.Part(anchor, shape, Vector3.zero, Vector3.one * 0.16f, def.Color, "Displayed");
                    if (anchor == res.Root.transform) go.transform.localPosition = new Vector3(0, spec.height + 0.1f, 0);
                }
                res.Fx.displayVisuals.Add(go.transform);
                if (res.Fx.colorFromDisplay != null)
                {
                    var r = res.Fx.colorFromDisplay;
                    r.sharedMaterial = MaterialCache.GetEmissive(def.Color);
                    foreach (var l in res.Fx.lights) l.color = Color.Lerp(def.Color, Color.white, 0.3f);
                }
            }
        }

        /// <summary>配置中の「影」用に、全部のレンダラーを半透明の色に置き換える。</summary>
        public static void MakeGhost(GameObject root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var baseC = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color") ? r.sharedMaterial.color : Color.white;
                var c = Color.Lerp(baseC, tint, 0.45f);
                c.a = 0.5f;
                r.sharedMaterial = MaterialCache.Get(c);
            }
            foreach (var l in root.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var col in root.GetComponentsInChildren<Collider>()) Object.Destroy(col);
        }
    }

    /// <summary>
    /// 家具の小さな動き（浮遊・揺れ・魚が泳ぐ・灯りの点灯・お天気窓・夜だけ光る花）。見た目だけの揺らぎなので UnityEngine.Random 可。
    /// </summary>
    public class FurnitureFx : MonoBehaviour
    {
        public Transform floatBody, wobbleBody, shapeBody, rockBody, spinner, pendulum, clockHand, swing, ripple, swimSpace;
        public Bounds swimVolume;
        public bool bubbles, spinAnchors, orbitHost, onlyAtNight;
        public readonly List<Renderer> glowParts = new List<Renderer>();
        public readonly List<Renderer> nightGlowParts = new List<Renderer>();
        public readonly List<Light> lights = new List<Light>();
        public readonly List<Transform> swayParts = new List<Transform>();
        public readonly List<Transform> displayVisuals = new List<Transform>();
        public readonly List<Transform> fish = new List<Transform>();
        public Renderer weatherPane;
        public Renderer colorFromDisplay;

        /// <summary>座っている・使っている（ぷるぷる揺れる、形が変わる）。</summary>
        public float occupiedUntil;
        /// <summary>かたちあわせソファ：座る住民の系統（-1=なし）。</summary>
        public int occupantLineage = -1;
        public bool lightOn = true;

        private readonly Dictionary<Renderer, Color> glowColors = new Dictionary<Renderer, Color>();
        private float seed;
        private Vector3 shapeBaseScale = Vector3.one;
        private bool lastEmissive = true;
        private float weatherTimer;

        private void Start()
        {
            seed = Random.Range(0f, 100f);
            foreach (var r in glowParts) if (r != null && r.sharedMaterial != null) glowColors[r] = BaseColor(r);
            foreach (var r in nightGlowParts) if (r != null && r.sharedMaterial != null) glowColors[r] = BaseColor(r);
            if (shapeBody != null) shapeBaseScale = shapeBody.localScale;
            ApplyLight(true);
        }

        private static Color BaseColor(Renderer r)
        {
            var m = r.sharedMaterial;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public void SetLight(bool on)
        {
            lightOn = on;
            ApplyLight(true);
        }

        private void ApplyLight(bool force)
        {
            bool night = Game.Instance != null && Game.Instance.Time != null && Game.Instance.Time.IsNight;
            bool on = lightOn && (!onlyAtNight || night);
            foreach (var l in lights) if (l != null) l.enabled = on;
            if (force || on != lastEmissive)
            {
                foreach (var r in glowParts)
                {
                    if (r == null || !glowColors.TryGetValue(r, out var c)) continue;
                    r.sharedMaterial = on ? MaterialCache.GetEmissive(c) : MaterialCache.Get(c);
                }
                lastEmissive = on;
            }
            foreach (var r in nightGlowParts)
            {
                if (r == null || !glowColors.TryGetValue(r, out var c)) continue;
                r.sharedMaterial = night ? MaterialCache.GetEmissive(c) : MaterialCache.Get(Color.Lerp(c, Color.white, 0.3f));
            }
        }

        private void Update()
        {
            float t = UnityEngine.Time.time + seed;
            float dt = UnityEngine.Time.deltaTime;
            if (floatBody != null) floatBody.localPosition = new Vector3(0f, 0.25f + Mathf.Sin(t * 1.3f) * 0.06f, 0f);
            if (spinner != null) spinner.Rotate(0f, 25f * dt, 0f);
            if (pendulum != null) pendulum.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 3f) * 18f);
            if (clockHand != null && Game.Instance != null && Game.Instance.Time != null)
                clockHand.localRotation = Quaternion.Euler(0, 0, -Game.Instance.Time.HourFloat * 30f);
            if (swing != null) swing.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.4f) * (occupiedUntil > UnityEngine.Time.time ? 12f : 4f), 0, 0);
            if (rockBody != null) rockBody.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.6f) * (occupiedUntil > UnityEngine.Time.time ? 6f : 1.5f), 0, 0);
            if (ripple != null) ripple.localScale = new Vector3(ripple.localScale.x, 0.01f + Mathf.Abs(Mathf.Sin(t)) * 0.004f, ripple.localScale.z);
            foreach (var s in swayParts) if (s != null) s.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.1f) * 3f);
            if (spinAnchors) foreach (var d in displayVisuals) if (d != null) d.Rotate(0, 40f * dt, 0, Space.World);
            if (wobbleBody != null)
            {
                bool busy = occupiedUntil > UnityEngine.Time.time;
                float k = busy ? Mathf.Sin(t * 9f) * 0.06f : Mathf.Sin(t * 2f) * 0.01f;
                wobbleBody.localScale = new Vector3(1f + k, 1f - k * (busy ? 1.6f : 1f), 1f + k);
            }
            if (shapeBody != null)
            {
                bool busy = occupiedUntil > UnityEngine.Time.time;
                Vector3 target = shapeBaseScale;
                if (busy)
                {
                    switch ((AlienLineage)Mathf.Max(0, occupantLineage))
                    {
                        case AlienLineage.Round: target = Vector3.Scale(shapeBaseScale, new Vector3(1.05f, 0.8f, 1.15f)); break;
                        case AlienLineage.MultiArm: target = Vector3.Scale(shapeBaseScale, new Vector3(1.2f, 1f, 1f)); break;
                        case AlienLineage.ChibiAnimal: target = Vector3.Scale(shapeBaseScale, new Vector3(1f, 0.7f, 1.25f)); break;
                        case AlienLineage.Floating: target = Vector3.Scale(shapeBaseScale, new Vector3(0.9f, 1.15f, 0.9f)); break;
                        default: target = Vector3.Scale(shapeBaseScale, new Vector3(1.1f, 0.9f, 1.1f)); break;
                    }
                }
                shapeBody.localScale = Vector3.Lerp(shapeBody.localScale, target, dt * 3f);
            }
            for (int i = 0; i < fish.Count; i++)
            {
                var f = fish[i];
                if (f == null || swimSpace == null) continue;
                float ph = t * (0.5f + i * 0.13f) + i * 1.7f;
                var c = swimVolume.center;
                var e = swimVolume.extents;
                var p = c + new Vector3(Mathf.Sin(ph) * e.x, Mathf.Sin(ph * 1.7f) * e.y * 0.5f, Mathf.Cos(ph * 0.8f) * e.z);
                var world = swimSpace.TransformPoint(p);
                var dir = world - f.position;
                f.position = world;
                if (dir.sqrMagnitude > 0.00001f) f.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, 90) * Quaternion.Euler(90, 0, 0);
            }
            weatherTimer -= dt;
            if (weatherTimer <= 0f)
            {
                weatherTimer = 1f;
                ApplyLight(false);
                if (weatherPane != null) UpdateWeatherPane();
            }
        }

        private void UpdateWeatherPane()
        {
            var g = Game.Instance;
            if (g == null) return;
            var weather = g.Service<IWeatherService>();
            float day = weather != null ? weather.Daylight : (g.Time != null && g.Time.IsNight ? 0.1f : 1f);
            var w = weather != null ? weather.Current : WeatherState.Fine;
            Color sky = Color.Lerp(new Color(0.08f, 0.1f, 0.25f), new Color(0.55f, 0.78f, 1f), day);
            if (w.IsRaining) sky = Color.Lerp(sky, new Color(0.45f, 0.5f, 0.6f), 0.6f);
            if (w.IsFoggy) sky = Color.Lerp(sky, Color.white, 0.4f);
            if (w.Has(AlienPhenomenon.LightRain)) sky = Color.Lerp(sky, new Color(0.5f, 1f, 0.9f), 0.5f + Mathf.Sin(UnityEngine.Time.time * 3f) * 0.2f);
            if (w.Has(AlienPhenomenon.StarWind)) sky = Color.Lerp(sky, new Color(1f, 0.95f, 0.7f), 0.35f);
            if (w.Has(AlienPhenomenon.ReverseRain)) sky = Color.Lerp(sky, new Color(0.6f, 0.7f, 1f), 0.45f);
            if (w.Has(AlienPhenomenon.StarBand)) sky = Color.Lerp(sky, new Color(0.8f, 0.6f, 1f), 0.4f);
            if (w.Has(AlienPhenomenon.BloomTide)) sky = Color.Lerp(sky, new Color(1f, 0.7f, 0.85f), 0.35f);
            weatherPane.sharedMaterial = MaterialCache.GetEmissive(Quantize(sky));
        }

        private static Color Quantize(Color c)
        {
            // マテリアルのキャッシュが増えすぎないよう色を丸める
            return new Color(Mathf.Round(c.r * 16f) / 16f, Mathf.Round(c.g * 16f) / 16f, Mathf.Round(c.b * 16f) / 16f, 1f);
        }
    }

    /// <summary>
    /// 置いた家具の [E]（家具仕様 §6：使う）。Shift+E で「調べる」（思い出・詳細）。
    /// </summary>
    public class FurnitureObject : MonoBehaviour, IInteractable
    {
        public HousingSystem System;
        public string InstanceId;
        public string ItemId;
        /// <summary>住民の家の家具（眺めるだけ）。</summary>
        public string ResidentHomeOwner;
        public FurnitureVisuals.BuildResult Visual;

        public ItemDefinition Def => ItemRegistry.GetOrPlaceholder(ItemId);

        public string GetInteractionPrompt(PlayerState player)
        {
            if (System == null) return null;
            return System.GetFurniturePrompt(this, player);
        }

        public bool CanInteract(PlayerState player) => System != null;

        public void Interact(PlayerState player)
        {
            if (System == null) return;
            if (GameInput.Run && string.IsNullOrEmpty(ResidentHomeOwner)) System.OpenFurnitureDetail(InstanceId, player);
            else System.UseFurniture(this, player);
        }
    }
}
