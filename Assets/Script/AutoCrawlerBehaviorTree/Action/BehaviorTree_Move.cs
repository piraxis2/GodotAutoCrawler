using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using Godot;
using BehaviorTree_Action = AutoCrawler.addons.behaviortree.node.BehaviorTree_Action;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

[GlobalClass, Tool]
public partial class BehaviorTree_Move : BehaviorTree_TurnAction
{
    private AStarGrid2D _aStar2D;

    protected override void OnInit(Node owner)
    {
        // TurnHelper.Instance.UpdateAStar(ref _aStar2D);
        // _aStar2D.GetIdPath()
    }

    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is ArticleBase article)
        {
            // article.GlobalPosition = article.GlobalPosition.MoveToward()
            
        }
        
        return Constants.BtStatus.Failure;
    }
}