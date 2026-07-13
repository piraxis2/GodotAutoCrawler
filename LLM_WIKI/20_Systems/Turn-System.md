---
type: system
system: Turn
status: active
updated: 2026-07-13
---

# Turn System

## Agent Brief

- 주요 파일: `Assets/Script/TurnHelper.cs`, `Assets/Script/Article/CharacterArticle.cs`
- 책임: 턴 순서 결정/정렬, 현재 유닛 실행, 다음 행동자 전환, 턴 시작 효과 호출, 전투 RNG·FX tick(U1 임시)
- 소유하지 않음(BS-001 이후 신규 세션 경로): 승패/게임오버 의미 판정, 결과 생성, 참가자 발견, ammo reset —
  `BattleSession`이 소유한다([[Battle-Session-System]], [[ADR-022-Battle-Session-Lifecycle]])
- 주의: Speed 0 일시정지, `AutoStart` 직접 실행 seam vs 명시 `Configure/StartBattle` 경로 구분

## Lifecycle Seam (BS-001)

`TurnHelper`는 명시적 수명 주기 상태(`Idle → Configured → Running → Stopped`)를 가진다.

- `[Export] AutoStart`(기본 true): 직접 씬 실행(`battle_field.tscn`) 호환 seam. `_Ready()`가 컨테이너에서
  참가자를 수집·ammo 충전한 뒤 `Configure`+`StartBattle`을 자동 수행한다. 모든 직접 실행 소비자 전환 뒤 삭제
  후보다.
- 신규 세션 경로: `AutoStart=false`로 두고 `BattleSession`이 `Configure(participants, seed)` +
  `StartBattle()`을 명시 호출한다. `StopBattle()`은 외부(세션) 요청으로 턴 진행을 멈춘다. start/stop은 멱등,
  configure 전 start는 fail-closed.
- `_PhysicsProcess()`는 `Running`에서만 턴을 진행한다. 진영 공백 기반 종료 감지는 legacy `AutoStart` 경로에서만
  유지하고, 명시 경로는 참가자 링 진행 가능 여부만 본다(승패는 `BattleSession` 소유).
- `Configure`는 사망 시 턴 순서에서 유닛을 제거하는 `OnDead` 구독을 보관했다가 재구성/`StopBattle` 시 해제한다
  (구독 누적 방지).

## Flow

직접 실행(`AutoStart=true`)에서 `TurnHelper._Ready()`가, 신규 세션에서 `TurnHelper.Configure`가 전투 RNG를
seed로 초기화하고 턴 대상 목록을 `Priority` 오름차순 → 스폰 순번(`SpawnIndex`) 오름차순으로 정렬한 뒤 `StartBattle`
이 첫 턴을 시작한다. 스폰 순번은 `ArticlesContainer._Ready()`가 씬 트리 순회 순서로 article을 등록하며 부여한다
(ADR-017 턴 순서 동점자 키).

턴이 시작되면 `TurnHelper.AdvanceToNextTurn()`이 현재 유닛을 선택하고, game over가 아니면
`ITurnAffectedArticle.ApplyTurnStartEffects()`를 정확히 1회 호출한다. `CharacterArticle`은 이 hook에서
순서만 오케스트레이션한다.

1. `StatusController.OnTurnStart()` — 제어/버프 카운터 틱
2. `ArticleStatus.ApplyTurnStartStatusElements()` — `HealthRegen`/`ManaRegen` 등 자연 회복(ADR-019)
3. 살아 있으면 `ArticleStatus.ApplyAffectingStatuses()` — 지속 효과

`TurnHelper._PhysicsProcess()`는 현재 유닛의 `TurnPlay(delta * Speed)`를 호출한다. 결과가 Success 또는
Failure이면 다음 유닛으로 넘어가며, 새 유닛의 턴 시작 효과가 1회 적용된다. Running frame 반복 중에는
지속 효과나 자연 회복을 다시 적용하지 않는다. 자연 회복은 RNG를 소비하지 않아 결정론 시퀀스를 바꾸지 않는다.

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

## Cursor (현재 유닛 사망 시)

턴 순서 링은 정수 커서(`_turnCursor`)를 진실로 삼는다. 현재 턴 유닛이 사망으로 리스트에서 제거되면 후임이 커서
슬롯으로 당겨지므로 커서를 +1 하지 않고 리스트 길이로 wrap만 한다. 제거 위치가 커서보다 앞이면 커서를 당긴다.
과거 `IndexOf(current) == -1` 뒤 `(index+1) % count == 0`으로 순번이 목록 처음으로 부당 리셋되던 결함은
BS-001 Step 1에서 제거됐다.

## Known Gaps

- 승패/게임오버 의미는 신규 세션 경로에서 `BattleSession`이 소유한다. legacy `AutoStart` 경로의 진영 공백 종료는
  여전히 TODO에서 조용히 멈춘다(직접 실행 호환용).
- RNG와 FX tick은 U1 동안 `TurnHelper`에 남는다(순수 시뮬/프레젠테이션 분리는 후속).
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
- `Assets/Script/Tests/st001_step1_natural_regen_test.tscn`: 턴 시작 자연 회복 1회 적용, Running frame 반복과
  `Speed` 변화 불변, 사망 유닛의 이후 turn-start 스탯/지속 효과 스킵.
- `Assets/Script/Tests/cb001_step4_determinism_test.tscn`: 실제 `battle_field.tscn` 결정론 회귀 —
  같은 시드 2회 이벤트 로그 완전 일치, 다른 시드 상이(스모크), 같은 시드 + 다른 배속 완전 일치,
  근접/체인/이동 배선의 씬 레벨 소비 보증.
- `Assets/Script/Tests/bs001_step1_lifecycle_test.tscn`: `AutoStart` on/off, `Configure/StartBattle/StopBattle`
  멱등·정지, 현재 유닛 사망 시 커서 후임/wrap/선행 제거 보정, 재구성 구독 무누적을 검증한다.

## Related

- [[Battle-Session-System]]
- [[BehaviorTree-System]]
- [[Article-Status-System]]
- [[CB-001-Deterministic-Combat-Resolution]]
- [[ADR-022-Battle-Session-Lifecycle]]