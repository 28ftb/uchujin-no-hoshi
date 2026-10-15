using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// スロット式の持ち物・収納。プレイヤーの持ち物、自宅の収納箱、商店の倉庫などで共通に使う。
    /// 空きスロットは count=0 の ItemStack で表す（JsonUtility が null 要素を扱えないため）。
    /// </summary>
    [Serializable]
    public class Inventory
    {
        public List<ItemStack> slots = new List<ItemStack>();
        public int capacity = 24;
        /// <summary>イベント発行用の持ち主ID（プレイヤーIDまたは収納ID）。</summary>
        public string ownerId;

        [NonSerialized] public Action Changed;

        public Inventory() { }

        public Inventory(int capacity, string ownerId)
        {
            this.capacity = capacity;
            this.ownerId = ownerId;
            EnsureSlots();
        }

        public void EnsureSlots()
        {
            if (slots == null) slots = new List<ItemStack>();
            while (slots.Count < capacity) slots.Add(new ItemStack());
            for (int i = 0; i < slots.Count; i++) if (slots[i] == null) slots[i] = new ItemStack();
        }

        public void SetCapacity(int newCapacity)
        {
            capacity = Mathf.Max(capacity, newCapacity);
            EnsureSlots();
            Changed?.Invoke();
        }

        public ItemStack GetSlot(int index)
        {
            EnsureSlots();
            if (index < 0 || index >= slots.Count) return null;
            return slots[index];
        }

        public int CountOf(string itemId, int minQuality = 0)
        {
            int n = 0;
            foreach (var s in slots) if (s != null && !s.IsEmpty && s.itemId == itemId && s.quality >= minQuality) n += s.count;
            return n;
        }

        public bool Has(string itemId, int count = 1, int minQuality = 0) => CountOf(itemId, minQuality) >= count;

        public int CountWhere(Func<ItemStack, bool> predicate)
        {
            int n = 0;
            foreach (var s in slots) if (s != null && !s.IsEmpty && predicate(s)) n += s.count;
            return n;
        }

        /// <summary>入る数（全部入るか事前に確認する用）。</summary>
        public int SpaceFor(ItemStack stack)
        {
            EnsureSlots();
            int max = Mathf.Max(1, stack.Definition.MaxStack);
            int space = 0;
            foreach (var s in slots)
            {
                if (s.IsEmpty) space += max;
                else if (s.CanStackWith(stack)) space += Mathf.Max(0, max - s.count);
            }
            return space;
        }

        public bool CanAdd(ItemStack stack) => stack != null && !stack.IsEmpty && SpaceFor(stack) >= stack.count;

        /// <summary>追加し、入りきらなかった数を返す（0なら全部入った）。</summary>
        public int Add(ItemStack stack, string source = null)
        {
            if (stack == null || stack.IsEmpty) return 0;
            EnsureSlots();
            int max = Mathf.Max(1, stack.Definition.MaxStack);
            int remaining = stack.count;
            // 既存の束へ
            foreach (var s in slots)
            {
                if (remaining <= 0) break;
                if (s.IsEmpty || !s.CanStackWith(stack)) continue;
                int add = Mathf.Min(remaining, max - s.count);
                if (add <= 0) continue;
                s.count += add;
                if (stack.size > s.size) s.size = stack.size;
                remaining -= add;
            }
            // 空きスロットへ
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;
                int add = Mathf.Min(remaining, max);
                var copy = stack.CloneWithCount(add);
                slots[i] = copy;
                remaining -= add;
            }
            int added = stack.count - remaining;
            if (added > 0)
            {
                EventBus.Publish(new ItemAddedEvent { PlayerId = ownerId, Stack = stack.CloneWithCount(added), Source = source });
                Changed?.Invoke();
            }
            return remaining;
        }

        /// <summary>指定数を取り除き、実際に取り除いた数を返す。品質の低い物から使う。</summary>
        public int Remove(string itemId, int count, int minQuality = 0, string reason = null)
        {
            if (count <= 0) return 0;
            int removed = 0;
            for (int q = minQuality; q <= 3 && removed < count; q++)
            {
                for (int i = 0; i < slots.Count && removed < count; i++)
                {
                    var s = slots[i];
                    if (s.IsEmpty || s.itemId != itemId || s.quality != q) continue;
                    int take = Mathf.Min(count - removed, s.count);
                    s.count -= take;
                    removed += take;
                    if (s.count <= 0) slots[i] = new ItemStack();
                }
            }
            if (removed > 0)
            {
                EventBus.Publish(new ItemRemovedEvent { PlayerId = ownerId, ItemId = itemId, Count = removed, Reason = reason });
                Changed?.Invoke();
            }
            return removed;
        }

        /// <summary>品質・データ・所有者まで一致する数。</summary>
        public int CountExact(ItemStack like)
        {
            if (like == null || like.IsEmpty) return 0;
            var probe = like.CloneWithCount(1);
            int n = 0;
            foreach (var s in slots) if (s != null && !s.IsEmpty && s.CanStackWith(probe)) n += s.count;
            return n;
        }

        /// <summary>品質・データ・所有者まで一致する物を count 個取り除き、取り除いた束（大きさは最大値）を返す。足りなければ何もせず null。</summary>
        public ItemStack RemoveExact(ItemStack like, int count, string reason = null)
        {
            if (like == null || like.IsEmpty || count <= 0 || CountExact(like) < count) return null;
            var probe = like.CloneWithCount(1);
            ItemStack result = null;
            int left = count;
            for (int i = 0; i < slots.Count && left > 0; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty || !s.CanStackWith(probe)) continue;
                var taken = TakeFromSlot(i, left, reason);
                if (taken == null) continue;
                left -= taken.count;
                if (result == null) result = taken;
                else
                {
                    result.count += taken.count;
                    if (taken.size > result.size) result.size = taken.size;
                }
            }
            return result;
        }

        /// <summary>スロットから取り出す。</summary>
        public ItemStack TakeFromSlot(int index, int count, string reason = null)
        {
            var s = GetSlot(index);
            if (s == null || s.IsEmpty) return null;
            int take = Mathf.Min(count, s.count);
            var result = s.CloneWithCount(take);
            s.count -= take;
            if (s.count <= 0) slots[index] = new ItemStack();
            EventBus.Publish(new ItemRemovedEvent { PlayerId = ownerId, ItemId = result.itemId, Count = take, Reason = reason });
            Changed?.Invoke();
            return result;
        }

        public void Swap(int a, int b)
        {
            EnsureSlots();
            if (a < 0 || b < 0 || a >= slots.Count || b >= slots.Count) return;
            (slots[a], slots[b]) = (slots[b], slots[a]);
            Changed?.Invoke();
        }

        public IEnumerable<ItemStack> NonEmpty()
        {
            foreach (var s in slots) if (s != null && !s.IsEmpty) yield return s;
        }

        public int FreeSlots()
        {
            int n = 0;
            foreach (var s in slots) if (s == null || s.IsEmpty) n++;
            return n;
        }

        public void Clear()
        {
            slots.Clear();
            EnsureSlots();
            Changed?.Invoke();
        }

        public void CopyFrom(Inventory other)
        {
            capacity = other.capacity;
            slots = new List<ItemStack>();
            foreach (var s in other.slots) slots.Add(s == null ? new ItemStack() : s.Clone());
            EnsureSlots();
            Changed?.Invoke();
        }
    }
}
