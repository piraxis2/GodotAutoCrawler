#if TOOLS
using Godot;

namespace AutoCrawler.addons.behaviortree;

[Tool]
public partial class BehaviorTreeEditor : EditorPlugin
{
	private BehaviorInspectorPlugin _inspectorPlugin;
	public override void _EnterTree()
	{
		_inspectorPlugin = new BehaviorInspectorPlugin(this);
		AddInspectorPlugin(_inspectorPlugin);
	}

	public override void _ExitTree()
	{
		RemoveInspectorPlugin(_inspectorPlugin);
			
	}

}
#endif
