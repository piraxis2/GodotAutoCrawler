using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class CharacterArticle : ArticleBase, ITurnAffectedArticle<ArticleBase>, ITacticActionAdapterProvider
{
    // 택틱 런타임에 전장(타일/AStar/상대/이동)을 제공하는 실제 어댑터(BT-002 Step 3.5). 유닛별 인스턴스라
    // 이동 연출 상태가 유닛 간에 섞이지 않는다(R9).
    private CharacterTacticAdapter _tacticAdapter;
    public ITacticActionAdapter TacticAdapter => _tacticAdapter ??= new CharacterTacticAdapter();

    // BT-003 Step 4: 유닛별 마지막 정상 compiled snapshot과 prepare/apply gate. draft/compile result는 보관하지
    // 않아 편집 상태와 적용 상태를 섞지 않는다(ADR-024 §1/§9).
    public TacticBoardApplyState TacticBoardApply { get; } = new();
    private bool _tacticSnapshotTeardownQueued;


    public TurnActionBase CurrentTurnAction { get; set; }
    public ITurnActionState CurrentTurnActionState { get; set; }
    public StatusController StatusController { get; } = new();
    // 유닛별 스킬 ammo remaining. StatusController와 동형의 순수 C# 상태로, .tres에 직렬화되지 않는다. ADR-021.
    public SkillAmmoState SkillAmmoState { get; } = new();

    // 영창 중인지 외부(HUD/조건 어휘/영창 취소)에서 SkillState 구체 타입 없이 조회하는 창구.
    public bool IsCasting => CurrentTurnActionState?.IsCasting ?? false;

    public override float DamageDealtMultiplier => StatusController.DamageDealtMultiplier;
    public override float DamageTakenMultiplier => StatusController.DamageTakenMultiplier;

    // 영창/현재 액션 폐기. 스턴 취소가 소비하며 이미 지불한 비용/마나는 되돌리지 않는다.
    public void CancelCasting()
    {
        CurrentTurnAction = null;
        CurrentTurnActionState = null;
    }
    public TacticBoardApplyStatus TryApplyCompiledTacticBoard(TacticCompileResult compileResult)
        => TacticBoardApply.TryApply(this, compileResult);

    // 실제 준비 화면/세션 consumer가 전투 시작 직전에 닫는다. BattleSession 연결은 BT-003 Step 4 범위 밖이다.
    public void BeginTacticBoardPreparation() => TacticBoardApply.BeginPreparation();
    public void SealTacticBoardForBattle() => TacticBoardApply.SealForBattle();

    public bool IsCurrentTacticBoardInputApplied(string compileInputSignature)
        => TacticBoardApply.IsCurrentInputApplied(compileInputSignature);

    public bool RequiresTacticBoardRecompile(string compileInputSignature)
        => TacticBoardApply.RequiresRecompile(compileInputSignature);



    // 택틱 런타임 수명 주기(BT-002 Step 4). 사망·tree exit·session teardown에서 latch/문맥/확정 대상/진행 중
    // 이동 연출을 모두 폐기해 stale 참조가 다음 전투로 넘어가지 않게 한다(R9).
    // 수제 BT면 루트가 ITacticNode가 아니라 no-op이다.
    public void ResetTacticRuntime()
    {
        (GetNodeOrNull<BehaviorTree>("BehaviorTree")?.Root as ITacticNode)?.ResetForNewTurn();
        _tacticAdapter?.CancelApproach(this);
        CancelCasting();
    }

    public override void _EnterTree()
    {
        OnDead += OnCharacterDead;
    }

    public override void _ExitTree()
    {
        OnDead -= OnCharacterDead;
        ResetTacticRuntime();
        TacticBoardApply.ForgetOnOwnerTreeExit();
    }

    private void OnCharacterDead(ArticleBase deadArticle)
    {
        ResetTacticRuntime();
        // Health.Dead() callback은 턴/물리 실행 중일 수 있다. tree 변경(root remove/free)은 idle에 한 번만
        // 수행해 현재 순회 중인 subtree를 동기 제거하지 않는다(BS-001 deferred teardown과 같은 원칙).
        if (_tacticSnapshotTeardownQueued) return;
        _tacticSnapshotTeardownQueued = true;
        Callable.From(() =>
        {
            _tacticSnapshotTeardownQueued = false;
            if (GodotObject.IsInstanceValid(this)) TacticBoardApply.Teardown(this);
        }).CallDeferred();
    }
    private BehaviorTree _behaviorTree;
    public BehaviorTree BehaviorTree => _behaviorTree ??= GetNode<BehaviorTree>("BehaviorTree");
    public int Priority { get; set; }

    // 유닛의 행동 목록은 wrapper 타입이 아니라 ITurnActionProvider로 순회한다(BT-002 Step 3.5). 그래야 수제 BT의
    // BehaviorTree_TurnAction과 택틱 보드의 TacticTurnAction이 함께 발견된다 — 한쪽만 훑으면 택틱 보드의 제한
    // 스킬이 ammo 충전에서 누락돼 처음부터 CanStart=false가 된다.
    private IReadOnlyList<TurnActionBase> CollectTurnActions()
    {
        var bt = GetNodeOrNull<BehaviorTree>("BehaviorTree");
        if (bt == null) return [];

        var providers = new List<ITurnActionProvider>();
        CollectTurnActionProviders(bt, providers);
        return providers
            .Select(provider => provider.TurnAction)
            .Where(action => action != null)
            .ToList();
    }

    // raw 노드 워크: BehaviorTree.Root(=GetChild(0)) 하위만 보는 FindNodeByType과 달리, Root 밖에 배선된 행이나
    // battle-start 시점의 TreeChildren 미갱신에도 영향받지 않는다.
    private static void CollectTurnActionProviders(Node node, List<ITurnActionProvider> acc)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is ITurnActionProvider provider) acc.Add(provider);
            CollectTurnActionProviders(child, acc);
        }
    }

    private IReadOnlyList<TurnActionBase> UsableTurnActions => CollectTurnActions()
        .Where(action => action.CanStart(this))
        .ToList();

    public bool HasUsableAttack => UsableTurnActions.Count > 0;

    private IReadOnlyList<Vector2I> AttackRangePositions => UsableTurnActions
        .Select(action => action.AttackRangePositions)
        .OrderByDescending(positions => positions.Count)
        .FirstOrDefault() ?? [];

    public List<Vector2I> CalculatedAttackRange => AttackRangePositions.Select(p => p + TilePosition).ToList();

    // 전투 시작 reset(ADR-021 §4): BT의 TurnAction_Skill 정의를 순회해 제한 스킬 ammo를 Ammo로 충전한다.
    // Unlimited은 Charge가 무시(무제한). Battle/Expedition(예약)은 이월 없이 매 전투 full charge → SkillAmmoState가
    // 매번 max로 재설정된다. remaining은 SkillAmmoState에만 살고 SkillDefinition Resource에는 쓰지 않는다.
    public void ChargeBattleAmmo()
    {
        foreach (TurnActionBase action in CollectTurnActions())
        {
            if (action is TurnAction_Skill { Definition: { } def } && def.IsValidV0())
            {
                SkillAmmoState.Charge(def.Id, def.Ammo, def.AmmoResetScope);
            }
        }
    }

    public void ApplyTurnStartEffects()
    {
        StatusController.OnTurnStart();
        ArticleStatus.ApplyTurnStartStatusElements();
        if (ArticleStatus.HasLivingHealth()) ArticleStatus.ApplyAffectingStatuses();

        // BT-002 Step 3(OD2/리뷰 P2-1): 새 자기 턴 시작 시 택틱 트리 latch/문맥을 명시적으로 초기화한다.
        // 멀티턴 action이 CurrentTurnAction으로 BT tick을 우회하면 트리 base status가 Running으로 남아 OnInit
        // 기반 reset이 안 걸릴 수 있으므로, 실제 턴 경계(AdvanceToNextTurn -> 여기)에서 직접 reset한다.
        // 택틱 루트가 아니면(수제 BT) no-op이라 무회귀다.
        (GetNodeOrNull<BehaviorTree>("BehaviorTree")?.Root as ITacticNode)?.ResetForNewTurn();

        // 남은 이동 연출을 정리한다(Step 3.5). 어댑터를 아직 만들지 않았으면 만들지 않는다(수제 BT 무회귀).
        _tacticAdapter?.ResetTurn(this);
    }

    public BtStatus TurnPlay(double delta)
    {
        if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");

        // 스턴 턴은 영창을 이어가지 않고 즉시 턴을 넘긴다(턴당 1회 소비).
        if (StatusController.ConsumeStunForTurn())
        {
            CancelCasting();
            return BtStatus.Success;
        }

        if (CurrentTurnAction == null) return BehaviorTree.Behave(delta, this);
        
        // 현재 턴 액션이 null이 아닐 경우, 액션을 실행
        ActionState actionState = CurrentTurnAction.Action(delta, this);

        if (actionState is ActionState.End or ActionState.Failure) CurrentTurnAction = null;

        // Running: 실행 중, Failure: 지불/검증 실패(다음 행 fallback), 그 외: 완료(Success)
        return actionState switch
        {
            ActionState.Running => BtStatus.Running,
            ActionState.Failure => BtStatus.Failure,
            _ => BtStatus.Success
        };

    }

}
