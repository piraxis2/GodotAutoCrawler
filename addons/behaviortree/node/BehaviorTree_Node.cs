using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Node : Node
{
    
    private long _elapsedTime = 0;
    private Constants.BtStatus _status = Constants.BtStatus.Failure;


    public BehaviorTree Tree { get; internal set; }

#if TOOLS
    public delegate void OnLogChangedEventHandler(Util.BehaviorLog log);
    public event OnLogChangedEventHandler OnLogChanged; 
    
#endif

    public abstract List<BehaviorTree_Node> GetTreeChildren();

    public override sealed void _Ready()
    {
        ChildOrderChanged += OnChildOrderChanged;
        OnTreeChanged();
        OnReady();
        OnInit();
    }

    protected virtual void OnReady(){}

    protected virtual void OnInit()
    {
        _elapsedTime = 0;
    }

    private void OnChildOrderChanged()
    {
        OnTreeChanged();
        Tree?.OnUpdate();
    }

    protected abstract void OnTreeChanged();
    
    public Constants.BtStatus Behave(double delta, Node owner)
    {
        if (_status is Constants.BtStatus.Success or Constants.BtStatus.Failure)
        {
            OnInit();
        }
        _elapsedTime = (long)(_elapsedTime + delta);
        _status = OnBehave(delta, owner);
        
#if TOOLS
        Util.BehaviorLog behaviorLog = new Util.BehaviorLog
        {
            Status = _status,
            Time = _elapsedTime
        };
        OnLogChanged?.Invoke(behaviorLog);
#endif
        return _status;
    }
    protected virtual Constants.BtStatus OnBehave(double delta, Node owner)
    {
        return Constants.BtStatus.Failure;
    }
    
    private bool IsLeafNode()
    {
        return GetTreeChildren().Count == 0;
    }
}