using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.TurnAction.Skill.Magic;
using AutoCrawler.Assets.Script.Util;
using Godot.Collections;

[GlobalClass, Tool]
public partial class TurnAction_ChainLightning : TurnActionBase, ISkill<TurnActionBase>
{
    [Export] private int _maxDamage = 20;
    public int Range => 3;
    public int Scale => 3;

    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);

    private int _chainCount = 3;
    
    private Vector2I _targetPosition;

    private Node2D _playingFx = null;

    protected override void OnInit(Node owner)
    {
        ActionQueue.Enqueue(Shot);
        _playingFx = null;
        ArticleBase ownerArticle = (ArticleBase)((BehaviorTree_Action)owner).Tree.GetParent();
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + ownerArticle.TilePosition).ToList();
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?.FirstOrDefault(t => t is { IsAlive: true } && t.IsOpponent(ownerArticle));
        if (target != null) _targetPosition = target.TilePosition;
    }


    protected ActionState Shot(double delta, ArticleBase owner)
    {
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticle(_targetPosition);
        if (_playingFx != null)
        {
            var aniPlayer = _playingFx.GetNode<AnimationPlayer>("AnimationPlayer");
            if (aniPlayer.CurrentAnimation == "end" && aniPlayer.CurrentAnimationPosition < aniPlayer.CurrentAnimationLength)
            {
                aniPlayer.Seek(aniPlayer.CurrentAnimationPosition + delta);
                return ActionState.Running;
            }
            
            _playingFx.QueueFree();
            ActionQueue.Dequeue();
            return ActionState.Executed;
        }
        
        owner.AnimationPlayer.Play("Idle");
        var spriteFx = GlobalUtil.GetBattleFieldCoreNode<FxPlayer>(owner);
        var ownerGlobalPosition = tileMapLayer?.ToGlobal(tileMapLayer.MapToLocal(owner.TilePosition));
        var targetGlobalPosition = tileMapLayer?.ToGlobal(tileMapLayer.MapToLocal(_targetPosition));
        if (target is { IsAlive: true })
        {
            target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, _maxDamage, _maxDamage));
        }

        _playingFx = spriteFx.PlayLineFx("Lightning", [ownerGlobalPosition.GetValueOrDefault(), targetGlobalPosition.GetValueOrDefault()]);
        return ActionState.Running;
    }

}