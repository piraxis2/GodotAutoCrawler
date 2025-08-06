using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill.Magic;

public abstract partial class TurnAction_Cast : TurnActionBase
{
    

    protected abstract ActionState Shot(double delta, ArticleBase owner);

    protected override void OnInit(Node owner)
    {
        if (MasterCost > 1)
            ActionQueue.Enqueue(CastingPhase);
        ActionQueue.Enqueue(Shot);
    }

    private ActionState CastingPhase(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation is "Cast" or "Casting")
        {
            if (owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
            {
                owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
                
                // If the animation is still playing, return Running state
                if (owner.AnimationPlayer.CurrentAnimationPosition + delta < owner.AnimationPlayer.CurrentAnimationLength) return ActionState.Running;
            }

            if (Cost == MasterCost)
                ActionQueue.Dequeue();
            return ActionState.Executed;
        }
        
        owner.AnimationPlayer.Play(Cost == MasterCost ? "Cast" : "Casting", -1, 0);
        return ActionState.Running;
    }
}
