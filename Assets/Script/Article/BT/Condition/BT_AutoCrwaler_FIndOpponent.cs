using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article.BT.Condition;

public partial class BT_AutoCrwaler_FIndOpponent: BT_Condition
{
    public override BT_Status OnBehave(double delta, Node owner)
    {
        return CheckCondition(delta, owner);
    }

    protected override BT_Status CheckCondition(double delta, Node owner)
    {
        return BT_Status.Failure;
    }
}