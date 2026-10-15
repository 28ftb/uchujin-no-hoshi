using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Housing
{
    // 家具職人（ResidentJob.Carpenter）の店（家具仕様 §16, §16.1, §18, §21）。
    public sealed partial class HousingSystem
    {
        /// <summary>親しくなると扱ってくれる特別なデザイン（§16.1）。</summary>
        public static bool IsSpecialDesign(FurnitureSpec spec)
        {
            if (spec == null) return false;
            return spec.series == FurnitureSeries.AlienTradition || spec.series == FurnitureSeries.Luxury || spec.lineage >= 0;
        }

        public int FurniturePrice(ItemDefinition def, IResidentInfo carpenter, string playerId)
        {
            var spec = def.ModuleData as FurnitureSpec;
            float rate = spec != null ? FurnitureCatalog.PriceRate(spec) : 1f;
            if (carpenter != null && carpenter.GetFriendship(playerId) >= HousingConst.CarpenterFriendDiscountFriendship) rate *= HousingConst.CarpenterFriendDiscount;
            return Mathf.Max(1, Mathf.RoundToInt(def.BasePrice * rate));
        }

        private void OpenCarpenterShop(IResidentInfo carpenter, PlayerState player)
        {
            var entries = new List<VendorEntry>();
            int friendship = carpenter != null ? carpenter.GetFriendship(player.PlayerId) : 0;
            // 素材（増築・制作依頼で必要。いつでも買える）
            foreach (var id in new[] { FurnitureCatalog.LumberId, FurnitureCatalog.StoneBlockId, FurnitureCatalog.GlassPaneId })
            {
                var def = ItemRegistry.Get(id);
                if (def == null) continue;
                entries.Add(new VendorEntry { ItemId = id, Price = def.BasePrice });
            }
            foreach (var def in FurnitureCatalog.DailyStock(planetSeed, Today, HousingConst.CarpenterDailyStock))
            {
                var spec = def.ModuleData as FurnitureSpec;
                bool locked = spec != null && IsSpecialDesign(spec) && friendship < HousingConst.SpecialDesignFriendship;
                string itemId = def.Id;
                string carpenterId = carpenter?.Id;
                int price = FurniturePrice(def, carpenter, player.PlayerId);
                entries.Add(new VendorEntry
                {
                    ItemId = itemId,
                    Price = price,
                    Stock = 3,
                    Locked = locked,
                    LockedReasonJa = "もっと親しくなると売ってくれそうだ。",
                    LockedReasonEn = "They might sell this once you're closer.",
                    CustomPurchase = (p, amount) =>
                    {
                        p.GiveOrDrop(new ItemStack(itemId, amount), "housing.vendor");
                        if (spec != null) for (int i = 0; i < amount; i++) AddProvenance(itemId, "carpenter", carpenterId);
                        // 売上は家具職人の生活費になる（住民の経済）
                        if (carpenterId != null)
                        {
                            try { Game.Service<IResidentDirectory>()?.GiveMoney(carpenterId, price * amount, "housing.furniture_sale"); }
                            catch (Exception e) { Debug.LogException(e); }
                        }
                    }
                });
            }
            string title = carpenter != null ? Loc.Format("{0}の家具", "{0}'s furniture", ResidentName(carpenter.Id)) : Loc.T("家具", "Furniture");
            UIManager.Open(new VendorWindow(title, player, entries));
        }
    }
}
