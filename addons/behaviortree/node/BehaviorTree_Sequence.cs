﻿using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public partial class BehaviorTree_Sequence: BehaviorTree_Composite
{
    protected override Constants.BtStatus OnBehave(double delta, Node owner)
    {
        foreach (BehaviorTree_Node node in TreeChildren)
        {
            Constants.BtStatus status = node.Behave(delta, owner);
            if (status is Constants.BtStatus.Success) continue;
            
            return status;
        }
        return Constants.BtStatus.Success;
    }
}