using System;
using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction;

[GlobalClass, Tool]
public abstract partial class TurnActionBase : Resource 
{
    public enum ActionState
    {
        Executed,
        Running,
        End
    }

    protected Queue<Func<double, ArticleBase, ActionState>> ActionQueue = [];

    protected virtual int MasterCost => 1;
    
    private int _usedCost = 0;

    public int Cost => MasterCost - _usedCost;

    public void Init(Node owner)
    {
        _usedCost = 0;
        ActionQueue.Clear();
        OnInit(owner);
    }
    public void Finish(Node owner)
    {
        _usedCost = 0;
        ActionQueue.Clear();
        OnFinish(owner);
    }

    protected virtual void OnInit(Node owner){}
    
    protected virtual void OnFinish(Node owner){}
    
    protected virtual void OnUsedCostChanged(int oldCost, int newCost){}

    public ActionState Action(double delta, ArticleBase owner)
    {
        if (Cost <= 0) return ActionState.End;

        ActionState status = ActionExecute(delta, owner);

        if (status != ActionState.Running)
        {
            OnUsedCostChanged(_usedCost++, _usedCost);
        }
        
        return Cost <= 0 ? ActionState.End : status;
    }

    protected virtual ActionState ActionExecute(double delta, ArticleBase owner)
    {
        return ActionQueue.Peek()(delta, owner);
    }


 
}