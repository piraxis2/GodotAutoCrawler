#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// BT-003 Step 1: versioned 택틱보드 draft schema + TacticActionCatalog의 .tres 저장/재로드 왕복 검증.
// 순수 데이터 Resource만 다루며 validator/compiler/runtime Node는 범위 밖이다(Step 2~4).
//
// 완료 조건 매핑:
//  A  board scalar(SchemaVersion/BoardId/DisplayName)와 player 행(순서·ID·enabled·source·조건 TypeId·
//     대상 TypeId·행동 TypeId+ActionId·접근 정책)이 저장/재로드 후 동일
//  B  잠긴 기본 system row(default_attack/terminal_wait)의 Locked/Source/순서 보존
//  C  catalog의 CatalogRevision과 entry(ActionId + template 타입) 보존
//  D  같은 board .tres를 두 소비자가 load(cache-ignore)해도 인스턴스가 분리되고 서로 변형하지 않음
//  E  unknown/future SchemaVersion을 파괴하거나 자동 재저장하지 않음(로드로 파일 불변)
//  F  null Target/Action과 빈 Conditions도 초안으로 저장·재로드됨(Step 2 validator가 보고 가능)
//  G  같은 catalog .tres를 두 소비자가 load해도 catalog/entry/template이 각각 분리되고 서로 변형하지 않음
public partial class Bt003Step1DraftRoundtripTest : Node
{
    private const string BoardPath = "user://bt003_step1_board.tres";
    private const string CatalogPath = "user://bt003_step1_catalog.tres";
    private const string FuturePath = "user://bt003_step1_future.tres";
    private const string EmptyPath = "user://bt003_step1_empty.tres";

    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[BT-003 Step1] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[BT-003 Step1] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-003 Step1] Running draft/catalog round-trip tests...");
        try
        {
            TestBoardScalarsAndPlayerRowsRoundTrip();
            TestLockedSystemRowsRoundTrip();
            TestCatalogRoundTrip();
            TestTwoConsumersAreIsolated();
            TestCatalogConsumersAreIsolated();
            TestFutureVersionPreservedAndNotResaved();
            TestNullAndEmptyDefinitionsRoundTrip();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-003 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        CleanUp();
        return _failures;
    }

    // ---- [A] board scalar + player 행 왕복 --------------------------------------------------------
    private void TestBoardScalarsAndPlayerRowsRoundTrip()
    {
        GD.Print("[A] board/player row 왕복");
        TacticBoardDefinition board = BuildBoard();
        CheckEq("A.save_ok", ResourceSaver.Save(board, BoardPath), Error.Ok);

        var loaded = ResourceLoader.Load<TacticBoardDefinition>(BoardPath, null, ResourceLoader.CacheMode.Ignore);
        if (loaded == null) { Fail("A.loaded_null"); return; }

        CheckEq("A.schema_version", loaded.SchemaVersion, 1);
        CheckEq("A.board_id", loaded.BoardId.ToString(), "board.pc");
        CheckEq("A.display_name", loaded.DisplayName, "테스트 보드");
        CheckEq("A.row_count", loaded.Rows.Count, 4);

        // Row0: enemy_casting -> context_target -> catalog(sonic_blow), Immediate
        TacticRowDefinition r0 = loaded.Rows[0];
        CheckEq("A.r0_id", r0.RowId.ToString(), "r_cast");
        CheckEq("A.r0_enabled", r0.Enabled, true);
        CheckEq("A.r0_locked", r0.Locked, false);
        CheckEq("A.r0_source", r0.Source, TacticRowSource.Player);
        CheckEq("A.r0_cond_count", r0.Conditions.Count, 1);
        CheckEq("A.r0_cond_type", r0.Conditions[0].TypeId.ToString(), "enemy_casting");
        CheckEq("A.r0_target_type", r0.Target?.TypeId.ToString(), "context_target");
        CheckEq("A.r0_action_type", r0.Action?.TypeId.ToString(), "catalog_action");
        CheckEq("A.r0_action_id", (r0.Action as TacticCatalogActionDefinition)?.ActionId.ToString(), "sonic_blow");
        CheckEq("A.r0_policy", r0.ApproachPolicy, TacticApproachPolicy.Immediate);

        // Row1: always -> nearest_enemy -> catalog(default_attack), ApproachAllowed
        TacticRowDefinition r1 = loaded.Rows[1];
        CheckEq("A.r1_id", r1.RowId.ToString(), "r_nearest");
        CheckEq("A.r1_cond_type", r1.Conditions[0].TypeId.ToString(), "always");
        CheckEq("A.r1_target_type", r1.Target?.TypeId.ToString(), "nearest_enemy");
        CheckEq("A.r1_action_id", (r1.Action as TacticCatalogActionDefinition)?.ActionId.ToString(), "default_attack");
        CheckEq("A.r1_policy", r1.ApproachPolicy, TacticApproachPolicy.ApproachAllowed);
    }

    // ---- [B] 잠긴 기본 system row 왕복 ------------------------------------------------------------
    private void TestLockedSystemRowsRoundTrip()
    {
        GD.Print("[B] 잠긴 system row 왕복");
        var loaded = ResourceLoader.Load<TacticBoardDefinition>(BoardPath, null, ResourceLoader.CacheMode.Ignore);
        if (loaded == null) { Fail("B.loaded_null"); return; }

        // Row2: default_attack (locked, Default source, 마지막 앞)
        TacticRowDefinition dflt = loaded.Rows[2];
        CheckEq("B.default_id", dflt.RowId.ToString(), "default_attack");
        CheckEq("B.default_locked", dflt.Locked, true);
        CheckEq("B.default_source", dflt.Source, TacticRowSource.Default);
        CheckEq("B.default_cond_any_enemy", dflt.Conditions[0].TypeId.ToString(), "any_enemy");
        CheckEq("B.default_target_nearest", dflt.Target?.TypeId.ToString(), "nearest_enemy");
        // §11 system row 계약: default_attack은 catalog_action/default_attack + ApproachAllowed여야 한다.
        CheckEq("B.default_action_type", dflt.Action?.TypeId.ToString(), "catalog_action");
        CheckEq("B.default_action_id", (dflt.Action as TacticCatalogActionDefinition)?.ActionId.ToString(), "default_attack");
        CheckEq("B.default_policy", dflt.ApproachPolicy, TacticApproachPolicy.ApproachAllowed);

        // Row3: terminal_wait (locked, Default, 마지막 행, targetless wait)
        TacticRowDefinition wait = loaded.Rows[3];
        CheckEq("B.wait_id", wait.RowId.ToString(), "terminal_wait");
        CheckEq("B.wait_locked", wait.Locked, true);
        CheckEq("B.wait_source", wait.Source, TacticRowSource.Default);
        CheckEq("B.wait_is_last", loaded.Rows.Count - 1, 3);
        CheckEq("B.wait_no_target", wait.Target == null, true);
        CheckEq("B.wait_action_type", wait.Action?.TypeId.ToString(), "wait");
    }

    // ---- [C] catalog 왕복 -------------------------------------------------------------------------
    private void TestCatalogRoundTrip()
    {
        GD.Print("[C] catalog 왕복");
        TacticActionCatalog catalog = BuildCatalog();
        CheckEq("C.save_ok", ResourceSaver.Save(catalog, CatalogPath), Error.Ok);

        var loaded = ResourceLoader.Load<TacticActionCatalog>(CatalogPath, null, ResourceLoader.CacheMode.Ignore);
        if (loaded == null) { Fail("C.loaded_null"); return; }

        CheckEq("C.revision", loaded.CatalogRevision.ToString(), "rev-1");
        CheckEq("C.entry_count", loaded.Entries.Count, 1);
        CheckEq("C.entry_action_id", loaded.Entries[0].ActionId.ToString(), "default_attack");
        CheckEq("C.entry_template_not_null", loaded.Entries[0].Template != null, true);
        CheckEq("C.entry_template_type", loaded.Entries[0].Template is TurnAction_Attack, true);
    }

    // ---- [D] 두 소비자 격리(cache-ignore) ---------------------------------------------------------
    private void TestTwoConsumersAreIsolated()
    {
        GD.Print("[D] 두 소비자 격리");
        var c1 = ResourceLoader.Load<TacticBoardDefinition>(BoardPath, null, ResourceLoader.CacheMode.Ignore);
        var c2 = ResourceLoader.Load<TacticBoardDefinition>(BoardPath, null, ResourceLoader.CacheMode.Ignore);
        if (c1 == null || c2 == null) { Fail("D.loaded_null"); return; }

        CheckEq("D.distinct_board", ReferenceEquals(c1, c2), false);
        CheckEq("D.distinct_row", ReferenceEquals(c1.Rows[0], c2.Rows[0]), false);

        // 한 소비자가 필드를 바꿔도 다른 소비자에 영향이 없다(공유 런타임 상태 없음).
        c1.DisplayName = "MUTATED";
        c1.Rows[0].RowId = "mutated";
        CheckEq("D.board_isolated", c2.DisplayName, "테스트 보드");
        CheckEq("D.row_isolated", c2.Rows[0].RowId.ToString(), "r_cast");
    }

    // ---- [G] catalog 두 소비자 격리(cache-ignore) -------------------------------------------------
    // 실제 실행 상태(TurnActionBase runtime) 격리는 Step 3 소관이지만, Step 1의 저장·로드 격리 증거는 여기서
    // 고정한다: 두 번 로드하면 catalog/entry/template 인스턴스가 각각 분리되고 한쪽 변경이 다른 쪽에 안 보인다.
    private void TestCatalogConsumersAreIsolated()
    {
        GD.Print("[G] catalog 두 소비자 격리");
        var k1 = ResourceLoader.Load<TacticActionCatalog>(CatalogPath, null, ResourceLoader.CacheMode.Ignore);
        var k2 = ResourceLoader.Load<TacticActionCatalog>(CatalogPath, null, ResourceLoader.CacheMode.Ignore);
        if (k1 == null || k2 == null) { Fail("G.loaded_null"); return; }

        CheckEq("G.distinct_catalog", ReferenceEquals(k1, k2), false);
        CheckEq("G.distinct_entry", ReferenceEquals(k1.Entries[0], k2.Entries[0]), false);
        CheckEq("G.distinct_template", ReferenceEquals(k1.Entries[0].Template, k2.Entries[0].Template), false);

        // 한 소비자가 revision/entry를 바꿔도 다른 소비자에 영향이 없다.
        k1.CatalogRevision = "MUT";
        k1.Entries[0].ActionId = "mutated";
        CheckEq("G.revision_isolated", k2.CatalogRevision.ToString(), "rev-1");
        CheckEq("G.entry_isolated", k2.Entries[0].ActionId.ToString(), "default_attack");
    }

    // ---- [E] future version 보존 + 자동 재저장 없음 ----------------------------------------------
    private void TestFutureVersionPreservedAndNotResaved()
    {
        GD.Print("[E] future version 보존");
        var future = new TacticBoardDefinition { SchemaVersion = 999, BoardId = "board.future" };
        CheckEq("E.save_ok", ResourceSaver.Save(future, FuturePath), Error.Ok);

        string before = FileAccess.GetFileAsString(FuturePath);
        var loaded = ResourceLoader.Load<TacticBoardDefinition>(FuturePath, null, ResourceLoader.CacheMode.Ignore);
        string after = FileAccess.GetFileAsString(FuturePath);

        if (loaded == null) { Fail("E.loaded_null"); return; }
        CheckEq("E.version_preserved", loaded.SchemaVersion, 999);
        CheckEq("E.board_id_preserved", loaded.BoardId.ToString(), "board.future");
        // Step 1은 로드로 파일을 수정/재저장하지 않는다. 거부는 Step 2 validator 소관이다.
        CheckEq("E.no_auto_resave", after, before);
    }

    // ---- [F] null/빈 definition 왕복 --------------------------------------------------------------
    private void TestNullAndEmptyDefinitionsRoundTrip()
    {
        GD.Print("[F] null/빈 definition 왕복");
        var board = new TacticBoardDefinition { SchemaVersion = 1, BoardId = "board.empty", DisplayName = "" };
        // 조건 없음 + Target/Action null. 초안으로 저장 가능해야 하고, Step 2 validator가 나중에 보고한다.
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_empty",
            Enabled = false,
            Source = TacticRowSource.Player,
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = null
        });
        CheckEq("F.save_ok", ResourceSaver.Save(board, EmptyPath), Error.Ok);

        var loaded = ResourceLoader.Load<TacticBoardDefinition>(EmptyPath, null, ResourceLoader.CacheMode.Ignore);
        if (loaded == null) { Fail("F.loaded_null"); return; }

        CheckEq("F.row_count", loaded.Rows.Count, 1);
        TacticRowDefinition r = loaded.Rows[0];
        CheckEq("F.row_id", r.RowId.ToString(), "r_empty");
        CheckEq("F.enabled_false", r.Enabled, false);
        CheckEq("F.conditions_empty", r.Conditions.Count, 0);
        CheckEq("F.target_null", r.Target == null, true);
        CheckEq("F.action_null", r.Action == null, true);
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    private static TacticBoardDefinition BuildBoard()
    {
        var board = new TacticBoardDefinition
        {
            SchemaVersion = 1,
            BoardId = "board.pc",
            DisplayName = "테스트 보드"
        };

        // 플레이어 행: [적이 영창 중] -> 그 적 -> sonic_blow, Immediate
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_cast",
            Enabled = true,
            Locked = false,
            Source = TacticRowSource.Player,
            Conditions = new Array<TacticConditionDefinition> { new TacticEnemyCastingConditionDefinition() },
            Target = new TacticContextTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "sonic_blow" },
            ApproachPolicy = TacticApproachPolicy.Immediate
        });

        // 플레이어 행: [항상] -> 가장 가까운 적 -> default_attack, ApproachAllowed
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_nearest",
            Enabled = true,
            Locked = false,
            Source = TacticRowSource.Player,
            Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
            ApproachPolicy = TacticApproachPolicy.ApproachAllowed
        });

        // 잠긴 기본 system row: default_attack
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "default_attack",
            Enabled = true,
            Locked = true,
            Source = TacticRowSource.Default,
            Conditions = new Array<TacticConditionDefinition> { new TacticAnyEnemyConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
            ApproachPolicy = TacticApproachPolicy.ApproachAllowed
        });

        // 잠긴 기본 system row: terminal_wait (targetless)
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "terminal_wait",
            Enabled = true,
            Locked = true,
            Source = TacticRowSource.Default,
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = new TacticWaitActionDefinition()
        });

        return board;
    }

    private static TacticActionCatalog BuildCatalog()
    {
        return new TacticActionCatalog
        {
            CatalogRevision = "rev-1",
            Entries = new Array<TacticActionCatalogEntry>
            {
                new TacticActionCatalogEntry
                {
                    ActionId = "default_attack",
                    Template = new TurnAction_Attack()
                }
            }
        };
    }

    private void CleanUp()
    {
        foreach (string path in new[] { BoardPath, CatalogPath, FuturePath, EmptyPath })
        {
            if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }

    // ---- 단언 헬퍼 -------------------------------------------------------------------------------

    private void CheckEq<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected))
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void Fail(string name)
    {
        _failures++;
        GD.Print($"  FAIL: {name}");
    }
}
#endif
