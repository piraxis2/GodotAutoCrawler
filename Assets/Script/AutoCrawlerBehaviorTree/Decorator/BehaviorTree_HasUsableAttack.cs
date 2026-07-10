using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator;

[GlobalClass, Tool]
public partial class BehaviorTree_HasUsableAttack : BehaviorTree_Decorator
{
    [Export] public bool Invert { get; set; }

    protected override bool IsValid(BehaviorTree_Node child, double delta, Node owner)
    {
        bool hasUsableAttack = owner is CharacterArticle character && character.HasUsableAttack;
        return Invert ? !hasUsableAttack : hasUsableAttack;
    }
}