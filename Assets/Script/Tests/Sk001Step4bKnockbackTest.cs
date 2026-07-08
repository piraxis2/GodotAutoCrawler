#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step4bKnockbackTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    private bool _moveFired;
    private Vector2I _moveFrom;
    private Vector2I _moveTo;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step4b] Running KnockbackBlock tests...");
        try
        {
            await TestBasicKnockbackSignalAStar();
            await TestMapBoundary();
            await TestOccupiedCollision();
            await TestDirection();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step4b] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step4b] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step4b] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    // [A] 기본 넉백 + OnMove 발행 + tilemap/AStar 점유 갱신.
    private async Task TestBasicKnockbackSignalAStar()
    {
        GD.Print("[A] Basic knockback + OnMove + AStar/occupancy");
        var battle = await LoadBattle(111);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            Check("A.MapBigEnough", rect.Size.X >= 3 && rect.Size.Y >= 6, true);

            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;
            var casterPos = new Vector2I(cx, topY);
            var targetStart = new Vector2I(cx, topY + 1);
            caster.TilePosition = casterPos;
            target.TilePosition = targetStart;
            ParkUnusedOpponents(battle, tileMap, casterPos, 3, new[] { target },
                new[] { targetStart, new Vector2I(cx, topY + 2), new Vector2I(cx, topY + 3) });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            _moveFired = false;
            target.OnMove += OnTargetMove;

            var action = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 2 } });
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action, caster);

            var expected = new Vector2I(cx, topY + 3);
            CheckEqual("A.TargetMoved2", target.TilePosition, expected);
            Check("A.KnockbackReported", state != null && state.Reports.Any(r => r == $"knockback:2:{target.GetPath()}"), true);

            // OnMove 발행: from=시작, to=최종.
            Check("A.OnMoveFired", _moveFired, true);
            CheckEqual("A.OnMoveFrom", _moveFrom, targetStart);
            CheckEqual("A.OnMoveTo", _moveTo, expected);

            // tilemap 점유 갱신.
            Check("A.OccupancyNewTile", ReferenceEquals(tileMap.GetArticle(expected), target), true);
            Check("A.OccupancyOldTileCleared", tileMap.GetArticle(targetStart) == null, true);

            // AStar 재구성: 새 칸 solid, 옛 칸 비solid.
            AStarGrid2D aStar = null;
            tileMap.UpdateAStar(ref aStar);
            Check("A.AStarNewSolid", aStar.IsPointSolid(expected), true);
            Check("A.AStarOldNotSolid", aStar.IsPointSolid(targetStart), false);

            target.OnMove -= OnTargetMove;
            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [B] 맵 경계에서 중단하고, 이미 경계면 더 밀리지 않는다.
    private async Task TestMapBoundary()
    {
        GD.Print("[B] Map boundary stop");
        var battle = await LoadBattle(222);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");

            int cx = rect.Position.X + rect.Size.X / 2;
            int bottomY = rect.End.Y - 1;              // 마지막 유효 행
            var casterPos = new Vector2I(cx, bottomY - 2);
            caster.TilePosition = casterPos;
            target.TilePosition = new Vector2I(cx, bottomY - 1);   // 경계 한 칸 위
            ParkUnusedOpponents(battle, tileMap, casterPos, 3, new[] { target },
                new[] { new Vector2I(cx, bottomY - 1), new Vector2I(cx, bottomY) });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var ownerNode = MakeActionOwner(caster);

            // 5칸 밀어도 경계(bottomY)에서 1칸만 이동해 멈춘다.
            var action = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 5 } });
            action.Init(ownerNode);
            RunToEnd(action, caster);
            CheckEqual("B.StopsAtBoundary", target.TilePosition, new Vector2I(cx, bottomY));

            // 이미 경계면 더 밀리지 않는다.
            var action2 = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 3 } });
            action2.Init(ownerNode);
            var state2 = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action2, caster);
            CheckEqual("B.NoMoveAtBoundary", target.TilePosition, new Vector2I(cx, bottomY));
            Check("B.ZeroReported", state2 != null && state2.Reports.Any(r => r == $"knockback:0:{target.GetPath()}"), true);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [C] 점유 칸에 부딪히면 그 앞에서 멈춘다.
    private async Task TestOccupiedCollision()
    {
        GD.Print("[C] Occupied tile collision stop");
        var battle = await LoadBattle(333);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opponents = battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>().ToList();
            var target = opponents[0];
            var blocker = opponents[1];

            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;
            var casterPos = new Vector2I(cx, topY);
            caster.TilePosition = casterPos;
            target.TilePosition = new Vector2I(cx, topY + 1);
            blocker.TilePosition = new Vector2I(cx, topY + 3);   // 대상 진행 방향 2칸 뒤
            ParkUnusedOpponents(battle, tileMap, casterPos, 3, new[] { target, blocker },
                new[] { new Vector2I(cx, topY + 1), new Vector2I(cx, topY + 2) });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var ownerNode = MakeActionOwner(caster);
            var action = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 5 } });
            action.Init(ownerNode);
            RunToEnd(action, caster);

            // topY+1 -> topY+2(빈 칸) 이동 후 topY+3(blocker) 앞에서 멈춤.
            CheckEqual("C.StopsBeforeBlocker", target.TilePosition, new Vector2I(cx, topY + 2));
            CheckEqual("C.BlockerUnmoved", blocker.TilePosition, new Vector2I(cx, topY + 3));

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [F] 방향 계산: 수평 밀기 + 대각 입력은 큰 축(카디널)으로.
    private async Task TestDirection()
    {
        GD.Print("[D] Direction (horizontal + dominant-axis cardinal)");
        var battle = await LoadBattle(444);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");

            // 좌상단 모서리 기준(폭이 좁은 맵에서도 Size.X>=3, Size.Y>=6면 여유가 있다).
            int leftX = rect.Position.X;
            int topY = rect.Position.Y + 1;

            // 수평: 시전자 왼쪽 -> 대상은 오른쪽(+X)으로 1칸 밀린다. Y 불변(카디널).
            var casterPos = new Vector2I(leftX, topY);
            caster.TilePosition = casterPos;
            target.TilePosition = new Vector2I(leftX + 1, topY);
            // 두 서브케이스(수평/대각)의 caster·target·경로를 모두 예약해 한 번에 격리한다.
            ParkUnusedOpponents(battle, tileMap, casterPos, 3, new[] { target },
                new[]
                {
                    new Vector2I(leftX + 1, topY), new Vector2I(leftX + 2, topY),
                    new Vector2I(leftX + 1, topY + 2), new Vector2I(leftX + 1, topY + 3)
                });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var ownerNode = MakeActionOwner(caster);
            var action = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 1 } });
            action.Init(ownerNode);
            RunToEnd(action, caster);
            CheckEqual("D.HorizontalPush", target.TilePosition, new Vector2I(leftX + 2, topY));

            // 대각 입력(더 큰 축이 Y): 순수 +Y로만 밀린다(X 불변).
            caster.TilePosition = new Vector2I(leftX, topY);
            target.TilePosition = new Vector2I(leftX + 1, topY + 2);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var action2 = BuildSkill("knock", 3, new EffectBlock[] { new KnockbackBlock { Distance = 1 } });
            action2.Init(ownerNode);
            RunToEnd(action2, caster);
            CheckEqual("D.DominantAxisCardinal", target.TilePosition, new Vector2I(leftX + 1, topY + 3));

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    private void OnTargetMove(Vector2I from, Vector2I to, ArticleBase article)
    {
        _moveFired = true;
        _moveFrom = from;
        _moveTo = to;
    }

    private static TurnAction_Skill BuildSkill(string id, int range, EffectBlock[] effects)
    {
        var def = new SkillDefinition
        {
            Id = new StringName(id), Range = range, ManaCost = 0, AnimationName = "",
            TargetSide = SkillTargetSide.Enemy, TargetSelectorDefault = SkillTargetSelector.Nearest,
            Effects = new Array<EffectBlock>()
        };
        foreach (var e in effects) def.Effects.Add(e);
        return new TurnAction_Skill { Definition = def };
    }

    private static void RunToEnd(TurnAction_Skill action, CharacterArticle caster)
    {
        for (int i = 0; i < 16; i++)
        {
            if (action.Action(100.0, caster) == ActionState.End) break;
        }
    }

    private async Task<BattleFieldScene> LoadBattle(long seed)
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(turnHelper, seed);
        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!
            .SetValue(turnHelper, 0.0f);

        AddChild(battle);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        return battle;
    }

    private async Task FreeBattle(BattleFieldScene battle)
    {
        battle.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetBattleFieldScene(null);
    }

    // 미사용 적을 맵 안(GetUsedRect) 비충돌 칸으로 옮긴다. 맵 밖 좌표를 두면 UpdateAStar가
    // SetPointSolid에서 out-of-bounds ERROR를 낸다. 사거리 밖 칸을 우선해 대상 확정을 교란하지 않는다.
    private static void ParkUnusedOpponents(BattleFieldScene battle, BattleFieldTileMapLayer tileMap,
        Vector2I casterPos, int range, CharacterArticle[] active, Vector2I[] reserved)
    {
        Rect2I rect = tileMap.GetUsedRect();
        var occupied = new HashSet<Vector2I>(reserved) { casterPos };
        foreach (var a in active) occupied.Add(a.TilePosition);

        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (System.Array.IndexOf(active, opp) >= 0) continue;
            Vector2I spot = FindFreeTile(rect, occupied, casterPos, range);
            opp.TilePosition = spot;
            occupied.Add(spot);
        }
    }

    private static Vector2I FindFreeTile(Rect2I rect, HashSet<Vector2I> occupied, Vector2I casterPos, int range)
    {
        Vector2I fallback = casterPos;
        int fallbackDist = -1;
        for (int y = rect.Position.Y; y < rect.End.Y; y++)
        for (int x = rect.Position.X; x < rect.End.X; x++)
        {
            var t = new Vector2I(x, y);
            if (occupied.Contains(t)) continue;
            int d = Math.Abs(t.X - casterPos.X) + Math.Abs(t.Y - casterPos.Y);
            if (d > range) return t;                       // 사거리 밖: 대상 후보가 될 수 없다
            if (d > fallbackDist) { fallbackDist = d; fallback = t; }
        }
        return fallback;                                   // 사거리 밖 칸이 없으면 가장 먼 미점유 칸
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001Step4bActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif
