using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Common;

public partial class TurnAction_Move: TurnActionBase
{
    private AStarGrid2D _aStar2D;
    protected override void OnInit()
    {
        
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        
        return ActionState.Executed;
    }
}