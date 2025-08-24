using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Common;
[GlobalClass, Tool]
public partial class TurnAction_Attack : TurnActionBase
{
    [Export] private int minDamage = 10;
    [Export] private int maxDamage = 20;

    protected override int Range => 1;
    protected override int Scale => 1;

    private ArticleBase _target;

    protected override void OnInit(Node owner)
    {
         _target = GetTarget(owner); 
        ActionQueue.Enqueue(StartPhase);
        ActionQueue.Enqueue(RunPhase);
        ActionQueue.Enqueue(EndPhase); 
    }

    private ActionState StartPhase(double delta, ArticleBase owner)
    {
        owner.AnimationPlayer.Play("Attack", -1, 0);
        ActionQueue.Dequeue();
        return ActionState.Running; 
    }

    private ActionState RunPhase(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation == "Attack" && owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
        {
            owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
            return ActionState.Running;
        }

        ActionQueue.Dequeue(); 
        return ActionState.Running;
    }

    private ActionState EndPhase(double delta, ArticleBase owner)
    {
        owner.AnimationPlayer.Play("Idle");
        if (_target is { IsAlive: true })
        {
            _target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<PhysicalDamage>(owner.ArticleStatus, minDamage, maxDamage));
        }

        ActionQueue.Dequeue();
        return ActionState.Executed;
    }
}