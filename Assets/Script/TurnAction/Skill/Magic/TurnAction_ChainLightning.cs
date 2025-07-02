using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.TurnAction.Skill.Magic;
using AutoCrawler.Assets.Script.Util;

public partial class TurnAction_ChainLightning : TurnActionBase, ISkill<TurnActionBase>
{
    [Export] private int minDamage = 10;
    [Export] private int maxDamage = 20;
    public int Range => 3;
    public int Scale => 3;
    
    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);
    
    private int _chainCount = 3;
    private bool _isChaining = false;
    
    protected override void OnInit(Node owner)
    {
        _chainCount = 3;
    }

    private ActionState ShotChainLightning(double delta, ArticleBase owner)
    {

        return ActionState.Running;
    } 

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        return ActionState.Executed;
    }
}
