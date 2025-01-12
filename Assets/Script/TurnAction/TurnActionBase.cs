using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction;

public abstract partial class TurnActionBase : GodotObject 
{
    public enum ActionState
    {
        Executed,
        Running,
        End
    }

    protected int MasterCost = 1;
    
    private int _usedCost = 0;

    public int Cost => MasterCost - _usedCost;

    public void Init(Node owner)
    {
        _usedCost = 0;
        OnInit(owner);
    }

    protected virtual void OnInit(Node owner){}

    public ActionState Action(double delta, ArticleBase owner)
    {
        if (Cost <= 0) return ActionState.End;

        ActionState status = ActionExecute(delta, owner);
        if (status == ActionState.Running) return status;

        _usedCost++;
        return Cost <= 0 ? ActionState.End : status;
    }

    protected abstract ActionState ActionExecute(double delta, ArticleBase owner);


}