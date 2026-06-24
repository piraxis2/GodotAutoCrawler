#if TOOLS
using Godot;

namespace AutoCrawler.addons.behaviortree.debugger;

public partial class BtDebuggerPlugin : EditorDebuggerPlugin
{
    private BehaviorTreeEditor _editor;

    public BtDebuggerPlugin() {}
    public BtDebuggerPlugin(BehaviorTreeEditor editor)
    {
        _editor = editor;
    }

    public override void _SetupSession(int sessionId)
    {
        var session = GetSession(sessionId);
        if (session != null)
        {
            _editor.RegisterSession(session);
        }
    }

    public void RegisterAvailableSessions()
    {
        foreach (EditorDebuggerSession session in GetSessions())
        {
            if (session != null)
            {
                _editor.RegisterSession(session);
            }
        }
    }

    public override bool _HasCapture(string capture)
    {
        return capture == "behavior_tree";
    }

    public override bool _Capture(string message, Godot.Collections.Array data, int sessionId)
    {
        if (message == "behavior_tree:register" ||
            message == "behavior_tree:unregister" ||
            message == "behavior_tree:structure" ||
            message == "behavior_tree:tick")
        {
            var session = GetSession(sessionId);
            if (session != null)
            {
                _editor.RegisterSession(session);
            }

            if (data.Count > 0 && data[0].Obj is Godot.Collections.Dictionary payload)
            {
                _editor.CallDeferred(nameof(BehaviorTreeEditor.HandleDebugMessage), message, payload);
            }
            return true;
        }
        return false;
    }
}
#endif
