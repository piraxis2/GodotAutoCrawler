#if TOOLS
using Godot;

namespace AutoCrawler.addons.behaviortree;

public partial class BehaviorInspectorPlugin : EditorInspectorPlugin
{
    public override bool _CanHandle(GodotObject @object)
    {
        return @object.GetType() == typeof(BT_Node);
    }


    public override void _ParseCategory(GodotObject @object, string category)
    {
        GD.Print("Parsing category");
        
        var tempButton = new Button();
        tempButton.Text = "Behavior Tree";
        tempButton.Pressed += () =>
        {
            GD.Print("Button pressed");
        };
        AddCustomControl(tempButton);
    }
}

public partial class EditorPropertyButton : EditorProperty
{

    private Button _button = new Button();

    public EditorPropertyButton()
    {
        AddChild(_button);
        AddFocusable(_button);
       _button.Pressed += OnButtonPressed; 
    }
    private void OnButtonPressed()
    {
        GD.Print("Button pressed");
    }
}
#endif