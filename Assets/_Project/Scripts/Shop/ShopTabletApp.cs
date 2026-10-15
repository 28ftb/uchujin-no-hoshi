using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Shop
{
    /// <summary>
    /// タブレットの「商店」アプリ（AppId "shop"）：売上レポート・来店履歴・値段の比較・棚の状況・要望・未精算。
    /// どこからでも事実を確認できる。マネピの再現・音声カード保存は店のマネピの前でだけできる。
    /// </summary>
    internal sealed class ShopTabletApp : ITabletApp
    {
        private readonly ShopSystem shop;
        private ShopReportView view;

        public ShopTabletApp(ShopSystem shop)
        {
            this.shop = shop;
        }

        public string AppId => ShopSystem.TabletAppId;
        public string Title => Loc.T("商店", "Shop");
        public int Order => 40;
        public string IconGlyph => Loc.T("店", "Sh");
        public Color IconColor => new Color(1f, 0.62f, 0.4f);
        public bool IsAvailable => Game.Instance != null && Game.Instance.State == GameState.Playing;

        public void OnOpen()
        {
            if (view == null) view = new ShopReportView(shop);
        }

        public void Draw(Rect area)
        {
            if (view == null) view = new ShopReportView(shop);
            GUILayout.BeginArea(area);
            try
            {
                view.Draw();
            }
            finally
            {
                GUILayout.EndArea();
            }
        }
    }

    /// <summary>家具職人に話しかけた時の「お店の改装を頼む」（商店 §8：家具職人への依頼という形）。</summary>
    internal sealed class ShopCarpenterProvider : IResidentInteractionProvider
    {
        private readonly ShopSystem shop;

        public ShopCarpenterProvider(ShopSystem shop)
        {
            this.shop = shop;
        }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            if (resident == null || player == null || resident.Job != ResidentJob.Carpenter) yield break;
            if (!ShopWindows.IsManager(player)) yield break;
            string id = resident.Id;
            yield return new ResidentInteractionOption("お店の改装を頼む", "Order shop upgrades", () => ShopWindows.OpenUpgrades(player, id), 60);
        }
    }
}
