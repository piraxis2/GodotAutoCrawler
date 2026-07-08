---
id: ADR-018
type: decision
status: accepted
date: 2026-07-06
updated: 2026-07-08
system: Combat
---

# Data-Driven Skill System

## Context

참조 기획서 `F:\beestation\ref\wizards_climber_wiki\기획서\10_스킬시스템_설계.md`는 스킬을 데이터로, 효과를 재사용 block 코드로, 실행 중 상태를 유닛별 객체로 분리하는 개편을 제안한다.

2026-07-06 갱신으로 연계(combo)는 보류 확정됐다. `SkillDefinition.combo`는 예약 필드로만 남기며, 현재 SK-001 범위에서는 구현하거나 데이터 입력하지 않는다. `07_수치표_Phase1.md`의 격투 라인도 연계 없이 단독 역할 기준으로 재조정됐다.

현재 AutoCrawler 코드는 다음 구조다.

- `TurnActionBase : Resource`가 `ActionQueue`, `_usedCost`를 가진다.
- 각 하드코딩 TurnAction Resource가 대상, FX, chain 진행 상태를 가진다.
- `BehaviorTree_TurnAction`은 export된 TurnAction Resource를 직접 실행하고, Running이면 `CharacterArticle.CurrentTurnAction`에 보관한다.
- CB-001 이후 전투 RNG와 타겟/이동 동점자 규칙은 ADR-017로 결정론이 고정되어 있다.

Resource는 여러 유닛이나 노드가 같은 파일을 참조할 수 있으므로, 런타임 진행 상태를 Resource에 저장하는 현재 구조는 데이터 기반 스킬 확장과 충돌한다.

## Decision

### 1. Definition과 State를 분리한다

`SkillDefinition`과 `EffectBlock`은 `.tres`로 저장되는 불변 Resource다.

전투 중 변경되는 값은 `SkillState`가 가진다.

- phase queue
- 사용한 행동력
- 확정 대상/타일
- 영창 중 플래그
- 직전 block 결과
- report accumulator

`SkillDefinition`/`EffectBlock`에는 전투 진행 상태를 저장하지 않는다.

`SkillDefinition.combo`는 예약 필드로만 둔다. 구현과 데이터 입력은 하지 않으며, 재개 시 별도 Task/ADR에서 의미와 실패 정책을 확정한다.

### 2. 단일 실행기 `TurnAction_Skill`을 도입한다

기존 BT 경로와 호환하기 위해 첫 단계에서는 `TurnActionBase` 상속을 유지한다. interface 기반 분리는 기존 3종 하드코딩 TurnAction 삭제가 확정되는 마지막 Step 이후 재평가한다.

Step 0 설계 리뷰에서 확인한 현재 코드 제약: `TurnActionBase.Action()`은 non-virtual이고 `_usedCost`를
private 필드로 직접 관리하며, `BehaviorTree_TurnAction`은 Running Resource를
`CharacterArticle.CurrentTurnAction`에 저장해 재개한다. 따라서 Step 1은 기존 BT/CurrentTurnAction 경로를
깨지 않는 작은 compatibility seam을 함께 도입한다. 목적은 `TurnAction_Skill`의 phase queue, used cost,
확정 대상 같은 실행 상태가 Resource 필드가 아니라 유닛별 `SkillState`에서만 복원되도록 하는 것이다.
legacy `TurnAction_Attack`/`MagicBolt`/`ChainLightning`의 현재 동작은 이 변경으로 깨지지 않아야 한다.

`TurnAction_Skill`은 export된 `SkillDefinition`을 읽고, 실행 시작 시 유닛별 `SkillState`를 만들어 다음 순서로 처리한다.

1. 비용/실행 가능 여부 검증
2. 대상 확정
3. windup phase
4. effect block phase
5. 종료 및 리포트 기록

combo 판정은 파이프라인에서 제거한다. `combo` 필드는 예약 상태다.

기존 `TurnAction_Attack`, `TurnAction_MagicBolt`, `TurnAction_ChainLightning`은 호환 기준선으로 남기고, 데이터 기반 구현이 검증된 뒤 단계적으로 제거한다.

### 3. 새 파일 위치는 `Assets/Script/SkillSystem`으로 분리한다

데이터 기반 스킬 시스템의 신규 C# 코드는 legacy `Assets/Script/TurnAction` 폴더가 아니라 `Assets/Script/SkillSystem` 아래에 둔다.

기존 TurnAction 3종은 migration 기준선으로 유지하되, 새 definition/state/block/context 코드는 legacy 폴더와 섞지 않는다.

### 4. EffectBlock은 SkillContext로만 세계를 본다

EffectBlock은 `SkillContext`를 통해서만 caster, tilemap, fx, RNG, report sink에 접근한다.

v0 `SkillContext`는 `BattleFieldScene.BattleField`를 감싸는 adapter로 시작한다. 단, block이 보는 API는 처음부터 주입 형태로 고정해 추후 adapter 교체만으로 headless 전환 가능하게 한다.

전투 결과 난수는 ADR-017의 `TurnHelper.CombatRandRange` 창구만 사용한다.

FX phase는 권위 상태를 변경하지 않는다. HP, status, tile position, death 같은 권위 상태 변경은 effect 적용 phase에서만 수행한다.

### 5. TargetSelector는 정규 동점자를 보존한다

TargetSelector는 후보의 1차 우선순위만 정한다.

모든 동점자는 ADR-017 정규 순서(`거리 -> Y -> X`)를 사용한다.

`target_side`는 적/아군/자기 필터를 명시한다.

### 6. DamageBlock migration은 2단계로 진행한다

Step 1 `DamageBlock`은 기존 Attack 기준선 일치를 위해 `Damage.CreateDamage<T>()`를 재사용한다.
`DamageBlock`의 public 실행 계약은 `SkillContext`를 받도록 고정하지만, Step 1에서는 legacy Damage 내부의
`BattleFieldScene.BattleField.TurnHelper` 직접 조회를 기준선 일치용 예외로 허용한다.

Step 3에서 명중 판정을 도입할 때 새 damage result API로 전환한다. 이 전환은 `Damage.ApplyImmediately`의 UI 직접 결합 제거 Follow-up과 병합한다.

Step 3 실현(2026-07-07): `DamageBlock.HitChance`를 도입했다. 명중 판정만 `SkillContext.CombatRandRange`(단일 CombatRng)로 옮겨 seam을 실제 소비하게 했고, RNG 순서는 `명중 -> (명중 시) 크리티컬 -> 피해 롤`로 고정된다. `HitChance == 100`(기본값)은 hit roll을 하지 않아 slash/magicbolt의 기존 baseline RNG 스트림을 보존한다. 피해 금액 산출과 Floater/Hit UI는 아직 legacy `Damage.CreateDamage<T>()`/`ApplyImmediately`에 남으며, `DamageBlock.ApplyDamage`가 그 UI 결합 제거·headless 피해 result API로 넘길 migration 경계다. 명중 실패/성공은 모두 `SkillState.Reports`(report sink)에 기록된다.

### 9. 제어/버프 상태는 StatusController 카운터로, 피해 배율은 giver/recipient hook으로 (Step 4a)

제어(Stun/Bind)와 버프(SelfBuff, 피해 수신 배율) 상태는 기존 `StatusAffect`(Damage 지향)가 아니라 유닛별 `StatusController`(턴 카운터)로 표현한다. 이유: `StatusAffect.IAffectedUntilTheEnd`는 1턴 제어에서 적용과 만료가 같은 tick에 붕괴한다. `StatusController`는 `OnTurnStart`(턴 시작 틱)와 `ConsumeStunForTurn`(TurnPlay 턴당 1회 소비)로 frame 비종속이다.

- Stun: `TurnPlay` 시작에서 소비→즉시 턴 스킵 + 영창 취소. Bind: `OnTurnStart`가 `BoundThisTurn` 확정→이동 노드가 이동 없이 Success. SelfBuff/피해수신 배율: 지속시간 동안 배율 유지 후 만료 시 1.0 복귀.
- 피해 배율 hook: `ArticleBase`의 virtual `DamageDealtMultiplier`/`DamageTakenMultiplier`(기본 1.0)를 `CharacterArticle`이 `StatusController`로 오버라이드. `Damage.ApplyImmediately`가 롤 이후에 곱하므로 RNG 스트림 무이동, 기본 1.0이라 baseline 보존.
- 스턴 영창 취소는 Step 2 `IsCasting` 플래그를 소비해 `CancelCasting`(CurrentTurnAction/SkillState 폐기, 지불 비용/마나 미환불)으로 처리한다. `CancelCasting`은 턴 리스트에서 유닛을 제거하지 않아 `GetNextTurnArticle` 리셋 위험이 없다.
- `KnockbackBlock`(Step 4b)은 별도 점유 테이블을 만들지 않고 최종 위치를 `TilePosition`으로 1회 설정한다. 이 세터가 `OnMove`를 발행→tilemap `_placedArticles` 갱신→`UpdateAStar`가 재구성한다. 경계(`GetUsedRect().HasPoint`)·점유(`GetArticle`) 검사는 이동 노드와 같은 API를 쓴다. 방향은 시전자→대상의 큰 축(동률 X)으로 4방향 카디널.
- `ChainBlock`(Step 5)은 `TurnAction_ChainLightning`의 대상 선택/피해만 이관한다. 첫 대상=`ConfirmedTargets[0]`, 홉마다 `미타격 우선 -> ADR-017 정규 순서`(legacy `GetChainTarget` 동일). 고정 `MagicalDamage`(RNG 미소비)를 단일 phase에 적용해 legacy와 대상별 HP가 일치한다. legacy의 다중 프레임 Cast/Ignition/Discharge FX phase는 §4대로 권위 상태 무변경이라 결과에 무영향. `TurnAction_ChainLightning`은 TempArticle3/PrincessKnight BT 배선이 남아 Step 6까지 유지.
- Phase1 데이터 팩(Step 6): `Assets/SkillData/Phase1/` 12종. 블록 확장 `StunBlock.Chance`(0~100, <100일 때만 CombatRng 1회, 기본 100 무roll)와 `SelfBuffBlock.Kind`(Dealt/Taken, 금강체=Taken 0.5)를 추가하되 기본값을 무효과로 둬 기존 baseline을 보존한다. 근사/미모델(크리 보너스·per-hit 명중·마나 흡수 정확 %·마법 min)은 damage-result API 등 후속. 기존 3종 하드코딩 TurnAction은 데이터로 대체 가능하나 BT 재배선/삭제는 밸런스 스왑·CB-001 회귀 때문에 별도 cleanup으로 이월. `SkillDefinition.combo`는 예약대로 필드/데이터 없음.

### 8. 지불 실패는 ActionState.Failure로 BT fallback을 낸다

마나 등 지불 게이트가 실패하면 상태 변경 없이 BT가 다음 행으로 넘어가야 한다. `ActionState`에 `Failure`를 추가하고, `BehaviorTree_TurnAction.PerformAction`과 `CharacterArticle.TurnPlay`가 이를 `BtStatus.Failure`로 매핑한다(Selector fallback). legacy 3종 TurnAction은 `Failure`를 반환하지 않으므로 additive 변경이다. `Mana : StatusElement`는 `TrySpend`로 부족 시 소모 없이 false를 반환하고, `TurnAction_Skill.Init`이 `ManaCost > 0`일 때만 지불한다(`ManaCost == 0`은 게이트 없음 → 기존 baseline 보존).

### 7. Report stream을 실행기의 계약에 포함한다

각 effect 적용은 report sink에 결과를 남긴다.

초기에는 테스트와 디버그 용도만으로 충분하지만, 장기적으로 사용 기반 XP, 전투 후 리포트, 택틱 보드 행별 발동 통계가 이 스트림을 소비한다.

## Rationale

- Resource를 불변 데이터로 제한하면 여러 유닛이 같은 스킬 파일을 공유해도 실행 상태가 섞이지 않는다.
- 기존 BT와 TurnAction 경로를 유지하면 개편 첫 단계의 blast radius를 줄일 수 있다.
- EffectBlock은 신규 스킬 12종의 중복 구현을 줄이고, 수치표 변경을 데이터 편집으로 끝낼 수 있게 한다.
- ADR-017 결정론 규칙을 그대로 이어가면 리플레이와 회귀 테스트 기반이 흔들리지 않는다.
- combo를 보류하면 windup, 스턴 취소, 마나 실패, 대상 유지가 얽힌 예외 규칙을 첫 개편 범위에서 제거할 수 있다.

## Consequences

### Positive

- 신규 스킬 추가와 밸런싱이 `.tres` 중심으로 바뀐다.
- Phase 1 스킬 12종을 9개 안팎의 block으로 표현할 수 있다.
- 택틱 보드 compiler는 SkillDefinition과 TargetSelector를 직접 참조할 수 있다.
- SkillState isolation 테스트로 Resource 공유 사고를 조기에 잡을 수 있다.
- combo 없이도 갱신된 격투 라인은 잽/스트레이트/소닉 블로의 단독 역할로 검증 가능하다.

### Negative

- 첫 단계에서는 기존 mutable `TurnActionBase`와 새 `SkillState`가 공존한다.
- `Damage.ApplyImmediately`의 UI 결합이 Step 3 전까지 남아 있어 순수 headless 테스트가 제한된다.
- `Mana`, `WeaponType`, skill level, prerequisite 등 아웃게임 모델이 아직 없어 Definition의 일부 필드는 v0에서 보류된다.
- `PhysicalDamage`의 RNG 접근이 아직 SkillContext 주입형이 아니므로 장기 API와 단기 baseline 사이에 adapter가 필요하다.
- combo 재개 시 별도 설계 비용이 필요하다.

## Verification

- SkillDefinition/EffectBlock `.tres` 저장 및 재로드.
- 같은 SkillDefinition을 두 유닛이 공유해도 SkillState의 phase queue, target, used cost가 독립임을 검증.
- 데이터 기반 `베기`와 기존 `TurnAction_Attack`의 결과 일치.
- 데이터 기반 windup fixture와 기존 `TurnAction_MagicBolt`/`TurnAction_Cast` 기준선 비교.
- `MasterCost = windup + act`, windup 중 턴 인터리브, BT 재평가 정지, 영창 중 플래그 외부 노출 검증.
- TargetSelector matrix와 ADR-017 동점자 유지 검증.
- 스턴 영창 취소가 턴 순환 순서를 교란하지 않음 검증.
- Knockback은 점유 칸 충돌, 맵 경계, `OnMove` signal, AStar/tilemap 점유 갱신을 별도 검증.
- CB-001 결정론 회귀 유지.

## Follow-ups

- `Mana` StatusElement를 production character scene(PrincessKnight/Puppet 등)에 배선(Step 3은 클래스+게이트만 도입, 소비 스킬이 나올 때 배선). 현재는 Mana 원소가 없는 유닛이 `ManaCost > 0` 스킬을 쓰면 fail-closed된다.
- 무대상(사거리 내 대상 0) 정책: 현재 마나 지불 후 no-op 턴 소모. 무대상 fail-closed/마나 환불 여부를 Step 4 turn/취소 정책과 함께 확정.
- `DamageBlock.ApplyDamage`의 legacy Damage/UI 결합 제거(headless 피해 result API).
- Mana/weapon/prerequisite 아웃게임 모델 확정.
- ChainLightning 삭제 전 ChainBlock baseline 비교.
- 택틱 보드 compiler가 SkillDefinition/TargetSelector를 소비하도록 연결.
- combo 재개 시 별도 Task/ADR 작성.

## Related

- [[SK-001-Data-Driven-Skill-System]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
