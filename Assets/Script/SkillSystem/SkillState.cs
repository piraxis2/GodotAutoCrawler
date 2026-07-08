using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.SkillSystem;

public sealed class SkillState : ITurnActionState
{
    public SkillState(TurnAction_Skill ownerAction, SkillDefinition definition)
    {
        OwnerAction = ownerAction;
        Definition = definition;
    }

    public TurnAction_Skill OwnerAction { get; }
    public SkillDefinition Definition { get; }
    public Queue<Func<double, ArticleBase, ActionState>> PhaseQueue { get; } = new();
    public List<ArticleBase> ConfirmedTargets { get; } = new();
    public int UsedCost { get; private set; }
    public bool Completed { get; set; }
    // 지불/검증 실패로 실행 없이 닫혀야 하는 상태. Action이 ActionState.Failure로 되돌린다.
    public bool Failed { get; set; }
    public List<string> Reports { get; } = new();

    public int RemainingCost => Math.Max((Definition?.MasterCost ?? 1) - UsedCost, 0);
    public bool IsCasting { get; set; }

    public void EnqueuePhase(Func<double, ArticleBase, ActionState> phase)
    {
        PhaseQueue.Enqueue(phase);
    }

    public void MarkCostUsed()
    {
        UsedCost++;
        if (RemainingCost <= 0)
        {
            Completed = true;
        }
    }
}