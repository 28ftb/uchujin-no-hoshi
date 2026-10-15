using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Housing
{
    // =====================================================================
    // 家・家具・装飾のセーブ／同期データ（JsonUtility で直列化するため public フィールドのみ）。
    // 家具仕様 v0.2 の §2〜§21 に対応する状態をまとめる。
    // =====================================================================

    /// <summary>部屋の種類（家具仕様 §3 増築項目）。部屋の「用途」は家具で決まる（§3.1）ので、ここは器の形だけ。</summary>
    public enum RoomKind
    {
        Normal,     // ふつうの部屋
        Glass,      // ガラス張りの部屋（景色・植物）
        Waterside,  // 水辺の部屋（奥が水場。水槽・水辺の生物）
        Terrace,    // テラス（屋根なし・屋外家具を置ける）
        Loft,       // 2階・ロフト（階段で上がる）
        Basement    // 地下室（倉庫・展示室・趣味室）
    }

    [Serializable]
    public class RoomData
    {
        public int index;
        public RoomKind kind;
        /// <summary>幅・奥行き（格子数。1格子 = HousingConst.Cell m）。</summary>
        public int widthCells;
        public int depthCells;
        /// <summary>壁紙・床材のアイテムID（空なら既定）。</summary>
        public string wallpaperId;
        public string floorId;
        /// <summary>個室の持ち主（マルチの共同生活 §20・マルチ仕様 §16.1）。空なら共有。</summary>
        public string ownerId;
    }

    /// <summary>家具の思い出（家具仕様 §14）。</summary>
    [Serializable]
    public class FurnitureMemory
    {
        public int day;
        public int minute;
        /// <summary>"origin"（入手元） "placed" "sat" "visit" "meal" "music" "creature" "gift_giver_left" "note"（自由メモ） など。</summary>
        public string kind;
        public string ja;
        public string en;
        public string residentId;
        public string playerId;
        /// <summary>関係する発話記録（声を聞き返す・音声カードに保存する）。</summary>
        public string recordId;
        /// <summary>プレイヤーが「大切」に印を付けたか（ゲームは勝手に判断しない §14.3）。</summary>
        public bool pinned;

        public string Text => Loc.T(ja, en);
    }

    /// <summary>置かれた家具1点（または、思い出を保つために片付けて保管中の記録）。</summary>
    [Serializable]
    public class PlacedFurniture
    {
        public string instanceId;
        public string itemId;
        public int quality;
        /// <summary>家具の所有者（プレイヤーID）。持ち帰りは所有者のみ（§20）。</summary>
        public string ownerId;
        /// <summary>HousingConst.LocHouse / LocGarden / LocStored。</summary>
        public string location;
        public int room;
        public int gx;
        public int gz;
        /// <summary>向き 0〜3（×90度）。</summary>
        public int rot;
        public bool lightOn = true;
        /// <summary>住んでいる生物のUID（生物の住処 §9）。</summary>
        public string creatureUid;
        /// <summary>個人用の収納にする（所有者だけが開ける。マルチ仕様 §16.4）。</summary>
        public bool privateStorage;
        /// <summary>贈ってくれた住民（思い出・訪問時の反応 §15.2）。</summary>
        public string giftFromResidentId;
        public int acquiredDay;
        public int placedDay;
        /// <summary>水槽・展示棚・食卓に飾った物。</summary>
        public List<ItemStack> displayed = new List<ItemStack>();
        public List<FurnitureMemory> memories = new List<FurnitureMemory>();
    }

    /// <summary>収納家具の中身。</summary>
    [Serializable]
    public class FurnitureStorageData
    {
        public string instanceId;
        public Inventory inventory = new Inventory();
    }

    /// <summary>家具職人への注文（増築・改装・家具の制作依頼 §3.2, §16.1）。</summary>
    [Serializable]
    public class ConstructionOrder
    {
        public string orderId;
        /// <summary>ConstructionKinds の値。</summary>
        public string kind;
        public int room = -1;
        /// <summary>家具の制作依頼のアイテムID。</summary>
        public string itemId;
        public int orderedDay;
        public int readyDay;
        public string ownerId;
        public string carpenterId;
        public int paid;
        public List<StringIntPair> materials = new List<StringIntPair>();
        public bool delivered;
    }

    public static class ConstructionKinds
    {
        public const string AddRoom = "add_room";
        public const string AddGlass = "add_glass";
        public const string AddWaterside = "add_waterside";
        public const string AddTerrace = "add_terrace";
        public const string AddLoft = "add_loft";
        public const string AddBasement = "add_basement";
        public const string ExpandWidth = "expand_width";
        public const string ExpandDepth = "expand_depth";
        public const string GardenExpand = "garden_expand";
        public const string Commission = "commission";
    }

    public enum InvitationStatus
    {
        Planned = 0,   // 約束した
        Arriving = 1,  // 家の前に向かっている／待っている
        Inside = 2,    // 家の中で過ごしている
        Done = 3,
        Missed = 4,    // 留守だった（マネピに伝言）
        Cancelled = 5,
        Declined = 6
    }

    /// <summary>住民の招待・訪問（§11）。</summary>
    [Serializable]
    public class Invitation
    {
        public string id;
        public string residentId;
        public string playerId;
        public int day;
        public int hour;
        public InvitationStatus status;
        /// <summary>住民が自分から遊びに来た（招待なし）。</summary>
        public bool spontaneous;
        /// <summary>家の中で過ごす終了時刻（TotalMinutes）。</summary>
        public long endTotalMinutes;
        /// <summary>今使っている家具。</summary>
        public string currentInstanceId;
        public int steps;
        /// <summary>楽しかった度合い -1〜1（内部。UIに出さない）。</summary>
        public float enjoyment;
        public List<string> usedInstances = new List<string>();
        public bool ateMeal;
    }

    /// <summary>住民の家に増えた家具（住民が買った・もらった物 §15、村仕様 §5.2）。</summary>
    [Serializable]
    public class ResidentHomeItem
    {
        public string itemId;
        public int day;
        public bool fromPlayerGift;
        public string giftPlayerId;
        /// <summary>イベントから自動で追加した（住民モジュールの明示的な登録と重複させないための印）。</summary>
        public bool autoFromEvent;
        public bool claimed;
        /// <summary>明示的な登録の後に届いたイベントと対応付け済み（イベント側で重複追加しない）。</summary>
        public bool eventMatched;
    }

    [Serializable]
    public class ResidentHomeData
    {
        public string residentId;
        public bool packing;
        public List<ResidentHomeItem> items = new List<ResidentHomeItem>();
    }

    /// <summary>まだ置いていない家具の入手元（置いた時に思い出として残す）。</summary>
    [Serializable]
    public class ProvenanceEntry
    {
        public string itemId;
        /// <summary>"gift" "carpenter" "commission" "craft" "quest" "shop"</summary>
        public string kind;
        public string residentId;
        public int day;
    }

    [Serializable]
    public class HousingPlanetState
    {
        public int version = 1;
        public string houseOwnerId;
        public List<RoomData> rooms = new List<RoomData>();
        public List<PlacedFurniture> furniture = new List<PlacedFurniture>();
        public List<FurnitureStorageData> storages = new List<FurnitureStorageData>();
        public List<ConstructionOrder> orders = new List<ConstructionOrder>();
        public List<Invitation> invitations = new List<Invitation>();
        public List<ResidentHomeData> residentHomes = new List<ResidentHomeData>();
        public int gardenWidthCells;
        public int gardenDepthCells;
        /// <summary>完成した家具の注文で、収納に入りきらなかった物。</summary>
        public List<ItemStack> deliveries = new List<ItemStack>();
        /// <summary>マルチ：ほかのプレイヤーが家に入れるか（家主の訪問設定 §20）。</summary>
        public bool guestsMayEnter = true;

        // 住民の家の室内を同じ計算で作るための一時設定（保存しない）
        [NonSerialized] public bool useCustomOrigin;
        [NonSerialized] public Vector3 customOrigin;
    }

    [Serializable]
    public class HousingProfileState
    {
        public List<ProvenanceEntry> pending = new List<ProvenanceEntry>();
    }

    [Serializable]
    public class HousingKnowledgeState
    {
        /// <summary>住民の家で見た家具の数（増えていたら住民辞典に事実を記録する）。</summary>
        public List<StringIntPair> seenResidentHomeItems = new List<StringIntPair>();
        /// <summary>自宅のマネピの伝言を何件まで見たか。</summary>
        public int seenHomeMessages;
    }

    /// <summary>モジュール共通の定数（【仮数値】はここで調整）。</summary>
    public static class HousingConst
    {
        public const string LocHouse = AreaIds.PlayerHouse; // "house:player"
        public const string LocGarden = "garden";
        public const string LocStored = "stored";

        /// <summary>格子の大きさ（m）。【仮数値】</summary>
        public const float Cell = 0.5f;
        public const float WallHeight = 2.6f;
        public const float WallThickness = 0.2f;
        /// <summary>壁のコライダーの高さ（カメラのめり込み回避レイに当たらない高さ）。</summary>
        public const float WallColliderHeight = 1.1f;

        /// <summary>家の中（屋外から離れた座標）。他モジュールの屋内と重ならないよう遠くに置く。【仮】</summary>
        public static readonly Vector3 HouseOrigin = new Vector3(-6000f, 0f, -6000f);
        public const float LoftRowOffsetZ = 40f;
        public const float BasementRowOffsetZ = -40f;
        /// <summary>住民の家の室内（1軒ずつ並べる）。【仮】</summary>
        public static readonly Vector3 ResidentHomesOrigin = new Vector3(-6000f, 0f, -5000f);
        public const float ResidentHomeSpacing = 50f;

        // ---- 部屋の大きさ【仮数値】----
        public const int InitialRoomWidth = 14;   // 7m
        public const int InitialRoomDepth = 12;   // 6m
        public const int NewRoomWidth = 12;
        public const int NewRoomDepth = 12;
        public const int MaxRoomWidth = 28;       // 14m
        public const int MaxRoomDepth = 24;       // 12m
        public const int ExpandStep = 4;          // 2m
        public const int MaxGroundRooms = 6;
        public const int MaxLoftRooms = 2;
        public const int MaxBasementRooms = 2;

        // ---- 庭【仮数値】----
        public const int InitialGardenWidth = 24;  // 12m
        public const int InitialGardenDepth = 20;  // 10m
        public const int MaxGardenWidth = 48;
        public const int MaxGardenDepth = 36;
        public const int GardenExpandStep = 8;

        // ---- 収納【仮数値】----
        public const int StarterChestSlots = 36;

        // ---- 訪問【仮数値】----
        public const int InviteMinFriendship = 10;
        public const int VisitDurationMinutes = 150;
        public const int VisitGraceMinutes = 60;
        public const int VisitStepMinutes = 15;
        public const int MaxVisitorsAtOnce = 6;
        public const int SpontaneousVisitMinFriendship = 55;
        public const float SpontaneousVisitChance = 0.18f;

        // ---- 思い出【仮数値】----
        public const int MaxAutoMemoriesPerFurniture = 40;

        // ---- 家具職人【仮数値】----
        public const int CarpenterDailyStock = 14;
        public const int CarpenterFriendDiscountFriendship = 50;
        public const float CarpenterFriendDiscount = 0.9f;
        public const int SpecialDesignFriendship = 60;
        public const float CommissionPriceRate = 0.6f;
        public const int CommissionDays = 2;
    }
}
