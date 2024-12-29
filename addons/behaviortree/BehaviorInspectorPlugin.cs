#if TOOLS
using Godot;

namespace AutoCrawler.addons.behaviortree;

public partial class BehaviorInspectorPlugin : EditorInspectorPlugin
{
    
    private BT_Node _node;
    private BehaviorTreeEditor _editor;
    private Button _button;
    
    public BehaviorInspectorPlugin(BehaviorTreeEditor editor)
    {
        _editor = editor; 
    }
    public override bool _CanHandle(GodotObject @object)
    {
        return @object is BT_Node;
    }

    public override void _ParseBegin(GodotObject @object)
    {
        _node = @object as BT_Node;
        _button = new Button();
        _button.Text = "Open Behavior Tree Editor";
        _button.Pressed += OnButtonPressed;
        AddCustomControl(_button);
    }
    
    private void OnButtonPressed()
    {
    }
}


#endif