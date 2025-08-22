using Godot;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.Util;

[GlobalClass, Tool]
public partial class TurnAction_ChainLightning : TurnActionBase 
{
    [Export] private int _maxDamage = 20;
    
    [Export] private int _chainCount = 3;
    protected override int Range => 3;
    protected override int Scale => 3;

    private Node2D _playingFx;
    private ArticleBase _owner;
    private ArticleBase _startingArticle;
    private ArticleBase _targetArticle;
    private HashSet<ArticleBase> _hitTargets;

    private BattleFieldTileMapLayer _tileMapLayer;
    private FxPlayer _fxPlayer;
    
    

    protected override void OnInit(Node owner)
    {
        _tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        _fxPlayer = GlobalUtil.GetBattleFieldCoreNode<FxPlayer>(owner);
        ActionQueue.Enqueue(StartPhase);
        ActionQueue.Enqueue(CastPhase);

        for (int i = 0; i < _chainCount; i++)
        {
            ActionQueue.Enqueue(IgnitionPhase);
            ActionQueue.Enqueue(DischargePhase);
        }
        ActionQueue.Enqueue(EndPhase);
        _playingFx = null;
        _owner = (ArticleBase)((BehaviorTree_Action)owner).Tree.GetParent();
        _startingArticle = _owner; 
        _hitTargets = [_owner];
        _targetArticle = GetTarget(owner);
    }

    private void ForceExit()
    {
        ActionQueue.Clear();
        ActionQueue.Enqueue(EndPhase);
    }

    private ArticleBase GetChainTarget(Vector2I tilePosition)
    {
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + tilePosition).ToList();
        var potentialTargets = _tileMapLayer?.GetArticles(calculatedAttackRange)?
            .Where(t => t is { IsAlive: true } && t.TilePosition != tilePosition && t.IsOpponent(_owner));
        
        return potentialTargets?.OrderBy(t => _hitTargets.Contains(t)).FirstOrDefault();
    }

    private ActionState StartPhase(double delta, ArticleBase owner)
    {
        owner.AnimationPlayer.Play("Cast");
        ActionQueue.Dequeue();
        return ActionState.Running;
    }
    private ActionState CastPhase(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation == "Cast" && owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
        {
            owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
            return ActionState.Running;
        }

        ActionQueue.Dequeue(); 
        return ActionState.Running;
    }

    //점화
    private ActionState IgnitionPhase(double delta, ArticleBase owner)
    {
        if (_targetArticle is null or { IsAlive: false })
        {
            ForceExit();
            return ActionState.Running;
        }
        
        var startingArticleGlobalPosition = _tileMapLayer?.ToGlobal(_tileMapLayer.MapToLocal(_startingArticle.TilePosition));
        var targetGlobalPosition = _tileMapLayer?.ToGlobal(_tileMapLayer.MapToLocal(_targetArticle.TilePosition));

        _hitTargets.Add(_targetArticle);
        _startingArticle = _targetArticle;
        _targetArticle.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, _maxDamage, _maxDamage));
        _targetArticle = GetChainTarget(_targetArticle.TilePosition);
        _playingFx = _fxPlayer.PlayLineFx("Lightning", [startingArticleGlobalPosition.GetValueOrDefault(), targetGlobalPosition.GetValueOrDefault()]);
        _fxPlayer.PlaySoundFx("Thunder");
        ActionQueue.Dequeue();
        return ActionState.Running; 
        
    }
    
    //방전
    private ActionState DischargePhase(double delta, ArticleBase owner)
    {
        var aniPlayer = _playingFx.GetNode<AnimationPlayer>("AnimationPlayer");
        if (aniPlayer.CurrentAnimation == "discharge" && aniPlayer.CurrentAnimationPosition < aniPlayer.CurrentAnimationLength)
        {
            aniPlayer.Seek(aniPlayer.CurrentAnimationPosition + delta);
            return ActionState.Running; 
        }
        
        _playingFx.QueueFree();
        _playingFx = null;
        ActionQueue.Dequeue();
        return ActionState.Running; 
    }

    private ActionState EndPhase(double delta, ArticleBase owner)
    {
        owner.AnimationPlayer.Play("Idle");
        ActionQueue.Clear();
        return ActionState.Executed;
    }
}