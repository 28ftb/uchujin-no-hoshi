using UnityEngine;

namespace Uchujin.Core
{
    // ===== ゲーム全体で共有するイベント型 =====
    // 各モジュールは自分の行動結果をここにある型で Publish し、
    // クエスト・住民・タブレット・ロケット等が Subscribe して連携する。
    // モジュール内部だけで使うイベントは各モジュール側で定義してよい。

    // ---- 時間 ----
    public struct MinuteTickEvent { public int Day; public int Hour; public int Minute; public long TotalMinutes; }
    public struct HourChangedEvent { public int Day; public int Hour; }
    public struct DayPartChangedEvent { public int Day; public DayPart Previous; public DayPart Current; }
    public struct DayEndedEvent { public int Day; }
    /// <summary>日付更新（0:00）。鉱山の再配置などはこの時点でプレイヤーの目の前でも即座に行う。</summary>
    public struct DayStartedEvent { public int Day; }
    public struct SleepStartedEvent { public string PlayerId; public int TargetHour; }
    public struct SleepEndedEvent { public string PlayerId; }

    // ---- ゲームフロー ----
    public struct GameStateChangedEvent { public GameState Previous; public GameState Current; }
    public struct PlanetReadyEvent { public PlanetInfo Planet; public bool IsNewPlanet; }
    public struct PlanetTeardownEvent { public PlanetInfo Planet; }
    public struct GameSavedEvent { public string SlotId; }

    // ---- アイテム・お金 ----
    public struct ItemAddedEvent { public string PlayerId; public ItemStack Stack; public string Source; }
    public struct ItemRemovedEvent { public string PlayerId; public string ItemId; public int Count; public string Reason; }
    public struct MoneyChangedEvent { public string PlayerId; public int Delta; public int Balance; public string Reason; }

    // ---- 活動（成果） ----
    public struct FishCaughtEvent { public string PlayerId; public string ItemId; public float SizeCm; public int Quality; public string SpotId; public Vector3 Position; }
    public struct OreMinedEvent { public string PlayerId; public string ItemId; public int Floor; public int Quality; }
    public struct CropHarvestedEvent { public string PlayerId; public string ItemId; public int Count; public int Quality; public bool ByCreature; }
    public struct CropPlantedEvent { public string PlayerId; public string CropItemId; public Vector3 Position; }
    public struct CreatureCaughtEvent { public string PlayerId; public string SpeciesId; public string CreatureUid; public Vector3 Position; }
    public struct CreatureObservedEvent { public string PlayerId; public string SpeciesId; public string Behavior; }
    public struct ItemCraftedEvent { public string PlayerId; public string ItemId; public int Count; public int Quality; public string RecipeId; public CraftStationType Station; public bool FirstTime; }
    public struct ToolUpgradedEvent { public string PlayerId; public ToolType Tool; public int NewLevel; }
    public struct FurniturePlacedEvent { public string PlayerId; public string ItemId; public string InstanceId; public string Location; }

    // ---- 商店・経済 ----
    /// <summary>主人公の商店で商品が売れた（セルフ販売）。</summary>
    public struct ShopItemSoldEvent { public string ShopOwnerId; public string ResidentId; public string ItemId; public int Quality; public int Price; public int Day; public int Minute; }
    public struct ResidentPurchaseEvent { public string ResidentId; public string ItemId; public int Price; public string Where; }

    // ---- 住民・会話 ----
    public struct ResidentMovedInEvent { public string ResidentId; public int Day; }
    public struct ResidentMoveOutDecidedEvent { public string ResidentId; public int PlannedDay; }
    public struct ResidentMovedOutEvent { public string ResidentId; public int Day; }
    public struct FriendshipChangedEvent { public string ResidentId; public string PlayerId; public int Delta; public int Value; public string Reason; }
    public struct GiftGivenEvent { public string PlayerId; public string ResidentId; public string ItemId; public int Quality; public int Reaction; }
    public struct ResidentTalkedEvent { public string PlayerId; public string ResidentId; }
    /// <summary>誰かが宇宙語で発話した（音声・吹き出し・記録の元になる）。</summary>
    public struct UtteranceSpokenEvent { public UtteranceRecord Record; }
    public struct PlayerGestureEvent { public string PlayerId; public PlayerGesture Gesture; public string TargetResidentId; }

    // ---- クエスト ----
    public struct QuestStateChangedEvent { public string QuestId; public string ResidentId; public QuestStatus Status; public string PlayerId; }

    // ---- ロケット ----
    public struct RocketDeviceRepairedEvent { public string DeviceId; public int RepairedCount; public int TotalCount; public string PlayerId; }
    public struct RocketLaunchReadyEvent { }
    public struct EndingStartedEvent { }
    public struct EndingFinishedEvent { public PostEndingChoice Choice; }

    // ---- 天候 ----
    public struct WeatherChangedEvent { public WeatherState Previous; public WeatherState Current; }

    // ---- タブレット ----
    public struct TabletOpenedEvent { public string AppId; }
    public struct TabletClosedEvent { }
    public struct DictionaryEntryEditedEvent { public string PlayerId; public string Surface; }

    // ---- ネットワーク ----
    public struct PlayerJoinedEvent { public string PlayerId; public string DisplayName; }
    public struct PlayerLeftEvent { public string PlayerId; }
}
