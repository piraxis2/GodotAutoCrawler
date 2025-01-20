using Godot;

namespace AutoCrawler.addons.behaviortree.node.Rating;

public abstract partial class BehaviorTree_RatingDecorator : BehaviorTree_Decorator
{
    public abstract int GetRating(Node owner);
}