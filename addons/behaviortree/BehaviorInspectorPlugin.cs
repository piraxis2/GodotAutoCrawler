#if TOOLS
using Godot;
namespace AutoCrawler.addons.behaviortree;

[Tool]
public partial class BehaviorInspectorPlugin : EditorInspectorPlugin
{
    
    private node.BehaviorTree_Node _node;
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
        return false;
    }

    public override void _ParseBegin(GodotObject @object)
    {
        _node = @object as node.BehaviorTree_Node;
        _button = new Button();
        _button.Text = "Open Behavior Tree Editor";
        _button.Pressed += OnButtonPressed;
        AddCustomControl(_button);
    }
    
    private void OnButtonPressed()
    {
        _editor.ShowDebuggerWindow(_node.Tree);
    }
}
#endif