---
id: SK-001
type: task
status: complete
system: Combat
created: 2026-07-06
updated: 2026-07-07
tags: [task, combat, skill-system, data-driven, effect-block]
---

# SK-001 Data-Driven Skill System

## Goal

`F:\beestation\ref\wizards_climber_wiki\기획서\10_스킬시스템_설계.md`의 방향을 현재 AutoCrawler 전투 코드에 맞게 이식한다.

목표 상태는 신규 스킬과 밸런싱을 코드 추가가 아니라 `.tres` 데이터 편집으로 처리하는 것이다.

핵심 원칙:

- 스킬 정의는 공유 불변 `SkillDefinition` Resource다.
- 실행 중 상태(`ActionQueue`, 사용 행동력, 확정 대상, 블록 결과)는 유닛별 `SkillState`가 가진다.
- 효과는 재사용 가능한 `EffectBlock` 코드가 담당한다.
- 기존 하드코딩 `TurnAction_Attack`, `TurnAction_MagicBolt`, `TurnAction_ChainLightning`은 단계적으로 데이터 기반 스킬로 대체한다.
- 연계(combo)는 2026-07-06 기준 보류다. `SkillDefinition.combo`는 예약 필드로만 남기고 구현·데이터 입력은 하지 않는다.

## Current Code Facts

- `TurnActionBase : Resource`가 `ActionQueue`, `_usedCost`를 직접 가진다.
- `TurnAction_Attack`은 `_target`을, `TurnAction_MagicBolt`는 `_targetPosition`을, `TurnAction_ChainLightning`은 `_owner`, `_startingArticle`, `_targetArticle`, `_hitTargets`, `_playingFx` 등 런타임 상태를 Resource 내부에 가진다.
- `BehaviorTree_TurnAction`은 export된 `TurnActionBase` Resource를 직접 `Init()`/`Action()`하고, Running이면 `CharacterArticle.CurrentTurnAction`에 같은 인스턴스를 보관한다.
- `PhysicalDamage`는 ADR-017에 따라 `BattleFieldScene.BattleField.TurnHelper.CombatRandRange`를 소비한다.
- `Damage.ApplyImmediately`는 아직 `DamageFloater`, `Hit()` UI를 직접 호출한다. 전투 시뮬레이션/프레젠테이션 분리는 CB-001 후속이다.
- `Mana`, `WeaponType`, 장비 게이트, 스킬 레벨 구매, 택틱 보드 데이터 모델은 아직 없다.
- `SkillUtil`은 ADR-017 정규 순서(`거리 -> Y -> X`)와 경로 후보 선택을 제공한다.
- 알려진 턴 순환 위험: 현재 턴 유닛이 리스트에서 제거되면 `GetNextTurnArticle`의 `IndexOf`가 -1을 반환해 순환이 리스트 처음으로 리셋될 수 있다. 영창 취소처럼 현재 턴 액션을 외부에서 끊는 기능은 이 부류의 회귀를 반드시 검증해야 한다.

## Source Design Summary

참조 기획서의 주요 설계:

- `SkillDefinition`: `id`, `display_name`, `track`, `level_cap`, `weapon_gate`, `turn_cost`, `mana_cost`, `range`, `target_side`, `target_selector_default`, `effects`, 예약 필드 `combo`, `prerequisite`, `gambit_unlocks`, `level_scaling`.
- `SkillState`: 전투 시작 시 장착 스킬마다 생성되는 유닛별 실행 상태. Resource 공유 상태 사고를 막는다.
- `EffectBlock`: 파라미터만 가진 불변 Resource. 실행 시 `SkillContext`를 받아 자기 몫의 phase를 큐에 추가한다.
- `TurnAction_Skill`: 단일 데이터 기반 실행기. 지불 검증 -> 대상 확정 -> windup -> effects -> 종료 및 리포트 기록을 처리한다.
- `TargetSelector`: `Nearest`, `LowestHp`, `HighestHp`, `Casting`, `ContextTarget`, `LowestHpAlly`, `Self`.
- 결정론 규율: 전투 RNG 단일 창구, FX와 상태 변경 분리, block report 기록.

## Scope

- 데이터 기반 스킬 골격(`SkillDefinition`, `SkillState`, `SkillContext`, `EffectBlock`, `TurnAction_Skill`) 설계와 단계적 도입.
- 첫 기준 스킬은 07 수치표의 `베기`이며, 기존 `TurnAction_Attack`과 같은 결과를 내는 것을 Step 1 완료 기준으로 삼는다. `베기` 기준은 `minDamage=10`, `maxDamage=20`, `range=1`로 유지한다.
- 멀티턴 windup 검증과 `TurnAction_MagicBolt` 대체는 Step 2에서 처리한다.
- 기존 BT 실행 경로(`BehaviorTree_TurnAction`, `CharacterArticle.CurrentTurnAction`)를 유지하면서 내부 실행 상태를 `SkillState`로 분리한다.
- 기존 결정론 규약(ADR-017)을 보존한다.

## Out of Scope

- 데모 최소 루프. `08_데모_스코프.md`는 기존 3종 스킬 그대로 진행 가능하다고 명시한다.
- 택틱 보드 UI와 프리셋 편집 UI.
- 스킬 구매/숙련 XP/장비 게이트/무기 시스템.
- 전투 시뮬레이션/프레젠테이션 완전 분리.
- 연계(combo): 보류(2026-07-06). 재개 시 별도 Task에서 `SkillDefinition.combo` 예약 필드의 의미, windup 생략 여부, 마나/대상 실패 정책을 다시 확정한다.
- 12종 스킬 전체 데이터 입력. 이는 골격과 블록 카탈로그가 검증된 뒤 진행한다.

## Proposed Architecture

### Data

- `SkillDefinition : Resource`
  - v0 필수: `Id`, `DisplayName`, `TurnCost`, `Range`, `TargetSide`, `TargetSelectorDefault`, `Effects`.
  - v0 예약/보류: `Combo`는 필드 예약만, 구현·데이터 입력 없음.
  - v0 후속: `WeaponGate`, `ManaCost`, `Prerequisite`, `GambitUnlocks`, `LevelScaling`.
- `EffectBlock : Resource`
  - v0 abstract base.
  - `EnqueuePhases(SkillContext ctx, SkillState state)` 또는 동등한 API로 phase를 추가한다.
  - Resource 내부에는 진행 상태를 저장하지 않는다.
- `SkillState`
  - definition 참조, skill level, phase queue, used cost, confirmed targets, casting flag, last block result, report accumulator.
  - 일반 C# 객체로 두고 `.tres` 저장 대상에서 제외한다.
- `SkillContext`
  - caster, tile map, fx player, combat RNG gateway, report sink.
  - v0는 `BattleFieldScene.BattleField`를 감싸는 adapter로 시작한다. 단, block이 보는 API는 처음부터 주입 형태로 고정해 추후 adapter 교체만으로 headless 전환 가능하게 한다.

### Runtime

- `TurnAction_Skill : TurnActionBase`
  - export `SkillDefinition`.
  - `TurnActionBase`와 BT 계약은 우선 유지하되, mutable 실행 상태는 `SkillState`로 위임한다.
  - v0에서는 `MasterCost = definition.turn_cost.windup + definition.turn_cost.act`와 동일하게 동작한다.
  - windup 중에는 `SkillState`/caster를 통해 "영창 중" 플래그를 외부에 노출하고, BT 재평가를 중지한다.
- 대상 선택
  - `target_side`로 적/아군/자기 필터를 정한다.
  - `TargetSelector`가 1차 정렬을 정하고, 모든 동점자는 `SkillUtil.ThenByCanonical`로 고정한다.

### Compatibility

- `TurnAction_Attack`, `TurnAction_MagicBolt`, `TurnAction_ChainLightning`은 즉시 삭제하지 않는다.
- Step 1~3 동안 기존 deterministic combat regression은 계속 기준선으로 유지한다.
- 데이터 기반 `베기`가 기존 `Attack`과 같은 결과를 낸 뒤 기존 scene/BT 일부를 전환한다.
- 데이터 기반 windup fixture가 기존 `MagicBolt`/`TurnAction_Cast` 패턴을 재현한 뒤 `TurnAction_MagicBolt` 대체 경로를 연다.

## Steps

### Step 0 - Design Review + ADR

Scope:

- 이 Task와 [[ADR-018-Data-Driven-Skill-System]]를 실제 코드와 대조해 리뷰한다.
- 제품 코드와 리소스는 수정하지 않는다.

Done condition:

- 설계 리뷰 판정 `Approved` 또는 `Approved after design fixes`.
- Step 1 구현 전 확정해야 할 API, 저장 경계, 실패 정책이 문서화되어 있다.

### Step 1 - Skeleton + DamageBlock + Slash Data

Scope:

- `Assets/Script/SkillSystem`을 신설하고 `SkillDefinition`, `SkillState`, `SkillContext`, `EffectBlock`, `DamageBlock`, `TurnAction_Skill`의 최소 골격을 추가한다.
- `베기` SkillDefinition `.tres`를 작성해 기존 `TurnAction_Attack`의 `minDamage=10`, `maxDamage=20`, range 1, physical damage를 데이터로 재현한다.
- `DamageBlock`은 기준선 일치를 위해 기존 `Damage.CreateDamage<T>()`를 재사용한다.
- 기존 `TurnAction_Attack`은 남긴다.

Done condition:

- 같은 seed와 같은 caster/target 조건에서 기존 Attack과 데이터 기반 베기의 HP 변화/크리티컬/RNG 소비 순서가 일치한다.
- `SkillDefinition` 저장 -> 재로드 후 id/range/effects 파라미터가 보존된다.
- `EffectBlock` Resource에는 실행 중 상태가 남지 않는다.
- 같은 `SkillDefinition`을 두 유닛이 공유해도 `SkillState`의 phase queue, target, used cost가 섞이지 않는다.

Out of Scope:

- Mana, hit chance, combo, status effects, 기존 Attack 삭제.

### Step 2 - Windup + MagicBolt Migration Fixture + TargetSelector

Scope:

- `Nearest`, `LowestHp`, `HighestHp`, `Self` v0 selector를 구현한다.
- enemy/ally/self target side 필터를 추가한다.
- 동점자는 ADR-017 정규 순서를 유지한다.
- windup 2턴짜리 스킬 fixture를 추가한다. 권장 기준은 `마탄`/`TurnAction_MagicBolt` 재현이며, `TurnAction_Cast` 패턴 이관을 함께 검증한다.
- 데이터 기반 windup fixture가 `TurnAction_MagicBolt` 대체 시점이다. 기존 `TurnAction_MagicBolt`는 이 Step의 기준선 비교가 통과하기 전 삭제하지 않는다.

Done condition:

- 복수 후보 fixture에서 selector별 대상과 동점자가 기대와 일치한다.
- 기존 `GetTarget` 기본 동작(`Nearest enemy`) 회귀가 유지된다.
- windup fixture에서 `MasterCost = windup + act` 등가가 검증된다.
- windup 중 턴이 다른 유닛에게 넘어가는 인터리브가 검증된다.
- windup 중 같은 caster의 BT 재평가가 일어나지 않는다.
- windup 중 "영창 중" 플래그가 조건 어휘/HUD/영창 취소가 소비 가능한 형태로 외부 노출된다.
- 데이터 기반 MagicBolt fixture가 기존 `TurnAction_MagicBolt`의 대상 확정, Cast/Casting 진행, 피해 적용 기준선을 재현한다.

Out of Scope:

- `Casting`, `ContextTarget`, 택틱 보드 row 조건과의 연결.
- 스턴으로 인한 영창 취소. 이 플래그를 소비하는 취소 처리는 Step 4에서 구현한다.

### Step 3 - Mana + Hit Chance Payment Gate

Scope:

- `Mana` StatusElement를 추가한다.
- `SkillDefinition.mana_cost`, `DamageBlock.hit_chance`를 반영한다.
- 마나 부족이면 행동이 실패하고 BT가 다음 행으로 넘어갈 수 있는 실패 신호를 유지한다.
- 명중 도입 시점에 새 damage result API로 전환하고, `Damage.ApplyImmediately` UI 직접 결합 제거 Follow-up과 병합할 수 있는 경계를 만든다.

Done condition:

- 마나 부족 시 HP/RNG/FX 상태 변경 없이 Failure.
- 명중 판정은 `CombatRng`만 소비하고, 같은 seed에서 재현된다.
- 명중 실패와 피해 적용 성공 모두 report sink에 기록된다.
- `DamageBlock`이 기존 `Damage.CreateDamage<T>()`에 묶여 있던 한계를 새 damage result API로 넘길 수 있는 migration 지점이 문서화된다.

Out of Scope:

- 아웃게임 마나 회복, 스킬 구매, 장비 게이트.

### Step 4 - Status and Control Blocks

Scope:

- `StunBlock`, `BindBlock`, `SelfBuffBlock`, `ManaDrainBlock`을 구현한다.
- `KnockbackBlock`은 타일 이동/점유/경로 갱신 성격이 달라 별도 sub-step 또는 별도 completion block으로 검증한다.
- 필요한 `StatusAffect` 구현체와 Damage 수신자 배율 hook을 추가한다.
- 스턴의 영창 취소는 Step 2의 "영창 중" 플래그를 소비해 `CharacterArticle.CurrentTurnAction`/`SkillState` 폐기를 통해 처리한다.

Done condition:

- `StunBlock`, `BindBlock`, `SelfBuffBlock`, `ManaDrainBlock`은 독립 fixture로 적용/만료/재진입을 검증한다.
- 상태 변경은 턴 시작 또는 effect 적용 phase에서만 일어나고 frame 반복에 종속되지 않는다.
- 스턴이 windup 중인 대상의 영창을 취소하고, 이미 지불한 비용/마나는 되돌리지 않는다.
- 영창 취소가 턴 순환 순서를 교란하지 않는다. 특히 현재 턴 유닛의 상태를 외부에서 바꾸는 상황에서도 다음 턴 순서가 `Priority -> SpawnIndex` 기준으로 유지되고 리스트 처음으로 리셋되지 않음을 회귀 검증한다.
- `KnockbackBlock`은 점유 칸 충돌, 맵 경계, 이동 중단 위치, `OnMove` signal 발행, `AStar`/tilemap 점유 갱신을 별도 fixture로 검증한다.

### Step 5 - ChainBlock Migration

Scope:

- `TurnAction_ChainLightning`의 연쇄 대상 선택과 phase 패턴을 `ChainBlock`으로 이관한다.
- 기존 ChainLightning과 데이터 기반 연쇄 뇌격의 결과 로그를 비교한다.

Done condition:

- 체인 대상 선택이 `미타격 우선 -> ADR-017 정규 순서`를 유지한다.
- 같은 seed에서 기존 ChainLightning 기준선과 데이터 기반 결과가 일치한다.
- `TurnAction_ChainLightning` 삭제 여부를 결정하기 전 기존 scene/BT 참조가 모두 데이터 기반 스킬로 대체 가능한지 확인한다.

### Step 6 - Phase 1 Skill Data Pack

Scope:

- `07_수치표_Phase1.md`의 12종 스킬을 `.tres`로 입력한다.
- 격투 라인은 갱신된 수치표 기준으로 단독 역할을 반영한다: 잽 10~16/명중100, 스트레이트 유지, 소닉 블로는 스턴 35% 영창 취소기.
- 기존 3종 하드코딩 TurnAction 사용을 중단할 수 있는지 검토한다.

Done condition:

- 12종 SkillDefinition 로드/검증 PASS.
- 대표 전투 fixture에서 기본기, 강기, 제어, 흡수가 실행된다.
- `SkillDefinition.combo`에는 데이터가 입력되지 않는다.

## Verification Plan

- C# headless tests:
  - SkillDefinition serialization round-trip.
  - SkillState isolation: 같은 SkillDefinition을 두 유닛이 공유해도 phase queue/target/used cost가 섞이지 않는다.
  - DamageBlock vs existing Attack baseline.
  - windup/MagicBolt baseline and turn interleave matrix.
  - selector matrix.
  - stun casting-cancel and turn-circulation regression.
  - knockback tile occupancy/bounds/signal/path update matrix.
  - RNG static guard continuation from CB-001.
- Godot headless editor load:
  - 새 `[GlobalClass]` Resource들이 parse/import 된다.
- Determinism regression:
  - `cb001_step1~4`는 SK-001 단계마다 유지한다.

## Risks

- 기존 `TurnActionBase` 자체가 mutable Resource이므로, `TurnAction_Skill`만 상태 분리해도 기존 하드코딩 액션에는 위험이 남는다. 삭제 전까지는 scene fixture가 같은 Resource를 공유하지 않는지 주의해야 한다.
- `Damage.ApplyImmediately`의 UI 직접 호출은 headless 단위 테스트를 어렵게 만든다. Step 1은 기존 Attack과 같은 결합을 일단 허용하고, Step 3 명중 도입 시 새 damage result API와 UI 결합 제거 경계를 만든다.
- `PhysicalDamage`가 `BattleFieldScene.BattleField.TurnHelper`를 직접 조회한다. 장기적으로는 `SkillContext` RNG gateway로 좁혀야 하지만, Step 1 baseline 일치를 위해 기존 경로를 보존한다.
- `Mana`와 `WeaponType`은 아직 모델이 없으므로 Step 3 이후 설계 변경 가능성이 있다.

## Resolved Decisions

- 파일 위치: `Assets/Script/SkillSystem`을 신설한다. legacy `TurnAction` 폴더와 분리한다.
- 실행기 계약: `TurnActionBase` 상속을 유지한다. interface 기반 분리는 기존 3종 삭제가 확정되는 마지막 Step 이후 재평가한다.
- `SkillContext`: v0는 `BattleFieldScene.BattleField`를 감싸는 adapter로 시작하되, block이 보는 API는 처음부터 주입 형태로 고정한다. 추후 adapter 교체만으로 headless 전환 가능하게 한다.
- `DamageBlock`: Step 1은 기존 `Damage.CreateDamage<T>()`를 재사용한다. Step 3 명중 도입 시점에 새 damage result API로 전환하며 `Damage.ApplyImmediately` UI 결합 제거 Follow-up과 병합한다.

## Step 0 Design Review Result

[[SK-001-Data-Driven-Skill-System-Review]] 판정: **Approved after design fixes**.

Step 1 구현 전 확정된 보완:

- `TurnActionBase.Action()`이 non-virtual이고 `_usedCost`가 private이므로, Step 1 범위에 작은 legacy
  compatibility seam을 포함한다. 목표는 기존 `BehaviorTree_TurnAction`/`CharacterArticle.CurrentTurnAction`
  경로와 legacy 3종 TurnAction 동작을 보존하면서, `TurnAction_Skill`의 phase queue/used cost/target이
  Resource 필드가 아니라 유닛별 `SkillState`로만 복원되게 하는 것이다.
- 구현 형태는 Step 1에서 코드 대조 후 선택하되, 완료 조건은 고정한다: 같은 `SkillDefinition`과 같은
  `TurnAction_Skill` Resource를 두 유닛 또는 두 BT 노드가 공유해도 `SkillState`의 phase queue, target,
  used cost가 섞이지 않아야 한다.
- `DamageBlock`의 public 실행 계약은 처음부터 `SkillContext`를 받는다. 다만 Step 1 baseline 일치를 위해
  legacy `Damage.CreateDamage<T>()` 내부의 `BattleFieldScene.BattleField.TurnHelper` 직접 조회는 명시적 예외로
  허용한다. 이 예외는 Step 3 damage result API 전환 시 제거 또는 축소한다.
- Step 1 baseline 테스트는 최종 HP만 보지 않고 기존 Attack과 데이터 기반 베기의 phase completion 지점,
  CombatRng 소비 순서/개수, 크리티컬 결과, 최종 HP를 함께 비교한다.
- v0 invalid definition 정책: definition 누락, empty id, range < 0, null effect는 SCRIPT ERROR 없이
  fail-closed되어야 한다. BT Failure와 no-op Success 중 정확한 반환은 Step 1 구현 시작 시 기존 전투 흐름과
  맞춰 하나로 고정하고 테스트에 남긴다.


## Step 1 Implementation Result

상태: **implemented, review needed**. 구현 완료 후 자체 완료 판정은 하지 않는다. 다음 단계는 Step 1 코드 리뷰다.

변경 파일:

- `Assets/Script/TurnAction/TurnActionBase.cs`: legacy 3종 동작을 유지하면서 `Init`/`Finish`/`Action`을 virtual seam으로 개방.
- `Assets/Script/Article/CharacterArticle.cs`: `CurrentTurnActionState` 런타임 상태 슬롯 추가. `TurnAction_Skill`만 사용하며 기존 `CurrentTurnAction` 경로는 유지.
- `Assets/Script/SkillSystem/`: `SkillDefinition`, `SkillState`, `SkillContext`, `EffectBlock`, `DamageBlock`, `TurnAction_Skill` v0 추가.
- `Assets/SkillData/slash.tres`: Step 1 기준 스킬 `slash`/`베기`, range 1, physical `DamageBlock(10~20)` 데이터.
- `Assets/Script/Tests/Sk001Step1SkillSystemTest.cs`, `sk001_step1_skill_system_test.tscn`: Step 1 headless 검증.

구현 내용:

- `SkillDefinition`/`EffectBlock`은 `.tres` 저장 Resource이고, 실행 중 phase queue/used cost/confirmed targets/report는 일반 C# 객체 `SkillState`가 보유한다.
- `TurnAction_Skill`은 기존 BT owner(`BehaviorTree_TurnAction`)에서 caster를 찾고, caster의 `CurrentTurnActionState`에 유닛별 `SkillState`를 저장한다. 같은 `SkillDefinition`과 같은 `TurnAction_Skill` Resource를 공유해도 상태가 섞이지 않는다.
- v0 target은 Step 1 범위에 맞춰 enemy + nearest만 지원하며, 동점자는 기존 `SkillUtil.OrderByCanonical`을 사용한다.
- `DamageBlock`은 public 계약상 `SkillContext`/`SkillState`를 받아 phase를 enqueue하지만, Step 1 baseline 일치를 위해 실제 피해는 legacy `Damage.CreateDamage<PhysicalDamage>`를 재사용한다.
- invalid definition(`null`, empty id, negative range, null effect)은 SCRIPT ERROR 없이 state를 만들지 않고 `ActionState.End`로 fail-closed된다. Step 1에서는 BT Failure가 아니라 no-op End를 선택했다. 이유: 현 `TurnActionBase.ActionState` 계약에는 Failure가 없고 legacy no-target action도 Success/End 쪽으로 닫히기 때문이다.

검증 결과:

- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `sk001_step1_skill_system_test.tscn` — ALL PASS: slash `.tres` load/save/reload, 기존 Attack 대비 phase/status+HP+다음 RNG baseline 동일, shared Resource SkillState isolation, invalid definition fail-closed.
- `godot --headless --path . --import` — exit 0, 새 `DamageBlock`/`EffectBlock`/`SkillDefinition`/`TurnAction_Skill` GlobalClass 등록 확인. 종료 시 기존성 `graph_offset deprecated`, ObjectDB/resource leak 로그가 출력됨.
- CB-001 회귀: `cb001_step1_turn_effect_test`, `cb001_step2_combat_rng_test`, `cb001_step3_canonical_order_test`, `cb001_step4_determinism_test` 모두 ALL PASS. Step4는 기존 전투 씬의 `Walk`/`Attack` 애니메이션 누락 ERROR 로그와 종료 leak warning을 출력하지만 exit 0 및 PASS.

남은 위험/리뷰 포인트:

- `CurrentTurnActionState`가 `object`라 타입 안정성은 약하다. Step 1 blast radius를 줄이기 위한 compatibility seam이며, 기존 하드코딩 TurnAction 삭제가 확정되기 전까지 유지한다.
- `DamageBlock`은 아직 legacy Damage 내부의 `BattleFieldScene.BattleField.TurnHelper` 직접 조회를 허용한다. Step 3 damage result API 전환 때 축소/제거한다.
- Step 1은 enemy nearest만 지원한다. selector 확장은 Step 2 범위다.
- Godot 종료 시 ObjectDB/resource leak warning이 남는다. 테스트 판정은 통과하지만 코드 리뷰에서 제품 변경 기인인지 기존 headless/import 로그인지 확인할 가치가 있다.

## Step 1 Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 1 판정: **Approved (minor fixes applied)**.

리뷰 후 적용한 정리:

- `ITurnActionState` marker interface 도입, `CharacterArticle.CurrentTurnActionState` 타입을 `object`에서 `ITurnActionState`로 축소, `SkillState`가 구현.
- `SkillDefinition.IsValidV0()`가 Step 1 미지원 target side/selector를 fail-closed 처리.
- `sk001_step1_skill_system_test` D 케이스에 unsupported side no-state/HP 불변 단언 추가.
- `DamageBlock` magical 분기에 legacy MagicBolt parity 주석 추가.

남은 후속은 Step 3 damage result API 전환 시 `SkillContext.CombatRandRange` seam 실증, Step 3 report sink 단언, Step 2 windup cost 모델 재검토다.

## Step 2 Implementation Result

상태: **implemented, review needed**.

변경 파일:
- `Assets/Script/SkillSystem/SkillEnums.cs`: `SkillTargetSelector`에 `LowestHp`, `HighestHp`, `Self` 추가.
- `Assets/Script/SkillSystem/SkillDefinition.cs`: `WindupCost` 속성 추가 및 `MasterCost` = `ActCost + WindupCost`로 수정. `IsValidV0()`의 타겟 제약 완화.
- `Assets/Script/SkillSystem/SkillState.cs`: `IsCasting` 프로퍼티 추가 (`UsedCost < WindupCost`).
- `Assets/Script/SkillSystem/TurnAction_Skill.cs`: `SelectTarget`에 `TargetSide` 및 `SkillTargetSelector` 분기 로직 적용. `Init()`에 `WindupCost` 횟수만큼 `CastingPhase` Enqueue.
- `Assets/SkillData/magicbolt.tres`: WindupCost 1이 포함된 `마탄` 스킬 픽스처 리소스 추가.
- `Assets/Script/Tests/Sk001Step2WindupTest.cs` (및 `sk001_step2_windup_test.tscn`): Target Selector 동작, Windup baseline 비교, BT 서스펜션 검증 테스트 추가.

구현 내용:
- `TargetSide.Self`는 사거리 계산을 우회하고 시전자를 즉시 반환하도록 구현. `LowestHp`, `HighestHp`는 정렬 조건 최우선에 반영되며, 모든 경우에 동점자는 `OrderByCanonical`로 처리된다.
- `TurnAction_Skill.Init` 단계에서 `Definition.WindupCost`가 존재할 경우, 그 횟수만큼 `CastingPhase`를 큐에 먼저 넣도록 하여 영창 단계를 구성.
- `CastingPhase`는 애니메이션 재생을 기다리며, 완료 시 `ActionState.Executed`를 반환하도록 설계. 이는 `CharacterArticle.TurnPlay` 내에서 `CurrentTurnAction`을 초기화하지 않은 채 BT 루프를 벗어나게 하여, 이후 턴에서 BehaviorTree를 재평가하지 않고 동일 Action(`CastingPhase` 남은 횟수 또는 다음 Phase)을 재개하도록 보장함. ("windup 중 같은 caster의 BT 재평가가 일어나지 않는다" 조건 충족).

검증 결과:
- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `sk001_step2_windup_test.tscn` (C# 코드 기반 Headless Tests): Target Selectors 정렬 확인, Legacy MagicBolt와의 상태/HP/애니메이션 Baseline 비교, Turn Interleave를 통한 BT 우회 여부 등 테스트 로직 작성. 단, 실행 환경상 Godot CLI가 없어 런타임 결과 확인은 생략됨(검증 환경 제약). 기존 로컬 환경에서의 `sk001_step1_skill_system_test.tscn` 및 Combat 회귀 테스트도 동일.

남은 위험/리뷰 포인트:
- `IsCasting` 속성은 현재 노출되었으나 소비처(예: 영창 취소)가 없다. 이는 Step 4 목표로 미뤄짐.

## Step 2 Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 2 판정: **수정 후 완료 (Approved, minor fixes applied)**.

리뷰 중 발견해 이 세션에서 수정·재검증한 항목:

- **[P2]** LowestHp/HighestHp 동점 처리가 유클리드 거리(`LengthSquared`)를 써 ADR-017 정규 순서를 위반했다.
  `SkillUtil.ThenByCanonical`(Manhattan -> Y -> X)로 교체하고, 유클리드/Manhattan이 갈리는 fixture
  `A.TieCanonical`를 테스트에 추가했다.
- **[P2]** "영창 중" 플래그가 구체 타입 `SkillState`로만 접근 가능했다. `ITurnActionState.IsCasting` getter를
  도입하고 `CharacterArticle.IsCasting` 창구를 추가해 SkillSystem 구체 타입 의존 없이 소비 가능하게 했다.
- **[P3]** `SkillTargetSelector.Self`가 미처리로 조용히 Nearest로 동작했다. `SelectTarget`에서
  side/selector 어느 쪽 Self든 시전자를 확정하도록 통합했다.

검증: `dotnet build` 경고/오류 0, `sk001_step2_windup_test` ALL PASS(`A.TieCanonical` 포함),
`sk001_step1_skill_system_test`·CB-001 Step 1~4 ALL PASS, `--import` exit 0.

남은 후속: 무대상 fail-closed 정책(현재 no-op, legacy 동형)은 Step 3 지불 게이트와 함께, `IsCasting` 실제
소비(스턴 영창 취소)는 Step 4에서 처리한다.

## Step 3 Implementation Result

상태: **implemented, review needed** → 리뷰까지 완료(아래 Step 3 Review Result).

변경 파일:
- `Assets/Script/Article/Status/Element/Mana.cs`: `Mana : StatusElement` 신규. `CurrentMana`/`MaxMana`, `CanAfford`, `TrySpend`(부족 시 소모 없이 false).
- `Assets/Script/TurnAction/TurnActionBase.cs`: `ActionState`에 `Failure` 추가(지불 실패 신호, additive).
- `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs`: `ActionState.Failure -> BtStatus.Failure`(CurrentTurnAction 미설정, Selector 다음 행).
- `Assets/Script/Article/CharacterArticle.cs`: `TurnPlay` 재개 경로도 `Failure -> BtStatus.Failure` 매핑 및 CurrentTurnAction 정리.
- `Assets/Script/SkillSystem/SkillDefinition.cs`: `ManaCost` 추가, `IsValidV0`에 `ManaCost < 0` fail-closed.
- `Assets/Script/SkillSystem/SkillState.cs`: `Failed` 플래그.
- `Assets/Script/SkillSystem/TurnAction_Skill.cs`: `Init`에 마나 지불 게이트(부족 시 `Failed` state, 상태 변경 없음), `Action`이 `Failed`를 `ActionState.Failure`로 반환. `TrySpendMana` 헬퍼.
- `Assets/Script/SkillSystem/Blocks/DamageBlock.cs`: `HitChance` 추가. `HitChance < 100`일 때만 `SkillContext.CombatRandRange` 1회로 명중 판정. 명중/실패 모두 report sink 기록. 피해 적용을 `ApplyDamage`로 분리하고 legacy Damage/UI migration 경계를 주석화.
- `Assets/Script/Tests/Sk001Step3ManaHitTest.cs` (+ `.tscn`): 마나 게이트/명중/결정론/ BT 실패 fallback 검증.

구현 내용:
- 지불 게이트는 `Init`에서 `ManaCost > 0`일 때만 `Mana.TrySpend`. 실패 시 상태 변경 없이 `Failed` state를 세우고, `Action`이 이를 `ActionState.Failure`로 되돌려 BT Selector가 다음 행으로 넘어간다. `ManaCost == 0` 스킬(slash/magicbolt)은 게이트를 타지 않아 기존 baseline이 그대로 유지된다.
- 명중 판정은 `SkillContext.CombatRandRange(0,100) >= HitChance`이면 miss. 이는 Step 1/2에서 미사용이던 context RNG seam을 실제 소비로 전환한다. RNG 순서는 `명중 -> 크리티컬 -> 피해`로 고정되며 같은 seed에서 재현된다. `HitChance == 100`은 hit roll을 생략해 slash/magicbolt의 RNG 스트림/HP baseline을 보존한다(Step 1 next_rng 포함 회귀 유지).
- 피해 금액과 Floater/Hit UI는 아직 legacy `Damage.CreateDamage<T>()`/`ApplyImmediately`에 남으며, `DamageBlock.ApplyDamage`가 문서화된 migration 경계다.

검증 결과:
- `dotnet build AutoCrawler.sln -c Debug` — 경고 0, 오류 0.
- `sk001_step3_mana_hit_test.tscn` — ALL PASS(20 assertions): 마나 부족 시 HP/RNG/마나 무변경 Failure, 지불 성공 시 피해 적용, HitChance 0/100 miss/hit + report sink 기록, miss가 CombatRng 정확히 1회 소비, 부분 명중 seed 재현, `PerformAction`/`TurnPlay` 양쪽 Bt Failure 매핑.
- `sk001_step1`·`sk001_step2`·CB-001 Step 1~4 — 전부 ALL PASS(회귀 없음).
- `godot --headless --path . --import` — exit 0, 새 `Mana` GlobalClass 등록.

남은 위험/리뷰 포인트:
- `Mana`는 production character scene에 아직 배선하지 않았다(소비 스킬이 없어 불필요). Mana 원소가 없는 유닛은 `ManaCost > 0` 스킬에 fail-closed된다. 배선은 후속.
- 마나 지불 후 무대상이면 no-op로 턴/마나가 소모된다(기존 legacy 무대상 거동과 동형). 무대상 fail-closed/환불은 Step 4 turn·취소 정책과 함께 확정.

## Step 3 Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 3 판정: **완료 (P0/P1 없음)**.

done condition 대조:
- 마나 부족 시 HP/RNG/FX 무변경 Failure — 검증(A.*, D.*).
- 명중 판정은 CombatRng만 소비, 같은 seed 재현 — 검증(C.MissConsumesExactlyOneRng, C.Reproducible).
- 명중 실패/성공 모두 report sink 기록 — 검증(B.MissReported, B.HitReported).
- damage result API migration 지점 문서화 — `DamageBlock.ApplyDamage` 주석 + ADR-018 §6/§8.

구현 중 발견해 수정한 것은 테스트 결함 1건(다중 상대 미격리로 확정 대상이 검사 대상과 달라 HP 단언 실패)이며 제품 코드 결함은 아니었다. 격리 + 방어력 0 설정을 테스트에 추가해 통과. 제품 코드는 P0~P3 없음.

## Step 4 Slicing

Step 4는 사용자 승인으로 **4a(이번)**와 **4b(KnockbackBlock)**로 분할한다(Task/리뷰 모두 KnockbackBlock 별도 sub-step 허용).

- **Step 4a**: `StunBlock`, `BindBlock`, `SelfBuffBlock`, `ManaDrainBlock` + 피해 배율 hook + 스턴 영창 취소 + 턴 순환 회귀.
- **Step 4b**: `KnockbackBlock`(타일 이동/점유 충돌/맵 경계/`OnMove` signal/`AStar`·tilemap 점유 갱신 별도 fixture).

## Step 4a Implementation Result

상태: **implemented, reviewed** (아래 Step 4a Review Result).

변경 파일:
- `Assets/Script/Article/Status/StatusController.cs`: 신규. 유닛별 턴 단위 제어/버프 상태(`StunTurns`, `BindTurns`/`BoundThisTurn`, `DamageDealtMultiplier`, `DamageTakenMultiplier`)를 카운터로 보유. `ApplyStun/Bind/DamageDealtBuff/DamageTakenDebuff`, `ConsumeStunForTurn`(TurnPlay 소비), `OnTurnStart`(bind/buff 틱).
- `Assets/Script/Article/ArticleBase.cs`: `DamageDealtMultiplier`/`DamageTakenMultiplier` virtual(기본 1.0).
- `Assets/Script/Article/CharacterArticle.cs`: `StatusController` 슬롯, 배율 오버라이드, `CancelCasting`, `TurnPlay` 시작에서 `ConsumeStunForTurn` 스킵, `ApplyTurnStartEffects`에서 `OnTurnStart` 틱.
- `Assets/Script/Article/Status/Affect/Damage.cs`: `Giver` 저장 + `ApplyImmediately`에서 giver/recipient 배율을 롤 이후 곱셈(RNG 무이동).
- `Assets/Script/SkillSystem/Blocks/{StunBlock,BindBlock,SelfBuffBlock,ManaDrainBlock}.cs`: 신규 EffectBlock 4종. StunBlock은 `IsCasting` 대상 영창 취소(환불 없음) 포함.
- `Assets/Script/AutoCrawlerBehaviorTree/Action/{BehaviorTree_Move,BehaviorTree_MultipleMove}.cs`: `BoundThisTurn`이면 이동 없이 `ActionExecuted()`(Success).
- `Assets/Script/Tests/Sk001Step4StatusControlTest.cs`(+`.tscn`): 컨트롤러 순수 로직 + 6개 통합 fixture.

설계 판단:
- 제어/버프 상태를 기존 `StatusAffect`(Damage 지향)가 아니라 `StatusController` 카운터로 표현했다. 이유: `StatusAffect`의 `IAffectedUntilTheEnd`는 1턴 제어에서 apply와 expire가 같은 tick에 붕괴한다. 카운터는 "턴 시작 틱 + TurnPlay 소비"로 frame 비종속이며 검증이 명확하다.
- 피해 배율은 롤 이후 곱셈이라 RNG 스트림을 흔들지 않고, 기본 1.0이라 slash/magicbolt/legacy 피해 baseline을 보존한다.
- 스턴은 TurnPlay 시작에서 소비(턴당 1회, 즉시 Success)하고 영창을 취소한다. 스턴 적용 시점(StunBlock)에도 `IsCasting` 대상 영창을 취소한다.

검증 결과:
- `dotnet build` 경고/오류 0.
- `sk001_step4_status_control_test` — ALL PASS(41, 리뷰 수정 포함): 컨트롤러 apply/만료/재진입(stun/bind/dealt/taken) + turns<=0 no-op, StunBlock 적용+TurnPlay 스킵(이동 없음)+소비, BindBlock 이동 차단+만료, SelfBuff 3배 피해, ManaDrain 대상 감소+시전자 회복, 스턴 영창 취소(마나 환불 없음), 턴 순환(중간 유닛 외부 상태 변경 후 다음 턴이 Priority->SpawnIndex 후속·리스트 처음 리셋 아님).
- `sk001_step1`·`sk001_step2`·`sk001_step3`·CB-001 Step 1~4 — 전부 ALL PASS(회귀 없음).
- `--import` exit 0, 새 GlobalClass(StunBlock/BindBlock/SelfBuffBlock/ManaDrainBlock) 등록.

남은 위험/후속:
- `KnockbackBlock`은 Step 4b.
- `StatusController`의 버프/제어는 아직 데이터 스킬 `.tres`에 배선되지 않았다(블록 카탈로그 검증 단계). Phase1 데이터 입력은 Step 6.
- `Mana`의 production character scene 배선은 여전히 후속(Step 3에서 이월).
- 무대상 fail-closed/마나 환불 정책은 여전히 미확정(현행 no-op).

## Step 4a Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 4a 판정: **완료 (minor fixes applied)**.

외부 리뷰 후 적용한 수정:
- **[P2]** `StatusController.ApplyDamageDealtBuff`/`ApplyDamageTakenDebuff`에서 `turns <= 0`이면 배율을 바꾸지 않고 early-return(카운터 0으로 두면 `OnTurnStart` 만료 분기를 못 타 영구 배율이 되는 문제). 테스트 `G.Dealt_zeroTurns_noop`/`G.Taken_negTurns_noop` 추가.
- **[P3]** `TestStunCancelsCasting`에서 `IsCasting`이 `WindupCost>0`일 때 `Init`만으로 true가 되므로, fixture에 없는 "Cast" 애니메이션을 트리거하던 `Action` 호출을 제거해 headless ERROR 로그를 없앴다.

done condition 대조:
- 4종 블록 독립 fixture로 적용/만료/재진입 — 검증(G.*, A.*, B.*, C.*, D.*).
- 상태 변경이 턴 시작/effect phase에서만, frame 비종속 — `OnTurnStart`(턴 시작)·`ConsumeStunForTurn`(TurnPlay 1회)·블록 phase. 검증.
- 스턴이 windup 대상 영창 취소, 비용/마나 미환불 — 검증(E.*).
- 영창 취소가 턴 순환 교란 없음(리스트 처음 리셋 금지) — 검증(F.*).
- `KnockbackBlock` — Step 4b로 분리.

제품 코드 P0~P3 없음. 기본값(배율 1.0/카운터 0)이 무효과라 baseline·CB-001 회귀 유지.

## Step 4b Implementation Result

상태: **implemented, reviewed** (아래 Step 4b Review Result).

변경 파일:
- `Assets/Script/SkillSystem/Blocks/KnockbackBlock.cs`: 신규. 대상을 시전자 반대 4방향으로 최대 `Distance`칸 밀되, 맵 경계(`GetUsedRect().HasPoint`)와 점유 칸(`GetArticle`)에서 중단. 최종 위치를 `TilePosition`으로 한 번 설정해 `OnMove` 발행 → tilemap `_placedArticles` → (다음)AStar 갱신을 태운다. `GlobalPosition`도 맞춰 시각 동기화.
- `Assets/Script/Tests/Sk001Step4bKnockbackTest.cs`(+`.tscn`): 넉백 이동/경계/충돌/방향 + OnMove/AStar 검증.

설계 판단:
- 방향은 시전자→대상 벡터의 더 큰 축(동률이면 X)을 택해 4방향 카디널로 고정한다(그리드 이동 모델 일치).
- 한 칸씩 전진하며 경계/점유를 검사해 "이동 중단 위치"를 마지막 유효 칸으로 확정한다.
- 점유·AStar를 직접 만지지 않고 `TilePosition` 설정 한 번으로 기존 `OnArticleMove`/`UpdateAStar` 파이프라인을 재사용한다(중복 상태 없음).

검증 결과:
- `dotnet build` 경고/오류 0.
- `sk001_step4b_knockback_test` — ALL PASS(17): 기본 넉백 2칸 + `OnMove`(from/to) + tilemap 점유 갱신 + AStar 새 칸 solid/옛 칸 비solid, 맵 경계 중단 + 이미 경계면 무이동(knockback:0), 점유 칸 앞 중단(blocker 불변), 수평 밀기 + 대각 입력의 카디널(큰 축) 방향.
- `sk001_step1~4`·CB-001 Step 3~4 — ALL PASS(회귀 없음).
- `--import` exit 0, `KnockbackBlock` GlobalClass 등록.

남은 후속:
- 제어/버프/넉백 블록의 `.tres` 데이터 배선은 Step 6.
- 다음은 Step 5 `ChainBlock`(ChainLightning 이관).

## Step 4b Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 4b 판정: **완료 (test fixture minor fix applied)**.

done condition 대조: 점유 칸 충돌(C), 맵 경계(B), 이동 중단 위치(B/C의 마지막 유효 칸), `OnMove` signal 발행(A.OnMove*), `AStar`/tilemap 점유 갱신(A.Occupancy*/A.AStar*) — 전부 별도 fixture로 검증. 제품 코드 P0~P3 없음.

외부 리뷰 후 수정([P2], 테스트 전용): 격리 헬퍼가 미사용 적을 (900,900) 맵 밖으로 옮겨 `_placedArticles`에 맵 밖 좌표가 남고, 이후 `UpdateAStar`의 `SetPointSolid`가 out-of-bounds ERROR를 냈다. `ParkUnusedOpponents`로 교체해 `GetUsedRect()` 안의 비충돌·사거리 밖(우선) 칸으로 격리하고, 대상은 항상 시전자 인접(dist 1)에 둬 확정을 고정했다. 제품 KnockbackBlock 무변경. 재실행 시 out-of-bounds ERROR 없음, 17 assertions ALL PASS.

## Step 5 Implementation Result

상태: **implemented, reviewed** (아래 Step 5 Review Result).

변경 파일:
- `Assets/Script/SkillSystem/Blocks/ChainBlock.cs`: 신규. `TurnAction_ChainLightning`의 연쇄 대상 선택/피해를 데이터 블록으로 이관. 첫 대상은 `ConfirmedTargets[0]`(SelectTarget nearest enemy = legacy `GetTarget`과 동일), 이후 각 홉마다 현재 대상 위치 기준 사거리 안에서 `미타격 우선 -> ADR-017 정규 순서`(legacy `GetChainTarget`과 동일 코드)로 다음 대상 선택. 고정 `MagicalDamage(MaxDamage)`, RNG 미소비.
- `Assets/Script/Tests/Sk001Step5ChainTest.cs`(+`.tscn`): ChainLightning vs ChainBlock baseline + 체인 선택 규칙 검증.

설계 판단:
- ChainBlock은 DamageBlock처럼 단일 phase에서 연쇄 피해를 적용한다. legacy의 Cast/Ignition/Discharge 다중 프레임 FX phase는 프레젠테이션일 뿐 권위 상태(HP)에 무영향이므로 결과가 동일하다(ADR-018 §4: FX phase는 권위 상태 무변경).
- 연쇄 피해는 `MagicalDamage`(IsCritical=false, RNG 미소비)라 baseline 비교는 대상별 HP 결과로 충분하다.

검증 결과:
- `dotnet build` 경고/오류 0.
- `sk001_step5_chain_test` — ALL PASS: 세로 4열 레이아웃에서 legacy와 block 모두 opp1/opp2/opp3에 32 피해, opp4 무피해로 **완전 동일**(`Character:500->468|...|Character4:500->500`). 체인 선택은 opp2 홉에서 이미 맞은 opp1(canonical Y 우선)이 아니라 미타격 opp3를 3번째로 골라 "미타격 우선" 검증. non-benign ERROR 없음(ChainLightning FX 헤드리스 정상).
- `sk001_step1~4b`·CB-001 Step 1~4 — ALL PASS(회귀 없음).
- `--import` exit 0, `ChainBlock` GlobalClass 등록.

ChainLightning 대체 가능성 확인(done condition 3): `TurnAction_ChainLightning`은 `Assets/Scenes/Character/Temp/TempArticle3.tscn`(Ally)와 `PrincessKnight.tscn`의 `BehaviorTree_TurnAction`으로 배선돼 있다. 데이터 기반 ChainBlock `.tres`를 그 BT 노드에 배선하면 대체 가능하나, 그 배선/삭제는 **Step 6**(데이터 팩 + 기존 3종 사용 중단 검토) 범위다. Step 5에서는 삭제하지 않는다.

## Step 5 Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 5 판정: **완료 (P0/P1 없음)**.

done condition 대조:
- 체인 대상 선택 `미타격 우선 -> ADR-017 정규 순서` — legacy `GetChainTarget`과 동일 코드, B.Hop2 unhit-priority로 검증.
- 같은 seed에서 legacy ChainLightning 기준선과 일치 — A.Baseline로 대상별 HP 완전 일치 검증.
- 삭제 전 scene/BT 참조 대체 가능성 확인 — TempArticle3/PrincessKnight BT 참조 확인, Step 6로 이월(삭제 안 함).

제품 코드 P0~P3 없음.

## Step 6 Implementation Result

상태: **implemented, reviewed** (아래 Step 6 Review Result). SK-001 마지막 Step.

변경 파일:
- `Assets/Script/SkillSystem/SkillEnums.cs`: `SelfBuffKind`(DamageDealt/DamageTaken) 추가.
- `Assets/Script/SkillSystem/Blocks/StunBlock.cs`: `Chance`(0~100) 추가. `Chance < 100`일 때만 `SkillContext.CombatRandRange` 1회로 발동 판정(강타 20%, 소닉 블로 35%). 기본 100은 roll 없음(Step 4a baseline 보존).
- `Assets/Script/SkillSystem/Blocks/SelfBuffBlock.cs`: `Kind` 추가. Taken이면 `ApplyDamageTakenDebuff`(금강체 0.5), 기본 Dealt는 기존 동작.
- `Assets/SkillData/Phase1/*.tres`: 12종 스킬 데이터(검/격투/활/지팡이 4계열 × 3).
- `Assets/Script/Tests/Sk001Step6DataPackTest.cs`(+`.tscn`): 생성(없을 때만)+로드/검증+직렬화 round-trip+카테고리 실행.

블록→스킬 매핑(07 수치표):
- 기본기: 베기(Dmg 10~20/95), 잽(10~16/100), 사격(10~18/90 R5), 마탄(Mag 15/95 R3, 마나5).
- 강기: 강타(Dmg+Knockback1+Stun20%, 마나15), 스트레이트(18~28/90, 마나10), 소닉 블로(30~45/85+Stun35%, windup1, 마나20), 조준 사격(25~40/100, windup1, 마나15), 연쇄 뇌격(ChainBlock 3체, windup1, 마나30).
- 제어: 금강체(SelfBuff Taken 0.5×3턴, Self, 마나25), 속박 화살(5~10/85+Bind3, R5, 마나20).
- 흡수: 마나 흡수(Mag 8~15/90 + ManaDrain16, R3).

검증 결과:
- `dotnet build` 경고/오류 0.
- `sk001_step6_data_pack_test` — ALL PASS: 12종 IsValidV0 + Effects>0, 직렬화 round-trip(스크래치, HitChance/StunChance 보존), `Combo` 필드 부재 확인, 기본기(베기 피해)/강기(강타 피해+넉백1)/제어(금강체 받는피해 0.5·속박 3턴)/흡수(마나 흡수: 대상 −16·시전자 +16) 실행, StunBlock Chance 0/100 결정론, 연쇄 뇌격 3체 명중.
- `sk001_step1~5`·CB-001 Step 1~4 — 전부 ALL PASS(블록 확장 기본값 무효과라 회귀 없음).
- `--import` exit 0, 12 `.tres` 파싱 정상.

의도적 근사/미모델(문서화):
- 조준 사격 "크리 +15%p": DamageBlock에 크리 보너스 없음 → 고위력+명중100 windup으로 모델(크리 보너스 후속).
- 연쇄 뇌격 "명중 90": ChainBlock은 per-hit 명중 없음(legacy ChainLightning parity, 항상 명중).
- 마나 흡수 "준 피해의 50% 회복": 정확한 %는 damage-result API 필요 → 고정 `ManaDrain(16)` 대상 흡수로 근사(설계 노트 "마나 ~16 회복"과 정합).
- 마법 min 미반영(마탄/연쇄뇌격 max only): D-2 공식 준수.
- `combo`: 데이터 미입력(`SkillDefinition`에 필드 없음, ADR-018 예약).

## Step 6 Review Result

[[SK-001-Data-Driven-Skill-System-Review]] Step 6 판정: **완료 (P2 test hardening applied)**.

done condition 대조:
- 12종 SkillDefinition 로드/검증 PASS — A.Exists/A.Valid/A.Match[*] 12건 + round-trip.
- 대표 fixture에서 기본기/강기/제어/흡수 실행 — B.* 검증.
- combo 미입력 — 필드 부재 확인.

외부 리뷰 후 수정([P2], 테스트 전용): 기존 테스트는 파일이 없으면 `res://`에 생성해 누락을 가릴 수 있었고, 로드 검증이 `IsValidV0`+`Effects>0`뿐이라 값 드리프트를 놓쳤다. 수정: 생성을 별도 부트스트랩(`Sk001Phase1DataGenerator` + `sk001_phase1_data_generator.tscn`, 회귀 스위트 비포함)으로 분리하고, 스펙을 공유 정본 `Phase1SkillSpec`로 추출했다. 테스트는 `res://`에 쓰지 않고 12개 파일 존재 강제 단언 + `Phase1SkillSpec.Signature`(전체 필드 + 이펙트 타입/파라미터)로 커밋 데이터를 정본과 비교하며, 디렉터리에 정확히 12개인지도 확인한다. 음성 검증으로 bow_shot HitChance 90→85 변조 시 `A.Match[bow_shot]` FAIL을 확인했다.

기존 3종 하드코딩 TurnAction 사용 중단 검토(done condition): 씬 참조는 `TurnAction_Attack`→`TempArticle2.tscn`, `TurnAction_MagicBolt`→씬 참조 없음(코드+Step 2 테스트만), `TurnAction_ChainLightning`→`PrincessKnight.tscn`·`TempArticle3.tscn`. 데이터 스킬로 기능 대체는 가능하다(DamageBlock/ChainBlock 등이 Steps 1/2/5에서 legacy 동작을 재현). 다만 실제 BT 재배선+스크립트 삭제는 **별도 cleanup으로 이월**한다. 이유: (a) 데이터 스킬은 수치표 밸런스(명중 95%·마나 비용)라 legacy 기본값과 다른 밸런스 스왑이고, (b) CB-001 결정론 회귀가 이 캐릭터 씬들을 사용하므로 재검증이 필요하며, (c) 3종 각각 대응 데이터(sword_slash/staff_magicbolt/staff_chainlightning)를 BT 노드에 연결한 뒤 회귀를 다시 봐야 한다.

**SK-001 전체 완료**: Step 0~6 모두 판정 통과. 데이터 기반 스킬 골격 + 블록 카탈로그(Damage/Stun/Bind/SelfBuff/ManaDrain/Knockback/Chain) + Phase1 12종 데이터.

## Related

- [[ADR-018-Data-Driven-Skill-System]]
- [[SK-001-Data-Driven-Skill-System-Review]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
- [[CB-001-Deterministic-Combat-Resolution]]
