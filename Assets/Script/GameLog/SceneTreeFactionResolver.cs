using Godot;

namespace AutoCrawler.Assets.Script.GameLog;

// production 진영 resolver. `SkillState.Reports`의 target path를 씬 트리에서 resolve하고, Article의 부모
// 노드 이름("Opponent"/"Ally")으로 진영을 판정한다 — `ArticlesContainer`가 쓰는 정본 진영 규약과 동일하다.
// 없는 경로/freed/"Neutral"·기타 부모는 `Unknown`으로 fail-closed한다.
public sealed class SceneTreeFactionResolver : ISkillTargetFactionResolver
{
    private readonly Node _context;

    public SceneTreeFactionResolver(Node context)
    {
        _context = context;
    }

    public LogFaction Resolve(string targetPath)
    {
        if (_context == null || !GodotObject.IsInstanceValid(_context)) return LogFaction.Unknown;
        if (string.IsNullOrEmpty(targetPath)) return LogFaction.Unknown;

        Node node = _context.GetNodeOrNull(targetPath);
        if (node == null || !GodotObject.IsInstanceValid(node)) return LogFaction.Unknown;

        string parentName = node.GetParent()?.Name.ToString();
        return parentName switch
        {
            "Opponent" => LogFaction.Enemy,
            "Ally" => LogFaction.Ally,
            _ => LogFaction.Unknown,
        };
    }
}
