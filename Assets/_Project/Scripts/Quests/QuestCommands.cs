using System;
using Uchujin.Core;

namespace Uchujin.Quests
{
    // =====================================================================
    // 星の状態（クエスト・掲示板・お手伝い・行事）を変える操作は WorldCommand でホストへ送る（マルチ仕様 §20）。
    // ソロ／ホストでは即時に実行される。報酬は ctx.PayMoney / ctx.GiveItem で発行者へ渡す。
    // =====================================================================

    public enum QuestResponse
    {
        Accept = 0,
        Decline = 1,
        Abandon = 2
    }

    /// <summary>お願いへの返事（うなずく＝引き受ける、首を振る＝断る、首振り2回＝やめる）。</summary>
    [Serializable]
    public class QuestRespondCommand : WorldCommand
    {
        public string questId;
        public int response;
    }

    /// <summary>住民へ物を渡した（stack は既に主人公の持ち物から離れている。不要なら返す）。</summary>
    [Serializable]
    public class QuestGiveCommand : WorldCommand
    {
        public string residentId;
        public string itemId;
        public int count;
        public int quality;
        public float size;
        public string data;
    }

    /// <summary>掲示板の紙を取る＝引き受ける（§6.2）。</summary>
    [Serializable]
    public class QuestTakePaperCommand : WorldCommand
    {
        public string paperId;
    }

    public enum HelpAction
    {
        Claim = 0,     // 手伝い始める（荷物を持つ・生き物を連れる・落とし物を拾う）
        Complete = 1,  // 目的地に着いた・取り込んだ
        Release = 2    // やめる（荷物を置く）
    }

    [Serializable]
    public class QuestHelpCommand : WorldCommand
    {
        public string helpId;
        public int action;
    }

    /// <summary>行事に居合わせた。</summary>
    [Serializable]
    public class QuestAttendCommand : WorldCommand
    {
        public string eventId;
    }

    /// <summary>市場の屋台で買った（代金はローカルで支払い済み。在庫切れならホストが返金する）。</summary>
    [Serializable]
    public class QuestMarketBuyCommand : WorldCommand
    {
        public string eventId;
        public int stallIndex;
        public string itemId;
        public int amount;
        public int paid;
    }

    /// <summary>個別ストーリーの進行（話した・身振りで答えた・その場にいた 等）。</summary>
    [Serializable]
    public class QuestStoryCommand : WorldCommand
    {
        public string storyId;
        public string action;
        public string arg;
        public float value;
    }

    /// <summary>住民が伝えたい事（お礼・報告）を伝え終えた。</summary>
    [Serializable]
    public class QuestAckCommand : WorldCommand
    {
        public string pendingId;
    }

    /// <summary>活動の成果（ゲストのローカルで起きた生き物の捕獲・釣果）をクエストの進行へ反映する。</summary>
    [Serializable]
    public class QuestProgressCommand : WorldCommand
    {
        public string kind; // "creature" "fish"
        public string itemId;
        public string speciesId;
        public float size;
    }

    /// <summary>捕まえた生き物を見せた／一緒に行った等の報告で依頼を完了する。</summary>
    [Serializable]
    public class QuestReportCommand : WorldCommand
    {
        public string questId;
    }
}
