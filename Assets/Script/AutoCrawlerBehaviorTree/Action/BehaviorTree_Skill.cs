using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

public abstract partial class BehaviorTree_Skill : BehaviorTree_TurnAction
{
    public int StrikingDistance { get; }

    public int EffectRange { get; } 
    
}