using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Mining
{
    /// <summary>
    /// 鉱山システム（鉱山・採掘システム仕様案 v0.4）。
    /// ・約10階層（§2）。各階は屋外から離れた座標の洞窟エリア（AreaManager に "mine:N" で登録）。地形はシードから決定的。
    /// ・鉱石は日替わり（§3）：日付更新（OnDayStarted）で、プレイヤーが鉱山内にいても目の前で即座に消失・出現する（時間天候仕様 §4.3【決定】）。
    ///   更新の延期・最低保証はしない。配置はシード＋日付から決定的に作るので、セーブ・同期は「日付＋採掘済みの差分」だけ。
    /// ・採掘：ツルハシでノードを叩く。硬さとツルハシの段階（§4）。採掘は WorldCommand（ホストが確定。二重取得防止：マルチ §8.3）。
    /// ・ツルハシ強化は鍛冶屋（住民メニュー＋施設の金床）。前段階の鉱石＋お金（§4、バランス §9.2）。金属加工は Forge（料理・加工モジュール）。
    /// ・採掘レベル（§5）、宝石の個体差（§6.1）、化石・遺物などの発見物（§6）、最下層の星の結晶の広間（§12）。
    /// ・戦闘・HP・スタミナは無い（§7 は未決定のため戦闘は入れない。地下の生物は Creatures モジュールが出す）。
    /// </summary>
    public sealed class MiningSystem : GameSystemBase, IMiningService
    {
        public override string SaveKey => "mining";
        public override int DailyUpdateOrder => 100;

        public struct DayInfo
        {
            public string FavoredOre;
            public bool GemDay;
        }

        private ulong seed;
        private readonly MineFloorLayout[] layouts = new MineFloorLayout[MiningTuning.FloorCount + 1];
        private readonly List<MineNodeData>[] nodes = new List<MineNodeData>[MiningTuning.FloorCount + 1];
        private readonly DayInfo[] dayInfo = new DayInfo[MiningTuning.FloorCount + 1];
        private readonly HashSet<int>[] mined = new HashSet<int>[MiningTuning.FloorCount + 1];
        private readonly Dictionary<int, float>[] damage = new Dictionary<int, float>[MiningTuning.FloorCount + 1];
        private readonly Dictionary<int, MineNodeView>[] views = new Dictionary<int, MineNodeView>[MiningTuning.FloorCount + 1];
        private readonly Transform[] nodeRoots = new Transform[MiningTuning.FloorCount + 1];
        private int oreDay = -1;
        private bool sceneBuilt;
        private MiningPlanetSave planetExtra = new MiningPlanetSave();
        private readonly Dictionary<string, MiningProfileData> profiles = new Dictionary<string, MiningProfileData>();
        private MineLogSave log = new MineLogSave();
        private Vector3 surfaceExit;
        private float surfaceExitYaw;
        private float clankToastCooldown;
        private float hallCheckTimer;

        private readonly ToolHandler toolHandler;
        private readonly BlacksmithInteractions interactions;
        private readonly MineLogApp app;

        public MineLogSave MineLog => log;

        public MiningSystem()
        {
            for (int f = 0; f <= MiningTuning.FloorCount; f++)
            {
                mined[f] = new HashSet<int>();
                damage[f] = new Dictionary<int, float>();
                views[f] = new Dictionary<int, MineNodeView>();
            }
            toolHandler = new ToolHandler(this);
            interactions = new BlacksmithInteractions(this);
            app = new MineLogApp(this);
        }

        // =====================================================================
        // ライフサイクル
        // =====================================================================

        public override void RegisterContent()
        {
            MineCatalog.RegisterItems();
            WorldAuthority.Register<MineHitCommand>(HandleHit);
            WorldAuthority.Register<UpgradePickaxeCommand>(HandleUpgrade);
            WorldAuthority.Register<SellToBlacksmithCommand>(HandleSell);
            WorldAuthority.Register<ReachFloorCommand>(HandleReachFloor);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            seed = ctx.Seed;
            oreDay = -1;
            sceneBuilt = false;
            planetExtra = new MiningPlanetSave();
            log = new MineLogSave();
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                layouts[f] = MineGenerator.Generate(ctx, f);
                nodes[f] = null;
                mined[f].Clear();
                damage[f].Clear();
                views[f].Clear();
                nodeRoots[f] = null;
                RegisterArea(f);
            }
            ShareWithOtherModules(ctx);
        }

        private void RegisterArea(int f)
        {
            var L = layouts[f];
            int floor = f;
            Game.Areas.Register(new AreaDescriptor
            {
                Id = AreaIds.MineFloor(f),
                NameJa = "鉱山 地下" + f + "階",
                NameEn = "Mine B" + f + "F",
                IsUnderground = true,
                ShowWeather = false,
                AmbientOverride = MineFloorBuilder.Ambient(f),
                DefaultSpawn = L.CellCenter(L.Entry) + Vector3.up * 0.2f,
                DefaultYaw = 0f,
                OnEnter = p => OnEnterFloor(p, floor)
            });
        }

        /// <summary>
        /// 他モジュールへの受け渡し（直接参照しない）：
        /// ・地下湖を Layout.FishingSpots に "underground" タグ付きで追加（釣りモジュールが地下の魚の出現に使う）。
        /// ・洞窟の生息地点を ctx.Shared["mining.caveHabitats"]（List&lt;HabitatPoint&gt;）で渡す（生き物モジュールの地下生物 No.041〜050 用）。
        /// </summary>
        private void ShareWithOtherModules(PlanetContext ctx)
        {
            var habitats = new List<HabitatPoint>();
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                var L = layouts[f];
                if (L.HasLake && ctx.Layout != null)
                {
                    float radius = Mathf.Min(L.Lake.width, L.Lake.height) * MiningTuning.Cell * 0.5f;
                    var spot = new FishingSpot
                    {
                        id = "mine.lake.f" + f,
                        waterId = "mine.lake.f" + f,
                        waterType = WaterType.Pond,
                        standPosition = L.CellCenter(L.LakeStand),
                        waterPosition = L.RectCenter(L.Lake) + Vector3.up * 0.1f
                    };
                    spot.tags.Add("underground");
                    spot.tags.Add("cave");
                    spot.tags.Add("floor:" + f);
                    spot.tags.Add("radius:" + radius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    if (f <= 4) spot.tags.Add("cold");
                    if (f >= 6) spot.tags.Add("deep");
                    if (f >= 8) spot.tags.Add("crystal");
                    ctx.Layout.FishingSpots.RemoveAll(s => s != null && s.id == spot.id);
                    ctx.Layout.FishingSpots.Add(spot);
                    habitats.Add(new HabitatPoint { position = spot.standPosition, area = AreaType.MineEntrance, feature = "cave_lake" });
                }
                for (int i = 0; i < L.WallAdjacent.Count; i += 9)
                    habitats.Add(new HabitatPoint { position = L.CellCenter(L.WallAdjacent[i]), area = AreaType.MineEntrance, feature = i % 2 == 0 ? "cave_wall" : "dark" });
                foreach (var b in L.Boulders) habitats.Add(new HabitatPoint { position = b, area = AreaType.MineEntrance, feature = "rock" });
                foreach (var c in L.Crystals) habitats.Add(new HabitatPoint { position = c, area = AreaType.MineEntrance, feature = "crystal" });
                if (L.HasDeepHall) habitats.Add(new HabitatPoint { position = L.RectCenter(L.DeepHall), area = AreaType.MineEntrance, feature = "deep_hall" });
            }
            ctx.Shared["mining.caveHabitats"] = habitats;
            ctx.Shared["mining.floorCount"] = MiningTuning.FloorCount;
            System.Func<Vector3, int> floorOf = FloorOfPosition;
            ctx.Shared["mining.floorOfPosition"] = floorOf;
        }

        public int FloorOfPosition(Vector3 p)
        {
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
                if (layouts[f] != null && layouts[f].ContainsWorld(p)) return f;
            return 0;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            if (ctx.Planet.planetIndex <= 1 && string.IsNullOrEmpty(ctx.Planet.previousWorldId) && LocalPlayer != null)
                profiles[LocalPlayer.PlayerId] = new MiningProfileData { playerId = LocalPlayer.PlayerId };
            Regenerate(Clock != null ? Clock.Day : 1, false);
        }

        public override void BuildScene()
        {
            var layout = Game.Layout;
            var root = PrimitiveBuilder.Empty("Mine", Game.SceneRoot, Vector3.zero);
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                if (layouts[f] == null) continue;
                MineFloorBuilder.Build(this, layouts[f], root.transform);
                nodeRoots[f] = PrimitiveBuilder.Empty("Nodes_" + f, root.transform, Vector3.zero).transform;
            }
            BuildSurface(layout);
            if (nodes[1] == null || (IsAuthority && Clock != null && oreDay != Clock.Day)) Regenerate(Clock != null ? Clock.Day : 1, false);
            sceneBuilt = true;
            for (int f = 1; f <= MiningTuning.FloorCount; f++) SyncFloorViews(f, false);
        }

        private void BuildSurface(WorldLayout layout)
        {
            if (layout == null) return;
            var fac = layout.GetFacility(FacilityType.MineEntrance);
            Vector3 pos = fac != null ? fac.entrance : layout.MineEntrance;
            if (fac == null && pos == Vector3.zero) pos = layout.PlayerSpawn + new Vector3(14f, 0f, 8f);
            pos = layout.OnGround(pos);
            var toVillage = layout.PlayerSpawn - pos;
            toVillage.y = 0;
            if (toVillage.sqrMagnitude < 0.01f) toVillage = Vector3.back;
            // 門の奥（+Z）が坑道、手前（-Z）が村側
            float yaw = Quaternion.LookRotation(-toVillage.normalized, Vector3.up).eulerAngles.y;
            MineFloorBuilder.BuildEntrance(this, Game.SceneRoot, pos, yaw);
            surfaceExit = layout.OnGround(pos + toVillage.normalized * 3f) + Vector3.up * 0.2f;
            surfaceExitYaw = Quaternion.LookRotation(toVillage.normalized, Vector3.up).eulerAngles.y;

            // 鍛冶屋の金床（住民が不在でも呼び鈴で頼める）
            var smith = layout.GetFacility(FacilityType.Blacksmith);
            Vector3 anvilPos;
            string facilityId = null;
            if (smith != null)
            {
                var toDoor = smith.entrance - smith.position;
                toDoor.y = 0;
                var side = toDoor.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toDoor.normalized) : Vector3.right;
                anvilPos = smith.entrance + side * 2.2f + (toDoor.sqrMagnitude > 0.01f ? toDoor.normalized : Vector3.zero) * 0.8f;
                facilityId = smith.id;
            }
            else anvilPos = pos + toVillage.normalized * 4f + Vector3.Cross(Vector3.up, toVillage.normalized) * 3f;
            anvilPos = layout.OnGround(anvilPos);
            var anvil = PrimitiveBuilder.Empty("BlacksmithAnvil", Game.SceneRoot, anvilPos);
            var dark = new Color(0.25f, 0.25f, 0.3f);
            PrimitiveBuilder.Part(anvil.transform, PrimitiveType.Cylinder, new Vector3(0, 0.3f, 0), new Vector3(0.6f, 0.3f, 0.6f), new Color(0.45f, 0.35f, 0.25f), "Stump");
            PrimitiveBuilder.Part(anvil.transform, PrimitiveType.Cube, new Vector3(0, 0.75f, 0), new Vector3(0.9f, 0.3f, 0.45f), dark, "Anvil");
            PrimitiveBuilder.Part(anvil.transform, PrimitiveType.Sphere, new Vector3(0.55f, 0.78f, 0), new Vector3(0.35f, 0.2f, 0.3f), dark, "Horn");
            PrimitiveBuilder.Part(anvil.transform, PrimitiveType.Cube, new Vector3(-0.2f, 0.98f, 0.1f), new Vector3(0.12f, 0.12f, 0.45f), new Color(0.5f, 0.4f, 0.3f), "Hammer");
            PrimitiveBuilder.Part(anvil.transform, PrimitiveType.Sphere, new Vector3(0.6f, 1.15f, 0.35f), Vector3.one * 0.18f, new Color(1f, 0.85f, 0.3f), "Bell");
            PrimitiveBuilder.AddBoxCollider(anvil, new Vector3(0, 0.6f, 0), new Vector3(1.3f, 1.2f, 0.9f));
            string fid = facilityId;
            SimpleInteractable.Attach(anvil, "金床（鍛冶屋）", "Anvil (blacksmith)", p => interactions.OpenAnvilMenu(p, fid));
        }

        public override void OnPlayStarted()
        {
            ToolUseRouter.Register(toolHandler);
            Game.Service<IResidentDirectory>()?.RegisterInteractionProvider(interactions);
            Game.Service<ITabletService>()?.RegisterApp(app);
        }

        public override void Teardown()
        {
            ToolUseRouter.Unregister(toolHandler);
            sceneBuilt = false;
            for (int f = 0; f <= MiningTuning.FloorCount; f++)
            {
                views[f].Clear();
                nodeRoots[f] = null;
            }
        }

        public override void Tick(float deltaTime)
        {
            clankToastCooldown -= deltaTime;
            var p = LocalPlayer;
            if (p == null || !AreaIds.IsMine(p.CurrentAreaId)) return;
            int f = AreaIds.MineFloorOf(p.CurrentAreaId);
            // 落下の救済（坑道の外へ落ちた場合）
            if (p.Avatar != null && f >= 1 && f <= MiningTuning.FloorCount && p.Position.y < MiningTuning.MineOrigin.y - 15f)
            {
                var L = layouts[f];
                if (L != null) p.Position = L.CellCenter(L.Entry) + Vector3.up * 0.5f;
            }
            // 最下層：星の結晶の広間（初めて来た時の短い演出 §12）
            hallCheckTimer -= deltaTime;
            if (hallCheckTimer <= 0f && f == MiningTuning.FloorCount && !log.sawDeepestHall)
            {
                hallCheckTimer = 0.5f;
                var L = layouts[f];
                if (L != null && L.HasDeepHall)
                {
                    var c = L.CellCenter(L.DeepHall.xMax - 2, L.DeepHall.y + L.DeepHall.height / 2);
                    if ((p.Position - c).sqrMagnitude < 9f * 9f) OnDeepHallReached(p);
                }
            }
        }

        public override void OnDayStarted(int day)
        {
            // 【決定】日付更新で即座に再配置。プレイヤーが鉱山内にいても延期しない。最低保証も追加しない。
            Regenerate(day, true);
            if (IsAuthority) MarkDirty();
            var p = LocalPlayer;
            if (p != null && AreaIds.IsMine(p.CurrentAreaId))
            {
                UIManager.Toast(Loc.T("日付が変わった瞬間、鉱石がきらめいて入れ替わった！", "As the date changed, the ores glittered and swapped places!"), 4f);
                RecordSighting(AreaIds.MineFloorOf(p.CurrentAreaId));
            }
        }

        // =====================================================================
        // 日替わりの鉱石
        // =====================================================================

        private void Regenerate(int day, bool animate)
        {
            oreDay = day;
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                if (layouts[f] == null) continue;
                nodes[f] = MineNodeGenerator.Generate(seed, day, layouts[f], out var fav, out var gemDay);
                dayInfo[f] = new DayInfo { FavoredOre = fav, GemDay = gemDay };
                mined[f].Clear();
                damage[f].Clear();
            }
            if (sceneBuilt) for (int f = 1; f <= MiningTuning.FloorCount; f++) SyncFloorViews(f, animate);
        }

        /// <summary>表示を状態に合わせる（冪等）。日付の違う古い表示は光って消え、新しい表示は光って現れる。</summary>
        private void SyncFloorViews(int f, bool animate)
        {
            if (!sceneBuilt || nodeRoots[f] == null || nodes[f] == null) return;
            var dict = views[f];
            var remove = new List<int>();
            foreach (var kv in dict)
            {
                var v = kv.Value;
                if (v == null) { remove.Add(kv.Key); continue; }
                if (v.day != oreDay)
                {
                    v.Vanish();
                    remove.Add(kv.Key);
                }
                else if (mined[f].Contains(kv.Key))
                {
                    var node = kv.Key < nodes[f].Count ? nodes[f][kv.Key] : null;
                    v.Break(node != null && node.ItemId != null ? ItemRegistry.GetOrPlaceholder(node.ItemId).Color : new Color(0.6f, 0.55f, 0.5f));
                    remove.Add(kv.Key);
                }
            }
            foreach (var k in remove) dict.Remove(k);
            foreach (var n in nodes[f])
            {
                if (mined[f].Contains(n.Index) || dict.ContainsKey(n.Index)) continue;
                dict[n.Index] = MineNodeView.Create(this, nodeRoots[f], n, oreDay, animate);
            }
        }

        public DayInfo GetDayInfo(int floor) => floor >= 1 && floor <= MiningTuning.FloorCount ? dayInfo[floor] : default(DayInfo);

        /// <summary>採掘師が話題にする階（行ける範囲で、宝石の多い階があればそこ）。</summary>
        public int TalkTopicFloor()
        {
            int reach = Mathf.Clamp(log.deepest + 1, 1, MiningTuning.FloorCount);
            for (int f = 1; f <= reach; f++) if (dayInfo[f].GemDay) return f;
            int day = Clock != null ? Clock.Day : 1;
            return 1 + day % reach;
        }

        public MineFloorLayout GetLayout(int floor) => floor >= 1 && floor <= MiningTuning.FloorCount ? layouts[floor] : null;

        // =====================================================================
        // 採掘
        // =====================================================================

        public void HitNode(MineNodeView view, PlayerState player)
        {
            if (view == null || player == null || !player.IsLocal) return;
            int f = view.floor;
            if (view.vanishing || view.day != oreDay || nodes[f] == null || view.index >= nodes[f].Count) return;
            if (mined[f].Contains(view.index)) return;
            var node = nodes[f][view.index];
            int tier = player.Tools.GetLevel(ToolType.Pickaxe);
            if (node.Kind != MineNodeKind.Forage)
            {
                if (tier <= 0)
                {
                    UIManager.Toast(Loc.T("ツルハシを持っていません", "You don't have a pickaxe"));
                    return;
                }
                if (tier < node.Hardness)
                {
                    view.PlayClank();
                    if (clankToastCooldown <= 0f)
                    {
                        clankToastCooldown = 2.5f;
                        UIManager.Toast(Loc.Format("カキン！ 硬くて歯が立たない…（「{0}」以上のツルハシが必要。鍛冶屋で強化しよう）",
                            "Clank! Too hard... (needs a \"{0}\" pickaxe or better - ask the blacksmith)", ToolTypeUtil.TierLabel(node.Hardness)), 3f);
                    }
                    return;
                }
                view.PlayHit();
            }
            int day = oreDay;
            int idx = view.index;
            var prof = GetProfile(player.PlayerId);
            WorldAuthority.Submit(new MineHitCommand { floor = f, nodeIndex = idx, day = day, pickaxeTier = Mathf.Max(1, tier), miningLevel = prof.level },
                r => OnHitResult(player, f, idx, day, r));
        }

        private void OnHitResult(PlayerState player, int f, int idx, int day, CommandResult r)
        {
            if (r == null || !r.Success || string.IsNullOrEmpty(r.MessageEn) || !r.MessageEn.StartsWith(MineNodeGenerator.PayloadPrefix)) return;
            var drops = MineNodeGenerator.Decode(r.MessageEn);
            MineNodeData node = day == oreDay && nodes[f] != null && idx < nodes[f].Count ? nodes[f][idx] : null;
            // 経験値・レベル（持ち歩く技能）
            var prof = GetProfile(player.PlayerId);
            int before = prof.level;
            prof.xp += MiningTuning.XpFor(node != null ? node.Kind : MineNodeKind.Ore, node != null ? node.Hardness : 1);
            prof.totalMined++;
            prof.level = MiningTuning.LevelForXp(prof.xp);
            if (prof.level > before) UIManager.Toast(Loc.Format("採掘の腕が上がった！（Lv{0}）", "Mining level up! (Lv{0})", prof.level), 4f);

            foreach (var d in drops)
            {
                var def = ItemRegistry.GetOrPlaceholder(d.ItemId);
                RecordFind(d, f);
                if (def.Category == ItemCategory.Ore || def.Category == ItemCategory.Gem)
                    EventBus.Publish(new OreMinedEvent { PlayerId = player.PlayerId, ItemId = d.ItemId, Floor = f, Quality = d.Quality });
                string extra = d.Size > 0f ? Loc.Format("（{0:0.0}cm）", " ({0:0.0}cm)", d.Size) : "";
                UIManager.Toast(Loc.Format("{0} {1}×{2}{3}", "{0} {1}x{2}{3}", def.Name, ItemStack.QualityStars(d.Quality), d.Count, extra), 2.5f);
            }
            // ゲスト：ホストのスナップショットを待たずに表示を更新（確定はホスト）
            if (!IsAuthority && day == oreDay)
            {
                mined[f].Add(idx);
                SyncFloorViews(f, false);
            }
        }

        private CommandResult HandleHit(MineHitCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("観光中は採掘できません", "Sightseers can't mine");
            if (cmd.day != oreDay) return CommandResult.Fail("鉱石は入れ替わってしまった", "The ores have already swapped");
            int f = cmd.floor;
            if (f < 1 || f > MiningTuning.FloorCount || nodes[f] == null || cmd.nodeIndex < 0 || cmd.nodeIndex >= nodes[f].Count)
                return CommandResult.Fail("そこには何もない", "Nothing there");
            if (mined[f].Contains(cmd.nodeIndex)) return CommandResult.Fail("ほかの誰かが先に掘った", "Someone mined it first");
            var node = nodes[f][cmd.nodeIndex];
            float dmg;
            if (node.Kind == MineNodeKind.Forage) dmg = node.Hp;
            else
            {
                if (cmd.pickaxeTier < node.Hardness) return CommandResult.Fail("硬くて掘れない", "Too hard to mine");
                dmg = MiningTuning.HitDamage(cmd.pickaxeTier, node.Hardness, cmd.miningLevel);
            }
            damage[f].TryGetValue(cmd.nodeIndex, out var total);
            total += dmg;
            if (total < node.Hp)
            {
                damage[f][cmd.nodeIndex] = total;
                return CommandResult.Ok(null, "hit");
            }
            mined[f].Add(cmd.nodeIndex);
            damage[f].Remove(cmd.nodeIndex);
            var drops = MineNodeGenerator.RollDrops(node, cmd.miningLevel);
            foreach (var d in drops)
            {
                ctx.GiveItem(new ItemStack(d.ItemId, d.Count, d.Quality) { size = d.Size }, "mining");
                if (!ctx.IsLocalIssuer)
                {
                    var def = ItemRegistry.GetOrPlaceholder(d.ItemId);
                    if (def.Category == ItemCategory.Ore || def.Category == ItemCategory.Gem)
                        EventBus.Publish(new OreMinedEvent { PlayerId = ctx.IssuerId, ItemId = d.ItemId, Floor = f, Quality = d.Quality });
                }
            }
            MarkDirty();
            SyncFloorViews(f, false);
            return CommandResult.Ok(null, MineNodeGenerator.Encode(drops));
        }

        // =====================================================================
        // 移動（入口・昇降機・階段）
        // =====================================================================

        public void OpenEntrance(PlayerState player)
        {
            var options = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("階段で地下1階へ", "Take the stairs to B1F"), () => GoToFloor(player, 1, true))
            };
            foreach (var f in ReachedFloorsSorted())
                if (f > 1) options.Add(new ChoiceOption(Loc.Format("昇降機で地下{0}階へ", "Elevator to B{0}F", f), () => GoToFloor(player, f, false)));
            options.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            if (options.Count == 2)
            {
                GoToFloor(player, 1, true);
                return;
            }
            UIManager.Choose(Loc.T("鉱山の入口", "Mine entrance"), Loc.T("どこへ行く？", "Where to?"), options);
        }

        public void OpenElevator(PlayerState player, int currentFloor)
        {
            var options = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("地上へ戻る", "Return to the surface"), () => ExitToSurface(player))
            };
            foreach (var f in ReachedFloorsSorted())
                if (f != currentFloor) options.Add(new ChoiceOption(Loc.Format("地下{0}階へ", "To B{0}F", f), () => GoToFloor(player, f, false)));
            options.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(Loc.T("昇降機", "Elevator"), Loc.T("到達した階へ直接行ける。", "Go straight to any floor you have reached."), options);
        }

        private List<int> ReachedFloorsSorted()
        {
            var list = new List<int>(log.reachedFloors);
            list.Sort();
            return list;
        }

        public void GoToFloor(PlayerState player, int floor, bool viaStairs)
        {
            if (player == null || floor < 1 || floor > MiningTuning.FloorCount) return;
            var L = layouts[floor];
            if (L == null) return;
            var spawn = viaStairs ? L.CellCenter(L.Entry) : L.CellCenter(L.Elevator) + new Vector3(MiningTuning.Cell, 0f, 0f);
            Game.Areas.Transition(player, AreaIds.MineFloor(floor), spawn + Vector3.up * 0.2f, 0f);
        }

        public void ExitToSurface(PlayerState player)
        {
            if (player == null) return;
            Game.Areas.Transition(player, AreaIds.Overworld, surfaceExit, surfaceExitYaw);
        }

        private void OnEnterFloor(PlayerState p, int f)
        {
            if (p == null || !p.IsLocal) return;
            bool first = !log.reachedFloors.Contains(f);
            if (first) log.reachedFloors.Add(f);
            UIManager.Toast(first ? Loc.Format("鉱山 地下{0}階（初めて来た）", "Mine B{0}F (first visit)", f) : Loc.Format("鉱山 地下{0}階", "Mine B{0}F", f), 2.5f);
            if (f > log.deepest)
            {
                log.deepest = f;
                WorldAuthority.Submit(new ReachFloorCommand { floor = f });
                if (f == MiningTuning.FloorCount)
                    UIManager.Toast(Loc.T("ここが鉱山のいちばん深い階のようだ。", "This seems to be the deepest floor of the mine."), 4f);
            }
            RecordSighting(f);
        }

        private void OnDeepHallReached(PlayerState p)
        {
            log.sawDeepestHall = true;
            UIManager.Message(Loc.T("星の結晶の広間", "Hall of the Star Crystal"),
                Loc.T("鉱山のいちばん奥で、巨大な星の結晶がゆっくりと脈打つように光っている。\nこの星の鉱石は、ここから毎日生まれ変わっているのかもしれない……。",
                    "In the deepest part of the mine, a giant star crystal glows with a slow pulse.\nPerhaps this planet's ores are reborn from here every day..."));
            Game.Service<ITabletService>()?.AddMaterial(new TabletMaterial
            {
                id = "mining.deep_hall",
                kind = TabletMaterialKind.Photo,
                titleJa = "星の結晶の広間",
                titleEn = "Hall of the Star Crystal",
                factJa = string.Format("{0}日目、鉱山の地下{1}階の奥で巨大な光る結晶を見た。", Clock != null ? Clock.Day : 1, MiningTuning.FloorCount),
                factEn = string.Format("Day {0}: saw a giant glowing crystal deep in mine B{1}F.", Clock != null ? Clock.Day : 1, MiningTuning.FloorCount),
                day = Clock != null ? Clock.Day : 1,
                tags = new List<string> { "mining" }
            });
        }

        // =====================================================================
        // 記録（PlayerPlanetKnowledge）
        // =====================================================================

        private void RecordFind(MineDrop d, int floor)
        {
            MineLogEntry e = null;
            foreach (var x in log.entries) if (x.itemId == d.ItemId) e = x;
            if (e == null)
            {
                e = new MineLogEntry { itemId = d.ItemId, firstDay = Clock != null ? Clock.Day : 1, firstFloor = floor };
                log.entries.Add(e);
            }
            e.count += d.Count;
            if (!e.floors.Contains(floor)) e.floors.Add(floor);
            e.bestSize = Mathf.Max(e.bestSize, d.Size);
            e.bestQuality = Mathf.Max(e.bestQuality, d.Quality);
        }

        private void RecordSighting(int f)
        {
            if (f < 1 || f > MiningTuning.FloorCount || nodes[f] == null) return;
            int day = oreDay;
            MineFloorSighting s = null;
            foreach (var x in log.sightings) if (x.floor == f && x.day == day) s = x;
            if (s == null)
            {
                s = new MineFloorSighting { floor = f, day = day };
                log.sightings.Add(s);
            }
            s.itemIds.Clear();
            foreach (var n in nodes[f])
            {
                if (n.ItemId == null || n.Kind == MineNodeKind.Forage || mined[f].Contains(n.Index)) continue;
                if (!s.itemIds.Contains(n.ItemId)) s.itemIds.Add(n.ItemId);
            }
            while (log.sightings.Count > 40) log.sightings.RemoveAt(0);
        }

        // =====================================================================
        // ツルハシ強化・買い取り・到達報告（ホスト権威）
        // =====================================================================

        public void RequestPickaxeUpgrade(PlayerState player, string residentId, int targetTier, System.Action<bool> done)
        {
            var up = PickaxeUpgrade.ForTier(targetTier);
            if (up == null) { done?.Invoke(false); return; }
            WorldAuthority.Submit(new UpgradePickaxeCommand { targetTier = targetTier, residentId = residentId }, r =>
            {
                bool ok = r != null && r.Success;
                if (ok)
                {
                    player.Tools.SetLevel(ToolType.Pickaxe, targetTier);
                    EventBus.Publish(new ToolUpgradedEvent { PlayerId = player.PlayerId, Tool = ToolType.Pickaxe, NewLevel = targetTier });
                }
                done?.Invoke(ok);
            });
        }

        private CommandResult HandleUpgrade(UpgradePickaxeCommand cmd, CommandContext ctx)
        {
            var up = PickaxeUpgrade.ForTier(cmd.targetTier);
            if (up == null) return CommandResult.Fail("これ以上は強化できません", "Cannot upgrade further");
            foreach (var kv in up.Ores)
                if (!ctx.HasItem(kv.Key, kv.Value)) return CommandResult.Fail("鉱石が足りません", "Not enough ore");
            if (!ctx.TryCharge(up.Money, "mining.pickaxe_upgrade")) return CommandResult.Fail("お金が足りません", "Not enough money");
            foreach (var kv in up.Ores)
            {
                if (!ctx.TakeItem(kv.Key, kv.Value, "mining.pickaxe_upgrade"))
                {
                    ctx.PayMoney(up.Money, "mining.pickaxe_upgrade_refund");
                    return CommandResult.Fail("鉱石を渡せませんでした", "Could not hand over the ore");
                }
            }
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null && !string.IsNullOrEmpty(cmd.residentId) && dir.Get(cmd.residentId) != null)
            {
                dir.GiveMoney(cmd.residentId, Mathf.RoundToInt(up.Money * MiningTuning.BlacksmithFeeShare), "mining.pickaxe_upgrade_fee");
                dir.ChangeFriendship(cmd.residentId, ctx.IssuerId, 2, "mining.pickaxe_upgrade");
                dir.AddMemory(cmd.residentId, new ResidentMemoryEntry { kind = "upgraded_pickaxe", playerId = ctx.IssuerId, day = Clock != null ? Clock.Day : 1, importance = 0.5f });
            }
            if (!ctx.IsLocalIssuer)
                EventBus.Publish(new ToolUpgradedEvent { PlayerId = ctx.IssuerId, Tool = ToolType.Pickaxe, NewLevel = cmd.targetTier });
            return CommandResult.Ok();
        }

        private CommandResult HandleSell(SellToBlacksmithCommand cmd, CommandContext ctx)
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || string.IsNullOrEmpty(cmd.residentId) || dir.Get(cmd.residentId) == null)
                return CommandResult.Fail("買い取ってくれる人がいない", "No one to buy it");
            var def = ItemRegistry.Get(cmd.itemId);
            if (def == null || def.SourceActivity != "mining" || cmd.count <= 0) return CommandResult.Fail("買い取れない物です", "Can't buy that");
            int today = Clock != null ? Clock.Day : 1;
            if (planetExtra.buybackDay != today)
            {
                planetExtra.buybackDay = today;
                planetExtra.buybackCount = 0;
            }
            int allowed = Mathf.Min(cmd.count, MiningTuning.BuybackDailyLimit - planetExtra.buybackCount);
            if (allowed <= 0)
            {
                dir.Speak(cmd.residentId, new Utterance(cmd.residentId, "mining.buyback.full")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ORE).Plural()).Adverb(Concepts.ALREADY))
                    .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).When(Concepts.TOMORROW))
                    .Face(Expression.Worried).With(Gesture.ShakeHead));
                return CommandResult.Fail("今日はもう買い取ってもらえない", "No more buyback today");
            }
            if (!ctx.HasItem(cmd.itemId, allowed)) return CommandResult.Fail("持っていません", "You don't have it");
            int price = Mathf.Max(1, Mathf.RoundToInt(def.BasePrice * MiningTuning.BuybackRate)) * allowed;
            if (!dir.TrySpend(cmd.residentId, price, "mining.buyback"))
            {
                dir.Speak(cmd.residentId, new Utterance(cmd.residentId, "mining.buyback.no_money")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAVE).Agent(Concepts.I).Patient(Concepts.MONEY).Not())
                    .Face(Expression.Sad).With(Gesture.Shrug));
                return CommandResult.Fail("鍛冶屋の財布が空っぽのようだ", "The smith seems to be out of money");
            }
            if (!ctx.TakeItem(cmd.itemId, allowed, "mining.buyback"))
            {
                dir.GiveMoney(cmd.residentId, price, "mining.buyback_refund");
                return CommandResult.Fail("渡せませんでした", "Could not hand it over");
            }
            ctx.PayMoney(price, "mining.buyback");
            dir.GiveItem(cmd.residentId, new ItemStack(cmd.itemId, allowed), "mining.buyback");
            planetExtra.buybackCount += allowed;
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleReachFloor(ReachFloorCommand cmd, CommandContext ctx)
        {
            int f = Mathf.Clamp(cmd.floor, 1, MiningTuning.FloorCount);
            foreach (var p in planetExtra.deepestByPlayer)
            {
                if (p.key != ctx.IssuerId) continue;
                if (f > p.value) { p.value = f; MarkDirty(); }
                return CommandResult.Ok();
            }
            planetExtra.deepestByPlayer.Add(new StringIntPair(ctx.IssuerId, f));
            MarkDirty();
            return CommandResult.Ok();
        }

        // =====================================================================
        // IMiningService
        // =====================================================================

        public int FloorCount => MiningTuning.FloorCount;

        public int GetDeepestFloorReached(string playerId)
        {
            if (LocalPlayer != null && playerId == LocalPlayer.PlayerId) return log.deepest;
            foreach (var p in planetExtra.deepestByPlayer) if (p.key == playerId) return p.value;
            return 0;
        }

        public IEnumerable<string> OreIdsOnFloor(int floor)
        {
            foreach (var m in MineCatalog.All)
            {
                if (m.FromRockOnly || m.Kind == MineNodeKind.Forage || m.Kind == MineNodeKind.Rock) continue;
                if (floor >= m.MinFloor && floor <= m.MaxFloor) yield return m.Id;
            }
        }

        public int GetMiningLevel(string playerId) => GetProfile(playerId).level;

        public MiningProfileData GetProfile(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) playerId = LocalPlayer != null ? LocalPlayer.PlayerId : "local";
            if (!profiles.TryGetValue(playerId, out var p))
            {
                p = new MiningProfileData { playerId = playerId };
                profiles[playerId] = p;
            }
            return p;
        }

        // =====================================================================
        // セーブ
        // =====================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    var d = new MiningPlanetSave
                    {
                        oreDay = oreDay,
                        deepestByPlayer = planetExtra.deepestByPlayer,
                        buybackDay = planetExtra.buybackDay,
                        buybackCount = planetExtra.buybackCount
                    };
                    for (int f = 1; f <= MiningTuning.FloorCount; f++)
                    {
                        if (mined[f].Count == 0 && damage[f].Count == 0) continue;
                        var fs = new MineFloorState { floor = f };
                        fs.mined.AddRange(mined[f]);
                        foreach (var kv in damage[f])
                        {
                            fs.damagedIdx.Add(kv.Key);
                            fs.damagedVal.Add(kv.Value);
                        }
                        d.floors.Add(fs);
                    }
                    return JsonUtility.ToJson(d);
                }
                case SaveScope.PlayerProfile:
                {
                    var save = new MiningProfileSave();
                    save.players.Add(GetProfile(playerId));
                    return JsonUtility.ToJson(save);
                }
                case SaveScope.PlayerPlanetKnowledge:
                    if (LocalPlayer != null && playerId != LocalPlayer.PlayerId) return null;
                    return JsonUtility.ToJson(log);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    var d = JsonUtility.FromJson<MiningPlanetSave>(json);
                    if (d == null) return;
                    // ゲストが先に日付更新を済ませた直後に届いた、1日前のスナップショットは無視（ホストもすぐ同じ日へ進む）
                    if (!IsAuthority && Game != null && Game.State == GameState.Playing && d.oreDay == oreDay - 1) return;
                    bool dayChanged = d.oreDay != oreDay || nodes[1] == null;
                    if (dayChanged) Regenerate(d.oreDay, sceneBuilt);
                    for (int f = 1; f <= MiningTuning.FloorCount; f++)
                    {
                        mined[f].Clear();
                        damage[f].Clear();
                    }
                    if (d.floors != null)
                    {
                        foreach (var fs in d.floors)
                        {
                            if (fs == null || fs.floor < 1 || fs.floor > MiningTuning.FloorCount) continue;
                            if (fs.mined != null) foreach (var i in fs.mined) mined[fs.floor].Add(i);
                            if (fs.damagedIdx != null && fs.damagedVal != null)
                                for (int i = 0; i < fs.damagedIdx.Count && i < fs.damagedVal.Count; i++) damage[fs.floor][fs.damagedIdx[i]] = fs.damagedVal[i];
                        }
                    }
                    planetExtra.deepestByPlayer = d.deepestByPlayer ?? new List<StringIntPair>();
                    planetExtra.buybackDay = d.buybackDay;
                    planetExtra.buybackCount = d.buybackCount;
                    if (sceneBuilt) for (int f = 1; f <= MiningTuning.FloorCount; f++) SyncFloorViews(f, false);
                    break;
                }
                case SaveScope.PlayerProfile:
                {
                    var d = JsonUtility.FromJson<MiningProfileSave>(json);
                    if (d == null || d.players == null) break;
                    foreach (var p in d.players)
                    {
                        if (p == null) continue;
                        if (string.IsNullOrEmpty(p.playerId)) p.playerId = playerId;
                        p.level = MiningTuning.LevelForXp(p.xp);
                        profiles[playerId ?? p.playerId] = p;
                    }
                    break;
                }
                case SaveScope.PlayerPlanetKnowledge:
                {
                    var d = JsonUtility.FromJson<MineLogSave>(json);
                    if (d == null) break;
                    if (d.reachedFloors == null) d.reachedFloors = new List<int>();
                    if (d.entries == null) d.entries = new List<MineLogEntry>();
                    if (d.sightings == null) d.sightings = new List<MineFloorSighting>();
                    log = d;
                    break;
                }
            }
        }

        // =====================================================================
        // 道具（ツルハシ）：レイが直接当たらなかった時は、前方の近いノードを叩く
        // =====================================================================

        private sealed class ToolHandler : IToolHandler
        {
            private readonly MiningSystem sys;
            public ToolHandler(MiningSystem sys) { this.sys = sys; }
            public int Priority => 40;

            public bool TryUseTool(ToolUseContext ctx)
            {
                if (ctx == null || ctx.Tool != ToolType.Pickaxe || !AreaIds.IsMine(ctx.AreaId)) return false;
                int f = AreaIds.MineFloorOf(ctx.AreaId);
                if (f < 1 || f > MiningTuning.FloorCount) return false;
                MineNodeView best = null;
                float bestD = 2.6f * 2.6f;
                var origin = ctx.Player.Position;
                foreach (var v in sys.views[f].Values)
                {
                    if (v == null || v.vanishing) continue;
                    var to = v.transform.position - origin;
                    to.y = 0;
                    float sq = to.sqrMagnitude;
                    if (sq > bestD) continue;
                    if (sq > 0.25f && Vector3.Dot(to.normalized, ctx.Forward) < 0.3f) continue;
                    bestD = sq;
                    best = v;
                }
                if (best != null) sys.HitNode(best, ctx.Player);
                return true;
            }
        }
    }
}
