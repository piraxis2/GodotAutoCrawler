using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class Article : Node
{
    [Export]
    private BehaviorTree _behaviorTreeRoot;
    public void TurnPlay(double delta)
    {
        _behaviorTreeRoot.Behave(delta, this);
    } 

}