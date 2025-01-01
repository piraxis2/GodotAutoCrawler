using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class Article : Node
{
    [Export]
    private BehaviorTree _behaviorTreeRoot;
    public void TurnPlay(double delta)
    {
        if (_behaviorTreeRoot != null)
        {
            _behaviorTreeRoot.Behave(delta, this);
        }
        else
        {
            throw new System.InvalidOperationException("BehaviorTree 루트 노드가 설정되지 않았습니다.");
        }
    } 

}