using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    // =====================================================================
    // 星の状態を変える操作（ホスト権威：WorldAuthority.Submit）
    //   ・時間経過の加工を器具に仕込む／取り出す（器具の中身は Planet スコープ）
    //   ・職人（鍛冶屋・修理屋・家具職人）への依頼（住民のお金が動く）
    //   ・設備の使用料（鍛冶屋の炉）
    // 調理そのもの（自分の持ち物から材料を取り、完成品を受け取る）はローカル操作。
    // =====================================================================

    [Serializable]
    public class CookStartTimedCommand : WorldCommand
    {
        public TimedSlotData slot;
        public int capacity;
    }

    [Serializable]
    public class CookCollectTimedCommand : WorldCommand
    {
        public string slotId;
    }

    [Serializable]
    public class CookCommissionCommand : WorldCommand
    {
        public string recipeId;
        public int batch;
        public int station;
        public int job;
        public int fee;
        public string outputItemId;
        public int outputCount;
        public List<StringIntPair> items = new List<StringIntPair>();
    }

    [Serializable]
    public class CookPayFeeCommand : WorldCommand
    {
        public int job;
        public int amount;
        public string reason;
    }
}
