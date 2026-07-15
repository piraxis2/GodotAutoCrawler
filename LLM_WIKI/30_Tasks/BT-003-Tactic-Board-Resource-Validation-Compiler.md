---
id: BT-003
type: task
status: complete
system: BehaviorTree
created: 2026-07-15
updated: 2026-07-15
tags: [task, behavior-tree, tactic-board, resource, validation, compiler]
---

# Tactic Board Resource, Validation, and Compiler

## Goal

플레이어가 편집한 유닛별 택틱보드를 저장 가능한 초안으로 보존하고, 구조화된 오류·경고를 제공한 뒤, 유효한 보드만 [[BT-002-Tactic-Board-Runtime-Extension]] 실행 구조로 컴파일한다.

```text
TacticBoardDefinition draft (.tres)
  -> TacticBoardValidator
  -> TacticDiagnostic[]
  -> TacticBoardCompiler
  -> BehaviorTreeValidation
  -> per-unit CompiledBoardSnapshot
  -> existing BehaviorTree에 atomic apply
```

BT-003은 플레이어용 편집 UI나 DungeonRun을 만들지 않는다. 이후 UI와 전투 준비 흐름이 의존할 수 있는 **데이터·진단·컴파일·적용 seam**만 완성한다.

## Player Contract Source

- 기획 기준: `GameDesign/기획서/09_택틱보드.md` H-3, H-6, H-7, H-8, H-11.
- 플레이어가 저장한 초안과 전투에 적용된 스냅샷은 서로 다른 상태다.
- `Error`가 하나라도 있으면 새 스냅샷을 만들거나 적용하지 않고 전투 시작을 차단한다.
- `Warning`은 적용을 허용하되 행 번호와 원인을 보여 줄 수 있어야 한다.
- 컴파일 실패가 마지막 정상 적용본을 파괴하면 안 된다. 다만 과거 설정으로 자동 출전시키지도 않는다.
- 전투 중에는 편집 Resource가 아니라 성공한 **유닛별 독립 스냅샷**을 실행한다.

## Dependencies

- [[BT-002-Tactic-Board-Runtime-Extension]]: 완료. Priority/Row/Context/Target/Approach/Action/Wait 런타임과 실제 `CharacterArticle` 어댑터를 제공한다.
- [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]: accepted. 우선순위, commit, 대상 공유, 접근 정책과 reset 계약을 소유한다.
- [[BehaviorTree-System]]: 기존 `BehaviorTree` 컨테이너, `BehaviorTreeValidation`, debugger 구조 갱신 계약.
- `SkillDefinition`/`TurnActionBase`/`ITurnActionProvider`: 행동 ID를 현재 유닛이 실제 실행할 수 있는 행동 인스턴스로 해석할 때 대조해야 한다.

## Current Code Evidence

2026-07-15 문서 작성 시 실제 코드와 대조했다.

- `BehaviorTree : Node`는 첫 자식을 `Root`로 보고 `_Ready()`와 child-order 변경 뒤 `SetTree()`로 각 노드의 `Tree`를 연결한다. 런타임 root 교체를 즉시 완료했다고 보장하는 public 설치 API는 없다.
- `CharacterArticle.BehaviorTree`는 자식 이름 `BehaviorTree`를 찾아 캐시한다. 컨테이너는 유지하고 그 root만 교체하는 쪽이 기존 캐시·debug registry 경계와 맞다.
- `BehaviorTree_Node`는 `_status`와 elapsed time을 인스턴스 상태로 가진다. 같은 컴파일 결과 Node subtree를 둘 이상의 유닛에 공유할 수 없다.
- `TacticRow`는 `RowId`와 유닛별 `TacticRowContext`를 소유한다. compiler는 저장된 행 순서와 RowId를 그대로 보존해야 debug payload·향후 발동 통계가 같은 행을 가리킨다.
- `TacticTurnAction`은 `TurnActionBase`를 실행하며 `TurnActionBase` 자체가 action queue와 진행 상태를 가진다. action template 또는 Resource 인스턴스를 유닛 사이에 공유하지 않는 복제/생성 계약이 필요하다.
- `BehaviorTreeValidation`은 생성된 택틱 구조의 필수 자식·셀렉터 위치·택틱 노드 제약을 이미 검사한다. draft validator와 생성 후 구조 validation은 서로 대체하지 않는다.
- BT-002는 코드/fixture에서 직접 Node를 조립한다. 저장 schema, action resolver, compile diagnostics, snapshot owner와 atomic apply는 아직 없다.

## Scope

- versioned `TacticBoardDefinition`/행/조건/행동/대상 정의 Resource와 `.tres` 왕복.
- draft 문법·RowId·기본 system row·어휘·대상/행동 호환성·행동 해석 validation.
- `Info`/`Warning`/`Error` 구조화 진단과 안정된 diagnostic code.
- 현재 BT-002 제품 어휘를 런타임 Node subtree로 변환하는 compiler(`NearestEnemy` 후보 공급 보장 [target-bearing 조건 부재 시만 `AnyEnemy` 주입] + selector 정규 매핑, ADR-024 §11).
- runtime BT와 독립적인 `TacticActionCatalog`(action template source, ADR-024 §12)와 catalog 기반 `ITacticActionResolver`.
- 유닛별 행동 resolver와 per-unit action/runtime state 격리(매 compile 새 `TurnActionBase` 인스턴스).
- 생성된 subtree에 대한 `BehaviorTreeValidation` 2차 검사(컴파일러 버그 안전망).
- 기존 `BehaviorTree` 컨테이너에 성공 결과만 atomic apply하고 이전 root를 안전하게 폐기하는 explicit root installer seam.
- 실패 시 기존 적용본 보존, 명시적 적용 상태와 source signature.
- 반복 compile/apply, 두 유닛 공유 draft, death/free/tree exit 수명주기 검증.

## Out of Scope

- 택틱보드 편집 Window, 드래그 재배열, 행 오류 표시 UI와 프리셋 선택 UX.
- `BattleSession`/`BattleRequest`/`PartySnapshot`에 실제 보드를 전달하는 배선.
- DungeonRun의 `Preparing -> Tactic setup -> Battle -> Next floor` 상태 기계.
- H-4 전체 어휘, 해금/성장 catalog, 장비·직업별 블록 제한의 전체 구현.
- 프리셋 복사/인계/save slot 영속과 migration UI.
- 전투 후 행별 발동 통계와 수정 제안.
- 기존 수제 BT를 Resource graph로 전환하거나 일반 BT editor source-of-truth를 바꾸는 작업.

## Proposed Data Contracts

이름과 세부 API는 Step 0에서 실제 Godot Resource/Node 제약과 대조해 확정한다. 장기 판단은 [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]]이 소유한다.

### Draft resources

```text
TacticBoardDefinition : Resource
  SchemaVersion: int
  BoardId: StringName
  DisplayName: string
  Rows: Array<TacticRowDefinition>

TacticRowDefinition : Resource
  RowId: StringName
  Enabled: bool
  Locked: bool
  Source: enum/player/default/quirk
  Conditions: Array<TacticConditionDefinition>
  Target: TacticTargetDefinition?     // NearestEnemy | ContextTarget | null(targetless)
  Action: TacticActionDefinition?
  ApproachPolicy: Immediate | ApproachAllowed
```

- v1 definition은 BT-002 실행 상태나 live `Node`/target을 저장하지 않는다.
- 조건·행동·대상 definition은 typed Resource와 안정 `TypeId`를 함께 가져 compiler registry가 명시적으로 지원 여부를 판단한다.
- 스킬 행동은 live `TurnActionBase` 인스턴스가 아니라 안정 `ActionId`/`SkillId`를 저장한다.
- 행 배열 순서가 우선순위의 유일한 source of truth다.
- **대상 후보 공급(ADR-024 §11)**: `TacticTargetSelector`는 `TacticRowContext.LiveCandidates`에서만 대상을 고르고 `TacticConditionAlways`는 후보를 만들지 않는다(실측 `TacticVocabulary.cs:16-19`, `TacticTargetSelector.cs:48-55`). 따라서 `NearestEnemy` 대상은 compiler가 후보 공급을 보장한다 — target-bearing 조건(any_enemy/enemy_casting)이 **없을 때만** `TacticConditionAnyEnemy`를 주입하고, 있으면 그 조건이 후보를 공급한다(`전체∩X=X` idempotent). `ContextTarget` 대상은 target-bearing 조건이 없으면 `missing_target_context` Error다.
- **잠긴 기본 system row 2개(ADR-024 §11)**: `default_attack`(`AnyEnemy → NearestEnemy(ApproachAllowed) → Sequence[Approach, TurnAction(default_attack)]`)와 `terminal_wait`(`TacticRow[TacticWait]`)를 draft에 직렬화한다. validator가 손상/누락/중복/순서/편집행 하위 배치를 오류로 보고하고 compiler가 몰래 합성하지 않는다. 잠긴 행 개수 표시/노출 정책은 BT-004 UI 범위다.

### Action catalog (production template source, ADR-024 §12)

```text
TacticActionCatalog : Resource            // CharacterArticle 또는 미래 PartySnapshot 소유. runtime BehaviorTree의 자식이 아니다.
  CatalogRevision: 안정 값                 // instance id/path 아님. 변경 시 applied CompileInputSignature 불일치(§7/§9).
  Entries: Array<TacticActionCatalogEntry>

TacticActionCatalogEntry
  ActionId: StringName                    // v1 기본 공격 = "default_attack"
  Template: TurnActionBase (immutable)     // 공유 template. 직접 실행하지 않는다.
  SkillDefinition/unlock metadata?         // 필요 시
```

- `ITacticActionResolver`는 현재 `BehaviorTree`를 raw walk하지 않고 catalog에서 `ActionId`를 조회한다(실측 순환 회피: `CharacterArticle.CollectTurnActionProviders`는 교체 대상 트리를 훑는다).
- resolver는 매 compile마다 template/타 유닛/이전 snapshot과 공유하지 않는 **새 `TurnActionBase` 인스턴스**를 반환한다(`ReferenceEquals == false`, fresh `ActionQueue`/cost/explicit-target/phase). 복제 방식(`Duplicate`/factory)은 Step 3 spike, 불완전하면 실패.
- catalog에 없는/중복/null/유효하지 않은 `ActionId`·template은 blocking Error다.
- `CompileInputSignature = board SourceSignature ⊕ CatalogRevision`(§7). applied 상태의 stale 판정 입력이며, catalog revision만 바뀌어도 재컴파일이 필요해진다(§9).

### Diagnostics

```text
TacticDiagnostic                         // Step 2 구현명(validate/compile 공용, phase-neutral)
  Severity: Info | Warning | Error
  Code: StringName                       // 안정 code(TacticDiagnosticCodes 상수)
  RowId: StringName?                     // board-level이면 빈 StringName
  FieldPath: string?
  MessageKey: StringName                 // v1: = Code
  MessageArgs: immutable values          // IReadOnlyList<string>(방어 복사)
  DeveloperDetail: string?

TacticValidationResult                   // Step 2: validator 결과(snapshot 없음)
  IsValid: bool                          // Error 심각도 진단이 하나도 없으면 true
  Diagnostics: read-only collection

TacticCompileResult                      // Step 3: compile 결과(TacticDiagnostic 재사용)
  Success: bool
  Diagnostics: immutable/read-only collection
  Snapshot: CompiledBoardSnapshot?       // Success일 때만
```

- Step 2 구현은 진단 타입을 phase-neutral `TacticDiagnostic`으로 두고(validate/compile 공용), 결과를 `TacticValidationResult`로 분리했다. Step 3 `TacticCompileResult`가 같은 진단 타입을 재사용한다(설계 리뷰의 `TacticCompileDiagnostic`을 `TacticDiagnostic`으로 명명 정련 — 의미 동일).
- UI는 `Code`/`RowId`/`FieldPath`로 행과 필드를 표시하고 `MessageKey`/args로 현지화한다.
- 내부 예외 문자열을 플레이어 메시지로 직접 사용하지 않는다.
- 반환 진단과 snapshot metadata를 호출자가 변조해 다음 compile 결과에 영향을 주면 안 된다.

### Compiled snapshot

`CompiledBoardSnapshot`은 다음 불변 metadata와 per-unit runtime root를 소유하는 단일 소유권 결과다.

- `SourceBoardId`, `SourceSchemaVersion`, deterministic `SourceSignature`(board), `CompileInputSignature`(board ⊕ catalog revision, §7/§9).
- compile 대상 `UnitId` 또는 resolver identity.
- 저장 순서가 보존된 RowId 목록.
- 아직 부모가 없는 `TacticPrioritySelector` root subtree.
- resolver가 catalog에서 만든 유닛 전용 `TurnActionBase` 인스턴스(§12, 격리).

snapshot은 편집 Resource를 참조해 실행 의미를 다시 읽지 않는다. Node subtree는 여러 유닛/컨테이너에 재사용하지 않고 유닛별로 새로 컴파일한다.

## Minimum Validation Contract

### Blocking errors

- null board/row/definition, 지원하지 않는 schema version.
- 빈/중복 `BoardId` 또는 `RowId`, 행 수·조건 수의 v1 한계 위반.
- 행동 또는 필수 파라미터 누락.
- 대상이 필요한 행동의 selector 누락, 대상 없는 행동의 잘못된 selector.
- `ContextTarget` 대상인데 target-bearing 조건이 하나도 없음 → `missing_target_context`(§11).
- 존재하지 않거나 catalog로 해석할 수 없는/중복/null template `ActionId`/`SkillId`(§12).
- 조건·대상·행동의 진영/타입/접근 정책 조합을 compiler가 지원하지 않음.
- 잠긴 기본 system row 위반(§11): `default_attack`/`terminal_wait` 누락·손상·중복, `terminal_wait`가 마지막 행이 아님, 편집 행이 system row 아래에 있음, `default_attack`이 `NearestEnemy` 후보 공급·`ApproachAllowed`·default action 계약 위반.
- compiler registry가 모르는 definition/type id.
- 생성된 runtime tree가 `BehaviorTreeValidation`을 통과하지 못함 → `generated_tree_invalid`(컴파일러 버그 안전망, ADR-024 Step 0 Design Fixes).
- resolver clone/factory 실패(불완전 복제) → 격리 Error, partial snapshot 정리, 기존 적용본 보존(§12).
- compiler 내부 예외. 예외는 격리하고 `compiler_internal_error` 진단으로 변환하며 기존 적용본을 건드리지 않는다.

### Non-blocking warnings

- 완전히 동일한 활성 행 중복.
- `Always` 상단 행이 아래 행을 확실하게 가리는 경우.
- validator가 정적으로 **증명할 수 있는** 죽은 행.
- 현재 loadout에서는 해석 가능하지만 비용/탄약 계획상 실행 가능성이 낮은 구성은 충분한 정적 데이터가 있을 때만 warning 또는 info.

불확실한 의미 분석을 blocking error나 확정적인 죽은 행으로 과장하지 않는다. v1은 확실히 증명 가능한 진단부터 시작한다.

## Compile and Apply Contract

1. validator가 draft와 resolver context를 읽기 전용으로 검사한다.
2. `Error`가 있으면 compiler를 실행하지 않고 `Success=false`, `Snapshot=null`을 반환한다.
3. compiler는 행 순서대로 BT-002 Node subtree와 유닛 전용 action을 만든다.
4. detached subtree에 대해 구조 validation이 가능한 임시 owner/container seam을 사용해 `BehaviorTreeValidation`을 실행한다.
5. 모든 검사가 통과한 뒤에만 snapshot을 성공 결과로 공개한다.
6. apply는 전투 실행 중이 아닌 준비 상태에서 기존 `BehaviorTree` 컨테이너의 root를 교체한다.
7. 새 root의 `Tree`/Blackboard/child cache가 **다음 deferred frame을 기다리지 않고** 사용할 수 있도록 명시적 install seam을 둔다.
8. install 성공이 확인된 뒤 이전 택틱 runtime을 reset하고 old root를 free한다. 실패 시 old root와 적용 metadata를 유지한다.
9. 같은 snapshot을 두 번 설치하거나 이미 부모가 있는 root를 재사용하는 것은 fail-closed한다.
10. compile 실패 뒤 마지막 정상본은 보존하지만, 전투 시작 consumer는 현재 draft의 적용 실패를 보고 명시적 `되돌리기` 없이는 과거 snapshot으로 자동 출전시키지 않는다.
11. applied 상태의 stale 판정은 `CompileInputSignature`(board `SourceSignature` ⊕ catalog `CatalogRevision`, §7/§9)로 한다. draft 편집뿐 아니라 **같은 board라도 catalog revision 변경**이 applied `CompileInputSignature`와 불일치하면 "재컴파일 필요" 상태다. 명시적 재적용 전에는 stale snapshot을 현재 입력의 최신 적용본으로 표시하지 않는다.

## Open Decisions for Step 0

> **닫힘 (2026-07-15).** Step 0 설계 리뷰([[BT-003-Tactic-Board-Resource-Validation-Compiler-Review]], 오너 판정 `수정 후 완료` / 설계 리뷰 `Approved after design fixes`)가 OD1~12를 실제 코드와 대조해 닫았고, [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]]을 `accepted`로 승격했다. 아래 권장안은 모두 확정됐으며, 추가로 (a) `TurnActionBase : Resource`이므로 resolver가 유닛별 새 인스턴스를 만들어야 함(OD2/OD3/OD12), (b) `BehaviorTree`에 명시적 root installer public API 추가(OD4/OD10), (c) 접근 정책 정규 구조 매핑 고정(OD1/OD5)을 ADR-024에 반영했다.
>
> **재검토 P1 2건 반영(2026-07-15).** 리뷰 재검토가 누락된 P1 두 건을 닫았고 ADR-024 §11/§12에 계약으로 고정했다. **OD11(후보 공급·기본 system row)**: `NearestEnemy`는 compiler가 후보 공급을 보장하며(Step 3 정련: target-bearing 조건 부재 시만 `TacticConditionAnyEnemy` 주입, `TacticConditionAlways`는 후보 미공급), `ContextTarget`은 target-bearing 조건 부재 시 `missing_target_context` Error. 기본 행동은 잠긴 두 system row `default_attack`/`terminal_wait`로 직렬화·보호(silent synthesis 금지). **OD12(production action template source)**: action ID는 runtime BT가 아니라 BT와 독립적으로 살아남는 `TacticActionCatalog`(`CharacterArticle`/미래 `PartySnapshot` 소유)에서 해석하고, resolver는 매 compile마다 격리된 새 `TurnActionBase` 인스턴스를 반환한다. 2차 P2로 applied stale 판정을 `CompileInputSignature`(board `SourceSignature` ⊕ catalog `CatalogRevision`, ADR-024 §7/§9)로 확정해 catalog revision 변경도 재컴파일 필요로 감지한다.

1. **Definition 표현** — 권장: typed Resource subclass + 안정 `TypeId`. enum 하나와 untyped Dictionary로 모든 파라미터를 담지 않는다.
2. **행동 참조** — 권장: draft는 안정 `ActionId`/`SkillId`, `ITacticActionResolver`가 유닛/loadout 기준으로 새 action 인스턴스를 제공. live `TurnActionBase` 직접 보관 금지.
3. **snapshot 형태** — 권장: per-unit detached Node subtree를 단일 소유하는 결과. shared Node graph/Resource runtime state 금지.
4. **적용 방식** — 권장: 기존 `BehaviorTree` 컨테이너 유지 + explicit atomic root installer. CharacterArticle의 컨테이너 cache/debug registry를 깨지 않는다.
5. **기본 행** — 권장: 잠긴 row로 draft에 저장하고 validator가 보호. compiler의 silent synthesis 금지.
6. **마지막 정상본** — 권장: compile/apply 실패 시 보존하되 자동 fallback 출전 금지. UI가 명시적 revert를 제공하는 것은 BT-004.
7. **source signature** — 권장: instance id/resource path가 아닌 정규화된 schema field와 행 순서로 결정론적 signature 생성.
8. **warning 범위** — 권장: v1은 확실히 증명 가능한 중복/가림만 Warning. 추정성 마나·발동 가능성 분석은 Info 또는 후속.
9. **schema version** — 권장: v1만 읽고 future/unknown은 error+원본 보존. 실제 migration framework는 두 번째 version이 생길 때 도입.
10. **compile/apply API 수명** — 권장: prepare 단계에서 동기 compile, 성공 결과만 동기 install. Node free/debug structure 갱신 순서는 Step 0에서 실측 확정.
11. **후보 공급·기본 system row**(재검토 P1-1, 닫힘) — 확정: `NearestEnemy`는 compiler가 후보 공급을 보장한 뒤 `TacticTargetSelector(Policy)`를 생성한다. Step 3 정련: target-bearing 조건(any_enemy/enemy_casting)이 **없을 때만** `TacticConditionAnyEnemy`를 주입하고, 있으면 그 조건이 공급한다(`전체∩X=X` idempotent라 결과 동일). `TacticConditionAlways`는 후보를 만들지 않으므로 셀렉터 단독은 항상 불성립. `ContextTarget`은 target-bearing 조건이 없으면 `missing_target_context` blocking Error. 기본 행동은 draft에 직렬화되는 잠긴 두 system row(`default_attack` = `AnyEnemy → NearestEnemy(ApproachAllowed) → Sequence[Approach, TurnAction(default_attack)]`, `terminal_wait` = `TacticRow[TacticWait]`)로 표현하고 compiler가 몰래 합성하지 않는다. 상세·validator error 목록은 ADR-024 §11.
12. **production action template source**(재검토 P1-2, 닫힘) — 확정: action ID는 runtime BT가 아니라 BT와 독립적으로 살아남는 `TacticActionCatalog`(또는 동등 loadout Resource, `CharacterArticle`/미래 `PartySnapshot` 소유)에서 해석한다. `ITacticActionResolver`는 catalog를 조회하며 현재 BT를 탐색하지 않고, 매 compile마다 격리된 새 `TurnActionBase` 인스턴스(`ReferenceEquals == false`, fresh queue/cost/target/phase)를 반환한다. 복제 불완전 시 실패. 상세·validator error·복제 spike는 ADR-024 §12.

## Step 0: Design Review

> **완료 (2026-07-15).** 오너 판정 `수정 후 완료`(설계 리뷰 `Approved after design fixes`). 리뷰: [[BT-003-Tactic-Board-Resource-Validation-Compiler-Review]]. ADR-024 `accepted`. OD1~12 닫힘(재검토 P1-1/P1-2 + 2차 P2 `CompileInputSignature` 포함), P0/P1 없음. Step 1~4 경계·API·검증 matrix 확정. 다음은 Step 1(Versioned Draft Resource + `TacticActionCatalog`) 구현.

Goal:

- Resource schema, resolver, diagnostics, compiler, snapshot과 atomic apply를 실제 BT-002/Skill/Godot Node 수명주기와 대조해 구현 가능한 계약으로 확정한다.

Scope:

- 이 Task, [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]], 기획 H-7/H-8/H-11과 실제 `BehaviorTree`/`CharacterArticle`/BT-002/TurnAction 코드 대조.
- Open Decisions 1~12와 Step 1~4 경계 확정.
- Resource round-trip, detached Node validation, apply timing에 필요한 spike 여부 결정.

Out of scope:

- 제품 코드, `.tscn`, `.tres` 수정.
- UI와 BattleSession/DungeonRun 설계 확장.

Done condition:

- [[Design-Review-Prompt]] 판정이 `Approved` 또는 `Approved after design fixes`다.
- P0/P1이 없고 Open Decisions 1~12가 닫힌다.
- ADR-024가 Step 1 전 `accepted`가 된다.
- 각 Step의 API·입출력·실패/정리 경로와 최소 검증 matrix가 구현 가능한 수준으로 확정된다.

Verification:

- 리뷰 문서 `LLM_WIKI/50_Reviews/BT-003-Tactic-Board-Resource-Validation-Compiler-Review.md` 작성.
- Task/ADR/기획/실제 코드 정적 대조. 필요한 경우 문서 전용 spike 결과 기록.

## Step 1: Versioned Draft Resource and Round-trip

> **완료 (2026-07-15).** `Assets/Script/AutoCrawlerBehaviorTree/Tactic/Board/`에 순수 데이터 draft schema를 구현했다:
> `TacticBoardDefinition`(SchemaVersion/BoardId/DisplayName/Rows), `TacticRowDefinition`(RowId/Enabled/Locked/
> Source/Conditions/Target/Action/ApproachPolicy) + typed base + concrete 어휘(조건 always/any_enemy/enemy_casting,
> 대상 nearest_enemy/context_target, 행동 wait/catalog_action) — 각 concrete는 안정 `TypeId`. `TacticActionCatalog`
> (`CatalogRevision` + `TacticActionCatalogEntry{ActionId, Template:TurnActionBase}`)로 §12 template source를 분리했다.
> **직렬화되는 C# Resource는 파일명=클래스명이어야 재로드된다**(1-class-per-file, SkillSystem/Blocks 동형; 초기 grouped
> 파일은 재로드 실패로 분리). 런타임 상태(latch/queue/target)는 없다. 검증: `dotnet build` 0/0, `--import` exit 0,
> `bt003_step1_draft_roundtrip_test` 60 assertions ALL PASS(A board/player·B 잠긴 system row(default_attack 행동
> catalog_action/default_attack + ApproachAllowed 포함)·C catalog+revision·D board 두 소비자 격리·G catalog/entry/
> template 격리·E future version 보존+무재저장·F null/빈 definition), BT-002 Step 1~4 회귀 ALL PASS. 다음은 Step 2 validator.

Goal:

- 실행 상태를 포함하지 않는 저장 가능한 택틱보드 초안 schema를 구현한다.

Scope:

- 승인된 Board/Row/Condition/Target/Action definition Resource + `TacticActionCatalog`/entry(§12).
- catalog는 안정 `CatalogRevision`을 소유한다(§7/§9 `CompileInputSignature`의 catalog 성분). instance id/path 아님.
- schema version, 안정 BoardId/RowId/TypeId, 잠긴 두 system row(`default_attack`/`terminal_wait`) 표현.
- v1 최소 어휘 definition: Always/AnyEnemy/EnemyCasting, ContextTarget/NearestEnemy, Wait/행동 ID, 접근 정책.
- `.tres` save -> cache-ignore load round-trip fixture(board + catalog, `CatalogRevision` 보존).

Out of scope:

- semantic validator, runtime Node 생성, UI.

Done condition:

- 행 순서·ID·enabled/locked/source·definition subtype/parameter·접근 정책이 저장/재로드 후 동일하다.
- `TacticActionCatalog`의 entry(ActionId/template)와 `CatalogRevision`이 저장/재로드 후 동일하다.
- 같은 Resource를 두 소비자가 읽어도 런타임 상태가 생기거나 서로 변형하지 않는다.
- unknown/future version draft를 파괴하거나 자동 재저장하지 않는다.
- null/빈 definition도 초안으로 저장 가능하며 Step 2 validator가 보고할 수 있다.

Verification:

- 신규 Step 1 headless Resource round-trip test.
- `dotnet build`, Godot `--import`, BT-002 Step 1~4 영향권 회귀.

## Step 2: Validator and Structured Diagnostics

> **완료 (2026-07-15).** `Assets/Script/AutoCrawlerBehaviorTree/Tactic/Validation/`에 pure/read-only
> `TacticBoardValidator.Validate(board, resolver)`를 구현했다. `TacticDiagnostic`(Severity/Code/RowId/FieldPath/
> MessageKey/MessageArgs/DeveloperDetail, 불변·방어 복사) + `TacticValidationResult`(IsValid = Error 0건) +
> `TacticDiagnosticCodes`(안정 code 상수). catalog 해석 seam은 `Board/ITacticActionResolver` +
> `TacticActionCatalogResolver`(Ok/Unknown/Duplicate/NullTemplate). 검사 범위: schema/BoardId/RowId(빈·중복)/
> 조건 수(≤2)·타입/대상 타입/행동 해석(§12 unknown·duplicate·null template·resolver 없음)/target-action 호환
> (`target_required`·`target_not_allowed`·`missing_target_context` §11)/잠긴 두 system row 존재·중복·순서·내용·
> `player_row_below_system`/warning(`duplicate_active_row`·`unreachable_after_unconditional`, 성공 불차단). 진단
> 순서는 board→row→field→code→FieldPath→args로 정렬해 Dictionary 순회·동률 비의존, validator 예외는
> `validator_internal_error`로 격리. draft/resolver/template 무변형, 결과 타입도 생성자 방어 복사. 행 수 한계는
> 기획 H-5 **최대 8행**(초과 `too_many_rows`), 조건 ≤2(H-8). 잠긴 system row는 Enabled/Locked/Source/정확한 조건
> 배열(default_attack = 정확히 `[any_enemy]`)까지 검사하고, system row 아래 어떤 행도 RowId identity로만 예외를
> 둬 Locked 위장을 차단한다. 검증: `dotnet build` 0/0, `--import` exit 0, `bt003_step2_validator_test`
> 61 assertions ALL PASS(A~S + O2 system row 손상 변형·O3 8행 경계·O4 tie-break·O5 결과 방어 복사·N.both 두 system row 동시 누락 overflow 회귀),
> BT-003 Step 1·BT-002 Step 1~4 회귀 ALL PASS. 명명 정련: 설계 `TacticCompileDiagnostic` → `TacticDiagnostic`
> (phase-neutral), Step 2 결과는 `TacticValidationResult`. 다음은 Step 3 compiler.

Goal:

- 잘못된 초안을 변경 없이 거부하고 UI가 행/필드별로 표시할 수 있는 진단을 만든다.

Scope:

- pure/read-only `TacticBoardValidator`와 immutable diagnostic/result.
- schema/identity/basic row/system row(`default_attack`/`terminal_wait`)/어휘/행동 해석/호환성 validation.
- `missing_target_context`(ContextTarget인데 후보 공급 조건 없음, §11), catalog 조회 error(unknown/duplicate/null template, §12).
- 확실한 중복·가림 warning.
- validator 예외 격리와 deterministic diagnostic ordering.

Out of scope:

- Node compiler, 실제 UI 렌더링, 추정성 고급 전략 분석.

Done condition:

- blocking error가 하나라도 있으면 success가 아니며 snapshot은 없다.
- 모든 진단은 안정 code와 가능한 RowId/FieldPath를 가진다.
- 같은 입력은 같은 순서의 진단을 내고 Dictionary/Resource 순회 순서에 의존하지 않는다.
- validator 전후 draft·resolver·action template이 불변이다.
- 반환 컬렉션/args를 호출자가 변형해도 다음 validation에 영향을 주지 않는다.

Verification:

- null/duplicate/unknown/missing/incompatible/default-row/warning matrix.
- 두 유닛 resolver 차이, freed/invalid resolver target, 반복 호출 불변 테스트.
- Step 1 round-trip 및 BT-002 회귀.

## Step 3: Compiler and Per-unit Snapshot

> **완료 (2026-07-15).** `Assets/Script/AutoCrawlerBehaviorTree/Tactic/Compile/`에 `TacticBoardCompiler.Compile(board,
> resolver, catalogRevision, unitId)`를 구현했다. 흐름 = validate 선행(Error면 미컴파일) → 정규 구조 Node 생성 →
> `BehaviorTreeValidation` 2차 검사(`generated_tree_invalid`) → `CompiledBoardSnapshot` 공개. 정규 매핑(§11):
> 행동 행 = `TacticRow[조건*, (NearestEnemy이고 target-bearing 조건 부재 시 SupplyAnyEnemy), TacticTargetSelector(Policy),
> TacticActionSequence[TacticApproach, TacticTurnAction(유닛 인스턴스)]]`, wait 행 = `TacticRow[조건*, TacticWait]`.
> **NearestEnemy 후보 공급 정규 규칙(ADR §11 확정)**: draft에 target-bearing 조건(any_enemy/enemy_casting)이
> 있으면 그 조건이 후보를 공급하므로 추가 노드 없이 `[조건, Selector, Seq]`, 없으면 `SupplyAnyEnemy`를 주입한다.
> `전체 ∩ X = X` idempotent라 두 경우 후보 집합·대상 선택이 동일하다("항상 생성"의 중복 노드만 제거 — ADR/Review 갱신).
> **비활성 행 skip(재검토 P1)**: compiler가 `Enabled=false` 행을 생성하지 않아 플레이어가 끈 상위 행이 전투에서
> 발동하지 않는다. snapshot `RowIds`도 실제 생성 행만 담는다(잠긴 system row는 validator가 Enabled 강제 → 항상 생성).
> action은 `ITacticActionResolver.CreateInstance`가 catalog template을 `Duplicate()`해 유닛별 격리 인스턴스 반환(§12)
> — 실측 확인: `Duplicate()`는 새 C# 인스턴스라 `_usedCost`/queue/explicit target 등 mutable 런타임 상태가 fresh다
> (spike 통과). signature는 정규화 schema+행순(비활성 행 포함) SHA256, `CompileInputSignature = SourceSignature ⊕
> CatalogRevision`. 실패/예외는 partial Node `Free()` + snapshot 미공개(`action_create_failed`/`generated_tree_invalid`/
> `compiler_internal_error`). 결과 타입 = `TacticCompileResult`/`CompiledBoardSnapshot`(모두 컬렉션 방어 복사).
> 검증: `dotnet build` 0/0, `--import` exit 0, `bt003_step3_compiler_test` 49 assertions ALL PASS(구조 매핑·후보 공급
> 주입/생략·비활성 행 skip·두 유닛 격리·결정론 signature·invalid 미컴파일·생성 실패/예외 정리·결과 방어 복사),
> `bt003_step3_compile_battle_test` ALL PASS(설치→접근+근접 공격·턴 경계 latch reset·비활성 상위 행 미발동·
> `EnemyCasting+NearestEnemy` 다중 상대 대상 선택(가까운 영창 적만 피해, 비영창/먼 영창 무피해 matrix #20)·
> 대상 없음→terminal_wait 이동/피해 없음 matrix #22·BT-002 parity), BT-003 Step 1~2·BT-002 Step 1~4 회귀 ALL PASS.
> production root 교체(explicit installer)와 BattleSession은 Step 4. 다음은 Step 4.

Goal:

- 유효한 definition을 BT-002의 실제 제품 Node와 유닛 전용 행동으로 결정론적으로 컴파일한다.

Scope:

- compiler registry/factory, catalog 기반 action resolver, source signature.
- PrioritySelector -> ordered Row -> conditions/selector/action sequence/approach/wait 생성. `NearestEnemy` 대상은 후보 공급 보장(target-bearing 조건 부재 시만 `TacticConditionAnyEnemy` 주입, §11).
- RowId/정책/대상 의미와 action spec 보존, 잠긴 두 system row 정규 매핑.
- resolver가 catalog `ActionId`를 조회해 매 compile 새 `TurnActionBase` 인스턴스 반환(§12, `ReferenceEquals == false`).
- 생성된 tree의 `BehaviorTreeValidation` 2차 검사(`generated_tree_invalid`)와 compiler exception 격리.
- 복제 방식(`Duplicate`/factory) 실측 spike, 실패 시 생성 중 Node/Resource 정리.

Out of scope:

- production Character의 기존 root 교체, BattleSession.

Done condition:

- 동일 draft+resolver context는 같은 구조/RowId/order/signature를 만든다.
- 같은 draft를 두 유닛에 컴파일하면 Node/context/action 인스턴스가 격리된다.
- action ID가 각 유닛의 올바른 action으로 해석되고 ammo/마나/TurnAction template을 변형하지 않는다.
- compile 중간 실패·구조 validation 실패에서 partial Node가 남지 않고 snapshot을 공개하지 않는다.
- 컴파일된 fixture가 작은 실제 전장에서 BT-002와 같은 행·대상·접근·행동 결과를 낸다.

Verification:

- definition -> exact Node structure mapping test.
- resolver missing/throws, factory unsupported/throws, structural validation failure cleanup.
- two-unit isolation, repeat compile, deterministic signature.
- BT-002 Step 1~4와 실제 battle fixture 회귀.

## Step 4: Atomic Apply, Replacement, and Completion
> **완료 (2026-07-15).** Step 4는 explicit `BehaviorTree.InstallRoot`/`RemoveInstalledRoot`, 유닛별 `TacticBoardApplyState`(last-good snapshot·prepare gate·`CompileInputSignature` stale query), `CharacterArticle` apply/사망/tree exit seam, `bt003_step4_apply_lifecycle_test`를 구현했다. installer는 새 root를 동기 연결하고 구조 update를 한 번만 발행하며, 실패·parented/foreign/sealed snapshot은 기존 root/metadata를 보존한다. 코드 리뷰 결과 P0/P1/P2 발견 없음, 판정 완료([[BT-003-Tactic-Board-Resource-Validation-Compiler-Completion-Review]]).
>
> 검증: `dotnet build` 0/0, Godot `--import` exit 0, Step 4 fixture **51 assertions ALL PASS**(3회 반복 exit 0, fixture 종료 ObjectDB/resource 경고 0줄). 리뷰 재검증으로 `dotnet build`, BT-003 Step 1~4 headless fixture가 PASS했다. clean import/Step 3 fixture의 ObjectDB/resource 경고는 별도 기존 경고로 유지한다.

Goal:

- 성공 snapshot만 기존 BehaviorTree에 원자적으로 적용하고 반복 교체·실패·teardown을 안전하게 닫는다.

Scope:

- 승인된 explicit root installer/apply owner.
- 준비 상태 apply gate, old root reset/free, Tree/Blackboard/child cache 즉시 초기화.
- last-good applied metadata와 명시적 `CompileInputSignature`(board `SourceSignature` ⊕ catalog `CatalogRevision`) 비교 seam. catalog revision 변경도 stale로 판정(§7/§9, matrix #28).
- debugger structure update와 death/tree exit/session teardown.
- 문서와 완료 리뷰.

Out of scope:

- 실제 전투 시작 버튼 차단 UI, 보드 창, PartySnapshot/DungeonRun.

Done condition:

- 성공 apply 직후 첫 tick부터 새 root만 실행한다.
- compile/apply 실패는 기존 root와 applied metadata를 변경하지 않는다.
- A -> B 교체 후 A의 latch/context/target/action/tween이 실행되거나 누수되지 않는다.
- 동일 snapshot 재사용/중복 parent/전투 중 교체는 fail-closed한다.
- 두 유닛이 같은 draft를 적용해도 runtime state가 격리된다.
- debug structure/RowId가 새 tree와 일치하고 사망/free/tree exit에서 snapshot 소유 Node가 남지 않는다.
- Task/ADR/System/Current-State/Open-Tasks/Completion Review가 실제 코드와 일치한다.

Verification:

- compile -> apply -> first tick, A -> B replace, invalid compile preserves A, duplicate apply rejection.
- same draft two units, repeated battle-like teardown/re-entry, debugger structure smoke.
- `dotnet build`, Godot `--import`, BT-001/BT-002/SK-002/SK-004/BS-001 영향권 회귀.
- **leak 추적**: 종료 시 `ObjectDB instances leaked`/resource 경고는 clean `--import` baseline에도 나온다(Step 3 순수·실전 테스트 모두 재현). Step 4 lifecycle 검증에서 baseline 경고와 테스트 생성분(예: `SceneTreeTimer`, spawn한 상대, 설치한 root)을 분리해 추적하고, snapshot 소유 Node/action이 death/free/tree exit에서 정리되는지 명시 단언한다. "leak 0"으로 단정하지 않는다.

## Minimum Verification Matrix

| # | Scenario | Required evidence |
|---|---|---|
| 1 | valid Resource save/reload | 모든 행/ID/subtype/parameter/order 보존 |
| 2 | invalid draft save | 초안 보존, validator Error, 자동 수정 없음 |
| 3 | unknown future version | Error, 원본 보존, snapshot 없음 |
| 4 | duplicate/null/missing row fields | 안정 code + RowId/FieldPath |
| 5 | default system row damaged | blocking Error, silent synthesis 없음 |
| 6 | warning-only board | Success 가능 + Warning 보존 |
| 7 | action unavailable in catalog | resolver Error, template/resources 무변형 |
| 8 | valid compile mapping | BT-002 exact node order/RowId/policy/action |
| 9 | deterministic repeat | structure/diagnostic order/signature 동일 |
| 10 | same draft, two units | Node/context/action/state identity 격리 |
| 11 | compiler/factory exception | internal Error, partial tree 정리, old applied 유지 |
| 12 | generated BT invalid | BehaviorTreeValidation Error로 compile 실패 |
| 13 | apply then first tick | deferred frame 의존 없이 새 tree 실행 |
| 14 | A -> B replacement | A reset/free, B만 실행, stale target/tween 없음 |
| 15 | invalid draft after A | A 보존, 자동 출전 아님을 consumer가 판별 가능 |
| 16 | duplicate/parented snapshot | fail-closed, tree 손상 없음 |
| 17 | death/free/teardown | compiled Node/action/runtime reference 정리 |
| 18 | existing handmade BT | compile/apply를 쓰지 않는 경로 무회귀 |
| 19 | Always + NearestEnemy + action (§11) | AnyEnemy 후보 공급 노드를 거쳐 실제 적 확정 |
| 20 | EnemyCasting + NearestEnemy (§11) | 영창 중 적 집합 교집합 내 최근접 확정 |
| 21 | Always + ContextTarget (§11) | `missing_target_context` blocking Error |
| 22 | default_attack row (§11) | 사거리 내 공격 / 접근 가능 시 접근 / 불가 시 terminal_wait로 진행 |
| 23 | compiler synthesis 금지 (§11) | terminal_wait 외 기본 행을 compiler가 임의 생성하지 않음 |
| 24 | same ActionId, two units (§12) | `TurnAction` `ReferenceEquals == false`, queue/cost/target 변형 비가시 |
| 25 | A -> B -> A recompile (§12) | catalog만으로 성공; old root free 뒤 catalog template 유효 |
| 26 | unknown/duplicate/null ActionId·template (§12) | blocking Error, 기존 applied root 보존 |
| 27 | clone/factory 실패 (§12) | partial snapshot 정리 + Error, applied 보존 |
| 28 | catalog revision 변경 (§7/§9) | 같은 board라도 `CompileInputSignature` 불일치 → 재컴파일 필요; 명시적 재적용 전 stale snapshot을 최신 적용본으로 표시 안 함 |

## Completion Criteria

- Step 0 설계 리뷰 승인과 ADR-024 accepted.
- Step 1~4 구현·리뷰·재검증 완료, P0/P1 없음.
- 저장 초안, 진단, compile result와 applied snapshot 상태가 구분된다.
- 유효한 draft만 BT-002 런타임으로 컴파일·적용된다.
- 실패가 draft 또는 마지막 정상 적용본을 파괴하지 않고 stale snapshot으로 조용히 출전시키지도 않는다.
- per-unit Node/context/action state와 반복 apply/teardown이 격리된다.
- Minimum Verification Matrix 전건에 자동 증거가 연결된다.
- UI/BattleSession/DungeonRun 후속 경계가 문서에 명확하다.

## Follow-ups

- BT-004: 택틱보드 편집 UX, 행별 진단 표시, 잠긴 system row 노출/행 개수 정책, 저장/적용/마지막 정상본 상태, 명시적 되돌리기.
- DL-001: `Preparing -> Tactic setup -> Battle -> Result -> Next floor`, PartySnapshot과 EncounterDefinition. `TacticActionCatalog` 소유권을 CharacterArticle에서 PartySnapshot으로 이관.
- BattleSession request에 unit-id별 compiled board 또는 source signature 적용.
- 프리셋 복사/유닛 할당/인계/save slot 영속과 schema migration.
- H-4 전체 어휘, 해금/성장 catalog(`TacticActionCatalog` entry 확장), 고급 정적 분석과 행별 발동 통계.
