using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Story
{
    /// <summary>演出で使う場所（ワールドの配置データと各モジュールの契約から求める。無ければ代わりの場所）。</summary>
    public static class StoryPlaces
    {
        private static WorldLayout Layout => Game.Instance != null ? Game.Instance.Layout : null;

        public static Vector3 Ground(Vector3 p) => StoryActors.Ground(p);

        public static Vector3 Rocket()
        {
            var rocket = Game.Instance != null ? Game.Instance.Service<IRocketService>() : null;
            if (rocket != null)
            {
                try
                {
                    var p = rocket.RocketPosition;
                    if (p != Vector3.zero) return Ground(p);
                }
                catch (System.Exception) { }
            }
            var layout = Layout;
            if (layout != null)
            {
                if (layout.CrashSite != Vector3.zero) return Ground(layout.CrashSite);
                var f = layout.GetFacility(FacilityType.RocketSite);
                if (f != null) return Ground(f.position);
                return Ground(layout.PlayerSpawn + new Vector3(12f, 0, 0));
            }
            return Vector3.zero;
        }

        /// <summary>村の中心（広場）。</summary>
        public static Vector3 Village()
        {
            var layout = Layout;
            if (layout == null) return Vector3.zero;
            var plaza = layout.GetFacility(FacilityType.Plaza);
            if (plaza != null) return Ground(plaza.entrance != Vector3.zero ? plaza.entrance : plaza.position);
            foreach (var a in layout.GetAreas(AreaType.VillageCenter)) return Ground(a.center);
            return Ground(layout.PlayerSpawn);
        }

        public static Vector3 ShopEntrance()
        {
            var shop = Game.Instance != null ? Game.Instance.Service<IShopService>() : null;
            if (shop != null)
            {
                try
                {
                    var p = shop.EntrancePosition;
                    if (p != Vector3.zero) return Ground(p);
                }
                catch (System.Exception) { }
            }
            var f = Layout?.GetFacility(FacilityType.PlayerShop);
            if (f != null) return Ground(f.entrance != Vector3.zero ? f.entrance : f.position);
            return Village() + new Vector3(8f, 0, 4f);
        }

        public static Vector3 PlayerHome()
        {
            var housing = Game.Instance != null ? Game.Instance.Service<IHousingService>() : null;
            if (housing != null)
            {
                try
                {
                    var p = housing.PlayerHomeEntrance;
                    if (p != Vector3.zero) return Ground(p);
                }
                catch (System.Exception) { }
            }
            var f = Layout?.GetFacility(FacilityType.PlayerHouse);
            if (f != null) return Ground(f.entrance != Vector3.zero ? f.entrance : f.position);
            return Village() + new Vector3(-10f, 0, 6f);
        }

        /// <summary>新しい住民がやってくる空き区画（無ければ村の外れ）。</summary>
        public static Vector3 EmptyPlotOrEdge(out bool foundPlot)
        {
            foundPlot = false;
            var layout = Layout;
            if (layout != null)
            {
                foreach (var plot in layout.HousePlots)
                {
                    if (plot == null || plot.occupied) continue;
                    foundPlot = true;
                    return Ground(plot.position);
                }
            }
            return Village() + new Vector3(14f, 0, -10f);
        }

        /// <summary>a から b への水平方向（同じ場所なら既定の向き）。</summary>
        public static Vector3 Dir(Vector3 a, Vector3 b, Vector3 fallback)
        {
            var d = b - a;
            d.y = 0;
            if (d.sqrMagnitude < 0.01f) return fallback;
            return d.normalized;
        }

        public static Vector3 Side(Vector3 forward) => Vector3.Cross(Vector3.up, forward).normalized;
    }
}
