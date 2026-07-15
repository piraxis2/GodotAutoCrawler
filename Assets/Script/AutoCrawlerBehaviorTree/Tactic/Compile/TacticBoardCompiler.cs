using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;

// 유효한 draft를 BT-002 제품 Node subtree + 유닛 전용 행동으로 결정론적으로 컴파일한다(BT-003 Step 3,
// ADR-024 §5/§11/§12). 흐름: validate -> (Error 없음) -> Node 생성 -> BehaviorTreeValidation 2차 검사 ->
// snapshot 공개. Error가 있으면 compiler를 실행하지 않고, 중간 실패/2차 검사 실패/예외는 생성 중 Node를
// 정리하고 snapshot을 공개하지 않는다.
//
// 정규 구조 매핑(§11 Step 0 Design Fixes):
//   행동 행: TacticRow[ 조건*, (NearestEnemy면 후보 공급 AnyEnemy), TacticTargetSelector(Policy),
//                        TacticActionSequence[TacticApproach, TacticTurnAction(유닛별 인스턴스)] ]
//   wait 행: TacticRow[ 조건*, TacticWait ]   (targetless, 셀렉터/시퀀스 없음)
public static class TacticBoardCompiler
{
    private static readonly HashSet<string> TargetBearingConditions = new() { "any_enemy", "enemy_casting" };

    public static TacticCompileResult Compile(TacticBoardDefinition board, ITacticActionResolver resolver,
        StringName catalogRevision, StringName unitId = null)
    {
        // 1. validation 선행. Error가 있으면 compile하지 않는다(ADR-024 §5).
        TacticValidationResult validation = TacticBoardValidator.Validate(board, resolver);
        if (!validation.IsValid)
            return new TacticCompileResult(false, validation.Diagnostics, null);

        TacticPrioritySelector root = null;
        var actions = new List<TurnActionBase>();
        try
        {
            // 2. Node 생성. 비활성(Enabled=false) 행은 컴파일하지 않는다 — 플레이어가 끈 상위 행이 전투에서
            //    발동하면 안 되기 때문이다. 잠긴 system row(default_attack/terminal_wait)는 validator가 Enabled를
            //    강제하므로 항상 생성돼 하한이 유지된다.
            root = new TacticPrioritySelector { Name = "TacticRoot" };
            var generatedRowIds = new List<StringName>();
            foreach (TacticRowDefinition rowDef in board.Rows)
            {
                if (!rowDef.Enabled) continue;

                TacticRow row = BuildRow(rowDef, resolver, actions, out bool created);
                if (!created)
                {
                    // resolver가 Ok로 해석했지만 인스턴스 생성 실패(복제 실패 등). 정리 후 Error.
                    CleanUp(root);
                    return Fail(TacticDiagnosticCodes.ActionCreateFailed, rowDef.RowId, "Action");
                }
                root.AddChild(row);
                generatedRowIds.Add(rowDef.RowId);
            }

            // 3. 생성된 tree 구조 2차 검사(컴파일러 버그 안전망). 임시 컨테이너에 붙였다 뗀다(트리 밖이라 부작용 없음).
            if (GeneratedTreeHasErrors(root, out string detail))
            {
                CleanUp(root);
                return Fail(TacticDiagnosticCodes.GeneratedTreeInvalid, developerDetail: detail);
            }

            // 4. signature 계산 + snapshot 공개. signature는 draft 전체(비활성 행 포함)로 계산한다 — 입력 identity라
            //    비활성 여부가 바뀌면 재컴파일이 필요하다. RowIds는 실제 생성된 행만 담는다(debug/발동 통계 정합).
            string sourceSignature = ComputeSourceSignature(board);
            string compileInputSignature = Hash(sourceSignature + "␄" + (catalogRevision?.ToString() ?? ""));

            var snapshot = new CompiledBoardSnapshot(board.BoardId, board.SchemaVersion, unitId,
                generatedRowIds, sourceSignature, compileInputSignature, root, actions);
            return new TacticCompileResult(true, Array.Empty<TacticDiagnostic>(), snapshot);
        }
        catch (Exception ex)
        {
            // compiler 예외 격리(ADR-024 §5). partial Node 정리, 기존 적용본은 건드리지 않는다(호출자 소관).
            CleanUp(root);
            return Fail(TacticDiagnosticCodes.CompilerInternalError, developerDetail: ex.ToString());
        }
    }

    // 한 행을 정규 구조로 만든다. created=false면 행동 인스턴스 생성에 실패한 것(호출자가 정리·Error).
    private static TacticRow BuildRow(TacticRowDefinition def, ITacticActionResolver resolver,
        List<TurnActionBase> actions, out bool created)
    {
        created = true;
        var row = new TacticRow { Name = $"Row_{def.RowId}", RowId = def.RowId.ToString() };

        string actionType = def.Action?.TypeId.ToString();

        // wait 행: 조건 + TacticWait(무대상).
        if (actionType == "wait")
        {
            AddConditions(row, def);
            row.AddChild(new TacticWait { Name = "Wait" });
            return row;
        }

        // 행동(catalog_action) 행: [후보 공급] 조건 + selector + action sequence.
        // NearestEnemy는 후보 공급 조건(any_enemy/enemy_casting)이 없으면 컴파일러가 AnyEnemy를 넣는다(§11).
        // 이미 target-bearing 조건이 있으면 그 후보와 교집합이라 추가 노드 없이도 최근접이 성립한다.
        bool hasSupplier = def.Conditions != null &&
            def.Conditions.Any(c => c != null && TargetBearingConditions.Contains(c.TypeId.ToString()));
        if (def.Target?.TypeId.ToString() == "nearest_enemy" && !hasSupplier)
            row.AddChild(new TacticConditionAnyEnemy { Name = "SupplyAnyEnemy" });

        AddConditions(row, def);
        row.AddChild(new TacticTargetSelector { Name = "Selector", Policy = def.ApproachPolicy });

        var sequence = new TacticActionSequence { Name = "ActionSeq" };
        sequence.AddChild(new TacticApproach { Name = "Approach" });

        StringName actionId = (def.Action as TacticCatalogActionDefinition)?.ActionId ?? new StringName();
        TurnActionBase instance = resolver.CreateInstance(actionId);
        if (instance == null)
        {
            created = false;
            row.Free();   // 이 행을 즉시 정리(아직 root에 붙기 전).
            return null;
        }
        actions.Add(instance);
        sequence.AddChild(new TacticTurnAction { Name = "Action", TurnAction = instance });
        row.AddChild(sequence);
        return row;
    }

    // draft 조건을 순서대로 런타임 조건 노드로 만들어 행에 붙인다. TypeId는 validation을 통과했으므로 known이다.
    private static void AddConditions(TacticRow row, TacticRowDefinition def)
    {
        if (def.Conditions == null) return;
        int i = 0;
        foreach (TacticConditionDefinition cond in def.Conditions)
        {
            BehaviorTree_Node node = MakeCondition(cond);
            if (node == null) continue;   // validation이 unknown/null을 이미 Error로 거른다(방어적).
            node.Name = $"Cond{i++}";
            row.AddChild(node);
        }
    }

    private static BehaviorTree_Node MakeCondition(TacticConditionDefinition def)
    {
        return def?.TypeId.ToString() switch
        {
            "always" => new TacticConditionAlways(),
            "any_enemy" => new TacticConditionAnyEnemy(),
            "enemy_casting" => new TacticConditionEnemyCasting(),
            _ => null
        };
    }

    // 임시 컨테이너로 생성된 root subtree에 BehaviorTreeValidation을 돌린다. IsError 항목이 있으면 true.
    private static bool GeneratedTreeHasErrors(TacticPrioritySelector root, out string detail)
    {
        detail = null;
        var container = new BehaviorTree();
        container.AddChild(root);
        try
        {
            var results = BehaviorTreeValidation.ValidateTree(container);
            var errors = results.Where(kv => kv.Value.IsError).ToList();
            if (errors.Count == 0) return false;
            detail = string.Join("; ", errors.Select(kv => $"{kv.Key.Name}:{kv.Value.ErrorType}"));
            return true;
        }
        finally
        {
            container.RemoveChild(root);   // root를 떼어 snapshot이 계속 소유하게 한다.
            container.Free();
        }
    }

    private static void CleanUp(TacticPrioritySelector root)
    {
        if (GodotObject.IsInstanceValid(root) && root.GetParent() == null) root.Free();
    }

    private static TacticCompileResult Fail(StringName code, StringName rowId = null, string fieldPath = null,
        string developerDetail = null)
    {
        var diag = new TacticDiagnostic(TacticDiagnosticSeverity.Error, code, rowId, fieldPath,
            developerDetail: developerDetail);
        return new TacticCompileResult(false, new[] { diag }, null);
    }

    // 정규화된 schema field + 행 순서로 결정론 signature를 만든다. instance id/resource path 미사용(ADR-024 §7).
    private static string ComputeSourceSignature(TacticBoardDefinition board)
    {
        const char us = '';   // unit separator
        const char rs = '';   // record separator
        var sb = new StringBuilder();
        sb.Append(board.SchemaVersion).Append(us).Append(board.BoardId);
        foreach (TacticRowDefinition row in board.Rows)
        {
            sb.Append(rs);
            sb.Append(row.RowId).Append(us)
              .Append(row.Enabled ? 1 : 0).Append(us)
              .Append(row.Locked ? 1 : 0).Append(us)
              .Append((int)row.Source).Append(us);
            if (row.Conditions != null)
                foreach (TacticConditionDefinition c in row.Conditions)
                    sb.Append(c?.TypeId.ToString() ?? "").Append(',');
            sb.Append(us)
              .Append(row.Target?.TypeId.ToString() ?? "").Append(us)
              .Append(row.Action?.TypeId.ToString() ?? "").Append(us)
              .Append((row.Action as TacticCatalogActionDefinition)?.ActionId.ToString() ?? "").Append(us)
              .Append((int)row.ApproachPolicy);
        }
        return Hash(sb.ToString());
    }

    private static string Hash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
