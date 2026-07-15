using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;

// apply 결과는 BT-004/DL-001 소비자가 fail-closed 사유를 표시할 수 있게 안정 enum으로 노출한다.
public enum TacticBoardApplyStatus
{
    Applied,
    CompilationFailed,
    PreparationClosed,
    NotReady,
    TurnInProgress,
    InvalidSnapshot,
    SnapshotParented,
    UnitMismatch,
    InstallFailed
}

// 유닛별 마지막 정상 적용본과 preparation gate를 소유한다(BT-003 Step 4, ADR-024 §7~§9).
// draft/compile result는 이 객체가 보관하지 않는다. 오직 실제로 설치에 성공한 snapshot만 last-good으로 유지한다.
public sealed class TacticBoardApplyState
{
    public CompiledBoardSnapshot AppliedSnapshot { get; private set; }
    public bool HasAppliedSnapshot => AppliedSnapshot != null;
    public bool IsPreparationOpen { get; private set; } = true;
    public string AppliedCompileInputSignature => AppliedSnapshot?.CompileInputSignature;

    // 현재 입력이 마지막 정상 적용본과 같은지 명시적으로 비교한다. 불일치 상태에서 오래된 적용본을 최신으로
    // 표시하지 않도록 consumer가 이 seam을 사용한다(ADR-024 §9).
    public bool IsCurrentInputApplied(string compileInputSignature)
        => AppliedSnapshot != null
           && !string.IsNullOrEmpty(compileInputSignature)
           && AppliedSnapshot.CompileInputSignature == compileInputSignature;

    public bool RequiresRecompile(string compileInputSignature)
        => !IsCurrentInputApplied(compileInputSignature);

    // 미래 준비 화면/전투 세션이 전투 시작 전에 호출한다. BattleSession 배선은 Step 4 범위 밖이다.
    public void BeginPreparation() => IsPreparationOpen = true;
    public void SealForBattle() => IsPreparationOpen = false;

    public TacticBoardApplyStatus TryApply(CharacterArticle owner, TacticCompileResult compileResult)
    {
        if (compileResult == null || !compileResult.Success || compileResult.Snapshot == null)
            return TacticBoardApplyStatus.CompilationFailed;

        return TryApplySnapshot(owner, compileResult.Snapshot);
    }

    public TacticBoardApplyStatus TryApplySnapshot(CharacterArticle owner, CompiledBoardSnapshot snapshot)
    {
        if (!IsPreparationOpen) return TacticBoardApplyStatus.PreparationClosed;
        if (!GodotObject.IsInstanceValid(owner)) return TacticBoardApplyStatus.NotReady;

        BehaviorTree tree = owner.GetNodeOrNull<BehaviorTree>("BehaviorTree");
        if (tree == null || !tree.IsInsideTree() || tree.Blackboard == null)
            return TacticBoardApplyStatus.NotReady;

        // v1은 전투 중 hot-swap을 허용하지 않는다. 실제 세션은 SealForBattle을 호출하고, 실행 중 action은
        // 이 방어선도 통과할 수 없다.
        if (owner.CurrentTurnAction != null || owner.CurrentTurnActionState != null)
            return TacticBoardApplyStatus.TurnInProgress;

        if (snapshot == null || !GodotObject.IsInstanceValid(snapshot.Root))
            return TacticBoardApplyStatus.InvalidSnapshot;
        if (snapshot.Root.GetParent() != null)
            return TacticBoardApplyStatus.SnapshotParented;
        if (!string.IsNullOrEmpty(snapshot.UnitId)
            && snapshot.UnitId != owner.Name)
            return TacticBoardApplyStatus.UnitMismatch;

        // InstallRoot가 성공하기 전에는 기존 root와 applied metadata를 건드리지 않는다.
        if (!tree.InstallRoot(snapshot.Root))
            return TacticBoardApplyStatus.InstallFailed;

        // 새 root는 fresh지만, 교체 전 어댑터 tween/영창 같은 CharacterArticle 소유 상태도 함께 비운다.
        // 이 호출은 설치 성공 뒤에만 하므로 install 실패 시 last-good runtime을 훼손하지 않는다.
        owner.ResetTacticRuntime();
        AppliedSnapshot = snapshot;
        return TacticBoardApplyStatus.Applied;
    }

    // death 완료 deferred 또는 정상 session teardown에서 호출한다. apply owner가 설치한 root identity만 제거하므로
    // 수제 BT/다른 consumer의 root를 지우지 않는다.
    public void Teardown(CharacterArticle owner)
    {
        CompiledBoardSnapshot snapshot = AppliedSnapshot;
        if (snapshot == null)
        {
            IsPreparationOpen = false;
            return;
        }

        owner?.ResetTacticRuntime();
        ResetSnapshotRuntime(snapshot);

        BehaviorTree tree = GodotObject.IsInstanceValid(owner)
            ? owner.GetNodeOrNull<BehaviorTree>("BehaviorTree")
            : null;
        if (GodotObject.IsInstanceValid(snapshot.Root))
        {
            if (tree != null && ReferenceEquals(tree.Root, snapshot.Root))
                tree.RemoveInstalledRoot(snapshot.Root);
            else
                snapshot.FreeRoot();
        }

        AppliedSnapshot = null;
        IsPreparationOpen = false;
    }

    // owner tree가 이미 exit 중이면 Node는 Godot 부모 수명주기가 free한다. 여기서는 action binding과 C# state만
    // 즉시 놓아 외부 snapshot holder가 죽은 유닛/대상을 붙잡지 않게 한다.
    public void ForgetOnOwnerTreeExit()
    {
        if (AppliedSnapshot != null) ResetSnapshotRuntime(AppliedSnapshot);
        AppliedSnapshot = null;
        IsPreparationOpen = false;
    }

    private static void ResetSnapshotRuntime(CompiledBoardSnapshot snapshot)
    {
        if (GodotObject.IsInstanceValid(snapshot.Root)
            && snapshot.Root is ITacticNode tacticRoot)
        {
            tacticRoot.ResetForNewTurn();
        }

        foreach (var action in snapshot.Actions)
        {
            action?.ClearExplicitTarget();
        }
    }
}

