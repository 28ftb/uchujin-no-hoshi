using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 村の共用調理場（§2.1）の見た目と [E] 操作。プリミティブで作る：
    /// 調理台・住民が用意したかまど（温かく光る）・水場・共用の干し棚・発酵樽・看板代わりの鍋。
    /// </summary>
    public sealed class CommunityKitchenView : MonoBehaviour
    {
        private Transform rackRoot;
        private readonly List<GameObject> rackItems = new List<GameObject>();
        private string facilityId;
        private CookingSystem sys;

        public static CommunityKitchenView Build(CookingSystem sys, Transform parent, Vector3 position, float yaw, string facilityId)
        {
            var root = PrimitiveBuilder.Empty("CommunityKitchen", parent, position);
            root.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
            var view = root.AddComponent<CommunityKitchenView>();
            view.sys = sys;
            view.facilityId = facilityId;
            var wood = new Color(0.72f, 0.55f, 0.38f);
            var stone = new Color(0.62f, 0.6f, 0.6f);

            // 調理台（[E] で調理画面）
            var counter = PrimitiveBuilder.Create(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(2.4f, 1f, 0.9f), wood, "Counter");
            PrimitiveBuilder.Part(counter.transform, PrimitiveType.Cube, new Vector3(0f, 0.52f, 0f), new Vector3(1.04f, 0.06f, 1.08f), new Color(0.9f, 0.88f, 0.8f), "Top");
            // 鍋とまな板
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cylinder, new Vector3(-0.6f, 1.15f, 0f), new Vector3(0.45f, 0.15f, 0.45f), new Color(0.35f, 0.38f, 0.45f), "Pot");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0.5f, 1.04f, 0f), new Vector3(0.6f, 0.04f, 0.4f), new Color(0.9f, 0.75f, 0.5f), "Board");
            SimpleInteractable.Attach(counter, "料理する", "Cook", p => view.Open(p));

            // かまど（共用の火：生物がいなくても温められる）
            var hearth = PrimitiveBuilder.Create(PrimitiveType.Cube, root.transform, new Vector3(-2.1f, 0.55f, 0f), new Vector3(1f, 1.1f, 1f), stone, "Hearth");
            PrimitiveBuilder.EmissivePart(hearth.transform, PrimitiveType.Sphere, new Vector3(0f, 0.15f, 0.42f), new Vector3(0.45f, 0.35f, 0.2f), new Color(1f, 0.55f, 0.2f), "Fire");
            PrimitiveBuilder.Part(hearth.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(0.25f, 0.5f, 0.25f), stone * 0.8f, "Chimney");
            SimpleInteractable.Attach(hearth, "料理する", "Cook", p => view.Open(p));

            // 水場
            var basin = PrimitiveBuilder.Create(PrimitiveType.Cylinder, root.transform, new Vector3(2.0f, 0.35f, 0f), new Vector3(0.9f, 0.35f, 0.9f), stone, "Basin");
            PrimitiveBuilder.Part(basin.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0f), new Vector3(0.85f, 0.1f, 0.85f), new Color(0.45f, 0.75f, 1f, 0.85f), "Water");
            SimpleInteractable.Attach(basin, "料理する", "Cook", p => view.Open(p));

            // 共用の干し棚と発酵樽（時間の加工）
            var rack = PrimitiveBuilder.Create(PrimitiveType.Cube, root.transform, new Vector3(-0.8f, 0.9f, -1.6f), new Vector3(1.6f, 0.08f, 0.5f), wood, "Rack");
            PrimitiveBuilder.Part(rack.transform, PrimitiveType.Cube, new Vector3(-0.47f, -5f, 0f), new Vector3(0.05f, 10f, 0.2f), wood * 0.8f, "LegL");
            PrimitiveBuilder.Part(rack.transform, PrimitiveType.Cube, new Vector3(0.47f, -5f, 0f), new Vector3(0.05f, 10f, 0.2f), wood * 0.8f, "LegR");
            view.rackRoot = rack.transform;
            SimpleInteractable.Attach(rack, "干し棚を見る", "Check the rack", p => view.Open(p));
            var barrel = PrimitiveBuilder.Create(PrimitiveType.Cylinder, root.transform, new Vector3(0.9f, 0.5f, -1.6f), new Vector3(0.7f, 0.5f, 0.7f), new Color(0.55f, 0.38f, 0.25f), "Barrel");
            PrimitiveBuilder.Part(barrel.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(1.05f, 0.08f, 1.05f), new Color(0.4f, 0.3f, 0.2f), "Lid");
            SimpleInteractable.Attach(barrel, "樽をのぞく", "Peek in the barrel", p => view.Open(p));

            // 屋根（日よけ）
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 2.6f, -0.4f), new Vector3(6.2f, 0.12f, 3.2f), new Color(0.85f, 0.45f, 0.4f), "Awning");
            foreach (var x in new[] { -3f, 3f })
                foreach (var z in new[] { 1f, -1.8f })
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Cylinder, new Vector3(x, 1.3f, z), new Vector3(0.12f, 1.3f, 0.12f), wood * 0.85f, "Post");

            WorldLabels.Add(root.transform, () => Loc.T("共用調理場", "Community kitchen"), new Vector3(0f, 3.2f, 0f), new Color(1f, 0.9f, 0.7f), 18f);
            return view;
        }

        private void Open(PlayerState p)
        {
            var svc = sys ?? CookingSystem.Instance;
            svc?.OpenStation(CraftStationType.CommunityKitchen, facilityId, p);
        }

        /// <summary>干し棚に仕込まれている物を小さく並べて見せる。</summary>
        public void ShowSlots(List<Color> colors)
        {
            foreach (var go in rackItems) if (go != null) Destroy(go);
            rackItems.Clear();
            if (rackRoot == null) return;
            for (int i = 0; i < colors.Count && i < 6; i++)
            {
                var go = PrimitiveBuilder.Part(rackRoot, PrimitiveType.Capsule, new Vector3(-0.35f + i * 0.14f, -2.5f, 0f), new Vector3(0.06f, 2.2f, 0.4f), colors[i], "Hanging");
                rackItems.Add(go);
            }
        }
    }

    /// <summary>調理中の小さな演出（湯気・泡・光の粒）。見た目だけなので UnityEngine.Random を使う。</summary>
    public sealed class CookingFx : MonoBehaviour
    {
        private float life;
        private float age;
        private Vector3 velocity;
        private Vector3 baseScale;

        public static void Puff(Vector3 position, Color color, int count = 5)
        {
            if (Game.Instance == null || Game.Instance.SceneRoot == null) return;
            for (int i = 0; i < count; i++)
            {
                var go = PrimitiveBuilder.Create(PrimitiveType.Sphere, Game.Instance.SceneRoot, position + Random.insideUnitSphere * 0.3f + Vector3.up * 1.1f,
                    Vector3.one * Random.Range(0.12f, 0.25f), color, "CookPuff", false);
                var fx = go.AddComponent<CookingFx>();
                fx.life = Random.Range(0.8f, 1.6f);
                fx.velocity = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1.2f), Random.Range(-0.2f, 0.2f));
                fx.baseScale = go.transform.localScale;
            }
        }

        /// <summary>計画に使った生き物・器具のあたりで、工程に合わせた色の演出を出す。</summary>
        public static void PlayAt(StationContext ctx, CraftPlan plan)
        {
            var pos = ctx.Position;
            var color = new Color(1f, 1f, 1f, 0.8f);
            foreach (var s in plan.Steps)
            {
                if (s.Mode == StepMode.Missing) continue;
                color = CookingTables.Step(s.Step).Color;
            }
            Puff(pos, color, 6);
        }

        private void Update()
        {
            age += UnityEngine.Time.deltaTime;
            transform.position += velocity * UnityEngine.Time.deltaTime;
            float k = 1f - Mathf.Clamp01(age / Mathf.Max(0.01f, life));
            transform.localScale = baseScale * (0.4f + 0.8f * k);
            if (age >= life) Destroy(gameObject);
        }
    }
}
