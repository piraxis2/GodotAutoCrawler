#if TOOLS
using Godot;

namespace AutoCrawler.addons.behaviortree;

public partial class BehaviorInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject @object)
    {
        return @object is BT_Node;
    }

    public override void _ParseBegin(GodotObject @object)
    {
        GD.Print("Parsing begin");
        AddPropertyEditor("CustomCategory", new EditorPropertyButton());
    }
}

public partial class EditorPropertyButton : EditorProperty
{

    private Button _button = new Button();

    public EditorPropertyButton()
    {
        SetLabel("Behavior Tree");
        AddChild(_button);
        AddFocusable(_button);
        _button.Text = "Load Behavior Tree";
        _button.Pressed += OnButtonPressed; 
    }
    private void OnButtonPressed()
    {
        
    }
}
#endif