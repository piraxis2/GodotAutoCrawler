namespace AutoCrawler.addons.behaviortree;

public abstract partial class BT_Composite : BT_Node
{
    public void AddNode(BT_Node node)
    {
        AddChild(node);
    }
    
}