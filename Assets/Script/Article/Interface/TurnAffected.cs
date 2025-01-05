using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.Article.Interface;

public interface ITurnAffected<T> where T : ArticleBase 
{
    public int Priority { get; set; }
    public TurnActionBase CurrentTurnAction { get; set; }
    protected BehaviorTree BehaviorTree { get; }
    public Constants.BtStatus TurnPlay(double delta);

}