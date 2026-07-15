---
id: ADR-024
type: decision
status: accepted
date: 2026-07-15
system: BehaviorTree
tags: [adr, tactic-board, resource, compiler, snapshot]
---

# ADR-024: Tactic Board Draft, Compile Diagnostics, and Runtime Snapshot

## Status

Accepted (2026-07-15). [[BT-003-Tactic-Board-Resource-Validation-Compiler]] Step 0 설계 리뷰가 실제 Godot Resource/Node/TurnAction 수명주기와 대조해 OD1~12를 닫고 승인했다(리뷰: [[BT-003-Tactic-Board-Resource-Validation-Compiler-Review]], 판정 `수정 후 완료`/`Approved after design fixes`). 아래 §3·§8·§11·§12와 Warning Policy 뒤 "Step 0 Design Fixes" 절이 승인 시 반영한 seam을 확정한다.

## Context

[[BT-002-Tactic-Board-Runtime-Extension]]은 택틱보드의 전투 실행 의미를 완성했다. 그러나 현재 보드는 코드나 fixture가 Node를 직접 조립해야 하며 다음 경계가 없다.

- 플레이어 편집본을 저장하는 versioned Resource.
- 오류와 경고를 행·필드별로 반환하는 validator.
- 유닛의 실제 행동/loadout을 해석하는 resolver.
- draft를 BT-002 Node 구조로 변환하는 compiler.
- 전투 중 편집 영향과 유닛 간 상태 공유를 막는 compiled snapshot.
- 성공 결과만 기존 `BehaviorTree`에 적용하는 atomic install.

Godot `Node`는 부모를 하나만 가질 수 있고 BT 노드와 `TurnActionBase`는 런타임 상태를 가진다. 따라서 프리셋 Resource나 컴파일 결과의 Node/action 인스턴스를 여러 유닛이 공유하면 latch, 대상, action queue와 디버그 경로가 오염된다.

기획 H-7/H-8은 저장과 적용을 구분한다. 컴파일 실패 시 마지막 정상본은 보존해야 하지만, 실패 사실을 숨기고 과거 설정으로 자동 출전시키면 플레이어가 화면에서 본 규칙과 실제 전투 AI가 달라진다.

## Decision

### 1. Draft Resource and runtime snapshot are different representations

플레이어 편집본은 versioned `TacticBoardDefinition : Resource`로 저장한다. draft에는 schema, 안정 ID, 행 순서와 조건/대상/행동/접근 정책만 보관한다.

다음은 draft에 저장하지 않는다.

- live `Node`/Article/target reference.
- 선택 행 latch, 후보, 확정 대상, commit 단계.
- tween/animation/action queue와 실행 중 `TurnActionBase` 상태.
- `BehaviorTree`/Blackboard/debug registry 상태.

전투용 `CompiledBoardSnapshot`은 특정 유닛 resolver context에서 생성한 per-unit 단일 소유 결과다. 편집 Resource를 실행 중 다시 읽지 않는다.

### 2. Definitions are typed and carry stable identifiers

Board/Row/Condition/Target/Action은 typed Resource로 표현하고 compiler registry가 이해하는 안정 `TypeId`를 가진다. 행은 안정 `RowId`, 보드는 안정 `BoardId`를 가진다.

범용 Dictionary 하나에 임의 key/value를 넣어 의미를 표현하지 않는다. 잘못된 필드와 지원하지 않는 타입을 load 후에도 validator가 정확한 code/field로 보고할 수 있어야 한다.

스킬/행동 definition은 live `TurnActionBase`를 직접 저장하지 않고 안정 `ActionId`/`SkillId`를 저장한다.

### 3. Action resolution is per unit and creates isolated instances

compiler는 `ITacticActionResolver`를 통해 action ID를 현재 유닛의 loadout/catalog와 대조한다.

- 찾을 수 없거나 사용할 권한이 없는 ID는 blocking Error다.
- resolver는 feasibility와 실행에 필요한 유닛 전용 action 인스턴스를 제공한다.
- template Resource와 다른 유닛의 action state를 공유하지 않는다.
- validation/compile은 template의 ammo/mana/action queue를 변경하지 않는다.

`TurnActionBase`는 `_usedCost`/`ActionQueue`/`_explicitTarget` 같은 mutable 상태를 가진 **`Resource`**이고 Godot `.tres` `load()`는 참조 공유 캐시를 돌려준다(Step 0 리뷰 Finding 2). 따라서 resolver는 유닛마다 **새 인스턴스**(`new` 또는 `Resource.Duplicate`)를 만들어 반환해야 하며, 로드된 template이나 다른 유닛의 인스턴스를 그대로 넘기지 않는다. 두 유닛을 같은 `ActionId`로 컴파일하면 `TacticTurnAction.TurnAction`은 `ReferenceEquals == false`여야 한다.

PartySnapshot/loadout이 도입되면 resolver context가 그 snapshot을 소비하되 draft schema는 유지한다.

### 4. Diagnostics are structured and deterministic

validation과 compile은 `Severity`, 안정 `Code`, 선택적 `RowId`/`FieldPath`, `MessageKey`/args와 developer detail을 가진 진단을 반환한다.

- `Error`: snapshot 생성·적용 불가.
- `Warning`: 적용 가능, 의도와 다르게 발동할 가능성.
- `Info`: 편집 도움 또는 확실하지 않은 전략 분석.

진단 순서는 board-level -> row order -> field order -> code 순으로 안정화한다. Resource/Dictionary 열거 순서나 object instance id에 의존하지 않는다.

### 5. Validation precedes compilation; generated BT is validated again

```text
draft validation
  -> no Error
  -> runtime node compilation
  -> BehaviorTreeValidation
  -> compiled snapshot publication
```

draft validator는 플레이어 언어의 문법·해금/해석·호환성을 검사한다. `BehaviorTreeValidation`은 compiler가 생성한 내부 Node 구조를 검사한다. 둘 중 하나를 생략하거나 다른 하나로 대체하지 않는다.

알 수 없는 definition은 기본 행동으로 바꾸지 않는다. compiler 예외는 격리하고 `compiler_internal_error`로 반환하며 partial Node를 정리한다.

### 6. The default row is stored and protected, not silently synthesized

기본 행/최종 wait 하한은 잠긴 `TacticRowDefinition`으로 draft에 저장한다. validator는 누락·중복·손상을 blocking Error로 보고한다.

compiler가 손상된 draft에 기본 행을 몰래 추가하면 저장된 내용과 실행 내용이 달라지므로 silent synthesis를 금지한다. BT-004 UI가 생성/복구 명령을 명시적으로 제공할 수 있다.

### 7. A snapshot has single ownership and is compiled per unit

성공 snapshot은 아직 부모가 없는 BT-002 root subtree와 유닛 전용 action 인스턴스를 소유한다.

- 같은 draft를 여러 유닛이 쓰더라도 유닛별 새 snapshot을 만든다.
- snapshot Node를 두 부모에 붙이거나 이미 적용한 snapshot을 재사용하지 않는다.
- source metadata는 `BoardId`, schema version, RowId order와 deterministic `SourceSignature`를 가진다.
- `SourceSignature`는 object instance id나 Resource path가 아니라 정규화된 schema field 값과 행 순서로 계산한다.
- **applied 상태의 stale 판정 입력 identity는 board `SourceSignature`만이 아니라 catalog까지 포함한 `CompileInputSignature`다**(Step 0 재검토 P2). `CompileInputSignature = SourceSignature ⊕ 안정 CatalogRevision`. §12의 action 해석이 catalog에 의존하므로, board가 같아도 catalog revision이 바뀌면 이전 컴파일 결과가 더 이상 현재 입력을 나타내지 않는다. `CatalogRevision`은 catalog Resource가 소유하는 안정 값이며 instance id/path가 아니다(Step 1에서 catalog Resource에 함께 넣는다).

### 8. Apply preserves the BehaviorTree container and atomically replaces its root

`CharacterArticle`의 기존 `BehaviorTree` 컨테이너는 유지한다. 컨테이너 root만 explicit installer를 통해 교체한다.

이 방식은 다음을 보존한다.

- `CharacterArticle.BehaviorTree` 캐시.
- 기존 debugger registry와 tree path.
- Blackboard/container 수명주기.
- 수제 BT를 사용하고 compiler/apply를 호출하지 않는 경로.

installer는 새 subtree의 `Tree`/child cache를 동기적으로 초기화하고 첫 tick이 deferred child-order update에 의존하지 않게 한다. 새 root 설치와 validation이 성공한 뒤에만 old root를 reset/free한다. 실패하면 old root와 applied metadata는 그대로다.

현재 `BehaviorTree`에는 이런 동기 설치 public API가 없다 — 유일한 동기 경로 `ChildOrderChanged → SetTree`는 private이고 `GD.Print`/`EmitSignal` 부작용을 동반하며, 명시 API `UpdateRequest`는 `CallDeferred`라 다음 프레임에 반영된다(Step 0 리뷰 Finding 1). 따라서 Step 4는 컨테이너에 **명시적 root installer public 메서드**(예: `InstallRoot(BehaviorTree_Node)`)를 추가한다. 이 메서드는 ①새 root를 child-0에 배치, ②동기 `SetTree(newRoot)`, ③디버거 활성 시 `SendStructure()` 재전송, ④install 성공 확인 후에만 old root reset/free를 수행하고 `ChildOrderChanged` 부작용에 의존하지 않는다.

전투 진행 중 hot-swap은 v1에서 거부한다. apply는 전투 전 준비 상태에서만 허용한다.

### 9. Last-good preservation never means silent fallback deployment

compile/apply 실패는 마지막 정상 적용본을 파괴하지 않는다. 그러나 consumer는 현재 입력 `CompileInputSignature`와 applied `CompileInputSignature`가 다르고 현재 compile이 실패했거나 아직 재적용하지 않았다는 상태를 구분해야 한다.

이 비교는 board뿐 아니라 catalog revision까지 포함한다(§7). 따라서 다음이 모두 "재컴파일 필요" 상태를 만든다.

- draft(board) 편집으로 `SourceSignature` 변경.
- **같은 board라도 catalog revision 변경**(예: loadout/해금 변화)으로 `CompileInputSignature` 변경(Step 0 재검토 P2).

전투 준비 화면은 다음 둘 중 하나를 요구한다.

1. 현재 입력을 다시 compile/apply.
2. `마지막 정상 설정으로 되돌리기`를 명시적으로 선택.

`CompileInputSignature`가 불일치하는 동안에는 stale 한 applied snapshot을 "현재 입력의 최신 적용본"으로 표시하지 않는다. 아무 설명 없이 과거 snapshot으로 전투를 시작하지 않는다. 실제 버튼 차단과 되돌리기 UI는 BT-004/DL-001 범위다.

### 10. Version v1 is strict; migration is introduced when needed

초기 `SchemaVersion=1`만 지원한다.

- unknown/future version은 blocking Error다.
- 원본 Resource를 자동 수정·재저장·파괴하지 않는다.
- null/불완전 draft도 저장 가능하며 validator가 진단한다.
- 실제 두 번째 version이 생기기 전에는 추상 migration framework를 만들지 않는다.

### 11. Target definitions declare how candidates are supplied; default rows are locked system rows

Step 0 재검토(리뷰 P1-1)가 실측한 런타임 계약: `TacticTargetSelector`는 `TacticRowContext.LiveCandidates`에서만 대상을 고르고, 후보가 비면 `Failure`다(`TacticTargetSelector.cs:48-55`, `TacticRowContext.cs:49-50`). `TacticConditionAlways`는 `Success`만 반환하고 **후보를 만들지 않는다**(`TacticVocabulary.cs:16-19`). 따라서 대상 셀렉터는 후보 공급 조건 없이는 절대 대상을 확정하지 못한다. 이를 draft→compiler 계약으로 고정한다.

- `NearestEnemy` target definition은 compiler가 **후보 공급 조건을 보장한 뒤** `TacticTargetSelector(Policy)`를 생성한다. 규칙(Step 3 확정): 행에 target-bearing 조건(`any_enemy`/`enemy_casting`)이 **없으면** compiler가 `TacticConditionAnyEnemy`(`SupplyAnyEnemy`)를 주입하고, **있으면** 그 조건이 이미 후보를 공급하므로 추가 노드를 만들지 않는다. `AnyEnemy` 후보는 살아 있는 상대 전체이고 다른 target-bearing 조건과 **교집합**이 idempotent(`전체 ∩ X = X`, `TacticRowContext.ContributeCandidates`)라, 두 경우의 런타임 후보 집합·대상 선택이 동일하다. 즉 "후보 공급을 보장한다"는 계약은 유지하면서 중복 노드만 제거한 정규 형태다.
- 예: `[always] → NearestEnemy` → `[SupplyAnyEnemy, Always, Selector, Seq]`. `[enemy_casting] → NearestEnemy` → `[EnemyCasting, Selector, Seq]`(영창 적 집합 내 최근접). `default_attack`(`[any_enemy] → NearestEnemy`) → `[AnyEnemy, Selector, Seq]`(중복 SupplyAnyEnemy 없음).
- `ContextTarget` target definition은 target-bearing condition이 하나 이상 있어야 한다. 없으면 후보가 비어 행이 항상 불성립하므로, validator가 blocking Error `missing_target_context`를 반환한다.
- `Wait`와 v1의 targetless action은 target selector를 만들지 않는다.

기본 행동은 draft에 직렬화되는 **두 개의 잠긴 system row**로 표현하고 compiler가 손상된 보드에 몰래 추가하지 않는다(§6 silent synthesis 금지 강화).

```text
default_attack (locked system row)
  AnyEnemy → NearestEnemy(ApproachAllowed)
           → TacticActionSequence[TacticApproach, TacticTurnAction(default_attack)]

terminal_wait (locked system row)
  TacticRow[TacticWait]
```

validator는 다음을 blocking Error로 보고한다.

- 기본 공격 행(`default_attack`) 누락/중복/손상.
- 최종 대기 행(`terminal_wait`) 누락/중복/손상.
- `terminal_wait`가 마지막 행이 아님.
- 플레이어 편집 행이 두 system row **아래**에 있음.
- 기본 공격 행이 `NearestEnemy` 후보 공급 또는 `ApproachAllowed`/default action 계약을 위반함.

두 system row가 잠긴 행이라는 사실은 문서 계약이고, **행 개수 표시/노출 정책은 BT-004 UI 단계**에서 결정한다.

### 12. Action IDs resolve from a catalog that outlives the runtime BehaviorTree

Step 0 재검토(리뷰 P1-2)가 실측한 순환: `CharacterArticle`은 현재 `BehaviorTree` 자식을 raw walk해 `ITurnActionProvider`를 찾는다(`CharacterArticle.cs:86-93`). 그러나 BT-003 apply는 그 root를 새 compiled root로 **교체하고 old root를 free**한다(§8). 교체 대상 트리에서 action template을 찾아 clone하는 방식은 root 교체 뒤 재컴파일, 보드 A→B→A 전환, 안정 `ActionId` 해석을 보장하지 못한다.

따라서 BT-003은 runtime BT와 **독립적으로 살아남는** `TacticActionCatalog`(또는 동등한 loadout Resource)를 도입한다.

- catalog는 `CharacterArticle` 또는 미래 `PartySnapshot`이 소유하며, **compiler가 교체할 `BehaviorTree` root의 자식이 아니다**.
- catalog는 안정 `CatalogRevision`을 소유한다. revision이 바뀌면 applied `CompileInputSignature`가 불일치해 재컴파일이 필요해진다(§7/§9). instance id/path가 아닌 안정 값이다.
- catalog entry 최소 의미: 안정 `ActionId: StringName`, immutable `TurnActionBase` template, 필요 시 `SkillDefinition`/해금 metadata 참조. v1 기본 공격의 안정 ID는 `default_attack`.
- `ITacticActionResolver`는 현재 `BehaviorTree`를 탐색하지 않고 **catalog에서 `ActionId`를 조회**한다.
- resolver는 매 compile마다 **새 `TurnActionBase` runtime instance**를 반환한다. template, 다른 유닛 instance, 이전 snapshot instance를 절대 공유하지 않는다(§3 강화).
- 새 instance는 최소 다음이 fresh여야 한다: `ActionQueue`, 사용 cost(`_usedCost`), explicit target/binding, 실행 중 phase, 다른 유닛/이전 전투의 Node 참조.
- 정확한 복제 방식(`Resource.Duplicate`, factory, 특수 clone)은 Step 3 spike에서 실측 확정한다. 계약은 `ReferenceEquals == false` + runtime state 격리이며, **복제가 불완전하면 resolver는 실패**한다.
- catalog에 없는 `ActionId`, 중복 `ActionId`, null/유효하지 않은 template은 validator의 blocking Error다.
- 기존 수제 BT는 catalog/resolver를 사용하지 않는 한 동작이 변하지 않는다.

## Warning Policy

v1 warning은 정적으로 확실히 증명할 수 있는 경우부터 시작한다.

- 완전히 동일한 활성 행 중복.
- 항상 참인 상위 행이 아래 행을 확실하게 가림.
- 동일 조건 범위로 포함 관계를 확실히 증명할 수 있는 죽은 행.

마나 고갈 가능성, 실제 발동 빈도, 장비/전장에 따른 낮은 실행 가능성처럼 불확실한 분석은 충분한 입력이 없으면 Error/Warning으로 단정하지 않는다. 향후 loadout/전투 리포트 데이터와 연결해 확장한다.

## Step 0 Design Fixes

Step 0 설계 리뷰가 확정한 두 seam(리뷰 Finding 3·4).

### Canonical structural mapping (compiler)

행동 행은 단일 정규 형태로 컴파일한다. 접근 정책 차이는 구조가 아니라 셀렉터 export로만 표현해 "동일 draft → 동일 구조"(matrix #8/#9)를 결정론적으로 단언할 수 있게 한다.

```text
TacticRow(RowId)
  ├─ TacticCondition*                     // 0..N pre-action 게이트
  ├─ TacticTargetSelector(Policy)         // 마지막 pre-action, 최대 1
  └─ TacticActionSequence
       ├─ TacticApproach
       └─ TacticTurnAction(per-unit TurnAction)
```

`TacticApproach`는 in-range면 무이동으로 행동에 이어지고 `Immediate`/`ApproachAllowed`를 `Policy`로 구분하므로, Immediate 행도 같은 시퀀스 형태를 쓴다. `NearestEnemy` 대상의 후보 공급 규칙(target-bearing 조건 부재 시만 `SupplyAnyEnemy` 주입)과 두 잠긴 system row(`default_attack`, `terminal_wait`)의 정확한 형태는 §11이 소유한다. 비활성(`Enabled=false`) 행은 compiler가 생성하지 않는다(Step 3). 최종 대기 행은 `TacticRow[TacticWait]`(조건·셀렉터 없음, RowId 보존)이며, `ReportRow`가 `TacticRow`만 보고하므로 기본 행도 debug/발동 통계에 연결된다.

### Validation boundary (Step 2 vs generated tree)

`BehaviorTreeValidation.ValidateTree`는 노드 타입별 child 구조만 검사하는 **컴파일러 버그 안전망**이다. "root가 우선순위 selector인지 / 기본 행이 존재하는지" 같은 플레이어 언어 규칙은 담지 않으며, 그 규칙은 Step 2 draft validator가 소유한다(§5·§6).

`ValidateTree`는 컨테이너(`BehaviorTree`)를 인자로 받으므로, detached subtree 검사는 트리 밖 임시 컨테이너에 root를 `AddChild` → 검사 → `RemoveChild`로 수행한다(트리 밖이라 `_Ready`/Blackboard/registry 부작용 없음). 결과에 `IsError` 항목이 있으면 `compiler_internal_error`가 아니라 `generated_tree_invalid` 진단으로 compile을 실패시키고 partial Node를 정리한다.

## Implementation Evidence (2026-07-15)

BT-003 Step 4 구현은 별도 코드 리뷰 대기 상태다.

- `BehaviorTree.InstallRoot`/`RemoveInstalledRoot`가 동기 root 교체와 identity-protected teardown을 제공한다.
- `TacticBoardApplyState`가 last-good snapshot, preparation gate, `CompileInputSignature` stale comparison을 소유한다. 실패·중복·foreign·sealed apply는 기존 적용본을 보존한다.
- `CharacterArticle`이 apply query seam과 death/tree exit 정리를 제공한다. BattleSession이 실제 gate를 소비하는 배선은 후속이다.
- `bt003_step4_apply_lifecycle_test`는 첫 tick, A→B→A, failed apply 보존, unit 격리, death/tree exit을 51 assertions로 검증한다.


## Consequences

### Positive

- 플레이어가 저장한 규칙과 전투에서 실행되는 규칙의 차이를 명시적으로 추적할 수 있다.
- 컴파일 실패가 기존 정상 설정을 파괴하지 않으며, stale 설정으로 조용히 출전하지도 않는다.
- 같은 프리셋을 여러 유닛이 사용해도 runtime latch/target/action state가 격리된다.
- BT-004 UI와 DL-001 준비 흐름이 안정된 데이터·진단·적용 API를 소비할 수 있다.
- RowId와 signature가 debugger/행별 통계/오류 바로가기를 연결한다.

### Negative

- draft, compiled snapshot, applied state라는 세 상태를 구분해야 한다.
- action resolver와 explicit root installer라는 새 경계가 필요하다.
- typed definition/registry를 추가할 때 새 어휘마다 definition과 compiler mapping을 함께 등록해야 한다.
- per-unit compile은 Node/action 인스턴스를 더 만들지만 전투 격리를 위해 필요한 비용이다.

## Rejected Alternatives

### Execute the editable Resource directly

전투 중 편집 영향, 유닛 간 Resource 상태 공유, validation 우회 때문에 거부한다.

### Store a ready-made BehaviorTree Node graph as the board preset

Node는 단일 부모이고 runtime 상태를 가진다. 프리셋 공유·save migration·행별 진단에도 부적합해 거부한다.

### Keep direct TurnActionBase references in the draft

행동 진행 상태가 Resource에 섞이고 유닛 loadout과 해금 검증이 어려워진다. 안정 ID + resolver를 사용한다.

### Auto-use the last successful snapshot when compile fails

화면에 보이는 설정과 실제 AI가 달라지는 silent fallback이다. 마지막 정상본은 보존하되 명시적 revert를 요구한다.

### Let the compiler repair invalid boards silently

기본 행 자동 생성이나 unknown action 대체는 저장/실행 의미를 다르게 만든다. 오류와 명시적 복구가 낫다.

### Replace the whole BehaviorTree container on every apply

CharacterArticle cache, debugger registry/tree path, Blackboard와 signal 수명주기를 불필요하게 흔든다. 컨테이너를 유지하고 root만 원자적으로 교체한다.

## Verification Obligations

- Resource save/reload와 unknown version 원본 보존.
- deterministic diagnostics and source signature.
- validator/compiler input mutation 없음.
- exact definition -> BT-002 Node mapping과 생성 후 `BehaviorTreeValidation`.
- same draft -> two units Node/context/action isolation.
- compiler exception/partial construction cleanup.
- atomic A -> B replacement, invalid compile preserves A.
- first tick immediate readiness, debug structure/RowId update.
- death/free/tree exit에서 compiled runtime 정리.
- compiler/apply를 쓰지 않는 기존 수제 BT와 BT-002 직접 fixture 무회귀.
- (§11) `NearestEnemy`가 후보 공급(target-bearing 조건 부재 시 `SupplyAnyEnemy` 주입, 있으면 그 조건 재사용)을 거쳐 실제 적을 확정; `EnemyCasting + NearestEnemy` 구조 = `[EnemyCasting, Selector, Seq]`(SupplyAnyEnemy 없음)이고 영창 집합 내 최근접 확정; `Always + ContextTarget`은 `missing_target_context` Error.
- (§11) `default_attack`/`terminal_wait` 잠긴 system row 직렬화·보호, 손상/누락/중복/순서/편집행 하위 배치 Error, compiler silent synthesis 없음.
- (§11/Step 3) 비활성(`Enabled=false`) player 행은 compile되지 않고 snapshot `RowIds`에도 없다. 비활성 상위 행이 발동하지 않고 기본 행으로 내려간다.
- (§12) 같은 `ActionId` 두 유닛 compile 시 `TurnAction` `ReferenceEquals == false`; queue/cost/explicit-target 변형 비가시; A→B→A가 catalog만으로 성공; old root free 뒤 catalog template 유효; unknown/duplicate/null ActionId·template Error + applied root 보존; clone/factory 실패 시 partial snapshot 정리 + Error.
- (§7/§9) 같은 board라도 catalog revision 변경 → `CompileInputSignature` 불일치 → "재컴파일 필요" 상태 → 명시적 재적용 전 stale snapshot을 현재 입력의 최신 적용본으로 표시하지 않음.

## Resolved by

- [[BT-003-Tactic-Board-Resource-Validation-Compiler]]
- 후속 [[BehaviorTree-System]] 현재 사실 갱신
