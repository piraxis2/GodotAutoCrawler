---
id: BT-003-review
type: review
status: design-review
updated: 2026-07-15
system: BehaviorTree
step: 0
---

# BT-003 Tactic Board Resource, Validation, and Compiler — Step 0 Design Review

## Scope

이 리뷰는 [[BT-003-Tactic-Board-Resource-Validation-Compiler]] **Step 0 (Design Review)** 전용이다. 제품 코드/`.tscn`/`.tres`는 수정하지 않는다(문서만 갱신).

목표는 [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]]의 draft resource · validator · resolver · diagnostics · compiler · snapshot · atomic apply 계약을 실제 BT-002 런타임 노드/`TurnActionBase`/`CharacterArticle`/`BehaviorTree` 컨테이너 수명주기와 정적으로 대조하고, Open Decisions 1~12를 닫아(1차 1~10 + 재검토 11~12) Step 1~4 경계와 API 형태를 확정하는 것이다.

## 대조한 실측 코드

- 컨테이너 root 계약: `addons/behaviortree/BehaviorTree.cs:15` — `Root => GetChildCount() > 0 ? GetChild(0) as BehaviorTree_Node : null`. root는 **첫 자식**이다.
- 컨테이너 수명: `BehaviorTree.cs:60-85` — `_Ready`가 `Blackboard`를 생성하고 `ChildOrderChanged += OnChildOrderChanged`, `SetTree(Root)`, 디버거 `_registry`/`register` 전송을 한다.
- root 배선: `BehaviorTree.cs:102-119` — `OnChildOrderChanged → OnUpdate() → SetTree(Root)`는 **동기**다. `SetTree`는 **private**이며 `node.Tree`를 재귀 설정한다.
- 지연 갱신 경로: `BehaviorTree.cs:244-257` — `UpdateRequest()`는 `CallDeferred(OnUpdate)`(지연). `OnUpdate`는 `!IsInsideTree()` 가드 + `EmitSignal("OnUpdateTree")` + `GD.Print` **부작용**을 가진다.
- 디버그 구조 재전송: `BehaviorTree.cs:121-135` — `SendStructure()`는 **public**, 디버거 활성 시에만 payload를 만든다.
- tick: `BehaviorTree.cs:236-242` — `Behave = Root?.Behave(delta, owner) ?? BtStatus.Failure`.
- 컨테이너 캐시: `Assets/Script/Article/CharacterArticle.cs:64-65` — `_behaviorTree ??= GetNode<BehaviorTree>("BehaviorTree")`. 컨테이너 참조를 캐시하며 **root 교체와 무관**하게 유지된다.
- live root 재조회: `CharacterArticle.cs:47,132` — `GetNodeOrNull<BehaviorTree>("BehaviorTree")?.Root as ITacticNode`. reset 경로는 캐시가 아니라 **live `Root`**를 읽으므로 교체 후 새 root를 본다.
- 행동 수집 seam: `CharacterArticle.cs:71-93` — `ITurnActionProvider` raw 노드 워크(`CollectTurnActionProviders`). `Assets/Script/TurnAction/ITurnActionProvider.cs:8-11`가 계약.
- ammo 충전: `CharacterArticle.cs:111-120` — `ChargeBattleAmmo`가 `TurnAction_Skill.Definition`을 순회. `SkillAmmoState`는 유닛 소유(`CharacterArticle.cs:27`).
- 턴 경계 reset: `CharacterArticle.cs:122-136` — `ApplyTurnStartEffects → (root as ITacticNode).ResetForNewTurn()` + 어댑터 `ResetTurn`.
- **행동 상태 소유자**: `Assets/Script/TurnAction/TurnActionBase.cs:22-23` — `abstract partial class TurnActionBase : Resource`. `ActionQueue`(25), `_usedCost`(39), `_explicitTarget`/`_explicitTargetBound`(66-67)가 **인스턴스 mutable 상태**다.
- 택틱 행동 wrapper: `Assets/Script/AutoCrawlerBehaviorTree/Tactic/TacticTurnAction.cs:19-21` — `TacticTurnAction : TacticExecuteAction, ITurnActionProvider`, `[Export] TurnActionBase TurnAction`. `ExecuteAction`(36-79)이 대상 바인딩·`Init` 1회·`ActionState` 매핑.
- 우선순위 selector: `Tactic/TacticPrioritySelector.cs:21-92` — `_latchedIndex` **인스턴스 필드** latch, `ResetForNewTurn`, 비택틱 자식 fail-closed(`GD.PushError`+skip).
- 행/문맥: `Tactic/TacticRow.cs:23,29` — `[Export] string RowId`, `_context = new TacticRowContext()` 인스턴스 필드. `Tactic/TacticRowContext.cs:33-65` — 후보 교집합/확정 대상/`Reset`.
- 행동 시퀀스/접근/대기: `Tactic/TacticActionSequence.cs`(`[Approach, Action]`), `Tactic/TacticApproach.cs:95-97`(Exhausted → policy 분기), `Tactic/TacticWait.cs`(항상 `ActionCompleted`, `ITacticNode`).
- 대상 셀렉터/정책: `Tactic/TacticTargetSelector.cs:24` — `[Export] TacticApproachPolicy Policy` 기본 `ApproachAllowed`, `IsActionable`이 feasibility 필터.
- 어휘: `Tactic/TacticVocabulary.cs` — `TacticConditionAlways/AnyEnemy/EnemyCasting`. 어댑터 seam `Tactic/ITacticActionAdapter.cs`, 실제 어댑터 `Tactic/CharacterTacticAdapter.cs`.
- 2차 구조 검사: `addons/behaviortree/BehaviorTreeValidation.cs:62-238` — `ValidateTree(BehaviorTree tree)`가 **컨테이너**를 받아 `GetChild(0)` + `GatherAllNodes`로 구조 검사. 택틱 타입(`TacticPrioritySelector/TacticRow/TacticActionSequence`) 규칙 포함.
- 설계 근거 문서: `GameDesign/기획서/09_택틱보드.md` H-3/H-6/H-7/H-8/H-11.

## Findings

설계는 전반적으로 구현 가능하다. draft/compiled/applied **세 상태 분리**, per-unit 격리, atomic root 교체, last-good 보존은 실제 코드 수명주기와 정합한다. 아래는 1차 리뷰가 찾은 P2/P3다. **재검토에서 추가로 P1 2건**(후보 공급·기본 행, action template source)이 확인됐다 — "재검토 (2026-07-15)" 절 참고. 모두 Open Decisions/ADR-024에서 닫았다.

### P2

1. **[P2] `BehaviorTree`에 원자적 root 설치 public API가 없다 — ADR-024 §8 / OD4·OD10 명문화 필요**
   - 실측: root 교체를 즉시 완료시키는 유일한 동기 경로는 `ChildOrderChanged → OnUpdate → SetTree(Root)`뿐인데(`BehaviorTree.cs:102-119`), `SetTree`는 **private**이고 `OnUpdate`는 `GD.Print`와 `EmitSignal("OnUpdateTree")` **부작용**을 동반한다. 명시 갱신 API `UpdateRequest`는 `CallDeferred`라 **다음 프레임**에야 반영된다(`BehaviorTree.cs:246-249`).
   - 영향: ADR-024 §8은 "새 subtree의 `Tree`/child cache를 **동기적으로** 초기화하고 첫 tick이 deferred child-order update에 의존하지 않게 한다"를 요구한다(matrix #13). 현재 API로는 (a) child-order 부작용에 우연히 기대거나 (b) deferred 프레임을 기다리는 두 경로만 있어 계약을 명시적으로 만족시키지 못한다.
   - 결론: Step 4는 컨테이너에 **명시적 root installer public 메서드**(예: `BehaviorTree.InstallRoot(BehaviorTree_Node newRoot)`)를 추가해야 한다. 이 메서드는 ①새 root add + child-0 보장, ②동기 `SetTree(newRoot)`, ③디버거 활성 시 `SendStructure()` 재전송(`BehaviorTree.cs:121`은 이미 public), ④install 성공 후에만 old root reset/free를 수행한다. `ChildOrderChanged` 부작용에 의존하지 않는다. → OD4/OD10에서 닫는다.

2. **[P2] `TurnActionBase`는 상태를 가진 `Resource`다 — resolver가 유닛별 **새 인스턴스**를 만들어야 한다 (ADR-024 §3 / OD2·OD3)**
   - 실측: `TurnActionBase : Resource`이며 `_usedCost`/`ActionQueue`/`_explicitTarget`/`_explicitTargetBound`가 인스턴스 상태다(`TurnActionBase.cs:25,39,66-67`). `TacticTurnAction.TurnAction`은 `[Export]`(`TacticTurnAction.cs:21`)라 컴파일러가 대입한다. Godot `.tres` `load()`는 **참조 공유 캐시**를 돌려준다.
   - 영향: 같은 `ActionId`로 두 유닛을 컴파일할 때 컴파일러가 **동일 `TurnActionBase` 인스턴스**를 두 snapshot에 대입하면 `_usedCost`/action queue/확정 대상이 유닛 간에 오염된다(matrix #10 위반). `SkillState`/ammo/mana는 이미 `CharacterArticle` 소유라 격리되지만 `TurnActionBase` 자체는 아니다.
   - 결론: `ITacticActionResolver`는 유닛마다 **새 `TurnActionBase`를 생성/`Duplicate`**해서 반환해야 하며, 로드된 template Resource나 다른 유닛의 인스턴스를 절대 공유하지 않는다(§3). matrix #10은 두 유닛의 `TacticTurnAction.TurnAction`에 대해 **`ReferenceEquals == false`**를 단언한다. → OD2/OD3에서 닫는다.

3. **[P2] "definition → exact node structure" 검증(matrix #8)을 위해 접근 정책의 **정규 구조 매핑**을 하나로 고정해야 한다 — Step 3 명문화**
   - 실측: 접근 정책은 두 곳에 실린다 — 구조(`TacticActionSequence[TacticApproach, TacticTurnAction]` vs 단독 `TacticTurnAction`)와 `TacticTargetSelector.Policy` export(`TacticTargetSelector.cs:24`). `TacticApproach`는 `Immediate`에서도 in-range면 무이동 `ActionCompleted`, Exhausted면 `CancelledAfterCommit`을 내므로(`TacticApproach.cs:73-99`) **Immediate 행에도 시퀀스를 둘 수 있다**.
   - 영향: 컴파일러가 어떤 행은 시퀀스, 어떤 행은 단독 행동으로 내면 "동일 draft → 동일 구조/순서"(matrix #8/#9)를 결정론적으로 단언하기 어렵다.
   - 권장(닫음): **모든 행동 행을 `TacticRow[조건*, TacticTargetSelector(Policy), TacticActionSequence[TacticApproach, TacticTurnAction]]` 단일 정규 형태**로 낸다. 정책 차이는 selector의 `Policy` export로만 표현하고 구조는 동일하게 유지한다. 기본 wait 행만 예외로 `TacticRow[TacticWait]`(조건·셀렉터 없음, RowId 보존)로 낸다. → OD1/OD5에서 닫는다.

### P3

4. **[P3] 2차 `BehaviorTreeValidation`은 "root가 PrioritySelector인지 / 기본 행이 있는지"를 모른다 — Step 2·Step 3 경계 명문화**
   - 실측: `ValidateTree`(`BehaviorTreeValidation.cs:62-238`)는 노드 타입별 child 규칙만 본다. root가 단독 `TacticWait`여도, selector에 행이 0개여도 **오류를 내지 않는다**. 또한 컨테이너(`BehaviorTree`) 인스턴스를 인자로 받으므로 detached subtree를 검사하려면 **임시 컨테이너**에 root를 붙였다 떼야 한다(Task Step 3 "임시 owner/container seam").
   - 영향: 2차 검사는 **컴파일러 버그의 안전망**(잘못 생성된 구조 차단)이지, "기본 행 존재/우선순위 root 타입" 같은 **플레이어 언어 규칙**을 담지 않는다. 그 규칙은 Step 2 draft validator가 소유한다(ADR-024 §5·§6).
   - 결론: Step 3은 임시 `BehaviorTree` 컨테이너를 만들어(트리 밖이면 `_Ready` 미실행 → Blackboard/registry 부작용 없음) `AddChild(root)` → `ValidateTree` → `RemoveChild` 후 snapshot에 넘긴다. 검사 후 결과에 `IsError` 항목이 하나라도 있으면 `compiler_internal_error`가 아니라 별도 `generated_tree_invalid` 진단으로 compile 실패시킨다(matrix #12). "root 타입/기본 행 누락"은 Step 2에서 잡는다.

5. **[P3] `RowId` 타입 정합 — draft `StringName` ↔ 런타임 `string`**
   - 실측: 런타임은 `TacticRow.RowId`가 `string`(`TacticRow.cs:23`)이고 디버그 payload도 `string`(`BehaviorTree.cs:195` `ReportTacticRow(string, int)`)이다. ADR-024 §2/Task는 draft `RowId`를 `StringName`으로 제안한다.
   - 결론: 컴파일러는 `StringName → string`을 **한 곳에서** 변환하고, `SourceSignature`·진단 정렬·debug RowId가 모두 같은 정규화 문자열을 쓰게 한다(matrix #9). 저장 schema는 `StringName`, 런타임 대입 시 `.ToString()` 정규화로 통일한다.

## Open Decisions (1~10 닫음; 11~12는 "재검토" 절)

1. **Definition 표현** — **확정: typed Resource subclass + 안정 `TypeId`.** 기존 런타임이 이미 타입별 노드(`TacticConditionAlways/AnyEnemy/EnemyCasting`, `TacticTargetSelector`, `TacticTurnAction`)로 갈리므로, draft도 타입별 definition + registry(`TypeId → 노드 factory`)가 자연스럽다. 범용 Dictionary 금지. Finding 3의 정규 구조로 매핑한다.
2. **행동 참조** — **확정: draft는 안정 `ActionId`/`SkillId`, `ITacticActionResolver`가 유닛별 새 `TurnActionBase` 인스턴스 제공.** `TurnActionBase`가 상태 있는 `Resource`이므로(Finding 2) resolver는 `new`/`Duplicate`로 격리 인스턴스를 만들고 template/타 유닛 인스턴스를 공유하지 않는다. 해석 불가 ID는 blocking Error.
3. **snapshot 형태** — **확정: per-unit detached Node subtree 단일 소유.** 모든 런타임 상태가 노드 인스턴스 필드(`_latchedIndex`, `TacticRow._context`, `TacticApproach._moving`)와 `CharacterArticle`(ammo/mana/`CurrentTurnAction`/어댑터)에 있으므로, subtree를 유닛별로 새로 컴파일하면 격리가 성립한다. 단, §3의 `TurnActionBase` 인스턴스 격리가 전제다.
4. **적용 방식** — **확정: 컨테이너 유지 + 명시적 atomic root installer.** `CharacterArticle.BehaviorTree` 캐시는 컨테이너 참조라 root 교체와 무관하게 유지되고(`CharacterArticle.cs:64-65`), reset 경로는 live `Root`를 재조회한다(`:47,132`). Finding 1의 새 public `InstallRoot` 메서드가 동기 `SetTree` + 디버그 구조 재전송 + install 성공 후 old root free를 소유한다.
5. **기본 행** — **확정: 잠긴 `TacticRowDefinition`으로 draft에 저장, validator가 보호, compiler silent synthesis 금지.** compiler는 이를 `TacticRow[TacticWait]`(RowId 보존, `ReportRow`가 `TacticRow`만 보고하므로 debug 통계 연결됨)로 낸다. 손상/누락/중복은 Step 2 blocking Error.
6. **마지막 정상본** — **확정: compile/apply 실패 시 보존, 자동 fallback 출전 금지.** apply owner가 `applied CompileInputSignature`와 `current CompileInputSignature`(board `SourceSignature` ⊕ catalog `CatalogRevision`, §7/§9)를 별도로 들고, 실패는 old root/metadata를 건드리지 않는다(Finding 1의 순서 계약). draft 편집뿐 아니라 catalog revision 변경도 stale로 판정한다(2차 P2, matrix #28). 명시적 revert UI는 BT-004.
7. **source signature** — **확정: 정규화된 schema field 값 + 행 순서로 결정론적 계산.** instance id/resource path 미사용. draft만의 순수 함수라 코드 의존 없음. `RowId`는 Finding 5의 정규화 문자열 사용.
8. **warning 범위** — **확정: v1은 정적으로 확실히 증명 가능한 것만.** 동일 활성 행 중복, 항상 참 상위 행의 하위 가림, 조건 포함으로 증명되는 죽은 행. 마나 고갈/발동 빈도/loadout 실행 가능성 같은 추정은 Info 또는 후속.
9. **schema version** — **확정: v1만 읽고 unknown/future는 blocking Error + 원본 보존.** 자동 수정/재저장/파괴 금지. migration framework는 실제 v2 등장 시 도입.
10. **compile/apply API 수명** — **확정: prepare 단계 동기 compile, 성공 결과만 동기 install.** `SetTree`가 이미 동기이므로(`BehaviorTree.cs:107-119`) 설치도 동기 가능. 순서 = ①compile(격리 노드/행동 생성) → ②임시 컨테이너 2차 validation(Finding 4) → ③snapshot 공개 → ④`InstallRoot`(동기 `SetTree` + `SendStructure`) → ⑤old root reset/free. 실패는 어느 단계든 partial Node를 정리하고 old applied를 유지한다.

## Step Assessment

- **Step 1 (Draft Resource + round-trip)**: 입력 = 없음(신규 schema). 출력 = versioned `TacticBoardDefinition` 및 하위 definition Resource + `.tres` 왕복. 선행 = OD1/OD9. 완료조건 관찰 가능(행 순서/ID/subtype/parameter/정책 보존). 런타임 상태 미포함이 핵심. **범위 적절.**
- **Step 2 (Validator + diagnostics)**: 입력 = Step 1 draft + resolver context. 출력 = pure/read-only validator + immutable diagnostics. 선행 = OD2(해석 검사)/OD4~6/OD8. 기본 행 보호·root 규칙은 여기 소유(Finding 4). **범위 적절.**
- **Step 3 (Compiler + per-unit snapshot)**: 입력 = 유효 draft + resolver. 출력 = 정규 구조 subtree(Finding 3) + 유닛별 `TurnActionBase`(Finding 2) + 2차 validation + signature. 선행 = OD2/OD3/OD7 + Step 1·2. 가장 무거운 Step. matrix #8~#12 집중. **범위 적절, 다만 정규 구조 매핑을 Step 시작 전 고정(Finding 3).**
- **Step 4 (Atomic apply + completion)**: 입력 = 성공 snapshot. 출력 = 새 public `InstallRoot`(Finding 1) + apply gate + last-good metadata + 문서/완료 리뷰. 선행 = OD4/OD6/OD10 + Step 3. matrix #13~#18. **범위 적절.**

Step 분할/병합 권고 없음. Step 3이 크지만 "정규 구조 생성"과 "2차 validation/signature"가 한 데이터 흐름이라 유지한다.

## Verification Assessment

Minimum Verification Matrix(18건)는 실패 시나리오를 잘 덮는다. Step 0 관점의 보강:

- **#10 (두 유닛 격리)**: Node/context 격리뿐 아니라 **`TacticTurnAction.TurnAction` 인스턴스 `ReferenceEquals == false`**를 명시 단언한다(Finding 2). 한 유닛의 `_usedCost` 진행이 다른 유닛에 안 보임도 확인.
- **#13 (apply then first tick)**: `InstallRoot` 직후 **deferred 프레임 없이** 곧바로 `BehaviorTree.Behave` 1회를 구동해 새 root가 tick되고 `Tree`가 세팅됐음(`ReportRow` 동작)을 단언한다. `ChildOrderChanged` 부작용에 의존하지 않음을 검증(Finding 1).
- **#12 (generated BT invalid)**: 임시 컨테이너 2차 validation이 `IsError`를 낼 때 `generated_tree_invalid` 진단으로 compile 실패 + partial Node 정리(Finding 4). 정상 컴파일 트리는 `ValidateTree` 결과가 비어 있음도 확인.
- **디버그 구조 재전송**: `InstallRoot`가 디버거 활성 시 `SendStructure`를 호출해 교체 후 구조/ RowId가 새 트리와 일치함(matrix #17의 debugger smoke와 연결).
- **#5 기본 행 손상**: compiler가 `TacticRow[TacticWait]`을 몰래 합성하지 않고 Step 2가 blocking Error를 내는지(OD5) 별도 단언.
- Step 1~4 회귀: BT-002 Step 1~4 헤드리스 테스트와 실제 battle fixture, `dotnet build`/`--import`를 각 Step에서 재실행. 기존 수제 BT 경로(matrix #18)는 `InstallRoot`/resolver를 쓰지 않으므로 무회귀.

빠진 실패 시나리오는 없다. 가정: `TurnActionBase`의 `new`/`Duplicate` 격리는 Step 3 spike로 실측 확인(로드 캐시 공유 여부·Duplicate deep/shallow 경계)한다.

## 재검토 (2026-07-15): 누락 P1 2건

1차 리뷰(위 Findings)는 P2/P3만 다뤘으나, 후보 공급 의미와 action template source에서 **P1급 설계 계약 누락**이 확인됐다. 아래 두 건을 실제 런타임 근거와 함께 닫고 ADR-024 §11/§12로 고정했다.

### [P1-1] 후보 공급과 기본 행 의미 미확정 — 처리: 수정(ADR-024 §11)

- 발생 조건: draft `NearestEnemy` 대상을 `TacticTargetSelector`만 생성해 컴파일하거나, `ContextTarget` 대상 행에 target-bearing 조건이 없는 경우.
- 런타임 근거(재대조): `TacticConditionAlways.PerformAction`은 `Success`만 반환하고 **후보를 만들지 않는다**(`Tactic/TacticVocabulary.cs:16-19`). `TacticConditionAnyEnemy`만 `Context.ContributeCandidates`로 후보를 공급한다(`:23-36`). `TacticTargetSelector`는 `_context.LiveCandidates`에서만 고르고 후보가 비면 `Failure`다(`Tactic/TacticTargetSelector.cs:48-55`). `TacticRowContext.LiveCandidates`는 `_candidates`가 null(gate-only)이면 empty를 돌려준다(`Tactic/TacticRowContext.cs:49-50`).
- 사용자 영향: 확정하지 않으면 compiler가 selector 단독 `NearestEnemy` 행을 생성해 **항상 불성립**하는 죽은 행이 되거나, 기본 공격 행이 대상을 못 잡아 유닛이 늘 wait한다. "가장 가까운 적을 공격"이 화면 규칙과 실제 AI가 어긋난다.
- 수정 방향(확정): compiler는 `NearestEnemy`에 후보 공급을 보장한다. **Step 3 정련(정규 규칙)**: target-bearing 조건(any_enemy/enemy_casting)이 **없을 때만** `TacticConditionAnyEnemy`를 주입하고, 이미 `EnemyCasting` 등이 있으면 그 조건이 후보를 공급한다(`전체∩X=X` idempotent라 두 경우 후보 집합·대상 선택 동일 — 중복 노드만 제거). `ContextTarget`에 후보 공급 조건이 없으면 `missing_target_context` blocking Error. 기본 행동은 잠긴 두 system row(`default_attack`/`terminal_wait`)로 draft에 직렬화·보호하고 compiler silent synthesis 금지. matrix #19~#23.

### [P1-2] action ID의 production source 미확정 — 처리: 수정(ADR-024 §12)

- 발생 조건: resolver가 "현재 `BehaviorTree`에서 action을 찾아 clone"하는 방식을 쓰고, apply가 그 root를 교체·free하는 경우. 특히 A→B→A 재컴파일과 반복 compile.
- 런타임 근거(재대조): `CharacterArticle.CollectTurnActionProviders`는 **현재 BT 자식**을 raw walk해 `ITurnActionProvider`를 찾는다(`Assets/Script/Article/CharacterArticle.cs:86-93`). apply(§8)는 그 root를 새 compiled root로 교체하고 old root를 free한다. `TurnActionBase : Resource`는 `_usedCost`/`ActionQueue`/`_explicitTarget` mutable 상태를 인스턴스에 가진다(`Assets/Script/TurnAction/TurnActionBase.cs:22-23,25,39,66-67`). `BehaviorTree_TurnAction`/`TacticTurnAction`의 `[Export] TurnActionBase`가 그 인스턴스를 참조한다.
- 사용자 영향: 교체 대상 트리를 template source로 삼으면 root free 뒤 template이 사라지고, `.tres` 공유 인스턴스를 그대로 대입하면 두 유닛의 cost/target이 오염된다(matrix #24 위반). 재컴파일이 순환에 빠진다.
- 수정 방향(확정): runtime BT와 독립적으로 살아남는 `TacticActionCatalog`(`CharacterArticle`/미래 `PartySnapshot` 소유)를 도입한다. `ITacticActionResolver`는 catalog에서 `ActionId`를 조회하고, 매 compile마다 격리된 새 `TurnActionBase` 인스턴스(`ReferenceEquals == false`, fresh queue/cost/target/phase)를 반환한다. 복제 방식은 Step 3 spike, 불완전하면 실패. unknown/duplicate/null ID·template은 Error + applied root 보존. matrix #24~#27.

두 P1은 **1차 Finding 2·3과 같은 방향**을 더 강하게 고정한 것으로, 서로 모순되지 않는다(§3 fresh-instance → §12 catalog source, Finding 3 canonical mapping → §11 candidate supply/system rows).

## 재검토 2차 (2026-07-15): P2 문서 보정 + P3 정합

오너 재검토 판정 `수정 후 완료`. P1 2건 해소를 확인하고 남은 문서 보정을 지시했다.

### [P2] compile 입력 signature가 catalog revision을 포함하지 않음 — 처리: 수정(ADR-024 §7/§9)

- 근거: §12로 action 해석이 catalog에 의존하게 됐으므로, board `SourceSignature`만으로 stale을 판정하면 **같은 board + catalog revision 변경**이 감지되지 않아 오래된 loadout으로 컴파일된 snapshot을 최신처럼 쓸 수 있다.
- 수정(확정): applied 상태의 stale 판정 입력을 `CompileInputSignature = board SourceSignature ⊕ catalog CatalogRevision`으로 정의(§7). catalog는 안정 `CatalogRevision`을 소유하고 Step 1에서 catalog Resource에 함께 넣는다. §9는 draft 편집뿐 아니라 catalog revision 변경도 "재컴파일 필요"로 판정하고, 명시적 재적용 전 stale snapshot을 현재 입력의 최신 적용본으로 표시하지 않는다. 최소 검증 matrix #28 추가.

### [P3] 문서 정합 — 처리: 수정

- Task Step 0 Scope(`230`)·Done condition의 `Open Decisions 1~10` → `1~12`. ADR-024 Status(`14`)의 `OD1~10` → `OD1~12`. Review Scope/헤더도 1~12 정합.
- `Home.md`/`Open-Tasks.md` frontmatter `updated: 2026-07-14` → `2026-07-15`.

## Verdict

**수정 후 완료** (오너 재검토 판정; 설계 리뷰 `Approved after design fixes`).

설계는 구현 가능하다. 1차 P2/P3(Finding 1~4), 재검토 P1-1/P1-2, 2차 P2/P3를 모두 반영했고 Open Decisions 1~12를 닫았다. Step 1 진입 전 반영한 문서 수정(제품 코드 아님):

1. ADR-024 §8: **명시적 root installer public API**(`InstallRoot`) + 디버그 구조 재전송·free 순서(Finding 1).
2. ADR-024 §3: resolver가 **유닛별 새 `TurnActionBase` 인스턴스**를 만든다(template/로드 Resource 공유 금지)(Finding 2).
3. ADR-024 Step 0 Design Fixes: **접근 정책 정규 구조 매핑**(단일 시퀀스 + system row 예외)(Finding 3).
4. ADR-024 Step 0 Design Fixes: **`BehaviorTreeValidation` = 컴파일러 버그 안전망**, root/기본 행 규칙은 Step 2 소유(Finding 4).
5. ADR-024 §11: **후보 공급 + 잠긴 두 system row**(재검토 P1-1).
6. ADR-024 §12: **`TacticActionCatalog` production source + 격리 resolver**(재검토 P1-2).
7. ADR-024 §7/§9: **`CompileInputSignature`(board ⊕ catalog revision) stale 판정**(2차 P2).

위 반영과 함께 ADR-024를 `accepted`로 유지한다. 코드 구현은 Step 1 프롬프트에서 시작한다.

## Related

- [[BT-003-Tactic-Board-Resource-Validation-Compiler]]
- [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]]
- [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]
- [[BT-002-Tactic-Board-Runtime-Extension]]
- [[BT-002-Tactic-Board-Runtime-Extension-Review]]
- [[BehaviorTree-System]]
