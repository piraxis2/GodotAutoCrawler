using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill.Magic;

[GlobalClass, Tool]
public partial class TurnAction_MagicBolt : TurnAction_Cast, ISkill<TurnActionBase>
{
    [Export] private int _maxDamage = 20;
    public int Range => 3;


    public int Scale => 3;    [Export] private int _exportCost = 2;
    protected override int MasterCost => _exportCost; 

    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);
    
    private Vector2I _targetPosition;


    protected override void OnInit(Node owner)
    {
        base.OnInit(owner);
        ArticleBase ownerArticle = (ArticleBase)((BehaviorTree_Action)owner).Tree.GetParent();
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + ownerArticle.TilePosition).ToList();
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?.FirstOrDefault(t => t is { IsAlive: true } && t.IsOpponent(ownerArticle));
        if (target != null) _targetPosition = target.TilePosition;
    }

    protected override ActionState Shot(double delta, ArticleBase owner)
    {
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticle(_targetPosition);
        owner.AnimationPlayer.Play("Idle");
        var spriteFx = GlobalUtil.GetBattleFieldCoreNode<FxPlayer>(owner);
        var targetGlobalPosition = tileMapLayer?.ToGlobal(tileMapLayer.MapToLocal(_targetPosition));
        spriteFx.PlaySpriteFx("IceBolt", targetGlobalPosition.GetValueOrDefault());
        
        if (target is { IsAlive: true })
        {
            target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, _maxDamage, _maxDamage));
        }

        ActionQueue.Dequeue();
        return ActionState.Executed;
    }

}