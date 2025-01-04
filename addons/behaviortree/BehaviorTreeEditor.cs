#if TOOLS
using AutoCrawler.addons.behaviortree.debugger;
using Godot;

namespace AutoCrawler.addons.behaviortree;

[Tool]
public partial class BehaviorTreeEditor : EditorPlugin
{
	private BehaviorInspectorPlugin _inspectorPlugin;
	private DebuggerWindow _debuggerWindow = null;
	
	public override void _EnterTree()
	{
		_inspectorPlugin = new BehaviorInspectorPlugin(this);
		AddInspectorPlugin(_inspectorPlugin);
	}

	public override void _ExitTree()
	{
		RemoveInspectorPlugin(_inspectorPlugin);
		if (_debuggerWindow != null)
		{
			_debuggerWindow.QueueFree();
			_debuggerWindow = null;
		}
	}
	
	public void ShowDebuggerWindow(BehaviorTree tree)
	{
		if (_debuggerWindow == null)
		{
			_debuggerWindow = GD.Load<PackedScene>("res://addons/behaviortree/debugger/DebuggerWindow.tscn").Instantiate<DebuggerWindow>();
			_debuggerWindow.CloseRequested += () =>
			{
				_debuggerWindow.QueueFree();
				_debuggerWindow = null;
			};
			EditorInterface.Singleton.GetBaseControl().AddChild(_debuggerWindow);
		}

		if (_debuggerWindow != null)
		{
			_debuggerWindow.AddTab(tree);
			_debuggerWindow.Show();
		}
		else
		{
			throw new System.InvalidOperationException("DebuggerWindow가 null입니다.");
		}

	}
}
#endif
