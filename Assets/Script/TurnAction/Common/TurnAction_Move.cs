using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;

namespace AutoCrawler.Assets.Script.TurnAction.Common;

public partial class TurnAction_Move: TurnActionBase
{
    protected override void OnInit()
    {
        
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        
        return ActionState.Executed;
    }
}