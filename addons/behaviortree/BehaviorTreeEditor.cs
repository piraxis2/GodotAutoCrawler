#if TOOLS
using AutoCrawler.addons.behaviortree.debugger;
using Godot;

namespace AutoCrawler.addons.behaviortree;

[Tool]
public partial class BehaviorTreeEditor : EditorPlugin
{
	private BehaviorInspectorPlugin _inspectorPlugin;
	private debugger.BtDebuggerPlugin _debuggerPlugin = null;
	private DebuggerWindow _debuggerWindow = null;
	
	public override void _EnterTree()
	{
		_inspectorPlugin = new BehaviorInspectorPlugin(this);
		AddInspectorPlugin(_inspectorPlugin);

		_debuggerPlugin = new debugger.BtDebuggerPlugin(this);
		AddDebuggerPlugin(_debuggerPlugin);
	}

	public override void _ExitTree()
	{
		RemoveInspectorPlugin(_inspectorPlugin);
		RemoveDebuggerPlugin(_debuggerPlugin);
		if (_debuggerWindow != null)
		{
			_debuggerWindow.QueueFree();
			_debuggerWindow = null;
		}
	}

	private readonly System.Collections.Generic.List<EditorDebuggerSession> _activeSessions = new();

	public void RegisterSession(EditorDebuggerSession session)
	{
		if (!_activeSessions.Contains(session))
		{
			_activeSessions.Add(session);
		}
	}

	public void StartDebugging(string treePath)
	{
		_debuggerPlugin?.RegisterAvailableSessions();
		foreach (var session in _activeSessions)
		{
			if (GodotObject.IsInstanceValid(session))
			{
				session.SendMessage("behavior_tree:start", new Godot.Collections.Array { treePath });
			}
		}
	}

	public void StopDebugging(string treePath)
	{
		_debuggerPlugin?.RegisterAvailableSessions();
		foreach (var session in _activeSessions)
		{
			if (GodotObject.IsInstanceValid(session))
			{
				session.SendMessage("behavior_tree:stop", new Godot.Collections.Array { treePath });
			}
		}
	}

	public void HandleDebugMessage(string message, Godot.Collections.Dictionary payload)
	{
		var debuggerWindow = EnsureDebuggerWindow();
		if (debuggerWindow != null && GodotObject.IsInstanceValid(debuggerWindow))
		{
			if (message == "behavior_tree:register" && !debuggerWindow.Visible)
			{
				debuggerWindow.Show();
			}
			debuggerWindow.HandleDebugMessage(message, payload);
		}
	}

	private DebuggerWindow EnsureDebuggerWindow()
	{
		if (_debuggerWindow != null && GodotObject.IsInstanceValid(_debuggerWindow))
		{
			return _debuggerWindow;
		}

		_debuggerWindow = GD.Load<PackedScene>("res://addons/behaviortree/debugger/DebuggerWindow.tscn").Instantiate<DebuggerWindow>();
		_debuggerWindow.SetEditor(this);
		_debuggerWindow.CloseRequested += () =>
		{
			_debuggerWindow.QueueFree();
			_debuggerWindow = null;
		};
		EditorInterface.Singleton.GetBaseControl().AddChild(_debuggerWindow);
		return _debuggerWindow;
	}
	
	public void ShowDebuggerWindow(BehaviorTree tree)
	{
		if (!GodotObject.IsInstanceValid(tree))
		{
			GD.PrintErr("BehaviorTreeEditor.ShowDebuggerWindow: BehaviorTree is invalid or has been freed.");
			return;
		}

		var debuggerWindow = EnsureDebuggerWindow();

		if (debuggerWindow != null)
		{
			debuggerWindow.AddTab(tree);
			debuggerWindow.PopupCentered();
		}
		else
		{
			throw new System.InvalidOperationException("DebuggerWindow가 null입니다.");
		}

	}
}
#endif
