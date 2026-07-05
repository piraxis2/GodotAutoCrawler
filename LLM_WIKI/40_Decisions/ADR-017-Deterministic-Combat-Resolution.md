---
id: ADR-017
type: decision
status: accepted
date: 2026-07-05
updated: 2026-07-05
system: Combat
---

# Deterministic Combat Resolution

## Context

CB-001의 목표는 현 전투 구조를 유지한 채 "같은 초기 배치와 같은 시드이면 같은 결과"를 보장하는
1단계 결정론 기반을 만드는 것이다. 전투 결과에는 턴 순서, 행동 대상, 이동 목적지, 피해량, 크리티컬
여부, 사망 순서, 최종 HP/위치가 포함된다.

현재 코드는 다음 이유로 실행마다 결과가 흔들릴 수 있다.

- `PhysicalDamage`가 Godot 전역 RNG(`GD.RandRange`)를 직접 사용한다.
- `HashSet<Vector2I>` 기반 사거리 열거와 `FirstOrDefault` 대상 선택이 후보 동점자를 명시적으로 정하지 않는다.
- `TurnHelper`가 `Priority`만 비교해 정렬하고, 같은 priority 유닛의 순서를 별도 키로 고정하지 않는다.
- `CharacterArticle.TurnPlay`가 자기 턴 동안 매 physics frame `ApplyAffectingStatuses()`를 호출한다.
- `BehaviorTree_Move`와 `BehaviorTree_MultipleMove`가 같은 길이/거리 후보의 최종 동점자를 명시하지 않는다.

2단계의 순수 `TurnResolver`/이벤트 로그 분리는 후속으로 미룬다. CB-001은 현재 `TurnHelper` +
`CharacterArticle` + `TurnAction` + `BehaviorTree` 실행 구조 안에서 비결정성을 제거한다.

## Decision

### 1. 전투 결과 RNG는 `CombatRng` 하나만 사용한다

전투 결과에 영향을 주는 난수는 전투 시작 시 시드된 단일 `CombatRng`에서만 소비한다.

- 소유자는 `TurnHelper`로 둔다.
- `TurnHelper`는 `[Export]` 고정 seed와 runtime seed 초기화를 제공한다.
- 런타임 접근은 기존 전투 전역 진입점인 `BattleFieldScene.BattleField.TurnHelper`를 통해 한다.
- 판정 코드가 직접 `GD.Rand*`, `new Random(...)`, `Guid.NewGuid()`를 호출하지 않도록 정적 가드로 막는다.
- FX/사운드/카메라 흔들림 같은 연출 전용 난수는 전투 결과 RNG를 소비하지 않는 한 별도 RNG를 쓸 수 있다.

RNG 소비는 판정 시점에만 허용한다. physics frame마다 반복되는 `RunPhase`/Tween/Fx tick류에서 전투 결과
RNG를 소비하지 않는다.

### 2. 후보 선택은 정규 순서로 결정한다

후보가 여럿인 전투 의사결정은 명시적 정규 순서를 따른다. 기본 전투 후보 키는 다음과 같다.

1. 기준점과의 Manhattan 거리 오름차순
2. 타일 `Y` 오름차순
3. 타일 `X` 오름차순

적용 범위:

- 공격 사거리 오프셋 생성(`SkillUtil.GetAttackRangePositions`)
- 기본 타겟 선택(`TurnActionBase.GetTarget`)
- 체인라이트닝 다음 타겟 선택(`TurnAction_ChainLightning.GetChainTarget`)
- 이동 목적지 선택(`BehaviorTree_Move.FindTarget`, `BehaviorTree_MultipleMove.FindPath`)

체인라이트닝은 아직 맞지 않은 대상 우선 여부를 1차 키로 유지하고, 그 안의 동점자는 거리/Y/X로 결정한다.

턴 순서는 별도 키를 쓴다.

1. `Priority` 오름차순
2. 스폰 순번 오름차순

스폰 순번은 `ArticlesContainer._Ready()`가 씬 트리 순회 순서로 article을 등록할 때 부여한 값을 사용한다.
같은 전투 씬에서 씬 트리 순회 순서는 재현 가능한 초기 배치의 일부로 본다.

### 3. 상태 변경은 이산 턴 판정 지점에서만 일어난다

전투 권위 상태(HP, 상태 효과, 타일 위치, 사망)는 턴 시작 또는 액션 판정 같은 이산 지점에서만 변경한다.

CB-001 Step 1에서는 지속 효과 적용을 `CharacterArticle.TurnPlay`의 매 frame 호출에서 제거하고,
`TurnHelper`가 새 살아있는 유닛 턴을 선택한 직후 한 번만 호출하도록 옮긴다. 죽었거나 제거된 유닛은
새 턴 효과 적용 대상이 아니다.

`TurnHelper.Speed`, physics frame rate, Tween/Fx 진행량은 판정 결과에 영향을 주지 않아야 한다.

## Rationale

- 현재 구조를 크게 갈아엎지 않고도 가장 큰 비결정성 원인인 전역 RNG, 해시/불안정 동점자, 프레임 종속
  상태 적용을 제거할 수 있다.
- `TurnHelper` 소유 RNG는 전투 lifecycle과 가장 가깝고, `BattleFieldScene.BattleField` 기존 접근 패턴과 맞는다.
- 거리/Y/X 정규 순서는 사람이 예측 가능하고 테스트 fixture에서 기대값을 명확히 쓸 수 있다.
- 스폰 순번은 scene child order를 보존하므로 같은 초기 배치의 일부로 취급하기 쉽다.
- 지속 효과를 턴 시작 1회로 제한하면 속도와 프레임 수가 밸런스에 영향을 주던 버그가 사라진다.

## Consequences

### Positive

- 같은 seed와 같은 초기 배치의 전투 결과를 회귀 테스트로 고정할 수 있다.
- `Speed`를 바꿔도 피해량/상태 효과/사망 순서가 변하지 않는다.
- 타겟과 이동 선택이 디버깅 가능한 규칙으로 설명된다.
- 향후 2단계 `TurnResolver`/이벤트 로그 분리의 입력 규약이 선명해진다.

### Negative

- 기존 관찰 결과가 달라질 수 있다. 특히 여러 대상이 같은 사거리 안에 있거나 같은 priority 유닛이
  있을 때 첫 행동 대상/순서가 바뀔 수 있다.
- 지속 효과가 기존보다 덜 자주 적용된다. 이는 의도한 버그 수정이지만 밸런스 값은 나중에 조정이
  필요할 수 있다.
- `TurnHelper`/`BattleFieldScene` 접근이 없는 순수 상태 단위 테스트에서는 `CombatRng` 주입용 테스트
  대역 또는 작은 public API가 필요하다.
- `Damage.ApplyImmediately`가 UI(`DamageFloater`, `Hit`)를 직접 호출하는 결합은 남는다. 결정론 회귀는
  현 단계에서 씬 포함 테스트나 대역 scene으로 검증한다.

## Verification

- Step 1: 같은 턴을 서로 다른 `Speed`로 실행해도 지속 효과 적용 횟수가 유닛 턴당 1회임을 검증한다.
- Step 2: 같은 seed로 `PhysicalDamage` 판정을 반복하면 크리티컬/피해 시퀀스가 동일함을 검증한다.
  `Assets/Script` 전투 판정 코드의 금지 난수 API 정적 가드를 추가한다.
- Step 3: 사거리 내 적 2명 이상, 체인 후보 동점, 같은 priority 유닛, 이동 목적지 동점 fixture에서
  규칙과 기대 선택이 일치함을 검증한다.
- Step 4: 대표 전투 씬을 같은 seed로 2회 실행해 결과 로그가 완전히 같고, 같은 seed + 다른 `Speed`도
  결과가 같음을 검증한다.

## Follow-ups

- 순수 `TurnResolver`와 이벤트 로그(`Moved`/`Attacked`/`Damaged`/`Died`) 기반 2단계 전투 판정 분리.
- `Damage.ApplyImmediately`의 UI 직접 호출 제거.
- 엔진 버전 간 리플레이 호환이 필요해지면 `AStarGrid2D` 대신 자체 결정론 경로탐색 검토.
- AoE(`Scale`) 구현 시 명중 대상 적용 순서에도 같은 정규 순서 적용.

## Related

- [[CB-001-Deterministic-Combat-Resolution]]
- [[Turn-System]]
- [[Article-Status-System]]
- [[BehaviorTree-System]]
