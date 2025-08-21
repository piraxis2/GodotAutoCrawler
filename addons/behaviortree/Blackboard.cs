using System.Collections.Generic;
using Godot;

namespace AutoCrawler.addons.behaviortree;

public partial class Blackboard : RefCounted
{
    private readonly Dictionary<string, Variant> _data = new();

    public void SetValue(string key, Variant value)
    {
        _data[key] = value;
    }

    public Variant GetValue(string key, Variant defaultValue = default)
    {
        return _data.GetValueOrDefault(key, defaultValue);
    }

    public bool HasValue(string key)
    {
        return _data.ContainsKey(key);
    }
}
