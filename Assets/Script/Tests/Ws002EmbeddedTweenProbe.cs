#if TOOLS
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-002 후속 native pop-out 전환의 self-checking probe다.
///
/// 목적: `gui_embed_subwindows` 비활성화 후 기존 <c>workspace.tscn</c>의 managed window가 OS native pop-out window로 뜨는지, 그리고 세계(World) 창이 <b>같은 인스턴스</b>를 유지한 채 거점 비율 ↔ 전장 비율로 크기 tween되는지를 판정한다.
///
/// 자동 판정 항목(headless 가능):
/// - root viewport의 <c>GuiEmbedSubwindows</c>가 꺼졌다(프로젝트 플래그 반영).
/// - World 창이 <c>IsEmbedded()</c> == false다(OS native window로 분리되어 마스터 밖 이동 가능).
/// - World 인스턴스 id가 tween 전/중/후 동일하다(재생성 없음, D2).
/// - World.Size가 거점(hub)에서 전장(battle) 사이를 중간값을 거쳐 연속적으로 지나간다(단일 점프 아님).
/// - tween이 목표 크기에서 종료하고, 되돌아오는 tween도 동일 인스턴스에서 성립한다.
///
/// 사람이 판정할 항목(GUI 실행): 크기 tween의 시각적 "부드러움". 이 probe는 프레임별 크기를 찍어
/// GUI 실행에서도 관찰할 수 있게 한다. <c>--headless</c>로 돌리면 구조 계약만 자동 단언한다.
///
/// 실행: `godot --path . res://Assets/Script/Tests/ws002_embedded_tween_probe.tscn`
/// (headless도 가능. class cache 갱신을 위해 최초 1회 `--import` 필요.)
/// </summary>
public partial class Ws002EmbeddedTweenProbe : Node
{
    private const double TweenSeconds = 0.4;

    private readonly List<string> _failures = new();

    private WorkspaceWindow _world;
    private ulong _worldInstanceIdAtStart;

    private Vector2I _hubSize;
    private Vector2I _battleSize;

    // tween 진행 중 관측된 크기(중간값 통과 = 연속성의 증거).
    private bool _sampling;
    private readonly List<Vector2I> _sampledSizes = new();

    public override void _Ready()
    {
        CallDeferred(nameof(Run));
    }

    private void Run()
    {
        Godot.Window root = GetTree().Root;

        // (1) 전역 native pop-out 플래그가 root viewport에 반영됐는가.
        Check(!root.GuiEmbedSubwindows, "root viewport GuiEmbedSubwindows == false (native pop-out flag applied)");

        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        AddChild(workspace);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        if (!manager.TryGetWindow("world", out _world))
        {
            _failures.Add("World window ('world') not found in registry.");
            Finish();
            return;
        }

        // 모든 managed 창이 native pop-out인지 기록하고 단언한다.
        var stillEmbedded = new List<string>();
        foreach (string id in manager.Registry.Keys)
        {
            if (!manager.TryGetWindow(id, out WorkspaceWindow w)) continue;
            bool embedded = w.IsEmbedded();
            GD.Print($"PROBE[ws002] window[{id}] embedded={embedded} visible={w.Visible} size={w.Size}");
            if (embedded) stillEmbedded.Add(id);
        }

        // (2) 모든 managed 창이 native OS window인가(= 마스터 밖으로 분리 가능).
        Check(stillEmbedded.Count == 0,
            $"all managed windows native pop-out (still embedded: [{string.Join(", ", stillEmbedded)}])");
        Check(!_world.IsEmbedded(), "World window IsEmbedded() == false (OS native pop-out path)");

        _worldInstanceIdAtStart = _world.GetInstanceId();

        // 거점 98×79 ↔ 전장 64×76 (E-7 개정판 비율). Step 1은 비율 소스로만 쓰고, 실제 preset 비율
        // 계약 교체는 Step 4다. 마스터 캔버스 = 프로젝트 viewport(1440×960) 기준으로 스파이크 크기를 만든다.
        // 주의: headless의 live root.Size는 실제 OS 창(작은 기본값)이라 비율 소스로 쓰면 안 된다.
        Vector2I viewport = GetProjectViewportSize();
        _hubSize = new Vector2I(Mathf.RoundToInt(viewport.X * 0.98f), Mathf.RoundToInt(viewport.Y * 0.79f));
        _battleSize = new Vector2I(Mathf.RoundToInt(viewport.X * 0.64f), Mathf.RoundToInt(viewport.Y * 0.76f));

        _world.Show();
        _world.Size = _hubSize;
        GD.Print($"PROBE[ws002] hubSize={_hubSize} battleSize={_battleSize} startSize={_world.Size} " +
                 $"worldInstanceId={_worldInstanceIdAtStart}");

        TweenWorldSizeTo(_battleSize, nameof(OnReachedBattle));
    }

    private void TweenWorldSizeTo(Vector2I target, string onDone)
    {
        _sampledSizes.Clear();
        _sampling = true;

        Tween tween = CreateTween();
        tween.TweenProperty(_world, "size", target, TweenSeconds);
        tween.Finished += () =>
        {
            _sampling = false;
            Callable.From(() => CallDeferred(onDone)).Call();
        };
    }

    public override void _Process(double delta)
    {
        if (_sampling && _world != null && GodotObject.IsInstanceValid(_world))
        {
            _sampledSizes.Add(_world.Size);
        }
    }

    private void OnReachedBattle()
    {
        AssertTweenLeg("hub->battle", _hubSize, _battleSize);
        TweenWorldSizeTo(_hubSize, nameof(OnReturnedToHub));
    }

    private void OnReturnedToHub()
    {
        AssertTweenLeg("battle->hub", _battleSize, _hubSize);
        Finish();
    }

    /// <summary>tween 한 구간에 대해 인스턴스 보존 + 중간값 통과 + 목표 도달을 단언한다.</summary>
    private void AssertTweenLeg(string label, Vector2I from, Vector2I to)
    {
        Check(GodotObject.IsInstanceValid(_world), $"[{label}] World instance still valid");
        Check(_world.GetInstanceId() == _worldInstanceIdAtStart,
            $"[{label}] World instance id unchanged (no re-create)");
        Check(!_world.IsEmbedded(), $"[{label}] World remains native pop-out after tween");

        // 목표 크기 도달(정수 반올림 오차 허용 2px).
        bool reached = (_world.Size - to).Abs() <= new Vector2I(2, 2);
        Check(reached, $"[{label}] World reached target size {to} (actual {_world.Size})");

        // 중간값 통과: 변하는 각 축이 최소 한 프레임은 from/to 사이 값을 가져야 단일 점프가 아니다.
        // 계약은 width가 아니라 size tween이므로 X/Y를 따로 본다(변화폭 2px 미만 축은 strict 중간값이 불가능하니 제외).
        int intermediateX = CountStrictlyBetween(s => s.X, from.X, to.X);
        int intermediateY = CountStrictlyBetween(s => s.Y, from.Y, to.Y);

        if (Mathf.Abs(from.X - to.X) >= 2)
            Check(intermediateX > 0,
                $"[{label}] width passed through intermediate values ({intermediateX} samples between {from.X} and {to.X})");
        if (Mathf.Abs(from.Y - to.Y) >= 2)
            Check(intermediateY > 0,
                $"[{label}] height passed through intermediate values ({intermediateY} samples between {from.Y} and {to.Y})");

        GD.Print($"PROBE[ws002] {label}: samples={_sampledSizes.Count} intermediateX={intermediateX} " +
                 $"intermediateY={intermediateY} endSize={_world.Size}");
    }

    private int CountStrictlyBetween(System.Func<Vector2I, int> axis, int from, int to)
    {
        int lo = Mathf.Min(from, to);
        int hi = Mathf.Max(from, to);
        int count = 0;
        foreach (Vector2I s in _sampledSizes)
        {
            int v = axis(s);
            if (v > lo && v < hi) count++;
        }

        return count;
    }

    private static Vector2I GetProjectViewportSize()
    {
        var width = (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1152);
        var height = (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 648);
        return new Vector2I(Mathf.Max(width, 1), Mathf.Max(height, 1));
    }

    private void Check(bool condition, string what)
    {
        if (condition) GD.Print($"PROBE[ws002] PASS: {what}");
        else _failures.Add(what);
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("PROBE[ws002] RESULT: ALL PASS — native pop-out tween structural gate met.");
            GetTree().Quit(0);
            return;
        }

        GD.PrintErr($"PROBE[ws002] RESULT: {_failures.Count} FAIL");
        foreach (string f in _failures) GD.PrintErr($"PROBE[ws002] FAIL: {f}");
        GetTree().Quit(1);
    }
}
#endif

