using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article.BT.Condition;

[GlobalClass, Tool]
public partial class BT_AutoCrwaler_FIndOpponent: BT_Decorator 
{
    protected override BT_Status Decorate(BT_Node node)
    {
        return BT_Status.Failure;
    }
}