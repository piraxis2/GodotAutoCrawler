---
type: system
system: Turn
status: active
updated: 2026-07-05
---

# Turn System

## Agent Brief

- 주요 파일: `Assets/Script/TurnHelper.cs`, `Assets/Script/Article/CharacterArticle.cs`
- 책임: 턴 대상 등록, 순서 결정, 전투 RNG 소유, 턴 시작 효과 적용, 현재 유닛 실행, 다음 턴 전환
- 주의: 사망 중 리스트 변경, 게임오버 조건, Speed 0 일시정지

## Flow

`TurnHelper._Ready()`가 전투 RNG를 `_combatSeed`로 초기화하고, 턴 대상 목록을 구성한 뒤
`Priority` 오름차순 → 스폰 순번(`SpawnIndex`) 오름차순으로 정렬하고 첫 턴을 시작한다. 스폰 순번은
`ArticlesContainer._Ready()`가 씬 트리 순회 순서로 article을 등록하며 부여한다(ADR-017 턴 순서 동점자 키).

턴이 시작되면 `TurnHelper.AdvanceToNextTurn()`이 현재 유닛을 선택하고, game over가 아니면
`ITurnAffectedArticle.ApplyTurnStartEffects()`를 정확히 1회 호출한다. `CharacterArticle`은 이 hook에서
`ArticleStatus.ApplyAffectingStatuses()`를 실행한다.

`TurnHelper._PhysicsProcess()`는 현재 유닛의 `TurnPlay(delta * Speed)`를 호출한다. 결과가 Success 또는
Failure이면 다음 유닛으로 넘어가며, 새 유닛의 턴 시작 효과가 1회 적용된다. Running frame 반복 중에는
지속 효과를 다시 적용하지 않는다.

## Combat RNG

`TurnHelper`는 전투 결과용 `RandomNumberGenerator`를 소유한다. `_combatSeed` export 값으로 초기화되며,
재현 테스트와 전투 재시작은 `ResetCombatRng(seed)`로 같은 RNG 시퀀스를 만들 수 있다. 판정 코드는
`CombatRandRange(double,double)` 또는 `CombatRandRange(int,int)`를 사용한다.

현재 `PhysicalDamage`의 크리티컬 판정과 데미지 롤이 이 RNG를 소비한다. 전투 결과 코드에서 Godot 전역
`GD.Rand*`, `new Random(...)`, `Guid.NewGuid()`를 직접 쓰지 않도록 CB-001 Step 2 정적 가드가 검증한다.

## Canonical Order

전투 의사결정의 후보 동점자는 ADR-017 정규 순서를 따른다. 공용 구현은 `SkillUtil`에 있다.

- `SkillUtil.OrderByCanonical`/`ThenByCanonical`: 기준점과의 Manhattan 거리 → 타일 Y → 타일 X.
- `SkillUtil.GetAttackRangePositions(distance)`: 정규 순서로 정렬된 `IReadOnlyList<Vector2I>`를 반환한다.
- `TurnActionBase.GetTarget`: 사거리 내 생존 적을 정규 순서로 정렬해 첫 번째를 선택한다.
- `TurnAction_ChainLightning.GetChainTarget`: 미타격 대상 우선 → 정규 순서.
- `SkillUtil.SelectCanonicalPath`: 경로 길이 → 목적지 거리 제곱 → 목적지 Y → 목적지 X.
  `BehaviorTree_Move.FindTarget`과 `BehaviorTree_MultipleMove.FindPath`가 사용한다.

## Known Gaps

- 게임오버 후속 처리가 TODO 상태다.
- Priority 정렬은 낮은 값이 먼저 실행되는 현재 구현을 기준으로 한다.
- `SpawnIndex`는 `ArticlesContainer._Ready()`가 `TurnHelper._Ready()`보다 먼저 실행되는 현재 씬 트리
  순서에 의존한다. `TurnHelper`가 `Articles` 딕셔너리에 의존하던 기존 불변식과 같은 조건이지만, 씬 트리
  재배치 시 함께 깨질 수 있는 지점이다.
- `CharacterArticle.AttackRangePositions`의 액션 후보 count 동점은 트리 순회 순서(결정적)에 의존하며
  명시적 동점자 규칙은 없다([[CB-001-Deterministic-Combat-Resolution]] Follow-ups).

## Verification

- `Assets/Script/Tests/cb001_step1_turn_effect_test.tscn`: 턴 시작 효과 1회 적용, Running frame 재적용 방지,
  Speed 차이 불변, game over 상태 미적용을 검증한다.
- `Assets/Script/Tests/cb001_step2_combat_rng_test.tscn`: 같은 seed RNG/PhysicalDamage 시퀀스 재현과
  금지 난수 API 정적 가드를 검증한다.
- `Assets/Script/Tests/cb001_step3_canonical_order_test.tscn`: 사거리 오프셋 정규 순서, 타겟/체인 동점자
  규칙, 턴 순서 Priority → SpawnIndex 안정 정렬, 경로 후보 동점자 규칙을 검증한다.
- `Assets/Script/Tests/cb001_step4_determinism_test.tscn`: 실제 `battle_field.tscn` 결정론 회귀 —
  같은 시드 2회 이벤트 로그 완전 일치, 다른 시드 상이(스모크), 같은 시드 + 다른 배속 완전 일치,
  근접/체인/이동 배선의 씬 레벨 소비 보증.

## Related

- [[BehaviorTree-System]]
- [[Article-Status-System]]
- [[CB-001-Deterministic-Combat-Resolution]]