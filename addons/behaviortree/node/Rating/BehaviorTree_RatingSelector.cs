using System;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace AutoCrawler.addons.behaviortree.node.Rating
{
    public partial class BehaviorTree_RatingSelector : BehaviorTree_Composite
    {
        private BehaviorTree_RatingDecorator _highestRatingDecorator;

        protected override BtStatus OnBehave(double delta, Node owner)
        {
            if (_highestRatingDecorator == null)
            {
                _highestRatingDecorator = TakeDecorator(owner);
                
                if (_highestRatingDecorator == null) return BtStatus.Failure;
            }

            var status = _highestRatingDecorator.Behave(delta, owner);

            if (status is BtStatus.Success or BtStatus.Failure)
            {
                _highestRatingDecorator = null;
            }

            return status;
        }

        BehaviorTree_RatingDecorator TakeDecorator(Node owner)
        {
            int highestRating = 0;
            BehaviorTree_RatingDecorator highestRatingDecorator = null;
            foreach (BehaviorTree_RatingDecorator decorator in TreeChildren.OfType<BehaviorTree_RatingDecorator>())
            {
                int rating = decorator.GetRating(owner);
                if (highestRating < rating)
                {
                    highestRating = rating;
                    highestRatingDecorator = decorator;
                }
            }

            return highestRatingDecorator;
        }
    }
}