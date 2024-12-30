using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class Article : Node
{
    [Export]
    private BT_Selector _btRoot;
    public void TurnPlay(double delta)
    {
        _btRoot.Behave(delta, this);
    } 

}