using System.Collections.Generic;
using Uchujin.Core;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// 住民1人の実行時オブジェクト。プロフィール（個性）・状態・アバターをまとめ、IResidentInfo を提供する。
    /// </summary>
    public sealed class Resident : IResidentInfo
    {
        public ResidentProfile Profile;
        public ResidentState State;
        public bool Regenerable;
        public ResidentAvatar Avatar;

        // ---- 実行時のみ（保存しない） ----
        internal List<Vector3> path = new List<Vector3>();
        internal int pathIndex;
        internal Vector3 currentTarget;
        internal bool hasTarget;
        internal float nextChatLineTime;
        internal float nextSelfTalkTime;
        internal float lastWaveTime = -999f;
        internal int chatTurn;
        internal string chatTopicKey;
        /// <summary>AIが最後に書き込んだ位置（外部から Transform を動かされたか判定する）。</summary>
        internal Vector3 lastWrittenPos;
        internal bool hasWrittenPos;

        public Resident(ResidentProfile profile, ResidentState state, bool regenerable)
        {
            Profile = profile;
            State = state;
            Regenerable = regenerable;
        }

        // ================= IResidentInfo =================
        public string Id => Profile.id;
        public AlienLineage Lineage => Profile.lineage;
        public ResidentJob Job => State.job;
        public bool IsActive => State.active;
        public Vector3 Position => Avatar != null ? Avatar.transform.position : new Vector3(State.px, State.py, State.pz);
        public Transform Transform => Avatar != null ? Avatar.transform : null;
        public ResidentActivity Activity => State.activity;
        public int Money => State.money;
        public string HomeFacilityId => State.homeFacilityId;
        public int MovedInDay => State.movedInDay;
        public Color MainColor => Profile.appearance.bodyColor;

        public bool IsGuide { get; internal set; }
        public bool IsVisitingFormer => !State.active && State.visitDay >= 0 && Game.Instance != null && Game.Instance.Time != null && Game.Instance.Time.Day == State.visitDay;
        public bool IsMovingOut => State.active && State.moveOutDay >= 0;
        public ResidentPersonality P => Profile.personality;

        /// <summary>
        /// アイテムへの好み -1〜1（内部の真実）。タグの好み・好きな色・カテゴリ・職業・趣味・苦手・希少度から算出する。
        /// 同じ品物でも住民ごとに評価が違う（商店 §3.2）。
        /// </summary>
        public float EvaluateItem(ItemDefinition item)
        {
            if (item == null) return 0f;
            float score = 0f;
            foreach (var tw in Profile.likes)
                if (item.HasTag(tw.tag)) score += tw.weight;
            string cat = item.Category.ToString();
            foreach (var cw in Profile.categoryLikes)
                if (cw.tag == cat) score += cw.weight;
            if (!string.IsNullOrEmpty(Profile.favoriteColorTag) && item.HasTag(Profile.favoriteColorTag)) score += 0.35f;
            if (!string.IsNullOrEmpty(Profile.dislikedTag) && item.HasTag(Profile.dislikedTag)) score -= 0.55f;
            foreach (var t in JobTable.JobTags(State.job))
                if (item.HasTag(t)) { score += 0.18f; break; }
            if (item.HasTag(JobTable.HobbyTag(Profile.hobby))) score += 0.22f;
            if (item.HasTag(JobTable.HobbyTag(Profile.hobby2))) score += 0.1f;
            // 収集家・好奇心旺盛な住民は珍しい物を好む
            if (item.Rarity >= 3) score += 0.1f * (item.Rarity - 2) * (P.curiosity + (Profile.hobby == ResidentHobby.Collecting ? 0.6f : 0f));
            // 食いしん坊は食べ物に甘い
            if (item.Consumable || item.HasTag(ItemTags.Food) || item.HasTag(ItemTags.Edible)) score += 0.15f * P.gluttony;
            // 売り物にならない物（手紙・鍵など）は贈り物としては微妙
            if (item.Category == ItemCategory.KeyItem || item.Category == ItemCategory.Tool) score -= 0.2f;
            return Mathf.Clamp(score, -1f, 1f);
        }

        public int GetFriendship(string playerId)
        {
            var b = FindBond(playerId);
            return b != null ? b.friendship : 0;
        }

        public bool Knows(string playerId)
        {
            var b = FindBond(playerId);
            return b != null && b.met;
        }

        // ================= 補助 =================

        public PlayerBond FindBond(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            foreach (var b in State.bonds) if (b.playerId == playerId) return b;
            return null;
        }

        public PlayerBond Bond(string playerId)
        {
            var b = FindBond(playerId);
            if (b != null) return b;
            b = new PlayerBond { playerId = playerId };
            State.bonds.Add(b);
            return b;
        }

        public RelationEntry FindRelation(string otherId)
        {
            if (string.IsNullOrEmpty(otherId)) return null;
            foreach (var r in State.relations) if (r.otherId == otherId) return r;
            return null;
        }

        public RelationEntry Relation(string otherId)
        {
            var r = FindRelation(otherId);
            if (r != null) return r;
            r = new RelationEntry { otherId = otherId, kind = RelationKind.Acquaintance };
            State.relations.Add(r);
            return r;
        }

        public bool IsQuarrelingWith(string otherId, int today)
        {
            var r = FindRelation(otherId);
            return r != null && r.quarrelUntilDay >= today;
        }

        public float Affinity(string otherId)
        {
            var r = FindRelation(otherId);
            return r != null ? r.affinity : 0f;
        }

        public string BestFriendId(Dictionary<string, Resident> all, bool activeOnly = true)
        {
            string best = null;
            float bestA = 20f;
            foreach (var r in State.relations)
            {
                if (r.affinity <= bestA) continue;
                if (activeOnly && (!all.TryGetValue(r.otherId, out var o) || !o.State.active)) continue;
                bestA = r.affinity;
                best = r.otherId;
            }
            return best;
        }

        public int CountItem(string itemId)
        {
            int n = 0;
            foreach (var s in State.items) if (s != null && !s.IsEmpty && s.itemId == itemId) n += s.count;
            return n;
        }

        public void AddItem(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty) return;
            foreach (var s in State.items)
            {
                if (s != null && s.CanStackWith(stack))
                {
                    s.count += stack.count;
                    return;
                }
            }
            State.items.Add(stack.Clone());
        }

        public bool RemoveItem(string itemId, int count = 1)
        {
            for (int i = 0; i < State.items.Count && count > 0; i++)
            {
                var s = State.items[i];
                if (s == null || s.IsEmpty || s.itemId != itemId) continue;
                int take = Mathf.Min(count, s.count);
                s.count -= take;
                count -= take;
            }
            State.items.RemoveAll(s => s == null || s.IsEmpty);
            return count <= 0;
        }

        /// <summary>持ち物の中で条件に合う最初の物。</summary>
        public ItemStack FindItem(System.Func<ItemDefinition, bool> predicate)
        {
            foreach (var s in State.items)
            {
                if (s == null || s.IsEmpty) continue;
                if (predicate(s.Definition)) return s;
            }
            return null;
        }

        public static bool IsEdible(ItemDefinition d)
        {
            if (d == null) return false;
            if (d.HasTag(ItemTags.Edible) || d.HasTag(ItemTags.Food) || d.HasTag(ItemTags.Drink)) return true;
            switch (d.Category)
            {
                case ItemCategory.Food:
                case ItemCategory.Processed:
                case ItemCategory.Fish:
                case ItemCategory.Crop:
                    return true;
            }
            return d.Consumable && d.Category != ItemCategory.CreatureFood && d.Category != ItemCategory.Fertilizer && d.Category != ItemCategory.Bait;
        }

        public static bool IsDisplayable(ItemDefinition d)
        {
            if (d == null) return false;
            if (d.Category == ItemCategory.Furniture || d.Category == ItemCategory.Decor || d.Category == ItemCategory.Gem) return true;
            return d.HasTag(ItemTags.Flower) || d.HasTag(ItemTags.Furniture) || d.HasTag(ItemTags.Shiny);
        }

        public void AddMemory(ResidentMemoryEntry e)
        {
            if (e == null) return;
            State.memories.Add(e);
            if (State.memories.Count > ResidentTuning.MaxMemories)
            {
                // 重要度の低い古い記憶から忘れる（§15：長期記憶。重要な記憶は残す）
                int worst = -1;
                float worstScore = float.MaxValue;
                for (int i = 0; i < State.memories.Count - 1; i++)
                {
                    var m = State.memories[i];
                    float s = m.importance * 10f + m.day * 0.1f;
                    if (s < worstScore)
                    {
                        worstScore = s;
                        worst = i;
                    }
                }
                if (worst >= 0) State.memories.RemoveAt(worst);
            }
        }

        public ResidentMemoryEntry LatestMemory(string kind, string playerId = null, int sinceDay = -1)
        {
            for (int i = State.memories.Count - 1; i >= 0; i--)
            {
                var m = State.memories[i];
                if (m.kind != kind) continue;
                if (playerId != null && m.playerId != playerId) continue;
                if (sinceDay >= 0 && m.day < sinceDay) continue;
                return m;
            }
            return null;
        }

        public int CountMemories(string kind, string playerId = null, int sinceDay = -1)
        {
            int n = 0;
            foreach (var m in State.memories)
            {
                if (m.kind != kind) continue;
                if (playerId != null && m.playerId != playerId) continue;
                if (sinceDay >= 0 && m.day < sinceDay) continue;
                n++;
            }
            return n;
        }

        public bool HasMemory(string kind, string playerId, int day)
        {
            foreach (var m in State.memories)
                if (m.kind == kind && m.playerId == playerId && m.day == day) return true;
            return false;
        }

        public bool HasMemoryWithResident(string kind, string otherId, int day)
        {
            foreach (var m in State.memories)
                if (m.kind == kind && m.otherResidentId == otherId && m.day == day) return true;
            return false;
        }

        public bool HasMemoryKindOnDay(string kind, int day)
        {
            foreach (var m in State.memories)
                if (m.kind == kind && m.day == day) return true;
            return false;
        }

        public Vector3 StatePosition
        {
            get => new Vector3(State.px, State.py, State.pz);
            set
            {
                State.px = value.x;
                State.py = value.y;
                State.pz = value.z;
            }
        }

        public float SpeedFactor
        {
            get
            {
                float f = 1f;
                if (Profile.age == ResidentAge.Child) f *= ResidentTuning.ChildSpeedFactor;
                else if (Profile.age == ResidentAge.Elder) f *= ResidentTuning.ElderSpeedFactor;
                f *= Mathf.Lerp(1.1f, 0.85f, P.easygoing);
                return f;
            }
        }

        public bool IsBirthday(int day) => ((day - 1) % ResidentTuning.BirthdayCycleDays) == Profile.birthdayIndex;
    }
}
