#if TOOLS
using Godot;
namespace AutoCrawler.addons.behaviortree;

[Tool]
public partial class BehaviorInspectorPlugin : EditorInspectorPlugin
{
    
    private BehaviorTreeEditor _editor = null;
    private Button _button;

    public BehaviorInspectorPlugin() {}
    public BehaviorInspectorPlugin(BehaviorTreeEditor editor)
    {
        _editor = editor; 
    }
    public override bool _CanHandle(GodotObject @object)
    {
        if (@object is node.BehaviorTree_Node node)
        {
            return node.Tree != null;
        }
        if (@object is BehaviorTree)
        {
            return true;
        }
        return false;
    }

    public override void _ParseBegin(GodotObject @object)
    {
        BehaviorTree tree = null;
        if (@object is node.BehaviorTree_Node node)
        {
            tree = node.Tree;
        }
        else if (@object is BehaviorTree bt)
        {
            tree = bt;
        }

        if (tree == null) return;

        _button = new Button();
        _button.Text = "🌵 Open Behavior Tree Editor";
        _button.Pressed += () =>
        {
            if (!GodotObject.IsInstanceValid(tree))
            {
                GD.PrintErr("BehaviorInspectorPlugin: BehaviorTree is invalid or has been freed.");
                return;
            }
            _editor.ShowDebuggerWindow(tree);
        };
        AddCustomControl(_button);
    }
}
#endif