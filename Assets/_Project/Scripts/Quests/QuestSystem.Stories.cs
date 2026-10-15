using System;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Quests
{
    // 個別ストーリー（§9）の窓口。物語ごとの中身は QuestStories.cs。
    public sealed partial class QuestSystem
    {
        private CommandResult HandleStoryCommand(QuestStoryCommand c, CommandContext ctx)
        {
            var st = FindStory(c.storyId, ctx.IssuerId);
            if (st == null || st.finished) return CommandResult.Ok();
            if (c.action == "talked") st.talkedThisStage = true;
            else StoryDef(c.storyId)?.HandleCommand(this, st, c.action, c.arg, c.value, ctx);
            Changed();
            return CommandResult.Ok();
        }

        private void LocalStoryCheck()
        {
            string me = LocalId;
            foreach (var st in S.stories)
            {
                if (st.finished || st.playerId != me) continue;
                try { StoryDef(st.storyId)?.LocalCheck(this, st); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void LocalStoryFish(FishCaughtEvent e)
        {
            string me = LocalId;
            foreach (var st in S.stories)
            {
                if (st.finished || st.playerId != me) continue;
                try { StoryDef(st.storyId)?.LocalFish(this, st, e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
