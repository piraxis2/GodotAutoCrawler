using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Interface;

public interface ITurnAffected<T> where T : ArticleBase 
{
    public int Priority { get; set; }
    protected BehaviorTree BehaviorTree { get; }
    public Constants.BtStatus TurnPlay(double delta);

}