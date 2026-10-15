using System;
using UnityEngine;
using Uchujin.Core.Visual;

namespace Uchujin.Core
{
    /// <summary>
    /// プレイヤーの見た目を作る関数の差し替え口。Story/CharacterMake モジュールがキャラメイクの見た目で上書きする。
    /// 生成物は "Visual" という名前の子を持ち、その下に "ToolHolder" を置くと道具を振る演出が動く。
    /// </summary>
    public static class PlayerAvatarFactory
    {
        public static Func<CharacterAppearance, Transform, GameObject> BuildVisual = BuildDefault;

        public static GameObject BuildDefault(CharacterAppearance a, Transform parent)
        {
            a = a ?? new CharacterAppearance();
            var visual = PrimitiveBuilder.Empty("Visual", parent, Vector3.zero);
            float h = Mathf.Clamp(a.height, 0.8f, 1.2f);
            PrimitiveBuilder.Part(visual.transform, PrimitiveType.Capsule, new Vector3(0, 0.75f * h, 0), new Vector3(0.6f, 0.55f * h, 0.45f), a.topColor, "Body");
            PrimitiveBuilder.Part(visual.transform, PrimitiveType.Sphere, new Vector3(0, 1.45f * h, 0), Vector3.one * 0.5f * a.headSize, a.skinColor, "Head");
            PrimitiveBuilder.Part(visual.transform, PrimitiveType.Sphere, new Vector3(0, 1.62f * h, -0.03f), new Vector3(0.54f, 0.3f, 0.54f) * a.headSize, a.hairColor, "Hair");
            PrimitiveBuilder.Part(visual.transform, PrimitiveType.Sphere, new Vector3(-0.1f, 1.48f * h, 0.22f), Vector3.one * 0.07f, a.eyeColor, "EyeL");
            PrimitiveBuilder.Part(visual.transform, PrimitiveType.Sphere, new Vector3(0.1f, 1.48f * h, 0.22f), Vector3.one * 0.07f, a.eyeColor, "EyeR");
            var holder = PrimitiveBuilder.Empty("ToolHolder", visual.transform, new Vector3(0.35f, 0.9f * h, 0.15f));
            PrimitiveBuilder.Part(holder.transform, PrimitiveType.Cube, new Vector3(0, 0.25f, 0.1f), new Vector3(0.06f, 0.5f, 0.06f), new Color(0.5f, 0.35f, 0.2f), "Handle");
            return visual;
        }
    }

    /// <summary>
    /// ローカルプレイヤーのアバター生成・カメラ・持ち物のセーブ（PlayerProfile スコープ）を担当するコアシステム。
    /// </summary>
    public sealed class PlayerSystem : GameSystemBase
    {
        public override string SaveKey => "core.player";

        public GameObject LocalAvatar { get; private set; }
        public PlayerController LocalController { get; private set; }
        public PlayerInteractor LocalInteractor { get; private set; }
        public CameraRig CameraRig { get; private set; }

        /// <summary>新しいゲーム開始時の所持金【仮数値】。</summary>
        public const int StarterMoney = 500;

        public override void RegisterContent()
        {
            ItemRegistry.Register(new ItemDefinition
            {
                Id = SharedItemIds.Letter,
                NameJa = "手紙",
                NameEn = "Letter",
                DescriptionJa = "宇宙人の文字で書かれた手紙。",
                DescriptionEn = "A letter written in alien script.",
                Category = ItemCategory.Letter,
                BasePrice = 0,
                MaxStack = 1,
                Color = new Color(1f, 0.95f, 0.8f)
            }.WithTags(ItemTags.NotSellable));
            ItemRegistry.Register(new ItemDefinition
            {
                Id = SharedItemIds.GiftBox,
                NameJa = "贈り物の包み",
                NameEn = "Gift box",
                DescriptionJa = "住民からもらった包み。",
                DescriptionEn = "A wrapped present from a resident.",
                Category = ItemCategory.Gift,
                BasePrice = 0,
                MaxStack = 10,
                Color = new Color(1f, 0.6f, 0.7f)
            }.WithTags(ItemTags.NotSellable));
        }

        /// <summary>新規ゲーム用に持ち物を初期化する（次の星へ進む場合は呼ばない）。</summary>
        public void ResetLocalProfileForNewGame(CharacterAppearance appearance)
        {
            var p = LocalPlayer;
            if (p == null) return;
            if (appearance != null) p.Appearance = appearance;
            p.Inventory.Clear();
            p.Wallet.money = StarterMoney;
            p.Tools.levels.Clear();
            var starter = ToolBelt.CreateStarter();
            foreach (var kv in starter.levels) p.Tools.SetLevel((ToolType)Enum.Parse(typeof(ToolType), kv.key), kv.value);
            p.Tools.equipped = ToolType.Hand;
            p.SelectedSlot = -1;
        }

        public override void BuildScene()
        {
            SpawnLocalAvatar();
        }

        public void SpawnLocalAvatar()
        {
            var p = LocalPlayer;
            if (p == null) return;
            if (LocalAvatar != null) UnityEngine.Object.Destroy(LocalAvatar);

            LocalAvatar = new GameObject("LocalPlayer");
            LocalAvatar.transform.SetParent(Game.SceneRoot, false);
            LocalAvatar.AddComponent<CharacterController>();
            LocalController = LocalAvatar.AddComponent<PlayerController>();
            LocalController.Player = p;
            LocalInteractor = LocalAvatar.AddComponent<PlayerInteractor>();
            LocalInteractor.Player = p;
            RebuildLocalVisual();
            p.Avatar = LocalAvatar.transform;

            var layout = Game.Layout;
            Vector3 spawn = layout != null ? layout.OnGround(layout.PlayerSpawn) + Vector3.up * 0.2f : Vector3.up;
            p.CurrentAreaId = AreaIds.Overworld;
            p.Position = spawn;

            var camGo = Camera.main != null ? Camera.main.gameObject : null;
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = 1200f;
                camGo.AddComponent<AudioListener>();
            }
            CameraRig = camGo.GetComponent<CameraRig>();
            if (CameraRig == null) CameraRig = camGo.AddComponent<CameraRig>();
            CameraRig.target = LocalAvatar.transform;
            CameraRig.SnapToTarget();
        }

        public void RebuildLocalVisual()
        {
            if (LocalAvatar == null) return;
            var old = LocalAvatar.transform.Find("Visual");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            GameObject visual = null;
            try
            {
                visual = PlayerAvatarFactory.BuildVisual(LocalPlayer.Appearance, LocalAvatar.transform);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (visual == null) visual = PlayerAvatarFactory.BuildDefault(LocalPlayer.Appearance, LocalAvatar.transform);
            visual.name = "Visual";
        }

        public override void Teardown()
        {
            if (LocalAvatar != null) UnityEngine.Object.Destroy(LocalAvatar);
            LocalAvatar = null;
            if (LocalPlayer != null) LocalPlayer.Avatar = null;
        }

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope != SaveScope.PlayerProfile || LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
            return JsonUtility.ToJson(LocalPlayer.CaptureProfile());
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (scope != SaveScope.PlayerProfile || LocalPlayer == null || playerId != LocalPlayer.PlayerId || string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<PlayerState.ProfileData>(json);
            LocalPlayer.RestoreProfile(d);
        }
    }
}
