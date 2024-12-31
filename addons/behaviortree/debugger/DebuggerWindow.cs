#if TOOLS
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.addons.behaviortree.debugger;

[Tool]
public partial class DebuggerWindow: Window
{
    private TabContainer _tabContainer;
    
    public override void _Ready()
    {
        _tabContainer = GetNode<TabContainer>("MarginContainer/VBoxContainer/TabContainer");
        TabBar tabBar = _tabContainer.GetTabBar();
        tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways;
        tabBar.TabClosePressed += OnTabClosePressed;
        CloseRequested += OnCloseRequested;
    }

    private void OnTabClosePressed(long tab)
    {
        _tabContainer.RemoveChild(_tabContainer.GetChild((int)tab));
    }

    private void OnCloseRequested()
    {
        QueueFree();
    }

    public void AddTab(BehaviorTree tree)
    {
        _tabContainer.AddChild(new DebuggerTree(tree));
    }
}
#endif